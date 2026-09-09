// Backend Proxy Server for SoundCloud API & YouTube API with Key Rotation
// Now with WebSocket support for real-time remote control (inspired by vkara)

import express from 'express'
import cors from 'cors'
import dotenv from 'dotenv'
import fs from 'fs'
import path from 'path'
import { fileURLToPath } from 'url'
import http from 'http'
import { WebSocketServer, WebSocket } from 'ws'
import { createRedisClient, getCached, REDIS_KEY_PREFIXES, CACHE_TTL } from './server-youtube-cache.js'
import { searchYoutubei, getVideoDetailsYoutubei, getPlaylistYoutubei } from './server-youtubei.js'

dotenv.config()

let redisClient = null
try {
  redisClient = createRedisClient()
} catch (err) {
  console.warn('⚠️ Redis initialization failed, continuing without cache')
}

const app = express()
const server = http.createServer(app)
// Support both PORT (host) and PROXY_PORT (local)
const PORT = process.env.PORT || process.env.PROXY_PORT || 3001

const __filename = fileURLToPath(import.meta.url)
const __dirname = path.dirname(__filename)
const EFFECTS_PATH = path.join(__dirname, 'data', 'effects.json')

// --- Security Middleware ---
const ALLOWED_ORIGINS = [
  'http://localhost:3000',
  'http://localhost:5173',
  'http://www.trinhquanghuy.net',
  'https://trinhquanghuy.net',
  'https://www.trinhquanghuy.net',
  'https://ktv-app.onrender.com',
  'https://huy.sale',
  'https://www.huy.sale',
]

const authorizeProxy = (req, res, next) => {
  const rawOrigin = req.headers.origin || req.headers.referer || ''

  // Allow requests with no origin (server-to-server, curl, Postman, internal)
  if (!rawOrigin) return next()

  // Normalize: strip trailing slash and path from referer
  let origin = rawOrigin
  let hostname = ''
  try {
    const parsed = new URL(rawOrigin)
    origin = `${parsed.protocol}//${parsed.host}` // just scheme + host
    hostname = parsed.hostname
  } catch (_) {
    // rawOrigin is not a full URL, use as-is
  }

  const isAllowedDomain = ALLOWED_ORIGINS.some(ao => origin.toLowerCase() === ao.toLowerCase())
  const isNgrok = hostname && (hostname.endsWith('.ngrok-free.app') || hostname.endsWith('.ngrok.io'))
  const isLan = hostname === 'localhost' ||
                hostname === 'www.localhost' ||
                hostname.endsWith('.localhost') ||
                hostname === '127.0.0.1' ||
                /^192\.168\.\d{1,3}\.\d{1,3}$/.test(hostname) ||
                /^10\.\d{1,3}\.\d{1,3}\.\d{1,3}$/.test(hostname) ||
                /^172\.(1[6-9]|2\d|3[0-1])\.\d{1,3}\.\d{1,3}$/.test(hostname)

  const isAllowed = isAllowedDomain || isNgrok || isLan

  if (!isAllowed) {
    console.warn(`[Security] Blocked unauthorized request from: "${origin}" (raw: "${rawOrigin}")`)
    return res.status(403).json({ error: 'Unauthorized origin' })
  }

  next()
}

// Enable CORS and Security
app.use(cors())
app.use(express.json({ limit: '50mb' }))
app.use(express.urlencoded({ limit: '50mb', extended: true }))
app.use('/api/youtube', authorizeProxy) // Protect all YouTube routes

// --- API Key Management ---
const getApiKeys = () => {
  const keys = [
    process.env.VITE_YOUTUBE_API_KEY,
    process.env.VITE_YOUTUBE_API_KEY_2,
    process.env.VITE_YOUTUBE_API_KEY_3,
    process.env.VITE_YOUTUBE_API_KEY_4,
    process.env.VITE_YOUTUBE_API_KEY_5
  ].filter(k => k && k.trim() !== '')

  if (keys.length === 0) {
    console.warn('⚠️ No YouTube API keys found in environment variables!')
  }
  return keys
}

const apiKeys = getApiKeys()
let currentKeyIndex = 0

const getNextKey = () => {
  if (apiKeys.length === 0) return null

  // Move to next key
  currentKeyIndex = (currentKeyIndex + 1) % apiKeys.length
  console.log(`🔄 Switching to API Key #${currentKeyIndex + 1}`)
  return apiKeys[currentKeyIndex]
}

const getCurrentKey = () => {
  if (apiKeys.length === 0) return null
  return apiKeys[currentKeyIndex]
}

// Helper to execute YouTube API request with fallback/rotation
const executeYouTubeRequest = async (baseUrl, params) => {
  if (apiKeys.length === 0) {
    throw new Error('No API keys configured')
  }

  let attempts = 0
  const maxAttempts = apiKeys.length

  while (attempts < maxAttempts) {
    const apiKey = getCurrentKey()
    const url = `${baseUrl}?${new URLSearchParams({ ...params, key: apiKey }).toString()}`

    try {
      const response = await fetch(url)

      if (response.ok) {
        return await response.json()
      }

      // Check for Quota Exceeded (403) or Rate Limit (429)
      if (response.status === 403 || response.status === 429) {
        const errorData = await response.json().catch(() => ({}))
        const reason = errorData.error?.errors?.[0]?.reason || ''
        const message = errorData.error?.message || ''

        console.warn(`⚠️ API Key #${currentKeyIndex + 1} failed: ${response.status} - ${reason} (${message})`)

        // Rotate for any 403 (quota/access/limit) or 429 (rate limit)
        // This ensures if one key is blocked or out of quota, we try others
        getNextKey()
        attempts++
        continue // Retry loop
      }

      // Return error for other status codes
      const errorText = await response.text()
      throw new Error(`YouTube API error ${response.status}: ${errorText}`)

    } catch (error) {
      // Network errors (fetch failed) should probably not rotate keys unless we want to try connection again?
      // For now, re-throw unless it's the specific fetch loop control
      throw error
    }
  }

  throw new Error('All API keys exhausted or failed.')
}

