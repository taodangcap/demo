// Vercel Serverless Function Handler
// Inline version - no external imports needed

import express from 'express'
import cors from 'cors'
import dotenv from 'dotenv'

// --- YouTubei Module (Inline) ---
import { Client } from 'youtubei'

let youtubeiClient = null

async function getYoutubeiClient() {
  if (!youtubeiClient) {
    try {
      youtubeiClient = new Client()
      console.log('✅ YouTubei client initialized')
    } catch (err) {
      console.error('❌ Failed to initialize YouTubei:', err.message)
      throw err
    }
  }
  return youtubeiClient
}

async function searchYoutubei(query, maxResults = 15) {
  try {
    const client = await getYoutubeiClient()
    const searchResults = await client.search(query, { type: 'video' })
    const items = searchResults.items || searchResults
    
    return items
      .slice(0, maxResults)
      .map(video => ({
        id: video.id,
        title: video.title || 'Untitled',
        description: video.description || '',
        thumbnail: video.thumbnails?.[0]?.url || video.bestThumbnail?.url || '',
        channelTitle: video.channel?.name || video.author?.name || 'Unknown',
        publishedAt: video.uploadDate || '',
        duration: formatDuration(video.duration) || '',
        viewCount: typeof video.viewCount === 'number' ? video.viewCount : 0,
        source: 'youtube',
        fromApi: 'youtubei'
      }))
  } catch (err) {
    console.error('YouTubei search failed:', err.message)
    throw err
  }
}

function formatDuration(seconds) {
  if (!seconds || seconds === 0) return '0:00'
  const hrs = Math.floor(seconds / 3600)
  const mins = Math.floor((seconds % 3600) / 60)
  const secs = seconds % 60
  if (hrs > 0) return `${hrs}:${String(mins).padStart(2, '0')}:${String(secs).padStart(2, '0')}`
  return `${mins}:${String(secs).padStart(2, '0')}`
}

async function getPlaylistYoutubei(playlistId, maxResults = 50) {
  try {
    const client = await getYoutubeiClient()
    const playlist = await client.getPlaylist(playlistId)
    const items = playlist.videos?.items || playlist.videos || []
    
    return items
      .slice(0, maxResults)
      .map(video => ({
        id: video.id,
        title: video.title || 'Untitled',
        description: video.description || '',
        thumbnail: video.thumbnails?.[0]?.url || video.bestThumbnail?.url || '',
        channelTitle: video.channel?.name || 'Unknown',
        publishedAt: video.uploadDate || '',
        source: 'youtube',
        fromApi: 'youtubei'
      }))
  } catch (err) {
    console.error('YouTubei playlist failed:', err.message)
    throw err
  }
}

// --- Cache Module (Inline) ---
const CACHE_DURATION = 5 * 60 * 1000 // 5 minutes
const MAX_CACHE_SIZE = 500
const memoryCache = new Map()
const inFlightRequests = new Map()

function cleanupIfNeeded() {
  if (memoryCache.size > MAX_CACHE_SIZE) {
    const keysToDelete = Array.from(memoryCache.keys()).slice(0, 100)
    keysToDelete.forEach(key => memoryCache.delete(key))
  }
}

