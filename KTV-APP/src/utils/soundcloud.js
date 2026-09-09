// SoundCloud API utility functions
// Uses backend proxy server to bypass CORS restrictions

const SOUNDCLOUD_CLIENT_ID = import.meta.env.VITE_SOUNDCLOUD_CLIENT_ID || ''

// Helper to get API Base URL
// Helper to get API Base URL for SoundCloud proxy
const getApiBaseServers = () => {
  const servers = []
  // Sử dụng server SoundCloud chuyên dụng
  if (import.meta.env.VITE_PROXY_SERVER_URL) {
    servers.push(import.meta.env.VITE_PROXY_SERVER_URL.replace(/\/$/, ''))
  }
  // Render có thể làm dự phòng nếu bạn có cài đặt SoundCloud trên đó
  if (import.meta.env.VITE_REMOTE_SERVER_URL) {
    servers.push(import.meta.env.VITE_REMOTE_SERVER_URL.replace(/\/$/, ''))
  }
  return servers.length > 0 ? servers : ['']
}

const PROXY_SERVERS = getApiBaseServers()
const PROXY_SERVER_URL_2 = import.meta.env.VITE_PROXY_SERVER_URL_2 || null

// Simple SoundCloud search using backend proxy server with fallback support
export const searchTracks = async (query, maxResults = 10) => {
  try {
    if (!SOUNDCLOUD_CLIENT_ID) {
      console.warn('SoundCloud Client ID is not set. Please set VITE_SOUNDCLOUD_CLIENT_ID in your .env file')
      return []
    }

    // Build list of proxy servers to try
    const proxyServers = [...PROXY_SERVERS]
    if (PROXY_SERVER_URL_2) {
      proxyServers.push(PROXY_SERVER_URL_2)
    }

    console.log(`Searching SoundCloud: "${query}" via ${proxyServers.length} proxy(s)`)

    // Try each proxy server in order
    let lastError = null
    for (let i = 0; i < proxyServers.length; i++) {
      const base = proxyServers[i]
      if (!base) continue

      // Try different endpoint patterns to be robust
      const endpoints = ['/api/soundcloud/search', '/search', '']

      for (const endpoint of endpoints) {
        try {
          // Use URL API for reliable construction
          const urlObj = new URL(endpoint, base.endsWith('/') ? base : base + '/')
          urlObj.searchParams.set('q', query)
          urlObj.searchParams.set('query', query) // Fallback for some proxies
          urlObj.searchParams.set('limit', maxResults.toString())
          urlObj.searchParams.set('client_id', SOUNDCLOUD_CLIENT_ID)

          const proxyUrl = urlObj.toString()
          console.log(`Trying SoundCloud search: ${proxyUrl}`)

          const response = await fetch(proxyUrl, {
            method: 'GET',
            headers: { 'Accept': 'application/json' },
          })

          if (!response.ok) {
            if (response.status === 404) continue // Try next endpoint

            const errorData = await response.json().catch(() => ({ error: 'Unknown error' }))
            throw new Error(`Proxy error ${response.status}: ${JSON.stringify(errorData)}`)
          }

          let data = await response.json()

          // Check if data needs transformation (if it's a raw SoundCloud response)
          let tracks = []
          if (data && data.collection && Array.isArray(data.collection)) {
            tracks = data.collection.map(track => ({
              id: track.id?.toString() || track.permalink_url || `sc_${Date.now()}_${Math.random()}`,
              title: track.title || 'Untitled',
              description: track.description || '',
              thumbnail: track.artwork_url || track.user?.avatar_url || '',
              channelTitle: track.user?.username || track.user?.full_name || 'Unknown Artist',
              duration: track.duration || 0,
              viewCount: track.playback_count || 0,
              publishedAt: track.created_at || new Date().toISOString(),
              source: 'soundcloud',
              streamUrl: track.stream_url,
              permalinkUrl: track.permalink_url || `https://soundcloud.com/${track.user?.permalink}/${track.permalink}`,
            }))
          } else if (Array.isArray(data)) {
            tracks = data
          }

          if (tracks && tracks.length >= 0) {
            console.log(`Found ${tracks.length} SoundCloud tracks using ${proxyUrl}`)
            return tracks
          }
        } catch (error) {
          if (error.status === 404) continue
          console.warn(`Attempt with ${endpoint} failed:`, error.message)
          lastError = error
        }
      }
    }

    // All proxy servers and patterns failed
    throw lastError || new Error('All attempts failed')

  } catch (error) {
    console.error('Error searching SoundCloud tracks:', error)

    // Check if proxy server is running
    if (error.message.includes('Failed to fetch') || error.message.includes('ECONNREFUSED')) {
      const errorMsg = PROXY_SERVER_URL_2
        ? 'Cả hai proxy server đều không chạy. Vui lòng kiểm tra VITE_PROXY_SERVER_URL và VITE_PROXY_SERVER_URL_2 trong .env'
        : 'Proxy server không chạy. Vui lòng chạy: npm run server'
      console.error(`⚠️ ${errorMsg}`)
      throw new Error(errorMsg)
    }

    return []
  }
}

export const formatDuration = (milliseconds) => {
  if (!milliseconds) return '0:00'
  const seconds = Math.floor(milliseconds / 1000)
  const minutes = Math.floor(seconds / 60)
  const remainingSeconds = seconds % 60
  return `${minutes}:${remainingSeconds.toString().padStart(2, '0')}`
}

export const formatViewCount = (count) => {
  if (count >= 1000000) {
    return `${(count / 1000000).toFixed(1)}M`
  }
  if (count >= 1000) {
    return `${(count / 1000).toFixed(1)}K`
  }
  return count.toString()
}