// --- Rate Limiting ---
const rateLimitStore = new Map() // { ip: { count: number, resetTime: timestamp } }

const rateLimit = (maxRequests = 30, windowMs = 60000) => {
  return (req, res, next) => {
    const ip = req.ip || req.connection.remoteAddress
    const now = Date.now()
    let entry = rateLimitStore.get(ip)

    if (!entry || now > entry.resetTime) {
      entry = { count: 0, resetTime: now + windowMs }
      rateLimitStore.set(ip, entry)
    }

    if (entry.count >= maxRequests) {
      return res.status(429).json({
        error: 'Too many requests',
        message: `Rate limit exceeded. Try again in ${Math.ceil((entry.resetTime - now) / 1000)} seconds.`
      })
    }

    entry.count++
    next()
  }
}

// Clean up old rate limit entries
setInterval(() => {
  const now = Date.now()
  for (const [ip, entry] of rateLimitStore.entries()) {
    if (now > entry.resetTime + 60000) rateLimitStore.delete(ip)
  }
}, 5 * 60 * 1000)

// --- Remote Control Store ---
const sessions = {}

// Room code -> session token mapping (vkara-style 4-digit code)
const roomCodeMap = {} // { '1234': 'ABC123' }

// Clean up old sessions
setInterval(() => {
  const now = Date.now()
  Object.keys(sessions).forEach(token => {
    if (sessions[token] && now - sessions[token].lastActive > 24 * 60 * 60 * 1000) {
      // Also clean up room code mapping
      const code = sessions[token].roomCode
      if (code && roomCodeMap[code] === token) delete roomCodeMap[code]
      delete sessions[token]
    }
  })
}, 60 * 60 * 1000)

// 1. Register/Keep-alive session (with optional room code and password)
app.post('/api/remote/session', (req, res) => {
  const { token, roomCode, password } = req.body
  if (!token) return res.status(400).json({ error: 'Token is required' })

  if (!sessions[token]) {
    sessions[token] = {
      commands: [],
      lastActive: Date.now(),
      remoteLastActive: 0,
      roomCode: roomCode || null,
      password: (typeof password === 'string' && password.trim().length > 0) ? password.trim() : null
    }
  } else {
    sessions[token].lastActive = Date.now()
    // Update room code if provided (player reconnecting with new code)
    if (roomCode) sessions[token].roomCode = roomCode
    if (typeof password === 'string' && password.trim().length > 0) {
      sessions[token].password = password.trim()
    }
  }

  // Register room code mapping for easy join
  if (roomCode) {
    // Clean any old mapping for this token
    Object.keys(roomCodeMap).forEach(c => { if (roomCodeMap[c] === token) delete roomCodeMap[c] })
    roomCodeMap[roomCode] = token
  }

  const remoteConnected = sessions[token].remoteLastActive && (Date.now() - sessions[token].remoteLastActive < 10000)
  res.json({
    status: 'ok',
    connected: true,
    remoteConnected,
    roomCode: sessions[token].roomCode,
    requiresPassword: !!(sessions[token].password && String(sessions[token].password).trim().length > 0)
  })
})

// 1.1 Verify session password
app.post('/api/remote/verify', (req, res) => {
  const { token, password } = req.body
  if (!token) return res.status(400).json({ error: 'Token is required' })
  const session = sessions[token]
  if (!session) return res.status(404).json({ error: 'Session not found' })

  if (!session.password || String(session.password).trim().length === 0) {
    return res.json({ status: 'ok', verified: true })
  }

  if (String(session.password).trim() === String(password || '').trim()) {
    return res.json({ status: 'ok', verified: true })
  }

  return res.status(401).json({ status: 'error', verified: false, message: 'Mật khẩu session không chính xác' })
})

// 1.2 Lookup session token by room code (vkara-style - join with 4-digit code)
app.get('/api/remote/room/:code', (req, res) => {
  const code = req.params.code
  const token = roomCodeMap[code]
  if (!token || !sessions[token]) {
    return res.status(404).json({ error: 'Room not found', code })
  }
  // Mark remote as active
  sessions[token].remoteLastActive = Date.now()
  res.json({
    status: 'ok',
    token,
    roomCode: code,
    requiresPassword: !!(sessions[token].password && String(sessions[token].password).trim().length > 0)
  })
})

// 1.5. Update Player State
app.post('/api/remote/state', (req, res) => {
  const { token, state } = req.body
  if (!sessions[token]) return res.status(404).json({ error: 'Session not found' })

  sessions[token].state = state
  sessions[token].lastActive = Date.now()
  res.json({ status: 'ok' })
})

