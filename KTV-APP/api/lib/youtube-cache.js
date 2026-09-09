// YouTube Cache Module for Vercel Serverless
// Memory-only cache (Redis optional)

const CACHE_DURATION = 5 * 60 * 1000 // 5 minutes
const MAX_CACHE_SIZE = 500
const memoryCache = new Map()
const inFlightRequests = new Map()

// LRU cleanup
const cleanupIfNeeded = () => {
  if (memoryCache.size > MAX_CACHE_SIZE) {
    const keysToDelete = Array.from(memoryCache.keys()).slice(0, 100)
    keysToDelete.forEach(key => memoryCache.delete(key))
  }
}

export const getCached = async (redisClient, prefix, key, ttl, fetchFn) => {
  const fullKey = `${prefix}:${key}`
  
  // 1. Check memory cache
  if (memoryCache.has(fullKey)) {
    const cached = memoryCache.get(fullKey)
    if (Date.now() - cached.timestamp < CACHE_DURATION) {
      return cached.data
    }
    memoryCache.delete(fullKey)
  }
  
  // 2. Check Redis (if available)
  if (redisClient) {
    try {
      const cached = await redisClient.get(fullKey)
      if (cached) {
        const data = JSON.parse(cached)
        memoryCache.set(fullKey, { data, timestamp: Date.now() })
        return data
      }
    } catch (err) {
      console.warn('Redis get error:', err.message)
    }
  }
  
  // 3. In-flight dedup
  if (inFlightRequests.has(fullKey)) {
    return inFlightRequests.get(fullKey)
  }
  
  // 4. Fetch new data
  const fetchPromise = fetchFn()
  inFlightRequests.set(fullKey, fetchPromise)
  
  try {
    const data = await fetchPromise
    
    // Save to memory cache
    memoryCache.set(fullKey, { data, timestamp: Date.now() })
    cleanupIfNeeded()
    
    // Save to Redis (if available)
    if (redisClient) {
      try {
        await redisClient.setex(fullKey, Math.floor(ttl / 1000), JSON.stringify(data))
      } catch (err) {
        console.warn('Redis set error:', err.message)
      }
    }
    
    return data
  } finally {
    inFlightRequests.delete(fullKey)
  }
}

export const createRedisClient = () => {
  // Redis is optional for Vercel
  // Use Upstash Redis for production
  const REDIS_HOST = process.env.REDIS_HOST
  const REDIS_PORT = process.env.REDIS_PORT || 6379
  const REDIS_PASSWORD = process.env.REDIS_PASSWORD
  
  if (!REDIS_HOST) {
    console.log('ℹ️ No Redis configured, using memory cache only')
    return null
  }
  
  try {
    const Redis = require('ioredis')
    const client = new Redis({
      host: REDIS_HOST,
      port: REDIS_PORT,
      password: REDIS_PASSWORD,
      maxRetriesPerRequest: 2,
      enableReadyCheck: false,
      lazyConnect: true
    })
    
    client.on('error', (err) => {
      console.warn('Redis connection error:', err.message)
    })
    
    console.log('✅ Redis client created for caching')
    return client
  } catch (err) {
    console.warn('⚠️ Redis initialization failed:', err.message)
    return null
  }
}

export const REDIS_KEY_PREFIXES = {
  SEARCH: 'yt-search',
  DETAILS: 'yt-details',
  PLAYLIST: 'yt-playlist'
}

export const CACHE_TTL = {
  SEARCH: 5 * 60 * 1000,
  DETAILS: 10 * 60 * 1000,
  PLAYLIST: 5 * 60 * 1000
}