async function getCached(redisClient, prefix, key, ttl, fetchFn) {
  const fullKey = `${prefix}:${key}`
  
  // Check memory cache
  if (memoryCache.has(fullKey)) {
    const cached = memoryCache.get(fullKey)
    if (Date.now() - cached.timestamp < CACHE_DURATION) {
      return cached.data
    }
    memoryCache.delete(fullKey)
  }
  
  // Check Redis (if available)
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
  
  // In-flight dedup
  if (inFlightRequests.has(fullKey)) {
    return inFlightRequests.get(fullKey)
  }
  
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

function createRedisClient() {
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

const REDIS_KEY_PREFIXES = {
  SEARCH: 'yt-search',
  DETAILS: 'yt-details',
  PLAYLIST: 'yt-playlist'
}

const CACHE_TTL = {
  SEARCH: 5 * 60 * 1000,
  DETAILS: 10 * 60 * 1000,
  PLAYLIST: 5 * 60 * 1000
}

// --- Main App ---
dotenv.config()

let redisClient = null
try {
  redisClient = createRedisClient()
  console.log('✅ Redis connected for YouTube caching')
} catch (err) {
  console.warn('⚠️ Redis initialization failed, continuing with memory cache only')
}

const app = express()

const ALLOWED_ORIGINS = [
  'http://localhost:3000',
  'http://localhost:5173',
  'https://trinhquanghuy.net',
  'https://www.trinhquanghuy.net',
  'https://ktv-app.onrender.com',
  'https://huy.sale',
  'https://www.huy.sale'
]

const authorizeProxy = (req, res, next) => {
  const rawOrigin = req.headers.origin || req.headers.referer || ''
  if (!rawOrigin) return next()
  
  let origin = rawOrigin
  let hostname = ''
  try {
    const parsed = new URL(rawOrigin)
    origin = `${parsed.protocol}//${parsed.host}`
    hostname = parsed.hostname
  } catch (_) {}
  
  const isAllowedDomain = ALLOWED_ORIGINS.some(ao => origin.toLowerCase() === ao.toLowerCase())
  const isNgrok = hostname && (hostname.endsWith('.ngrok-free.app') || hostname.endsWith('.ngrok.io'))
  const isLan = hostname === 'localhost' ||
                hostname === '127.0.0.1' ||
                /^192\.168\.\d{1,3}\.\d{1,3}$/.test(hostname) ||
                /^10\.\d{1,3}\.\d{1,3}\.\d{1,3}$/.test(hostname) ||
                /^172\.(1[6-9]|2\d|3[0-1])\.\d{1,3}\.\d{1,3}$/.test(hostname)

  const isAllowed = isAllowedDomain || isNgrok || isLan

  if (!isAllowed) {
    console.warn(`[Security] Blocked: "${origin}"`)
    return res.status(403).json({ error: 'Unauthorized origin' })
  }
  next()
}

app.use(cors())
app.use(express.json({ limit: '50mb' }))
app.use(express.urlencoded({ limit: '50mb', extended: true }))
app.use('/youtube', authorizeProxy)

const getApiKeys = () => {
  const keys = [
    process.env.VITE_YOUTUBE_API_KEY,
    process.env.VITE_YOUTUBE_API_KEY_2,
    process.env.VITE_YOUTUBE_API_KEY_3,
    process.env.VITE_YOUTUBE_API_KEY_4,
    process.env.VITE_YOUTUBE_API_KEY_5
  ].filter(k => k && k.trim() !== '')
  return keys
}

const apiKeys = getApiKeys()
let currentKeyIndex = 0

const getCurrentKey = () => apiKeys.length > 0 ? apiKeys[currentKeyIndex] : null
const getNextKey = () => {
  if (apiKeys.length === 0) return null
  currentKeyIndex = (currentKeyIndex + 1) % apiKeys.length
  return apiKeys[currentKeyIndex]
}

const executeYouTubeRequest = async (baseUrl, params) => {
  if (apiKeys.length === 0) throw new Error('No API keys configured')
  
  let attempts = 0
  const maxAttempts = apiKeys.length
  
  while (attempts < maxAttempts) {
    const apiKey = getCurrentKey()
    const url = `${baseUrl}?${new URLSearchParams({ ...params, key: apiKey }).toString()}`
    
    try {
      const response = await fetch(url)
      if (response.ok) return await response.json()
      
      if (response.status === 403 || response.status === 429) {
        console.warn(`⚠️ Quota exceeded, rotating key...`)
        getNextKey()
        attempts++
        continue
      }
      
      const errorText = await response.text()
      throw new Error(`YouTube API error ${response.status}: ${errorText}`)
    } catch (error) {
      throw error
    }
  }
  
  throw new Error('All API keys exhausted')
}

// Helper to search using Cloudflare Worker API as fallback
const searchCloudflareWorker = async (keyword, maxResults = 10) => {
  const workerApiKey = process.env.YOUTUBE_WORKER_API_KEY || 'sk_e8ad55ac66e07d016ebf4e086a3bd60fc36334b8b64e7cfe'
  const workerUrl = `https://youtube-search-api.trinhhuy12343.workers.dev/search?q=${encodeURIComponent(keyword)}&key=${workerApiKey}&maxResults=${maxResults}&language=vi&region=VN`

  console.log(`[YouTube Search] Calling Cloudflare Worker API fallback for: "${keyword}"`)
  
  const response = await fetch(workerUrl)
  if (!response.ok) {
    throw new Error(`Cloudflare Worker HTTP ${response.status}`)
  }

  const json = await response.json()
  if (!json.success || !json.data || !json.data.results) {
    throw new Error(json.error?.message || 'Invalid Cloudflare Worker response')
  }

  // Filter out non-video items (like shelves) and map to standard format
  return (json.data.results || [])
    .filter(item => item.id && item.type === 'video')
    .map(item => ({
      id: item.id,
      title: item.title,
      description: item.description || '',
      thumbnail: item.thumbnail || '',
      channelTitle: item.author || item.channelTitle || '',
      channelId: item.authorId || item.channelId || '',
      publishedAt: item.published || item.publishedAt || '',
      duration: item.duration || '',
      viewCount: item.viewCount || '0',
      contentDetails: {
        duration: item.duration
      },
      status: {
        embeddable: true
      },
      source: 'youtube',
      fromApi: 'cloudflare-api'
    }))
}

app.get('/youtube/search-full', async (req, res) => {
  try {
    const { keyword, maxResults = 10 } = req.query
    if (!keyword) return res.status(400).json({ error: 'Keyword required' })
    
    const cacheKey = `${keyword}:${maxResults}`
    
    const results = await getCached(
      redisClient,
      REDIS_KEY_PREFIXES.SEARCH,
      cacheKey,
      CACHE_TTL.SEARCH,
      async () => {
        // Try YouTubei first (unlimited, no quota)
        try {
          console.log(`[YouTube Search] Trying YouTubei first (unlimited)...`)
          const youtubeiResults = await searchYoutubei(keyword, maxResults)
          
          if (youtubeiResults && youtubeiResults.length > 0) {
            console.log(`✅ YouTubei success: ${youtubeiResults.length} results`)
            return youtubeiResults.map(v => ({ ...v, fromApi: 'youtubei' }))
          }
          
          throw new Error('YouTubei returned no results')
        } catch (youtubeiErr) {
          // Fallback to YouTube API if YouTubei fails
          console.warn(`⚠️ YouTubei failed: ${youtubeiErr.message}`)
          console.log(`[YouTube Search] Fallback to YouTube API (with quota)...`)
          
          try {
            const searchData = await executeYouTubeRequest(
              'https://www.googleapis.com/youtube/v3/search',
              {
                part: 'snippet',
                type: 'video',
                q: keyword,
                maxResults
              }
            )
            
            const videoIds = (searchData.items || []).map(item => item.id.videoId).join(',')
            if (!videoIds) {
              throw new Error('YouTube API returned no video IDs')
            }
            
            const detailsData = await executeYouTubeRequest(
              'https://www.googleapis.com/youtube/v3/videos',
              {
                part: 'snippet,contentDetails,statistics,status',
                id: videoIds
              }
            )
            
            const officialResults = (detailsData.items || []).map(item => ({
              id: item.id,
              title: item.snippet.title,
              description: item.snippet.description,
              thumbnail: item.snippet.thumbnails?.high?.url || item.snippet.thumbnails?.default?.url,
              channelTitle: item.snippet.channelTitle,
              publishedAt: item.snippet.publishedAt,
              duration: item.contentDetails?.duration,
              viewCount: item.statistics?.viewCount,
              contentDetails: item.contentDetails,
              status: item.status,
              source: 'youtube',
              fromApi: 'official'
            }))
            
            console.log(`✅ YouTube API success: ${officialResults.length} results`)
            return officialResults
          } catch (apiErr) {
            console.warn(`⚠️ Official YouTube API failed: ${apiErr.message}`)
            console.log(`[YouTube Search] Fallback to Cloudflare Worker API...`)
            try {
              return await searchCloudflareWorker(keyword, maxResults)
            } catch (workerErr) {
              console.error('❌ All search layers failed (YouTubei, API v3, and Cloudflare Worker)')
              throw new Error(`Search failed: YouTubei (${youtubeiErr.message}) + API (${apiErr.message}) + Worker (${workerErr.message})`)
            }
          }
        }
      }
    )
    
    res.json(results)
  } catch (error) {
    console.error('Search failed:', error.message)
    res.status(500).json({ error: error.message })
  }
})

app.get('/youtube/playlist', async (req, res) => {
  try {
    const { playlistId, maxResults = 25 } = req.query
    if (!playlistId) return res.status(400).json({ error: 'Playlist ID required' })
    
    const cacheKey = `${playlistId}:${maxResults}`
    
    const results = await getCached(
      redisClient,
      REDIS_KEY_PREFIXES.PLAYLIST,
      cacheKey,
      CACHE_TTL.PLAYLIST,
      async () => {
        try {
          const data = await executeYouTubeRequest(
            'https://www.googleapis.com/youtube/v3/playlistItems',
            {
              part: 'snippet',
              playlistId: playlistId,
              maxResults: maxResults
            }
          )

          const videos = (data.items || []).map(item => ({
            id: item.snippet.resourceId.videoId,
            title: item.snippet.title,
            description: item.snippet.description,
            thumbnail: item.snippet.thumbnails?.high?.url || item.snippet.thumbnails?.default?.url,
            channelTitle: item.snippet.videoOwnerChannelTitle || item.snippet.channelTitle,
            publishedAt: item.snippet.publishedAt,
            source: 'youtube',
            fromApi: 'official'
          }))

          return videos
        } catch (err) {
          console.warn(`⚠️ YouTube Data API playlist fetch failed (${err.message}). Trying fallback to YouTubei...`)
          try {
            const youtubeiResults = await getPlaylistYoutubei(playlistId, maxResults)
            return youtubeiResults.map(v => ({
              ...v,
              fromApi: 'youtubei',
              source: 'youtube'
            }))
          } catch (youtubeiErr) {
            console.error('YouTubei playlist fallback failed:', youtubeiErr.message)
            throw err
          }
        }
      }
    )
    
    res.json(results)
  } catch (error) {
    console.error('Playlist failed:', error.message)
    res.status(500).json({ error: error.message })
  }
})

app.get('/youtube/test-youtubei', async (req, res) => {
  try {
    const { q, maxResults = 5 } = req.query
    if (!q) return res.status(400).json({ error: 'Query param "q" is required' })
    
    const results = await searchYoutubei(q, Number(maxResults))
    res.json({ source: 'youtubei', items: results })
  } catch (err) {
    console.error('Test YouTubei failed:', err)
    res.status(500).json({ error: err.message || 'YouTubei test failed' })
  }
})

// Trending suggestions - Cached trending videos (1 hour TTL)
app.get('/youtube/trending-suggestions', async (req, res) => {
  try {
    const { maxResults = 10 } = req.query
    const cacheKey = 'trending:videos:vn:official'
    
    // Try cache first
    const cached = await getCached(
      redisClient,
      REDIS_KEY_PREFIXES.TRENDING,
      cacheKey,
      3600, // 1 hour TTL
      async () => {
        console.log('[Trending] Fetching trending videos for VN...')
        try {
          // Try YouTube API v3 first for official trending list (mostPopular in VN)
          console.log('[Trending] Trying YouTube API v3 chart: mostPopular...')
          const trendingData = await executeYouTubeRequest(
            'https://www.googleapis.com/youtube/v3/videos',
            {
              part: 'snippet,statistics,contentDetails',
              chart: 'mostPopular',
              regionCode: 'VN',
              maxResults: Number(maxResults),
              videoCategoryId: '10' // Music category
            }
          )
          if (trendingData && trendingData.items && trendingData.items.length > 0) {
            console.log(`✅ Trending from YouTube API v3 (VN): ${trendingData.items.length} videos`)
            return trendingData.items.map(item => ({
              id: item.id,
              title: item.snippet.title,
              channel: item.snippet.channelTitle,
              duration: item.contentDetails?.duration,
              thumbnail: item.snippet.thumbnails?.high?.url || item.snippet.thumbnails?.default?.url,
              views: item.statistics?.viewCount || '0',
              fromApi: 'youtube'
            }))
          }
          throw new Error('YouTube API v3 returned empty items')
        } catch (err) {
          console.warn('⚠️ YouTube API v3 trending failed, fallback to searchYoutubei:', err.message)
          // Fallback to YouTubei search
          const results = await searchYoutubei('nhạc trẻ hot hiện nay', Number(maxResults))
          if (results && results.length > 0) {
            console.log(`✅ Trending fallback from YouTubei: ${results.length} videos`)
            return results.slice(0, Number(maxResults))
          }
          throw new Error('Both YouTube API v3 trending and YouTubei fallback failed')
        }
      }
    )
    
    res.json(cached)
  } catch (error) {
    console.error('Trending suggestions error:', error.message)
    res.status(500).json({ error: error.message })
  }
})

// Search suggestions - Auto-complete based on keyword
app.get('/youtube/search-suggestions', async (req, res) => {
  try {
    const { q } = req.query
    if (!q || q.length < 2) {
      return res.json([])
    }
    
    const cacheKey = `suggest:${q.toLowerCase()}`
    
    const suggestions = await getCached(
      redisClient,
      REDIS_KEY_PREFIXES.SEARCH,
      cacheKey,
      300, // 5 minute TTL
      async () => {
        console.log(`[Suggestions] Finding suggestions for: "${q}"...`)
        try {
          // Use YouTubei for suggestions (faster)
          const results = await searchYoutubei(q, 8)
          if (results && results.length > 0) {
            console.log(`✅ Suggestions from YouTubei: ${results.length}`)
            return results.map(v => ({
              title: v.title,
              channel: v.channel,
              thumbnail: v.thumbnail,
              id: v.id
            }))
          }
          throw new Error('YouTubei suggestions failed')
        } catch (err) {
          console.warn('⚠️ YouTubei suggestions failed, trying YouTube API')
          // Fallback to YouTube API
          const searchData = await executeYouTubeRequest(
            'https://www.googleapis.com/youtube/v3/search',
            {
              part: 'snippet',
              type: 'video',
              q,
              maxResults: 8
            }
          )
          
          const videoIds = (searchData.items || []).map(item => item.id.videoId).filter(Boolean)
          if (videoIds.length === 0) return []
          
          const details = await executeYouTubeRequest(
            'https://www.googleapis.com/youtube/v3/videos',
            {
              part: 'snippet',
              id: videoIds.join(','),
              maxResults: 8
            }
          )
          
          return (details.items || []).map(v => ({
            title: v.snippet.title,
            channel: v.snippet.channelTitle,
            thumbnail: v.snippet.thumbnails?.high?.url || v.snippet.thumbnails?.default?.url,
            id: v.id
          }))
        }
      }
    )
    
    res.json(suggestions)
  } catch (error) {
    console.error('Search suggestions error:', error.message)
    res.status(500).json({ error: error.message })
  }
})

// Related videos - Get videos related to a specific video
app.get('/youtube/related-videos', async (req, res) => {
  try {
    const { videoId, maxResults = 5 } = req.query
    if (!videoId) {
      return res.status(400).json({ error: 'videoId param required' })
    }
    
    const cacheKey = `related:${videoId}:${maxResults}`
    
    const related = await getCached(
      redisClient,
      REDIS_KEY_PREFIXES.SEARCH,
      cacheKey,
      600, // 10 minute TTL
      async () => {
        console.log(`[Related] Finding related videos for: ${videoId}...`)
        try {
          // Get the video details first to know the title/channel
          const videoDetails = await executeYouTubeRequest(
            'https://www.googleapis.com/youtube/v3/videos',
            {
              part: 'snippet',
              id: videoId,
              maxResults: 1
            }
          )
          
          if (!videoDetails.items || videoDetails.items.length === 0) {
            throw new Error('Video not found')
          }
          
          const videoTitle = videoDetails.items[0].snippet.title
          
          // Search for similar videos using the title as keyword
          const searchData = await executeYouTubeRequest(
            'https://www.googleapis.com/youtube/v3/search',
            {
              part: 'snippet',
              type: 'video',
              q: videoTitle,
              maxResults: Number(maxResults),
              relatedToVideoId: videoId
            }
          )
          
          const videoIds = (searchData.items || []).map(item => item.id.videoId).filter(Boolean)
          if (videoIds.length === 0) return []
          
          const details = await executeYouTubeRequest(
            'https://www.googleapis.com/youtube/v3/videos',
            {
              part: 'snippet,contentDetails,statistics',
              id: videoIds.join(','),
              maxResults: Number(maxResults)
            }
          )
          
          return (details.items || []).map(v => ({
            id: v.id,
            title: v.snippet.title,
            channel: v.snippet.channelTitle,
            duration: v.contentDetails?.duration,
            thumbnail: v.snippet.thumbnails?.high?.url || v.snippet.thumbnails?.default?.url,
            views: v.statistics?.viewCount || '0',
            fromApi: 'youtube'
          }))
        } catch (err) {
          console.error('Error fetching related videos:', err.message)
          return []
        }
      }
    )
    
    res.json(related)
  } catch (error) {
    console.error('Related videos error:', error.message)
    res.status(500).json({ error: error.message })
  }
})

app.get('/health', (req, res) => res.json({ status: 'ok', service: 'KTV API Server' }))

export default app