// 2. Check session status
app.get('/api/remote/session/:token', (req, res) => {
  const { token } = req.params
  if (sessions[token]) {
    sessions[token].lastActive = Date.now()
    sessions[token].remoteLastActive = Date.now()
    res.json({
      status: 'ok',
      connected: true,
      requiresPassword: !!(sessions[token].password && String(sessions[token].password).trim().length > 0),
      state: sessions[token].state || {}
    })
  } else {
    res.status(404).json({ status: 'error', connected: false, message: 'Session not found' })
  }
})

// 3. Send command
app.post('/api/remote/command', (req, res) => {
  const { token, command, data, password } = req.body
  if (!sessions[token]) return res.status(404).json({ error: 'Session not found' })

  // Verify password if protected
  if (sessions[token].password && String(sessions[token].password).trim().length > 0) {
    const authPwd = password || req.headers['x-session-password'] || data?.password || data?.pwd
    if (String(sessions[token].password).trim() !== String(authPwd || '').trim()) {
      return res.status(401).json({ error: 'Yêu cầu mật khẩu để thực hiện lệnh điều khiển' })
    }
  }

  const commandData = {
    id: Date.now() + Math.random(),
    command,
    data,
    timestamp: Date.now()
  }

  sessions[token].commands.push(commandData)
  sessions[token].lastActive = Date.now()

  if (sessions[token].waitingResponses && sessions[token].waitingResponses.length > 0) {
    const responses = sessions[token].waitingResponses
    sessions[token].waitingResponses = []
    const commandsToSend = sessions[token].commands
    sessions[token].commands = []

    responses.forEach(w => {
      const isRemoteConnected = sessions[token] && sessions[token].remoteLastActive && (Date.now() - sessions[token].remoteLastActive < 10000)
      w.send(commandsToSend, !!isRemoteConnected)
    })
  }

  if (sessions[token].commands.length > 100) sessions[token].commands = sessions[token].commands.slice(-50)
  res.json({ status: 'ok', commandId: commandData.id })
})

// 4. Get commands (Long Polling)
app.get('/api/remote/commands/:token', (req, res) => {
  const { token } = req.params
  if (!sessions[token]) return res.status(404).json({ error: 'Session not found' })

  const pendingCommands = sessions[token].commands
  const remoteConnected = sessions[token].remoteLastActive && (Date.now() - sessions[token].remoteLastActive < 10000)

  if (pendingCommands.length > 0) {
    sessions[token].commands = []
    sessions[token].lastActive = Date.now()
    return res.json({ commands: pendingCommands, remoteConnected: !!remoteConnected })
  }

  if (!sessions[token].waitingResponses) sessions[token].waitingResponses = []

  const sendLongPollResponse = (cmds, currentRemoteStatus) => {
    try {
      const isRemoteConnected = currentRemoteStatus !== undefined ? currentRemoteStatus :
        (sessions[token] && sessions[token].remoteLastActive && (Date.now() - sessions[token].remoteLastActive < 10000))
      res.json({ commands: cmds, remoteConnected: !!isRemoteConnected })
    } catch (e) {
      // console.error('Error in long polling response:', e)
    }
  }

  sessions[token].waitingResponses.push({ res, send: sendLongPollResponse })

  setTimeout(() => {
    if (sessions[token] && sessions[token].waitingResponses) {
      const index = sessions[token].waitingResponses.findIndex(w => w.res === res)
      if (index !== -1) {
        sessions[token].waitingResponses.splice(index, 1)
        sendLongPollResponse([])
      }
    }
  }, 30000)
})

// --- SoundCloud Proxy ---
app.get('/api/soundcloud/search', async (req, res) => {
  try {
    const { q, limit = 10, client_id } = req.query
    if (!q || !client_id) return res.status(400).json({ error: 'Missing q or client_id' })

    const apiUrl = `https://api-v2.soundcloud.com/search/tracks?q=${encodeURIComponent(q)}&limit=${limit}&client_id=${client_id}`
    // console.log(`SoundCloud Search: ${q}`)

    const response = await fetch(apiUrl, {
      headers: {
        'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36'
      }
    })

    if (!response.ok) throw new Error(`SoundCloud API: ${response.status}`)
    const data = await response.json()

    const tracks = (data.collection || []).map(track => ({
      id: track.id?.toString() || track.permalink_url || `sc_${Date.now()}_${Math.random()}`,
      title: track.title || 'Untitled',
      description: track.description || '',
      thumbnail: track.artwork_url || track.user?.avatar_url || '',
      channelTitle: track.user?.username || 'Unknown Artist',
      duration: track.duration || 0,
      viewCount: track.playback_count || 0,
      publishedAt: track.created_at || new Date().toISOString(),
      source: 'soundcloud',
      streamUrl: track.stream_url,
      permalinkUrl: track.permalink_url
    }))

    res.json(tracks)
  } catch (error) {
    console.error('SoundCloud error:', error)
    res.status(500).json({ error: error.message })
  }
})

// --- YouTube Proxy with Key Rotation ---

