# YouTube Search Optimization - Hướng dẫn

## Tổng quan

Hệ thống đã được tối ưu hóa để giảm thiểu chi phí quota YouTube Data API v3 và tự động fallback sang YouTubei (unofficial API) khi hết quota.

## Kiến trúc tối ưu hóa

### 1. **Server-side Caching (Redis + Memory)**
- **Redis cache**: TTL 5 phút cho search results
- **Memory cache**: TTL 5 phút, giới hạn 500 entries (LRU cleanup)
- **Client-side cache**: TTL 10 phút (backup)

### 2. **In-flight Deduplication**
- Coalesce các request giống nhau đang chạy đồng thời
- Nhiều users search cùng từ khóa → chỉ 1 API call thực sự
- Giảm đáng kể API calls khi có traffic cao

### 3. **Automatic Fallback to YouTubei**
- **Primary**: YouTube Data API v3 (official, có quota)
- **Fallback**: YouTubei (unofficial, unlimited, không tốn quota)
- Tự động chuyển sang YouTubei khi nhận 403/429 (quota exceeded)

## Cấu hình Redis (Khuyến nghị cho Production)

### Thêm vào `.env`:
```env
# Redis Configuration (for YouTube caching)
REDIS_HOST=localhost
REDIS_PORT=6379
REDIS_PASSWORD=your_redis_password_here
```

### Nếu không có Redis:
- Hệ thống vẫn hoạt động bình thường với memory cache
- Log warning sẽ hiện ra: "Redis initialization failed, continuing without cache"

## Các file đã thay đổi

### 1. **server-youtube-cache.js** (MỚI)
- Module caching với Redis + Memory
- In-flight deduplication helper
- LRU cache cleanup tự động

### 2. **server-youtubei.js** (MỚI)
- YouTubei client wrapper
- Search, details, playlist methods
- Format conversion từ YouTubei → YouTube Data API format

### 3. **server.js** (CẬP NHẬT)
```javascript
// Import modules
import { createRedisClient, getCached, REDIS_KEY_PREFIXES, CACHE_TTL } from './server-youtube-cache.js'
import { searchYoutubei, getVideoDetailsYoutubei, getPlaylistYoutubei } from './server-youtubei.js'

// Initialize Redis
let redisClient = createRedisClient()

// Endpoint /api/youtube/search-full giờ có:
// - Redis caching (5 phút)
// - In-flight dedup
// - Auto fallback to YouTubei
```

### 4. **src/utils/youtube.js** (CẬP NHẬT)
- Frontend giờ call server endpoint thay vì gọi trực tiếp YouTube API
- Server-side caching + fallback được xử lý tự động
- Client-side cache vẫn giữ làm backup

## Packages đã cài

```json
{
  "ioredis": "^5.x",  // Redis client
  "youtubei": "^4.x"   // YouTubei unofficial API
}
```

## Workflow hoạt động

```
User Search Request
       ↓
Frontend (youtube.js)
       ↓
Server /api/youtube/search-full
       ↓
┌─────────────────────────┐
│ Check Memory Cache      │ → Hit? Return
└─────────────────────────┘
       ↓ Miss
┌─────────────────────────┐
│ Check Redis Cache       │ → Hit? Return
└─────────────────────────┘
       ↓ Miss
┌─────────────────────────┐
│ In-flight Dedup Check   │ → Running? Wait & Return
└─────────────────────────┘
       ↓ New Request
┌─────────────────────────┐
│ YouTube Data API v3     │ → Success? Cache & Return
└─────────────────────────┘
       ↓ Quota Exceeded (403/429)
┌─────────────────────────┐
│ Fallback to YouTubei    │ → Success? Cache & Return
└─────────────────────────┘
       ↓
Return Results to Frontend
```

## Lợi ích

### Tiết kiệm Quota
- **Trước**: Mỗi search = 100 units
- **Sau**: 
  - Cache hit (5 phút) = 0 units
  - Nhiều users cùng search = 0 units (dedup)
  - Quota hết → tự động dùng YouTubei = 0 units

### Hiệu suất
- Faster response time (cache hit < 10ms)
- Giảm load lên YouTube servers
- Ổn định hơn khi traffic cao

### Độ tin cậy
- Không bị downtime khi hết quota
- Automatic failover
- Multiple layers of caching

## Test

### 1. Test caching
```bash
# Search lần 1 (gọi API thật)
curl "http://localhost:3001/api/youtube/search-full?keyword=karaoke&maxResults=5"

# Search lần 2 trong 5 phút (từ cache)
curl "http://localhost:3001/api/youtube/search-full?keyword=karaoke&maxResults=5"
```

### 2. Test in-flight dedup
```bash
# Gọi đồng thời 10 requests cùng keyword
for i in {1..10}; do
  curl "http://localhost:3001/api/youtube/search-full?keyword=test&maxResults=5" &
done
wait
# Chỉ 1 API call thật sẽ được thực hiện
```

### 3. Check logs
```bash
npm start
# Xem logs:
# ✅ Redis connected for YouTube caching
# [YouTube API] Searching for: "..." (server-side caching + YouTubei fallback enabled)
# [YouTube API] Results from: official (10 videos)
```

## Monitoring

### Redis Stats (nếu có Redis CLI)
```bash
redis-cli
> KEYS yt-search:*
> TTL yt-search:karaoke:10
> GET yt-search:karaoke:10
```

### Server Logs
- `✅ Redis connected` → Redis hoạt động
- `⚠️ Redis initialization failed` → Chạy memory-only mode
- `⚠️ YouTube Data API quota exceeded. Falling back to YouTubei...` → Đã chuyển sang fallback

## Troubleshooting

### Redis không kết nối được
- Kiểm tra Redis service: `redis-cli ping` → phải trả về `PONG`
- Kiểm tra env vars: REDIS_HOST, REDIS_PORT, REDIS_PASSWORD
- Hệ thống vẫn chạy được với memory cache nếu Redis fail

### YouTubei fallback không hoạt động
- Kiểm tra package đã cài: `npm list youtubei`
- Xem error logs trong console
- YouTubei đôi khi bị rate limit, hãy chờ 1-2 phút

### Cache không hoạt động
- Check Redis: `redis-cli KEYS yt-search:*`
- Check memory cache size trong logs
- Verify TTL settings trong server-youtube-cache.js

## Production Deployment

### Khuyến nghị
1. **Sử dụng Redis** (bắt buộc cho multi-instance deployment)
2. **Redis managed service**: AWS ElastiCache, Redis Cloud, DigitalOcean Managed Redis
3. **Monitor quota usage**: Set up alerts khi quota < 20%
4. **Log aggregation**: Track fallback frequency

### Environment Variables Production
```env
REDIS_HOST=your-production-redis.cache.amazonaws.com
REDIS_PORT=6379
REDIS_PASSWORD=your-strong-password-here
VITE_YOUTUBE_API_KEY=key1
VITE_YOUTUBE_API_KEY_2=key2
VITE_YOUTUBE_API_KEY_3=key3
```

## Tác giả & Credit

Optimization strategy inspired by [vkara](https://github.com/lehuygiang28/vkara) project.

Implementation:
- Server-side caching: Redis + Memory (2-tier)
- In-flight deduplication: Coalesce pattern
- YouTubei integration: Automatic fallback

---

**Lưu ý**: YouTubei là unofficial API, có thể thay đổi format bất cứ lúc nào. Sử dụng làm fallback, không nên làm primary source.