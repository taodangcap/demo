# 🎬 YouTube Search API Documentation

A modern, production-ready, SerpAPI-style API for YouTube search and data retrieval, running on Cloudflare Workers. 

- **Production Base URL:** `https://youtube-search-api.trinhhuy12343.workers.dev`
- **Interactive API Playground (Swagger):** `https://youtube-search-api.trinhhuy12343.workers.dev/docs`
- **OpenAPI Specification JSON:** `https://youtube-search-api.trinhhuy12343.workers.dev/openapi.json`

---

## 🔑 Authentication

All endpoints (except `/health` and `/docs`) require a valid API key. You can authenticate using **either** of the following methods:

### Method 1: HTTP Authorization Header (Recommended for Server Integrations)
```http
Authorization: Bearer sk_e8ad55ac66e07d016ebf4e086a3bd60fc36334b8b64e7cfe
```

### Method 2: Query Parameter (Recommended for Browser/Direct Testing)
Append `key` or `apikey` to your query string:
```http
https://youtube-search-api.trinhhuy12343.workers.dev/search?q=mck&key=sk_e8ad55ac66e07d016ebf4e086a3bd60fc36334b8b64e7cfe
```

---

## 🚀 Integration Snippets

### 1. cURL
```bash
# Using Header
curl -X GET "https://youtube-search-api.trinhhuy12343.workers.dev/search?q=lofi" \
  -H "Authorization: Bearer sk_e8ad55ac66e07d016ebf4e086a3bd60fc36334b8b64e7cfe"

# Using Query Param
curl -X GET "https://youtube-search-api.trinhhuy12343.workers.dev/search?q=lofi&key=sk_e8ad55ac66e07d016ebf4e086a3bd60fc36334b8b64e7cfe"
```

### 2. JavaScript (Fetch API / Node.js)
```javascript
const API_KEY = "sk_e8ad55ac66e07d016ebf4e086a3bd60fc36334b8b64e7cfe";
const BASE_URL = "https://youtube-search-api.trinhhuy12343.workers.dev";

async function searchYouTube(query) {
  const url = `${BASE_URL}/search?q=${encodeURIComponent(query)}&key=${API_KEY}`;
  
  try {
    const response = await fetch(url);
    const result = await response.json();
    if (result.success) {
      console.log("Results:", result.data.results);
      console.log("Cached Hit?", result.meta.cached);
    } else {
      console.error("API Error:", result.error);
    }
  } catch (error) {
    console.error("Fetch failed:", error);
  }
}

searchYouTube("lofi beats");
```

### 3. Python (requests)
```python
import requests

API_KEY = "sk_e8ad55ac66e07d016ebf4e086a3bd60fc36334b8b64e7cfe"
BASE_URL = "https://youtube-search-api.trinhhuy12343.workers.dev"

def search_youtube(query):
    url = f"{BASE_URL}/search"
    params = {
        "q": query,
        "key": API_KEY
    }
    
    try:
        response = requests.get(url, params=params)
        result = response.json()
        if result.get("success"):
            print("Search Results:", result["data"]["results"])
            print("Cache Hit:", result["meta"]["cached"])
        else:
            print("API Error:", result["error"])
    except Exception as e:
        print("Request failed:", e)

search_youtube("lofi beats")
```

---

## 🌐 API Endpoints

### 1. YouTube Search
Returns paginated search results for videos, channels, playlists, or all mixed.
- **URL:** `GET /search`
- **Params:**
  - `q` (string, required): The search query.
  - `page` (integer, optional, default: `1`): Page number (1-50).
  - `sort` (enum, optional, default: `relevance`): `relevance` | `date` | `views` | `rating`.
  - `type` (enum, optional, default: `all`): `video` | `channel` | `playlist` | `all`.
  - `region` (string, optional, default: `US`): 2-letter ISO country code.
  - `language` (string, optional, default: `en`): Language code (e.g., `vi`, `en`).

### 2. Video Details
Returns full metadata, keywords, description, thumbnails, and related videos for a video ID.
- **URL:** `GET /video/:id`
- **Example:** `GET /video/dQw4w9WgXcQ`

### 3. Channel Info
Returns channel stats, info, and tabs (latest videos, shorts, playlists) by handle or channel ID.
- **URL:** `GET /channel/:id`
- **Example (ID):** `GET /channel/UCVHoI2-OGIjnjY0JuGfWNxw`
- **Example (Handle):** `GET /channel/@LofiGirl`

### 4. Playlist Videos
Returns playlist title, description, subscriber count, and full list of videos.
- **URL:** `GET /playlist/:id`
- **Example:** `GET /playlist/PLbpi6ZahtOH6Ar_3GPy3HFZWMg9Rb8sWT`

### 5. Video Comments
Returns paginated video comments and replies.
- **URL:** `GET /comments/:id`
- **Params:**
  - `page` (integer, optional, default: `1`): Page token or number.
  - `sortBy` (enum, optional, default: `top`): `top` | `new`.

### 6. Video Transcripts / Captions
Returns full transcript text with timestamps for a video.
- **URL:** `GET /captions/:id`
- **Params:**
  - `lang` (string, optional, default: `en`): Language code for the captions.

### 7. Search Suggestions (Autocomplete)
Returns search autocomplete queries.
- **URL:** `GET /suggest`
- **Params:**
  - `q` (string, required): Query prefix.

### 8. Trending Videos
Returns real-time trending videos on YouTube.
- **URL:** `GET /trending`
- **Params:**
  - `category` (enum, optional, default: `default`): `default` | `music` | `gaming` | `news` | `movies`.
  - `region` (string, optional, default: `US`): 2-letter ISO country code.

---

## 🛡️ Response Codes & Standard Output

All responses follow a standardized format:

### Success Response
```json
{
  "success": true,
  "data": { ... },
  "meta": {
    "cached": true,
    "cachedAt": "2026-06-15T05:28:10.000Z",
    "responseTime": 12
  }
}
```

### Error Response
```json
{
  "success": false,
  "error": {
    "code": "VALIDATION_ERROR",
    "message": "Invalid query parameter",
    "details": { ... }
  }
}
```

---

## 📊 Tier limits & Quota

Your current API Key (`sk_e8ad55ac66e07d016ebf4e086a3bd60fc36334b8b64e7cfe`) is on the **Pro Plan**:
- **Quota per day:** 50,000 requests (resets daily at 00:00 UTC)
- **Rate limit:** 100 requests per minute

---

## 🛠️ Admin API (API Keys Management)
Admin endpoints require: `Authorization: Bearer <ADMIN_SECRET>` (Default: `change-this-in-production`).

- `GET /admin/stats` - Fetch overall API request volume and top-performing endpoints.
- `GET /admin/apikeys` - List all generated API keys (masked value).
- `POST /admin/apikeys` - Create new API keys.
- `DELETE /admin/apikeys/:id` - Revoke API keys.