// 1. Search
app.get('/api/youtube/search', async (req, res) => {
  try {
    const { keyword, maxResults = 15 } = req.query
    if (!keyword) return res.status(400).json({ error: 'Keyword required' })

    // console.log(`YouTube Search: ${keyword}`)

    const data = await executeYouTubeRequest(
      'https://www.googleapis.com/youtube/v3/search',
      {
        part: 'snippet',
        type: 'video',
        q: keyword,
        maxResults
      }
    )

    const videos = (data.items || []).map(item => ({
      id: item.id.videoId,
      title: item.snippet.title,
      description: item.snippet.description,
      thumbnail: item.snippet.thumbnails?.high?.url || item.snippet.thumbnails?.default?.url,
      channelTitle: item.snippet.channelTitle,
      publishedAt: item.snippet.publishedAt
    }))

    res.json(videos)
  } catch (error) {
    console.error('YouTube Search Failed:', error.message)
    res.status(500).json({ error: error.message })
  }
})

// 2. Video Details (Expanded to support 'part' param for optimization)
app.get('/api/youtube/details', async (req, res) => {
  try {
    const { ids, part } = req.query
    if (!ids) return res.status(400).json({ error: 'Video IDs required' })

    // Allow client to request specific parts, default to contentDetails,statistics
    // Client optimization will request 'snippet,contentDetails,statistics'
    const partParam = part || 'contentDetails,statistics'

    // console.log(`YouTube Details: ${ids} (part: ${partParam})`)

    const data = await executeYouTubeRequest(
      'https://www.googleapis.com/youtube/v3/videos',
      {
        part: partParam,
        id: ids
      }
    )

    const videos = (data.items || []).map(item => {
      // Base object
      const result = {
        id: item.id,
        duration: item.contentDetails?.duration,
        viewCount: item.statistics?.viewCount,
        contentDetails: item.contentDetails, // Include full contentDetails for restriction checks
        status: item.status // Include status for embeddable check
      }

      // If snippet was requested, add it (for Search by ID optimization)
      if (item.snippet) {
        result.title = item.snippet.title
        result.description = item.snippet.description
        result.thumbnail = item.snippet.thumbnails?.high?.url || item.snippet.thumbnails?.default?.url
        result.channelTitle = item.snippet.channelTitle
        result.publishedAt = item.snippet.publishedAt
      }

      return result
    })

    res.json(videos)
  } catch (error) {
    console.error('YouTube Details Failed:', error.message)
    res.status(500).json({ error: error.message })
  }
})

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

// 2.5 Unified Search & Details (Best for UX - combines search and details metadata)
// WITH CACHING + IN-FLIGHT DEDUP + YouTubei FALLBACK + Cloudflare Worker Fallback
app.get('/api/youtube/search-full', async (req, res) => {
  try {
    const { keyword, maxResults = 10 } = req.query
    if (!keyword) return res.status(400).json({ error: 'Keyword required' })

    // Use caching + in-flight dedup from vkara's strategy
    const cacheKey = `${keyword}:${maxResults}`

    const results = await getCached(
      redisClient,
      REDIS_KEY_PREFIXES.SEARCH,
      cacheKey,
      CACHE_TTL.SEARCH,
      async () => {
        try {
          // Try official YouTube Data API v3 first
          const searchData = await executeYouTubeRequest(
            'https://www.googleapis.com/youtube/v3/search',
            {
              part: 'snippet',
              type: 'video',
              q: keyword,
              maxResults
            }
          )

          const videoItems = searchData.items || []
          if (videoItems.length === 0) return []

          const videoIds = videoItems.map(item => item.id.videoId).join(',')

          const detailsData = await executeYouTubeRequest(
            'https://www.googleapis.com/youtube/v3/videos',
            {
              part: 'snippet,contentDetails,statistics,status',
              id: videoIds
            }
          )

          return (detailsData.items || []).map(item => ({
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
        } catch (err) {
          // If official API fails, fallback to YouTubei first
          console.warn(`⚠️ YouTube Data API failed (${err.message}). Falling back to YouTubei...`)
          try {
            const youtubeiResults = await searchYoutubei(keyword, maxResults)
            return youtubeiResults.map(v => ({
              ...v,
              fromApi: 'youtubei',
              source: 'youtube'
            }))
          } catch (youtubeiErr) {
            console.error('YouTubei fallback also failed:', youtubeiErr.message)
            // If YouTubei also fails, fallback to Cloudflare Worker search API
            try {
              return await searchCloudflareWorker(keyword, maxResults)
            } catch (workerErr) {
              console.error('Cloudflare Worker API fallback also failed:', workerErr.message)
              throw new Error(`Search failed: API v3 (${err.message}) + YouTubei (${youtubeiErr.message}) + Worker (${workerErr.message})`)
            }
          }
        }
      }
    )

    res.json(results)
  } catch (error) {
    console.error('YouTube Search Full Failed:', error.message)
    res.status(500).json({ error: error.message })
  }
})

// 2.6 Free Search Fallback (Using Invidious instances)
app.get('/api/youtube/search-free', async (req, res) => {
  try {
    const { keyword, maxResults = 15 } = req.query
    if (!keyword) return res.status(400).json({ error: 'Keyword required' })

    const instances = [
      'https://yewtu.be',
      'https://iv.melmac.space',
      'https://invidious.snopyta.org'
    ]

    for (const instance of instances) {
      try {
        // console.log(`Trying Free Search: ${instance} with query: ${keyword}`)
        const response = await fetch(`${instance}/api/v1/search?q=${encodeURIComponent(keyword)}&page=1&limit=${maxResults}&region=VN`)

        if (response.ok) {
          const data = await response.json()
          const results = (data || []).map(item => ({
            id: item.videoId,
            title: item.title,
            thumbnail: item.videoThumbnails?.find(t => t.quality === 'high')?.url || item.videoThumbnails?.[0]?.url,
            channelTitle: item.author,
            duration: item.duration,
            viewCount: item.viewCount,
            source: 'youtube',
            isFreeSource: true
          }))
          return res.json(results)
        }
      } catch (err) {
        console.warn(`Free Search instance ${instance} failed...`)
      }
    }
    res.status(404).json({ error: 'Could not find a working free search source' })
  } catch (error) {
    res.status(500).json({ error: error.message })
  }
})

// 3. Playlist Items (New Endpoint for Optimization)
app.get('/api/youtube/playlist', async (req, res) => {
  try {
    const { playlistId, maxResults = 25 } = req.query
    if (!playlistId) return res.status(400).json({ error: 'Playlist ID required' })

    // console.log(`YouTube Playlist: ${playlistId}`)

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
        publishedAt: item.snippet.publishedAt
      }))

      res.json(videos)
    } catch (err) {
      // Bất kỳ lỗi nào từ API v3 (bao gồm lỗi 403, 429, hoặc 400 do không hỗ trợ playlist Mix RD...) đều tự động thử fallback sang YouTubei
      console.warn(`⚠️ YouTube Data API playlist fetch failed (${err.message}). Trying fallback to YouTubei...`)
      try {
        const youtubeiResults = await getPlaylistYoutubei(playlistId, maxResults)
        return res.json(youtubeiResults.map(v => ({
          ...v,
          fromApi: 'youtubei',
          source: 'youtube'
        })))
      } catch (youtubeiErr) {
        console.error('YouTubei playlist fallback failed:', youtubeiErr.message)
        throw err // Trả về lỗi gốc của API v3 nếu cả YouTubei cũng thất bại
      }
    }
  } catch (error) {
    console.error('YouTube Playlist Failed:', error.message)
    res.status(500).json({ error: error.message })
  }
})

