# YouTube Search API Integration Guide

## Overview

The YouTube Search API has been integrated into the KTV APP as a **fallback mechanism** when internal servers fail. This provides better reliability and redundancy for your application.

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                    YouTube API Layer                            │
│  ┌──────────────────┐    ┌─────────────────────────────────┐   │
│  │  Internal Servers│    │  Cloudflare Workers (Fallback)  │   │
│  │  (Primary)       │    │  - searchYouTubeAPI()          │   │
│  │  - server.js     │◄──►│  - getSuggestionsAPI()         │   │
│  │  - render.com    │    │  - getTrendingVideosAPI()      │   │
│  └──────────────────┘    └─────────────────────────────────┘   │
│                    (Automatic Fallback)                         │
└─────────────────────────────────────────────────────────────────┘
```

## Configuration

Edit `src/utils/youtube.js` to control the external API behavior:

```javascript
// Line 10: Enable/Disable external API fallback
const USE_EXTERNAL_API_FALLBACK = true // Set to false to disable
```

## API Functions Available

### New Functions in `src/utils/youtube-search-api.js`

| Function | Parameters | Description |
|----------|------------|-------------|
| `searchYouTubeAPI(query, options)` | `query`, `maxResults`, `sort`, `type`, `region`, `language` | Search videos, channels, playlists |
| `getVideoDetailsAPI(videoId)` | `videoId` | Get detailed video information |
| `getChannelInfoAPI(channelId)` | `channelId` | Get channel information and videos |
| `getPlaylistVideosAPI(playlistId)` | `playlistId` | Get all videos in a playlist |
| `getSuggestionsAPI(query)` | `query` | Get search autocomplete suggestions |
| `getTrendingVideosAPI(options)` | `category`, `region` | Get trending videos |
| `getVideoCommentsAPI(videoId, options)` | `videoId`, `page`, `sortBy` | Get video comments |
| `getVideoCaptionsAPI(videoId, lang)` | `videoId`, `lang` | Get video transcripts |

### Updated Functions in `src/utils/youtube.js`

| Function | Behavior |
|----------|----------|
| `searchVideos(query, maxResults)` | Tries internal servers first, falls back to external API |
| `fetchSearchSuggestions(keyword)` | Tries internal servers first, falls back to external API |
| `fetchTrendingSuggestions()` | Tries internal servers first, falls back to external API |

## Usage Examples

### Example 1: Search Videos (Automatic Fallback)
```javascript
import { searchVideos } from '@/utils/youtube'

const results = await searchVideos('lofi beats', 20)
// Uses internal servers or external API automatically
```

### Example 2: Direct Cloudflare API Usage
```javascript
import { searchYouTubeAPI, getSuggestionsAPI } from '@/utils/youtube-search-api'

// Search videos
const videos = await searchYouTubeAPI('kpop hits', {
  maxResults: 15,
  sort: 'date',
  type: 'video',
  region: 'VN',
  language: 'vi'
})

// Get search suggestions
const suggestions = await getSuggestionsAPI('tra')
// Returns: ['trà sữa', 'trà sữa trân châu', 'trà đào']
```

### Example 3: Trending Videos
```javascript
import { getTrendingVideosAPI } from '@/utils/youtube-search-api'

// Get all trending videos
const trending = await getTrendingVideosAPI({
  category: 'default',  // default, music, gaming, news, movies
  region: 'VN'
})
```

### Example 4: Channel Info
```javascript
import { getChannelInfoAPI } from '@/utils/youtube-search-api'

// By channel ID
const channel = await getChannelInfoAPI('UCVHoI2-OGIjnjY0JuGfWNxw')

// By handle
const channel = await getChannelInfoAPI('@LofiGirl')
```

### Example 5: Search Suggestions with Fallback
```javascript
import { fetchSearchSuggestions } from '@/utils/youtube'

