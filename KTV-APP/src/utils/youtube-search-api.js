// YouTube Search API Integration (Cloudflare Workers)
// Documentation: YOUTUBE-API-DOCUMENTATION.md

const API_BASE_URL = 'https://youtube-search-api.trinhhuy12343.workers.dev'
const API_KEY = 'sk_e8ad55ac66e07d016ebf4e086a3bd60fc36334b8b64e7cfe'

// Cache for API responses
const API_CACHE = new Map()
const CACHE_DURATION = 5 * 60 * 1000 // 5 minutes

/**
 * Make authenticated request to YouTube Search API
 * @param {string} endpoint - API endpoint path
 * @param {object} params - Query parameters
 * @returns {Promise<object>} API response data
 */
const makeApiRequest = async (endpoint, params = {}) => {
  // Build URL with parameters
  const url = new URL(`${API_BASE_URL}${endpoint}`)
  
  // Add API key to params
  params.key = API_KEY
  
  // Add all params to URL
  Object.keys(params).forEach(key => {
    if (params[key] !== undefined && params[key] !== null) {
      url.searchParams.append(key, params[key])
    }
  })

  try {
    const response = await fetch(url.toString(), {
      method: 'GET',
      headers: {
        'Accept': 'application/json'
      }
    })

    if (!response.ok) {
      const errorData = await response.json().catch(() => ({}))
      throw new Error(errorData.error?.message || `HTTP ${response.status}`)
    }

    const result = await response.json()
    
    if (!result.success) {
      throw new Error(result.error?.message || 'API request failed')
    }

    return result.data
  } catch (error) {
    console.error(`[YouTube Search API] Error calling ${endpoint}:`, error.message)
    throw error
  }
}

/**
 * Search for videos, channels, or playlists
 * @param {string} query - Search query
 * @param {object} options - Search options
 * @returns {Promise<Array>} Search results
 */
export const searchYouTubeAPI = async (query, options = {}) => {
  const {
    maxResults = 10,
    page = 1,
    sort = 'relevance',
    type = 'video',
    region = 'US',
    language = 'vi'
  } = options

  // Check cache
  const cacheKey = `search:${query}:${maxResults}:${page}:${sort}:${type}`
  if (API_CACHE.has(cacheKey)) {
    const cached = API_CACHE.get(cacheKey)
    if (Date.now() - cached.timestamp < CACHE_DURATION) {
      console.log(`[YouTube Search API] Returning cached results for: ${query}`)
      return cached.data
    }
  }

  try {
    const data = await makeApiRequest('/search', {
      q: query,
      page,
      sort,
      type,
      region,
      language
    })

    // Filter out non-video items (like shelves) and transform results to match existing format
    const results = data.results?.filter(item => item.id && item.type === 'video').map(item => ({
      id: item.id,
      title: item.title,
      description: item.description,
      thumbnail: item.thumbnail || item.thumbnails?.high?.url || item.thumbnails?.default?.url || '',
      channelTitle: item.author || item.channelTitle || '',
      channelId: item.authorId || item.channelId || '',
      publishedAt: item.published || item.publishedAt || '',
      duration: item.duration,
      viewCount: item.viewCount,
      source: 'youtube',
      fromApi: 'cloudflare-api'
    })) || []

    // Cache results
    API_CACHE.set(cacheKey, {
      data: results,
      timestamp: Date.now()
    })

    console.log(`[YouTube Search API] Found ${results.length} results for: ${query}`)
    return results
  } catch (error) {
    console.error('[YouTube Search API] Search failed:', error.message)
    throw error
  }
}

/**
 * Get video details by ID
 * @param {string} videoId - YouTube video ID
 * @returns {Promise<object>} Video details
 */
export const getVideoDetailsAPI = async (videoId) => {
  const cacheKey = `video:${videoId}`
  
  if (API_CACHE.has(cacheKey)) {
    const cached = API_CACHE.get(cacheKey)
    if (Date.now() - cached.timestamp < CACHE_DURATION) {
      return cached.data
    }
  }

  try {
    const data = await makeApiRequest(`/video/${videoId}`)
    
    // Transform to match existing format
    const video = {
      id: videoId,
      title: data.title,
      description: data.description,
      thumbnail: data.thumbnail || (Array.isArray(data.thumbnails) ? (data.thumbnails[data.thumbnails.length - 1]?.url || data.thumbnails[0]?.url || '') : (data.thumbnails?.high?.url || data.thumbnails?.default?.url || '')),
      channelTitle: data.author || data.channelTitle || '',
      channelId: data.authorId || data.channelId || '',
      publishedAt: data.publishDate || data.publishedAt || '',
      duration: data.duration,
      viewCount: data.viewCount,
      likeCount: data.likeCount,
      tags: data.keywords || [],
      source: 'youtube',
      fromApi: 'cloudflare-api'
    }

    API_CACHE.set(cacheKey, {
      data: video,
      timestamp: Date.now()
    })

    return video
  } catch (error) {
    console.error(`[YouTube Search API] Failed to get video details for ${videoId}:`, error.message)
    throw error
  }
}