// 4. Channel Details (New Endpoint for Optimization)
app.get('/api/youtube/channel', async (req, res) => {
  try {
    const { id, forHandle } = req.query

    let params = { part: 'contentDetails' }
    if (id) params.id = id
    else if (forHandle) params.forHandle = forHandle
    else return res.status(400).json({ error: 'Channel ID or Handle required' })

    // console.log(`YouTube Channel: ${id || forHandle}`)

    const data = await executeYouTubeRequest(
      'https://www.googleapis.com/youtube/v3/channels',
      params
    )

    res.json(data.items || [])
  } catch (error) {
    console.error('YouTube Channel Failed:', error.message)
    res.status(500).json({ error: error.message })
  }
})

// 5. Get Direct Stream URL (To bypass geoblocking)
app.get('/api/youtube/stream/:id', async (req, res) => {
  const videoId = req.params.id
  // Danh sách các Invidious API để lấy stream
  const instances = [
    'https://yewtu.be',
    'https://iv.melmac.space',
    'https://invidious.snopyta.org',
    'https://invidious.rocks'
  ]

  for (const instance of instances) {
    try {
      const response = await fetch(`${instance}/api/v1/videos/${videoId}`)
      if (response.ok) {
        const data = await response.json()
        if (data.formatStreams && data.formatStreams.length > 0) {
          // Lấy video có chất lượng tốt nhất hoặc mp4
          const stream = data.formatStreams.find(s => s.container === 'mp4') || data.formatStreams[0]
          return res.json({
            url: stream.url,
            title: data.title,
            instance: instance
          })
        }
      }
    } catch (err) {
      console.warn(`Instance ${instance} failed, trying next...`)
    }
  }
  res.status(404).json({ error: 'Could not find a working stream' })
})

// Contact Form Rate Limiter (Max 3 messages per 24 hours per IP)
const contactRateLimitStore = new Map()

const contactRateLimit = (req, res, next) => {
  const ip = req.ip || req.connection.remoteAddress
  const now = Date.now()
  const windowMs = 24 * 60 * 60 * 1000 // 24 hours
  const maxRequests = 3

  let entry = contactRateLimitStore.get(ip)

  if (!entry || now > entry.resetTime) {
    entry = { count: 0, resetTime: now + windowMs }
    contactRateLimitStore.set(ip, entry)
  }

  if (entry.count >= maxRequests) {
    const timeLeftMs = entry.resetTime - now
    const hoursLeft = Math.ceil(timeLeftMs / (1000 * 60 * 60))
    return res.status(429).json({
      error: `Bạn đã gửi quá 3 tin nhắn trong ngày hôm nay. Vui lòng quay lại sau ${hoursLeft} giờ nữa!`
    })
  }

  entry.count++
  next()
}

// Clean up old contact rate limit entries periodically
setInterval(() => {
  const now = Date.now()
  for (const [ip, entry] of contactRateLimitStore.entries()) {
    if (now > entry.resetTime) contactRateLimitStore.delete(ip)
  }
}, 60 * 60 * 1000) // Every hour