// Automatically falls back to external API if needed
const suggestions = await fetchSearchSuggestions('nhac')
// Returns array of suggested search terms
```

## Caching Strategy

### Client-Side Caching (Internal API)
- Cache duration: 10 minutes
- Stored in `SEARCH_CACHE` Map
- Key format: `query_maxResults`

### Cloudflare API Caching
- Cache duration: 5 minutes
- Stored in `API_CACHE` Map
- Key format includes all parameters (e.g., `search:query:10:relevance:video`)

## Response Format

### Search Results
```javascript
[
  {
    id: "videoId123",
    title: "Video Title",
    description: "Video description...",
    thumbnail: "https://i.ytimg.com/vi/...",
    channelTitle: "Channel Name",
    channelId: "channelId123",
    publishedAt: "2024-01-15T10:30:00Z",
    duration: "PT3M30S",
    viewCount: 1234567,
    source: "youtube",
    fromApi: "cloudflare-api"  // Indicates external API source
  }
]
```

### Trending Videos
```javascript
[
  {
    id: "videoId123",
    title: "Trending Video",
    thumbnail: "https://i.ytimg.com/vi/...",
    channelTitle: "Channel Name",
    viewCount: 9876543,
    source: "youtube",
    fromApi: "cloudflare-api"
  }
]
```

## Error Handling

The integration handles errors gracefully:

```javascript
try {
  const results = await searchVideos('query')
  console.log(results)
} catch (error) {
  console.error('Search failed:', error)
  // Both internal servers and external API failed
}
```

### Logs You'll See
```
[YouTube API] Searching for: "lofi" (server-side caching + YouTubei fallback enabled)
[YouTube API] Lỗi kết nối tới : Network error
[YouTube API] Trying external API fallback for: "lofi"
[YouTube Search API] Found 10 results for: lofi
```

## API Limits & Quotas

### Cloudflare Workers API (Pro Plan)
- **Daily Quota:** 50,000 requests
- **Rate Limit:** 100 requests per minute
- **Auto-reset:** Daily at 00:00 UTC

### API Info Object
```javascript
import { API_INFO } from '@/utils/youtube-search-api'

console.log(API_INFO)
// {
//   baseUrl: "https://youtube-search-api.trinhhuy12343.workers.dev",
//   hasApiKey: true,
//   quotaLimit: 50000,
//   rateLimit: 100
// }
```

## When to Use Which API

| Scenario | Recommended API |
|----------|-----------------|
| Production (normal operation) | Internal servers (primary) |
| Internal servers down | External API (automatic fallback) |
| High traffic days | External API as overflow |
| Development/testing | Either (external has stable quota) |
| Vietnam region focus | Both (configure region parameter) |

## Monitoring API Usage

To monitor which API is being used:

```javascript
// Check source in results
const results = await searchVideos('query')
const sources = results.map(r => r.fromApi)
console.log('Using sources:', [...new Set(sources)])
```

## Best Practices

1. **Use existing functions**: Always use `searchVideos()` instead of calling `searchYouTubeAPI()` directly unless you specifically need the external API.

2. **Configure region**: When using the external API directly, set `region: 'VN'` and `language: 'vi'` for Vietnamese content.

3. **Check cache first**: Both APIs implement caching, but for high-traffic scenarios, you might want to increase cache duration.

4. **Monitor logs**: Watch for "[YouTube Search API]" logs to track external API usage.

5. **Handle failures gracefully**: The fallback is automatic, but always wrap calls in try-catch for production.

## Troubleshooting

### Issue: External API not being used
**Solution:** Verify `USE_EXTERNAL_API_FALLBACK = true` in `youtube.js`

### Issue: Rate limiting
**Solution:** The 100 requests/minute limit should be sufficient for most apps. If exceeded, cache results longer.

### Issue: Wrong language results
**Solution:** Set `language: 'vi'` when calling external API functions directly.

### Issue: API returns empty results
**Solution:** Check console logs for error messages. Verify the API key is valid in `youtube-search-api.js`.

## Summary

- Internal servers are tried first for optimal performance
- External Cloudflare API serves as reliable fallback
- Automatic failover with proper error handling
- Configurable via single constant
- Caching implemented on both layers