/**
 * Get channel information
 * @param {string} channelId - Channel ID or handle (@username)
 * @returns {Promise<object>} Channel information
 */
export const getChannelInfoAPI = async (channelId) => {
  const cacheKey = `channel:${channelId}`
  
  if (API_CACHE.has(cacheKey)) {
    const cached = API_CACHE.get(cacheKey)
    if (Date.now() - cached.timestamp < CACHE_DURATION) {
      return cached.data
    }
  }

  try {
    const data = await makeApiRequest(`/channel/${channelId}`)
    
    API_CACHE.set(cacheKey, {
      data,
      timestamp: Date.now()
    })

    return data
  } catch (error) {
    console.error(`[YouTube Search API] Failed to get channel info for ${channelId}:`, error.message)
    throw error
  }
}

/**
 * Get playlist videos
 * @param {string} playlistId - Playlist ID
 * @returns {Promise<Array>} Playlist videos
 */
export const getPlaylistVideosAPI = async (playlistId) => {
  const cacheKey = `playlist:${playlistId}`
  
  if (API_CACHE.has(cacheKey)) {
    const cached = API_CACHE.get(cacheKey)
    if (Date.now() - cached.timestamp < CACHE_DURATION) {
      return cached.data
    }
  }

  try {
    const data = await makeApiRequest(`/playlist/${playlistId}`)
    
    const videos = data.videos?.map(item => ({
      id: item.id,
      title: item.title,
      thumbnail: item.thumbnail || item.thumbnails?.high?.url || item.thumbnails?.default?.url || '',
      channelTitle: item.author || item.channelTitle || '',
      duration: item.duration,
      source: 'youtube',
      fromApi: 'cloudflare-api'
    })) || []

    API_CACHE.set(cacheKey, {
      data: videos,
      timestamp: Date.now()
    })

    return videos
  } catch (error) {
    console.error(`[YouTube Search API] Failed to get playlist ${playlistId}:`, error.message)
    throw error
  }
}

/**
 * Get search suggestions (autocomplete)
 * @param {string} query - Partial query string
 * @returns {Promise<Array>} Suggestion strings
 */
export const getSuggestionsAPI = async (query) => {
  if (!query || query.length < 2) return []

  try {
    const data = await makeApiRequest('/suggest', { q: query })
    return data || []
  } catch (error) {
    console.error('[YouTube Search API] Failed to get suggestions:', error.message)
    return []
  }
}

/**
 * Get trending videos
 * @param {object} options - Trending options
 * @returns {Promise<Array>} Trending videos
 */
export const getTrendingVideosAPI = async (options = {}) => {
  const { category = 'default', region = 'US' } = options
  
  const cacheKey = `trending:${category}:${region}`
  
  if (API_CACHE.has(cacheKey)) {
    const cached = API_CACHE.get(cacheKey)
    if (Date.now() - cached.timestamp < CACHE_DURATION) {
      return cached.data
    }
  }

  try {
    const data = await makeApiRequest('/trending', { category, region })
    
    // In the Worker response, trending returns { category, region, videos: VideoResult[] }
    const trendingList = Array.isArray(data) ? data : (data?.videos || [])
    
    const videos = trendingList.map(item => ({
      id: item.id,
      title: item.title,
      thumbnail: item.thumbnail || item.thumbnails?.high?.url || item.thumbnails?.default?.url || '',
      channelTitle: item.author || item.channelTitle || '',
      viewCount: item.viewCount,
      source: 'youtube',
      fromApi: 'cloudflare-api'
    }))

    API_CACHE.set(cacheKey, {
      data: videos,
      timestamp: Date.now()
    })

    return videos
  } catch (error) {
    console.error('[YouTube Search API] Failed to get trending videos:', error.message)
    throw error
  }
}

/**
 * Get video comments
 * @param {string} videoId - Video ID
 * @param {object} options - Comment options
 * @returns {Promise<Array>} Video comments
 */
export const getVideoCommentsAPI = async (videoId, options = {}) => {
  const { page = 1, sortBy = 'top' } = options

  try {
    const data = await makeApiRequest(`/comments/${videoId}`, { page, sortBy })
    return data || []
  } catch (error) {
    console.error(`[YouTube Search API] Failed to get comments for ${videoId}:`, error.message)
    return []
  }
}

/**
 * Get video captions/transcripts
 * @param {string} videoId - Video ID
 * @param {string} lang - Language code (default: 'en')
 * @returns {Promise<Array>} Video captions
 */
export const getVideoCaptionsAPI = async (videoId, lang = 'vi') => {
  try {
    const data = await makeApiRequest(`/captions/${videoId}`, { lang })
    return data || []
  } catch (error) {
    console.error(`[YouTube Search API] Failed to get captions for ${videoId}:`, error.message)
    return []
  }
}

// Export API info
export const API_INFO = {
  baseUrl: API_BASE_URL,
  hasApiKey: !!API_KEY,
  quotaLimit: 50000, // requests per day
  rateLimit: 100 // requests per minute
}