// Contact Form Proxy to Discord Webhook (Secured)
app.post('/api/contact', contactRateLimit, async (req, res) => {
  try {
    const { name, email, msg } = req.body
    if (!name || !email || !msg) {
      return res.status(400).json({ error: 'Thiếu thông tin bắt buộc' })
    }

    const webhookUrl = process.env.DISCORD_WEBHOOK_URL
    if (!webhookUrl) {
      console.error('❌ DISCORD_WEBHOOK_URL is not configured in .env')
      return res.status(500).json({ error: 'Server configuration error' })
    }

    // Format Embed Discord
    const discordPayload = {
      username: 'Portfolio Contact Bot',
      avatar_url: 'https://www.trinhquanghuy.net/logo.png',
      embeds: [
        {
          title: '📩 Lời nhắn mới từ Portfolio!',
          color: 16020150, // Pink (#f472b6)
          fields: [
            {
              name: '👤 Người gửi',
              value: name,
              inline: true
            },
            {
              name: '✉️ Email',
              value: email,
              inline: true
            },
            {
              name: '📝 Lời nhắn',
              value: msg
            }
          ],
          timestamp: new Date().toISOString()
        }
      ]
    }

    const response = await fetch(webhookUrl, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json'
      },
      body: JSON.stringify(discordPayload)
    })

    if (!response.ok) {
      const errText = await response.text()
      throw new Error(`Discord API error ${response.status}: ${errText}`)
    }

    res.json({ status: 'ok', message: 'Gửi tin nhắn thành công!' })
  } catch (error) {
    console.error('Failed to send contact message to Discord:', error)
    res.status(500).json({ error: 'Failed to send message' })
  }
})

// Health check
app.get('/health', (req, res) => res.json({ status: 'ok', service: 'KTV Proxy Server', websocket: true }))

// --- Sound Effects Shared Storage ---
app.get('/api/effects', (req, res) => {
  try {
    if (!fs.existsSync(EFFECTS_PATH)) {
      return res.json({})
    }
    const data = fs.readFileSync(EFFECTS_PATH, 'utf8')
    res.json(JSON.parse(data))
  } catch (error) {
    console.error('Failed to read effects:', error)
    res.status(500).json({ error: 'Failed to read effects' })
  }
})

app.post('/api/effects', (req, res) => {
  try {
    const effects = req.body

    if (!effects) {
      console.error('--- [Effects API] Lỗi: Không có dữ liệu gửi lên! ---');
      return res.status(400).json({ error: 'Effects data required' })
    }

    // Ensure data directory exists
    const dataDir = path.dirname(EFFECTS_PATH)
    if (!fs.existsSync(dataDir)) {
      fs.mkdirSync(dataDir, { recursive: true })
    }

    fs.writeFileSync(EFFECTS_PATH, JSON.stringify(effects, null, 2), 'utf8')
    res.json({ status: 'ok' })
  } catch (error) {
    console.error('--- [Effects API] Lỗi khi lưu file:', error);
    res.status(500).json({ error: 'Failed to save effects' })
  }
})

// ============================================================
// WEBSOCKET SERVER - Real-time Remote Control (vkara-inspired)
// ============================================================
const wss = new WebSocketServer({ server, path: '/ws' })

// Track WebSocket connections per session
const wsClients = new Map() // sessionToken -> Set<{ws, role, lastPing}>

const broadcastToSession = (token, message, excludeWs = null) => {
  const clients = wsClients.get(token)
  if (!clients) return

  const payload = JSON.stringify(message)
  for (const client of clients) {
    if (client.ws !== excludeWs && client.ws.readyState === WebSocket.OPEN) {
      try {
        client.ws.send(payload)
      } catch (e) {
        // Client disconnected
      }
    }
  }
}

const getSessionInfo = (token) => {
  const clients = wsClients.get(token)
  if (!clients || clients.size === 0) return null

  let hasPlayer = false
  let hasRemote = false
  for (const client of clients) {
    if (client.role === 'player') hasPlayer = true
    if (client.role === 'remote') hasRemote = true
  }

  return { hasPlayer, hasRemote, clientCount: clients.size }
}

