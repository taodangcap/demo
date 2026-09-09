// YouTube API utility functions - using Backend Proxy with Key Rotation
// All API keys are now hidden on the server for security (Hacker cannot see them in Network tab).
// Client detection logic optimizes request types (Video ID vs Search) but all traffic goes through proxy.

// Import external YouTube Search API as fallback
import { 
  searchYouTubeAPI, 
  getVideoDetailsAPI, 
  getSuggestionsAPI,
  getTrendingVideosAPI,
  API_INFO 
} from './youtube-search-api.js'

// Configuration: Enable external API fallback
const USE_EXTERNAL_API_FALLBACK = true // Set to false to disable Cloudflare Workers API

// Regex for ID/URL detection
const VIDEO_ID_REGEX = /^[a-zA-Z0-9_-]{11}$/
const YOUTUBE_URL_REGEX = /(?:youtube\.com\/(?:[^/]+\/.+\/|(?:v|e(?:mbed)?)\/|.*[?&]v=)|youtu\.be\/)([^"&?/\s]{11})/
const PLAYLIST_URL_REGEX = /[?&]list=([^#&?]+)/
const PLAYLIST_ID_REGEX = /^(PL|RD|UU|LL|FL|MV)[a-zA-Z0-9_-]{10,}$/
const CHANNEL_ID_REGEX = /youtube\.com\/channel\/(UC[a-zA-Z0-9_-]{22})/
const CHANNEL_HANDLE_REGEX = /youtube\.com\/(@[a-zA-Z0-9_.-]+)/

// Helper to get API Base URL for proxy (Returns priority ordered list)
export const getApiBaseServers = () => {
  const servers = []
  
  const host = typeof window !== 'undefined' ? window.location.hostname : 'localhost'
  const isLocal = host === 'localhost' || host === '127.0.0.1'
  const isLan = /^(192\.168\.|10\.|172\.(1[6-9]|2[0-9]|3[0-1])\.)/.test(host)

  // 1. Ưu tiên Local/LAN ở môi trường phát triển (dùng relative path được proxy qua Vite hoặc gọi trực tiếp cổng 3001)
  if (isLocal || isLan) {
    servers.push('') // Relative path: được proxy tới localhost:3001 qua Vite
    servers.push(`http://${host}:3001`)
  }
  
  // 2. Render server (ktv-app.onrender.com)
  const renderServer = 'https://ktv-app.onrender.com'
  servers.push(renderServer)
  
  // 3. Custom remote server từ env
  if (import.meta.env.VITE_REMOTE_SERVER_URL && 
      !import.meta.env.VITE_REMOTE_SERVER_URL.includes('localhost')) {
    const remoteUrl = import.meta.env.VITE_REMOTE_SERVER_URL.replace(/\/$/, '')
    if (remoteUrl !== renderServer && !servers.includes(remoteUrl)) {
      servers.push(remoteUrl)
    }
  }
  
  // 4. Fallback relative path
  if (!servers.includes('')) {
    servers.push('')
  }
  
  console.log('[YouTube API] Server priority:', servers)
  return servers
}

// 0. Generic Fetch with Fallback (No keys sent from frontend for absolute security)
export const fetchWithFallback = async (endpoint, options = {}) => {
  const servers = getApiBaseServers()
  let lastError = null

  for (const server of servers) {
    try {
      let url = `${server}${endpoint.startsWith('/') ? endpoint : '/' + endpoint}`
      
      // Chúng ta KHÔNG CÒN truyền 'key' từ frontend vì lý do bảo mật!
      // Server (server.js) sẽ tự dùng các Key bí mật trong file .env của nó.
      const timestamp = Date.now()
      url += (url.includes('?') ? '&' : '?') + `_t=${timestamp}`

      // console.log(`[YouTube API] Gọi Server: ${server || 'Local Host'}`)

      const response = await fetch(url, {
        ...options,
        headers: {
          'Accept': 'application/json',
          'Cache-Control': 'no-cache',
          ...options.headers
        }
      })

      if (response.ok) {
        return await response.json()
      }

      const status = response.status
      let errorMsg = `HTTP ${status}`
      
      try {
        const errorData = await response.json()
        errorMsg = errorData.error?.message || errorData.error || errorMsg
      } catch {
        // Ignore malformed error payloads and fall back to HTTP status.
      }

      console.warn(`[YouTube API] ${server || 'Local'} lỗi ${status}: ${errorMsg}`)
      lastError = new Error(errorMsg)

      // Nếu hết Quota hoặc bị chặn, thử server dự phòng tiếp theo
      if (status === 403 || status === 429 || status === 404) {
        continue 
      }

      break 
    } catch (e) {
      console.error(`[YouTube API] Lỗi kết nối tới ${server || 'Local'}:`, e.message)
      lastError = e
      continue 
    }
  }

  throw lastError || new Error('Dịch vụ YouTube hiện không khả dụng. Vui lòng thử lại sau.')
}

// 1. Get Playlist Items via Proxy (Cost: 1 unit)
const getPlaylistItemsProxy = async (playlistId, maxResults = 50) => {
  return await fetchWithFallback(`/api/youtube/playlist?playlistId=${playlistId}&maxResults=${maxResults}`)
}

// 2. Get Channel Items via Proxy (Cost: 2 units)
const getChannelItemsProxy = async (identifier, maxResults) => {
  let query = ''
  if (identifier.startsWith('UC') && identifier.length === 24) {
    query = `id=${identifier}`
  } else {
    query = `forHandle=${identifier}`
  }

  try {
    const data = await fetchWithFallback(`/api/youtube/channel?${query}`)
    if (!data || data.length === 0) return []

    const uploadsId = data[0].contentDetails?.relatedPlaylists?.uploads
    if (uploadsId) {
      return await getPlaylistItemsProxy(uploadsId, maxResults)
    }
  } catch (error) {
    console.error('Error fetching channel items:', error)
  }
  return []
}

// 3. Get Video Details via Proxy with Snippet (Cost: 1 unit)
const getVideoDetailsProxy = async (videoId) => {
  return await fetchWithFallback(`/api/youtube/details?ids=${videoId}&part=snippet,contentDetails,statistics,status`)
}

// Cache object to save quota
const SEARCH_CACHE = new Map()
const CACHE_DURATION = 10 * 60 * 1000 // 10 minutes

export const searchVideos = async (query, maxResults = 10) => {
  // 0. Check Client-side Cache First (local optimization)
  const cacheKey = `${query}_${maxResults}`
  if (SEARCH_CACHE.has(cacheKey)) {
    const cached = SEARCH_CACHE.get(cacheKey)
    if (Date.now() - cached.timestamp < CACHE_DURATION) {
      console.log(`[YouTube API] Returning client-cached results for: ${query}`)
      return cached.data
    }
  }

  // --- OPTIMIZATION START ---
  try {
    const channelIdMatch = query.match(CHANNEL_ID_REGEX)
    if (channelIdMatch && channelIdMatch[1]) {
      return await getChannelItemsProxy(channelIdMatch[1], maxResults)
    }

    const channelHandleMatch = query.match(CHANNEL_HANDLE_REGEX)
    if (channelHandleMatch && channelHandleMatch[1]) {
      return await getChannelItemsProxy(channelHandleMatch[1], maxResults)
    }

    const playlistMatch = query.match(PLAYLIST_URL_REGEX)
    if (playlistMatch && playlistMatch[1]) {
      return await getPlaylistItemsProxy(playlistMatch[1], maxResults)
    }

    if (PLAYLIST_ID_REGEX.test(query)) {
      return await getPlaylistItemsProxy(query, maxResults)
    }

    let videoId = null
    if (VIDEO_ID_REGEX.test(query)) {
      videoId = query
    } else {
      const urlMatch = query.match(YOUTUBE_URL_REGEX)
      if (urlMatch && urlMatch[1]) {
        videoId = urlMatch[1]
      }
    }

    if (videoId) {
      const details = await getVideoDetailsProxy(videoId)
      if (details && details.length > 0) {
        return details.map(d => ({
          ...d,
          id: d.id
        }))
      }
    }
  } catch (optError) {
    console.warn('Optimization failed, falling back to search:', optError)
  }

  // Final Action: Search via Server with Caching + YouTubei Fallback
  // Server now handles: Redis caching (5 min), in-flight dedup, and automatic fallback to YouTubei
  try {
    console.log(`[YouTube API] Searching for: "${query}" (server-side caching + YouTubei fallback enabled)`)
    const results = await fetchWithFallback(`/api/youtube/search-full?keyword=${encodeURIComponent(query)}&maxResults=${maxResults}`)

    // Save to client-side cache as backup
    if (results && results.length > 0) {
      SEARCH_CACHE.set(cacheKey, { data: results, timestamp: Date.now() })
      
      // Log source info if available
      const sources = results.map(r => r.fromApi || 'unknown').join(', ')
      console.log(`[YouTube API] Results from: ${sources} (${results.length} videos)`)
    }

    return results
  } catch (error) {
    console.error('Error in searchVideos (internal servers):', error)
    
    // Try external Cloudflare Workers API as final fallback
    if (USE_EXTERNAL_API_FALLBACK) {
      try {
        console.log(`[YouTube API] Trying external API fallback for: "${query}"`)
        const externalResults = await searchYouTubeAPI(query, { 
          maxResults, 
          language: 'vi',
          region: 'VN' 
        })
        
        if (externalResults && externalResults.length > 0) {
          console.log(`[YouTube API] External API returned ${externalResults.length} results`)
          SEARCH_CACHE.set(cacheKey, { data: externalResults, timestamp: Date.now() })
          return externalResults
        }
      } catch (externalError) {
        console.error('External API also failed:', externalError)
      }
    }
    
    throw error
  }
}

export const getVideoDetails = async (videoIds) => {
  if (!videoIds || videoIds.length === 0) return []

  try {
    const ids = Array.isArray(videoIds) ? videoIds.join(',') : videoIds
    return await fetchWithFallback(`/api/youtube/details?ids=${encodeURIComponent(ids)}&part=snippet,contentDetails,statistics,status`)
  } catch (error) {
    console.error('Error fetching video details:', error)
    return []
  }
}

export const formatDuration = (duration) => {
  if (!duration) return '0:00'

  // Parse ISO 8601 duration (PT3M30S format)
  const match = duration.match(/PT(\d+H)?(\d+M)?(\d+S)?/)
  if (!match) return '0:00'

  const hours = (match[1] || '').replace('H', '')
  const minutes = (match[2] || '').replace('M', '')
  const seconds = (match[3] || '').replace('S', '')

  if (hours) {
    return `${hours}:${minutes.padStart(2, '0')}:${seconds.padStart(2, '0')}`
  }
  return `${minutes || '0'}:${seconds.padStart(2, '0')}`
}

export const formatViewCount = (count) => {
  const num = parseInt(count)
  if (isNaN(num)) return '0'

  if (num >= 1000000000) {
    return `${(num / 1000000000).toFixed(1)}B`
  }
  if (num >= 1000000) {
    return `${(num / 1000000).toFixed(1)}M`
  }
  if (num >= 1000) {
    return `${(num / 1000).toFixed(1)}K`
  }
  return num.toString()
}

// Helper kiểm tra video có phát được không (cho YouTube)
export const isPlayableVideo = (video) => {
  if (!video) return false
  if (video.source !== 'youtube') return true

  // 1. Kiểm tra tính năng nhúng (phải cho phép nhúng)
  if (video.status && video.status.embeddable === false) {
    console.log(`Video ${video.id} bị chặn: Không cho phép nhúng (Embed Disabled)`)
    return false
  }

  // 2. Kiểm tra giới hạn vùng lãnh thổ
  if (video.contentDetails && video.contentDetails.regionRestriction) {
    const { blocked } = video.contentDetails.regionRestriction

    // Chúng ta cho phép hiển thị vì đã có cơ chế "Direct Stream Proxy" tự động vượt chặn
    if (blocked && blocked.some(code => code.toUpperCase() === 'VN')) {
      console.log(`Video ${video.id} bị chặn tại VN. Sẽ tự động dùng luồng dự phòng nếu cần.`)
      return true
    }
  }

  // 3. Kiểm tra trạng thái tải lên và quyền riêng tư
  if (video.status) {
    if (video.status.uploadStatus === 'rejected' || video.status.uploadStatus === 'failed') {
      console.log(`Video ${video.id} bị lỗi bản quyền hoặc lỗi tải lên.`)
      return false
    }
    if (video.status.privacyStatus === 'private') {
      return false
    }
  }

  return true
}

// Helper chuyển đổi thời lượng ISO 8601 sang giây
export const parseISO8601Duration = (duration) => {
  if (!duration) return 0
  
  const match = duration.match(/PT(\d+H)?(\d+M)?(\d+S)?/)
  if (!match) return 0
  
  const hours = parseInt((match[1] || '0').replace('H', '')) || 0
  const minutes = parseInt((match[2] || '0').replace('M', '')) || 0
  const seconds = parseInt((match[3] || '0').replace('S', '')) || 0
  
  return (hours * 3600) + (minutes * 60) + seconds
}

// Fetch suggestions từ server (trending + related)
export const fetchTrendingSuggestions = async () => {
  try {
    const response = await fetch('/api/youtube/trending-suggestions')
    if (!response.ok) throw new Error('Failed to fetch suggestions')
    return await response.json()
  } catch (error) {
    console.error('Error fetching trending suggestions from internal servers:', error)
    
    // Fallback to external API
    if (USE_EXTERNAL_API_FALLBACK) {
      try {
        return await getTrendingVideosAPI({ category: 'default', region: 'VN' })
      } catch (externalError) {
        console.error('External API trending also failed:', externalError)
      }
    }
    
    return []
  }
}

// Fetch search suggestions dựa trên keyword
export const fetchSearchSuggestions = async (keyword) => {
  const query = keyword?.trim()
  if (!query || query.length < 2) return []

  const normalizeSuggestions = (data) => {
    if (!Array.isArray(data)) return []

    const seen = new Set()
    return data
      .map(item => typeof item === 'string' ? item : item?.title)
      .map(title => title?.trim())
      .filter(Boolean)
      .filter(title => {
        const key = title.toLocaleLowerCase('vi')
        if (seen.has(key)) return false
        seen.add(key)
        return true
      })
      .slice(0, 10)
      .map(title => ({ title }))
  }
  
  // Try internal servers first
  try {
    const data = await fetchWithFallback(`/api/youtube/search-suggestions?q=${encodeURIComponent(query)}`)
    return normalizeSuggestions(data)
  } catch (error) {
    console.error('Error fetching search suggestions from internal servers:', error)
    
    // Fallback to external API
    if (USE_EXTERNAL_API_FALLBACK) {
      try {
        return normalizeSuggestions(await getSuggestionsAPI(query))
      } catch (externalError) {
        console.error('External API suggestions also failed:', externalError)
      }
    }
    
    return []
  }
}

// Get related videos dựa trên video ID
export const fetchRelatedVideos = async (videoId, maxResults = 5) => {
  if (!videoId) return []
  try {
    const response = await fetch(`/api/youtube/related-videos?videoId=${videoId}&maxResults=${maxResults}`)
    if (!response.ok) throw new Error('Failed to fetch related videos')
    return await response.json()
  } catch (error) {
    console.error('Error fetching related videos:', error)
    return []
  }
}
