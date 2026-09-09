// YouTube Caching & In-flight Deduplication Module
// Inspired by vkara's optimization strategy

import Redis from 'ioredis'

// Cache configuration
const REDIS_KEY_PREFIXES = {
  SEARCH: 'yt-search:',
  DETAILS: 'yt-details:',
  PLAYLIST: 'yt-playlist:',
}

const CACHE_TTL = {
  SEARCH: 5 * 60, // 5 minutes
  DETAILS: 30 * 60, // 30 minutes
  PLAYLIST: 15 * 60, // 15 minutes
}

// In-memory cache for active searches (before Redis)
const memoryCache = new Map()
const MAX_MEMORY_CACHE_SIZE = 500

// In-flight deduplication - prevent duplicate concurrent requests
const inFlightRequests = new Map()

/**
 * Create in-flight deduplication helper
 * Coalesces concurrent requests for the same resource
 */
export function createInFlightDedup() {
  return {
    async run(key, factory) {
      const existing = inFlightRequests.get(key)
      if (existing) {
        return existing
      }

      const promise = factory().finally(() => {
        inFlightRequests.delete(key)
      })
      
      inFlightRequests.set(key, promise)
      return promise
    }
  }
}

const dedup = createInFlightDedup()

/**
 * Get cached data or execute factory function
 */
export async function getCached(redis, prefix, key, ttl, factory) {
  const cacheKey = `${prefix}${key}`
  
  // 1. Check memory cache first
  if (memoryCache.has(cacheKey)) {
    const cached = memoryCache.get(cacheKey)
    if (Date.now() - cached.timestamp < ttl * 1000) {
      return cached.data
    }
    memoryCache.delete(cacheKey)
  }
  
  // 2. Check Redis cache
  if (redis) {
    try {
      const cached = await redis.get(cacheKey)
      if (cached) {
        const data = JSON.parse(cached)
        // Also store in memory cache for faster access
        memoryCache.set(cacheKey, { data, timestamp: Date.now() })
        return data
      }
    } catch (err) {
      console.warn('Redis cache read failed:', err.message)
    }
  }
  
  // 3. Use in-flight dedup to prevent concurrent identical requests
  return dedup.run(cacheKey, async () => {
    const data = await factory()
    
    // Store in memory cache
    memoryCache.set(cacheKey, { data, timestamp: Date.now() })
    
    // Limit memory cache size (LRU-style cleanup)
    if (memoryCache.size > MAX_MEMORY_CACHE_SIZE) {
      const entries = Array.from(memoryCache.entries())
      const sortedByTime = entries.sort((a, b) => a[1].timestamp - b[1].timestamp)
      const toRemove = sortedByTime.slice(0, Math.floor(MAX_MEMORY_CACHE_SIZE * 0.2))
      toRemove.forEach(([key]) => memoryCache.delete(key))
    }
    
    // Store in Redis cache
    if (redis) {
      try {
        await redis.setex(cacheKey, ttl, JSON.stringify(data))
      } catch (err) {
        console.warn('Redis cache write failed:', err.message)
      }
    }
    
    return data
  })
}

/**
 * Initialize Redis client
 */
export function createRedisClient() {
  const redisHost = process.env.REDIS_HOST || 'localhost'
  const redisPort = process.env.REDIS_PORT || 6379
  const redisPassword = process.env.REDIS_PASSWORD || ''
  
  try {
    const redis = new Redis({
      host: redisHost,
      port: redisPort,
      password: redisPassword || undefined,
      retryStrategy: (times) => {
        if (times > 3) {
          console.warn('Redis connection failed after 3 retries. Running without Redis cache.')
          return null // Stop retrying
        }
        return Math.min(times * 200, 2000)
      },
      maxRetriesPerRequest: 3,
    })
    
    redis.on('connect', () => {
      console.log('✅ Redis connected for YouTube caching')
    })
    
    redis.on('error', (err) => {
      console.warn('⚠️ Redis error (cache disabled):', err.message)
    })
    
    return redis
  } catch (err) {
    console.warn('⚠️ Could not initialize Redis:', err.message)
    return null
  }
}

// Export cache configuration
export { REDIS_KEY_PREFIXES, CACHE_TTL }