wss.on('connection', (ws, req) => {
  let sessionToken = null
  let clientRole = null
  let clientAuthenticated = false
  let heartbeatInterval = null
  let isAlive = true

  // Heartbeat to detect dead connections
  const startHeartbeat = () => {
    heartbeatInterval = setInterval(() => {
      if (!isAlive) {
        ws.terminate()
        return
      }
      isAlive = false
      ws.ping()
    }, 30000) // Ping every 30s
  }

  ws.on('pong', () => { isAlive = true })

  ws.on('message', (raw) => {
    try {
      const message = JSON.parse(raw.toString())

      switch (message.type) {
        case 'join': {
          // Client joins a session (player or remote)
          sessionToken = message.token
          clientRole = message.role || 'remote' // 'player' or 'remote'

          if (!sessionToken) {
            ws.send(JSON.stringify({ type: 'error', message: 'Token required' }))
            return
          }

          // Authenticate client
          const joinPwd = message.password || message.pwd
          const currentSession = sessions[sessionToken]
          if (clientRole === 'player' || !currentSession?.password || String(currentSession.password).trim().length === 0) {
            clientAuthenticated = true
          } else if (joinPwd && String(joinPwd).trim() === String(currentSession.password).trim()) {
            clientAuthenticated = true
          } else {
            clientAuthenticated = false
          }

          // Register this connection
          if (!wsClients.has(sessionToken)) {
            wsClients.set(sessionToken, new Set())
          }
          wsClients.get(sessionToken).add({ ws, role: clientRole, lastPing: Date.now() })

          // Also register in the HTTP sessions for backward compatibility
          if (!sessions[sessionToken]) {
            sessions[sessionToken] = { commands: [], lastActive: Date.now(), remoteLastActive: 0 }
          }
          sessions[sessionToken].lastActive = Date.now()
          if (clientRole === 'remote') {
            sessions[sessionToken].remoteLastActive = Date.now()
          }

          startHeartbeat()

          // Send join confirmation with session info
          const info = getSessionInfo(sessionToken)
          ws.send(JSON.stringify({
            type: 'joined',
            token: sessionToken,
            role: clientRole,
            authenticated: clientAuthenticated,
            requiresPassword: !!(currentSession?.password && String(currentSession.password).trim().length > 0),
            ...info
          }))

          // Notify others that a new client connected
          broadcastToSession(sessionToken, {
            type: 'peer_joined',
            role: clientRole,
            ...info
          }, ws)

          // If state exists (from HTTP), send it to the new remote
          if (clientRole === 'remote' && sessions[sessionToken]?.state) {
            ws.send(JSON.stringify({
              type: 'state_sync',
              state: sessions[sessionToken].state
            }))
          }
          break
        }

        case 'auth': {
          const authPwd = message.password || message.pwd
          const currentSession = sessionToken ? sessions[sessionToken] : null
          if (!currentSession?.password || String(currentSession.password).trim() === String(authPwd || '').trim()) {
            clientAuthenticated = true
            ws.send(JSON.stringify({ type: 'auth_success', token: sessionToken }))
          } else {
            clientAuthenticated = false
            ws.send(JSON.stringify({ type: 'auth_failed', error: 'Mật khẩu không chính xác' }))
          }
          break
        }

        case 'command': {
          // Remote sends a command to the player
          if (!sessionToken) return

          const session = sessions[sessionToken]
          if (session?.password && String(session.password).trim().length > 0 && !clientAuthenticated) {
            const cmdPwd = message.password || message.data?.password || message.data?.pwd
            if (cmdPwd && String(cmdPwd).trim() === String(session.password).trim()) {
              clientAuthenticated = true
            } else {
              ws.send(JSON.stringify({ type: 'error', error: 'unauthorized', message: 'Yêu cầu mật khẩu để điều khiển' }))
              return
            }
          }

          const commandData = {
            id: Date.now() + Math.random(),
            command: message.command,
            data: message.data || {},
            timestamp: Date.now()
          }

          sessions[sessionToken] && (sessions[sessionToken].lastActive = Date.now())

          // Broadcast command directly to all player clients in the session
          broadcastToSession(sessionToken, {
            type: 'command',
            ...commandData
          }, ws)

          // Also store for HTTP polling fallback
          if (sessions[sessionToken]) {
            sessions[sessionToken].commands.push(commandData)
            if (sessions[sessionToken].commands.length > 100) {
              sessions[sessionToken].commands = sessions[sessionToken].commands.slice(-50)
            }

            // Resolve any waiting HTTP long-poll responses
            if (sessions[sessionToken].waitingResponses?.length > 0) {
              const responses = sessions[sessionToken].waitingResponses
              sessions[sessionToken].waitingResponses = []
              const commandsToSend = sessions[sessionToken].commands
              sessions[sessionToken].commands = []
              responses.forEach(w => w.send(commandsToSend, true))
            }
          }

          // ACK back to sender
          ws.send(JSON.stringify({ type: 'ack', id: commandData.id }))
          break
        }

        case 'state_sync': {
          // Player syncs state to remotes
          if (!sessionToken) return

          // Save for HTTP fallback
          if (sessions[sessionToken]) {
            sessions[sessionToken].state = message.state
            sessions[sessionToken].lastActive = Date.now()
          }

          // Broadcast state to all remote clients
          broadcastToSession(sessionToken, {
            type: 'state_sync',
            state: message.state
          }, ws)
          break
        }

        case 'ping': {
          ws.send(JSON.stringify({ type: 'pong', timestamp: Date.now() }))
          isAlive = true
          break
        }

        default:
          // Forward unknown messages to session peers
          if (sessionToken) {
            broadcastToSession(sessionToken, message, ws)
          }
      }
    } catch (e) {
      console.error('[WS] Error processing message:', e.message)
    }
  })

  ws.on('close', () => {
    if (heartbeatInterval) clearInterval(heartbeatInterval)

    // Remove from session clients
    if (sessionToken && wsClients.has(sessionToken)) {
      const clients = wsClients.get(sessionToken)
      for (const client of clients) {
        if (client.ws === ws) {
          clients.delete(client)
          break
        }
      }

      // Notify remaining clients
      const info = getSessionInfo(sessionToken)
      if (info) {
        broadcastToSession(sessionToken, {
          type: 'peer_left',
          role: clientRole,
          ...info
        })
      }

      // Cleanup empty sessions
      if (clients.size === 0) {
        wsClients.delete(sessionToken)
      }
    }
  })

  ws.on('error', (err) => {
    console.error('[WS] Connection error:', err.message)
  })
})

// Cleanup stale WebSocket sessions periodically
setInterval(() => {
  for (const [token, clients] of wsClients.entries()) {
    for (const client of clients) {
      if (client.ws.readyState !== WebSocket.OPEN) {
        clients.delete(client)
      }
    }
    if (clients.size === 0) {
      wsClients.delete(token)
    }
  }
}, 60000)

/**
 * Test-only endpoint: call YouTubei directly for diagnostics.
 * Example: GET /api/youtube/test-youtubei?q=karaoke&maxResults=5
 * This bypasses the Data API flow and returns YouTubei results.
 */
