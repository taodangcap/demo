// YouTubei Module for Vercel Serverless
// Unofficial YouTube API (No quota cost)

import { Client } from 'youtubei'

let youtubeiClient = null

/**
 * Get or create YouTubei client
 */
export async function getYoutubeiClient() {
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

/**
 * Search videos using YouTubei (no quota cost)
 */
export async function searchYoutubei(query, maxResults = 15) {
  try {
    const client = await getYoutubeiClient()
    const searchResults = await client.search(query, {
      type: 'video'
    })
    
    const items = searchResults.items || searchResults
    
    const videos = items
      .slice(0, maxResults)
      .map(video => ({
        id: video.id,
        title: video.title || 'Untitled',
        description: video.description || '',
        thumbnail: video.thumbnails?.[0]?.url || video.bestThumbnail?.url || '',
        channelTitle: video.channel?.name || video.author?.name || 'Unknown',
        publishedAt: video.uploadDate || '',
        duration: formatDurationFromSeconds(video.duration) || '',
        viewCount: typeof video.viewCount === 'number' ? video.viewCount : 0,
        source: 'youtube',
        fromApi: 'youtubei'
      }))
    
    return videos
  } catch (err) {
    console.error('YouTubei search failed:', err.message)
    throw err
  }
}

/**
 * Get video details using YouTubei
 */
export async function getVideoDetailsYoutubei(videoIds) {
  try {
    const client = await getYoutubeiClient()
    const ids = Array.isArray(videoIds) ? videoIds : [videoIds]
    
    const results = await Promise.all(
      ids.map(async (id) => {
        try {
          const info = await client.getVideo(id)
          return {
            id: id,
            title: info.title || 'Untitled',
            description: info.description || '',
            thumbnail: info.thumbnails?.[0]?.url || info.bestThumbnail?.url || '',
            channelTitle: info.channel?.name || 'Unknown',
            publishedAt: info.uploadDate || '',
            duration: formatDurationFromSeconds(info.duration) || '',
            viewCount: typeof info.viewCount === 'number' ? info.viewCount : 0,
            source: 'youtube',
            fromApi: 'youtubei',
            contentDetails: {
              duration: formatDurationISO8601(info.duration || 0)
            },
            status: {
              embeddable: true
            }
          }
        } catch (err) {
          console.warn(`Failed to get details for ${id}:`, err.message)
          return null
        }
      })
    )
    
    return results.filter(r => r !== null)
  } catch (err) {
    console.error('YouTubei getDetails failed:', err.message)
    throw err
  }
}

/**
 * Get playlist items using YouTubei
 */
export async function getPlaylistYoutubei(playlistId, maxResults = 50) {
  try {
    const client = await getYoutubeiClient()
    const playlist = await client.getPlaylist(playlistId)
    
    const items = playlist.videos?.items || playlist.videos || []
    
    const videos = items
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
    
    return videos
  } catch (err) {
    console.error('YouTubei playlist failed:', err.message)
    throw err
  }
}

/**
 * Helper: Format duration from seconds to MM:SS or HH:MM:SS
 */
function formatDurationFromSeconds(seconds) {
  if (!seconds || seconds === 0) return '0:00'
  
  const hrs = Math.floor(seconds / 3600)
  const mins = Math.floor((seconds % 3600) / 60)
  const secs = seconds % 60
  
  if (hrs > 0) {
    return `${hrs}:${String(mins).padStart(2, '0')}:${String(secs).padStart(2, '0')}`
  }
  return `${mins}:${String(secs).padStart(2, '0')}`
}

/**
 * Helper: Format duration to ISO 8601 format (PT#H#M#S)
 */
function formatDurationISO8601(seconds) {
  if (!seconds || seconds === 0) return 'PT0S'
  
  const hrs = Math.floor(seconds / 3600)
  const mins = Math.floor((seconds % 3600) / 60)
  const secs = seconds % 60
  
  let result = 'PT'
  if (hrs > 0) result += `${hrs}H`
  if (mins > 0) result += `${mins}M`
  if (secs > 0) result += `${secs}S`
  
  return result
}