app.get('/api/youtube/test-youtubei', async (req, res) => {
  try {
    const { q, maxResults = 5 } = req.query
    if (!q) return res.status(400).json({ error: 'Query param "q" is required' })

    // Call youtubei directly (no caching, diagnostic only)
    const results = await searchYoutubei(q, Number(maxResults))
    res.json({ source: 'youtubei', items: results })
  } catch (err) {
    console.error('Test YouTubei failed:', err)
    res.status(500).json({ error: err.message || 'YouTubei test failed' })
  }
})

// Trending suggestions - Cached trending videos (1 hour TTL)
app.get('/api/youtube/trending-suggestions', async (req, res) => {
  try {
    const { maxResults = 10 } = req.query
    const cacheKey = 'trending:videos:vn:official'

    const cached = await getCached(
      redisClient,
      REDIS_KEY_PREFIXES.TRENDING,
      cacheKey,
      3600000, // 1 hour TTL
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

// Search suggestions - Auto-complete based on keyword with Acronym Translation (vkara-style)
const ACRONYMS_MAP = {
  'hnkm': 'hoa nở không màu karaoke',
  'actdm': 'ai chung tình được mãi karaoke',
  'vlmb': 'vùng lá me bay karaoke',
  'dp': 'duyên phận karaoke',
  'sg': 'sóng gió karaoke',
  'bp': 'bạc phận karaoke',
  'hn': 'hồng nhan karaoke',
  'egm': 'em gái mưa karaoke',
  'psmcg': 'phía sau một cô gái karaoke',
  'lt': 'lạc trôi karaoke',
  'ctcht': 'chúng ta của hiện tại karaoke',
  'nnca': 'nơi này có anh karaoke',
  'cmnq': 'cơn mưa ngang qua karaoke',
  'cctvlc': 'có chàng trai viết lên cây karaoke',
  'stc': 'sau tất cả karaoke',
  'xtllndce': 'tháng tư là lời nói dối của em karaoke',
  'cha': 'chiều hôm ấy karaoke',
  'bca': 'buồn của anh karaoke',
  'slca': 'sai lầm của anh karaoke',
  'atn': 'anh thanh niên karaoke',
  'gvh': 'gió vẫn hát karaoke',
  'dtkdn': 'độ ta không độ nàng karaoke',
  'xnkkv': 'xuân này con không về karaoke',
  'tc': 'tình cha karaoke',
  'lm': 'lòng mẹ karaoke',
  'lkn': 'liên khúc nghèo karaoke',
  'lkmt': 'liên khúc mưa tình karaoke',
  'hcy': 'hơn cả yêu karaoke',
  'ndt': 'ngày đầu tiên karaoke',
  'gnkol': 'gặp nhưng không ở lại karaoke',
  'chtt': 'chạy về khóc với anh karaoke',
  'dd': 'đế vương karaoke',
  'tlt': 'từng yêu karaoke',
  'dnlim': 'đau nhất là lặng im karaoke'
}

app.get('/api/youtube/search-suggestions', async (req, res) => {
  try {
    const { q } = req.query
    if (!q || q.length < 2) {
      return res.json([])
    }

    let query = q.trim().toLowerCase()
    // 1. Dịch từ viết tắt tiếng Việt nếu có trong map
    if (ACRONYMS_MAP[query]) {
      query = ACRONYMS_MAP[query]
      console.log(`[Suggestions] Acronym match! Translated "${q}" to "${query}"`)
    }

    const cacheKey = `suggest:${query.replace(/\s+/g, '_')}`

    const suggestions = await getCached(
      redisClient,
      REDIS_KEY_PREFIXES.SEARCH,
      cacheKey,
      300000, // 5 minute TTL
      async () => {
        console.log(`[Suggestions] Fetching suggestions from Google Autocomplete for: "${query}"...`)
        try {
          // Sử dụng API autocomplete chính thức của YouTube thông qua Google (y hệt vkara)
          const url = `https://suggestqueries.google.com/complete/search?client=firefox&ds=yt&q=${encodeURIComponent(query)}`
          const response = await fetch(url, {
            headers: {
              'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36'
            }
          })

          if (response.ok) {
            const data = await response.json()
            const googleSuggestions = data[1] || []
            console.log(`✅ Google Autocomplete suggestions count: ${googleSuggestions.length}`)
            return googleSuggestions.map(item => ({ title: item }))
          }
          throw new Error(`Google Autocomplete failed with HTTP status ${response.status}`)
        } catch (err) {
          console.warn('⚠️ Google Autocomplete failed, falling back to YouTubei search...', err.message)
          try {
            // Fallback to YouTubei search titles
            const results = await searchYoutubei(query, 8)
            if (results && results.length > 0) {
              return results.map(v => ({ title: v.title }))
            }
          } catch (fallbackErr) {
            console.error('All suggestions options failed:', fallbackErr.message)
          }
          return []
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
app.get('/api/youtube/related-videos', async (req, res) => {
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
      600000, // 10 minute TTL
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

server.listen(PORT, () => {
  console.log(`🚀 Proxy Server running on port ${PORT}`)
  console.log(`🔌 WebSocket Server active at ws://localhost:${PORT}/ws`)
  console.log(`🔑 YouTube Key Rotation: Active (${apiKeys.length} keys loaded)`)
})
