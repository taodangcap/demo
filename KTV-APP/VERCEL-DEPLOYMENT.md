# Deployment lên Vercel - KTV App

Hướng dẫn deploy app lên Vercel với cả server backend (API) và frontend (React/Vite).

## 📋 Requirements

- Vercel account: https://vercel.com
- GitHub account (để connect repo)
- 5x YouTube Data API Keys (set trong environment variables)
- Redis instance (optional, cho production caching)

## 🚀 Deployment Steps

### 1. Connect GitHub Repository

```bash
# Trong project của bạn, push code lên GitHub
git add .
git commit -m "Add YouTube optimization + Vercel API"
git push origin main
```

### 2. Deploy trên Vercel

**Option A: Từ Vercel Dashboard**
1. Vào https://vercel.com/dashboard
2. Click "New Project"
3. Select GitHub repository
4. Click "Import"

**Option B: Dùng Vercel CLI**
```bash
npm i -g vercel
vercel login
vercel
```

### 3. Set Environment Variables

Trong Vercel Dashboard → Project Settings → Environment Variables, thêm:

```
VITE_YOUTUBE_API_KEY=your_key_1
VITE_YOUTUBE_API_KEY_2=your_key_2
VITE_YOUTUBE_API_KEY_3=your_key_3
VITE_YOUTUBE_API_KEY_4=your_key_4
VITE_YOUTUBE_API_KEY_5=your_key_5

# Redis (optional, nhưng recommended cho production)
REDIS_HOST=your-redis-host.redis.upstash.com
REDIS_PORT=6379
REDIS_PASSWORD=your-redis-password

# Discord Webhook (nếu có)
DISCORD_WEBHOOK_URL=your-discord-webhook-url

# Frontend will use Vercel API
VITE_REMOTE_SERVER_URL=https://your-vercel-domain.vercel.app
```

### 4. Build & Deploy

Vercel sẽ tự động build khi push code lên GitHub:

1. Frontend (Vite) → static files trong `dist/`
2. API → serverless functions trong `api/` folder

## 📁 Project Structure (Vercel)

```
project/
├── api/
│   └── index.js          ← Serverless function (auto-deployed)
├── src/
│   └── ...               ← React/Vite source
├── dist/                 ← Built frontend (auto-deployed)
├── public/               ← Static files
├── vite.config.js        ← Vite build config
├── vercel.json           ← Vercel project config
└── package.json
```

## 🔧 How Vercel Deployment Works

### Frontend
- Vite builds React app → `dist/` folder
- Vercel serves static files từ `dist/`
- SPA routing được handle bởi rewrite rule trong `vercel.json`

### Backend API
- Files trong `api/` folder → serverless functions
- `/api/**` requests route tới functions
- Express app export từ `api/index.js`
- Auto-scalable, no cold start delays

### Routing
```
GET /                        → index.html (SPA)
GET /about                   → index.html (React Router)
GET /api/youtube/search-full → api/index.js handler
GET /health                  → api/index.js /health endpoint
```

## 🎯 YouTube Caching on Vercel

### Memory Cache (Always Available)
- TTL: 5 phút
- LRU cleanup tự động
- Limit: 500 entries

### Redis Cache (Optional but Recommended)
- Persistent caching across function invocations
- Better for high-traffic scenarios
- Setup Redis từ: Upstash, Redis Cloud, hoặc AWS ElastiCache

**Sử dụng Upstash Redis (recommended cho Vercel):**
1. Tạo account: https://upstash.com
2. Create Database → Copy connection string
3. Set env vars trong Vercel:
   ```
   REDIS_HOST=xxx.upstash.io
   REDIS_PORT=6379
   REDIS_PASSWORD=xxx
   ```

## 🌍 Custom Domain

1. Vercel Dashboard → Project Settings → Domains
2. Add your domain
3. Follow DNS instructions
4. HTTPS auto-enabled

## 📊 Monitoring & Logs

**View Logs:**
```bash
vercel logs     # Project logs
vercel env ls   # Environment variables
```

**Or từ Dashboard:**
- Vercel Dashboard → Deployments → Click deployment → View Logs

## ⚡ Optimize Performance

1. **Enable Edge Caching**
   - vercel.json: Add cache headers
   - Frontend assets cached 1 year
   - API responses cached 5 min

2. **Use Redis for YouTube Cache**
   - Cross-function cache sharing
   - Reduced YouTube API calls
   - Better cold-start performance

3. **Multiple API Keys**
   - Vercel automatically rotates keys
   - Better quota management
   - Automatic fallback to YouTubei

4. **YouTubei Fallback**
   - Zero quota cost
   - Automatic activation when Data API quota exceeded
   - Seamless user experience

## 🐛 Troubleshooting

### API not working (502/503 error)
- Check API logs: `vercel logs`
- Verify env vars are set
- Check YouTube API keys are valid
- Verify CORS settings

### Slow API responses
- Enable Redis for caching
- Check YouTube API quota
- Monitor function execution time
- Use Vercel Analytics

### Cold starts (first request slow)
- Expected on Vercel serverless
- Subsequent requests faster (warm)
- Use Redis to cache frequently requested data

### CORS errors
- Update ALLOWED_ORIGINS in api/index.js
- Deploy new version
- Verify request origin matches

## 📝 Production Checklist

- [ ] All 5 YouTube API keys configured
- [ ] Redis connection setup (recommended)
- [ ] Custom domain configured
- [ ] HTTPS enabled
- [ ] Environment variables all set
- [ ] API endpoints tested
- [ ] Frontend routes working
- [ ] Analytics enabled (optional)
- [ ] Monitoring/alerts setup
- [ ] Backup plan for quota exceeded

## 🔗 Useful Links

- Vercel Docs: https://vercel.com/docs
- YouTube API Docs: https://developers.google.com/youtube/v3
- YouTubei: https://github.com/SuspiciousLookingOwl/youtubei
- Upstash Redis: https://upstash.com

## 💡 Tips & Best Practices

1. **Use Environment Variables**
   - Never commit API keys to GitHub
   - Use `.env.local` for local development
   - Vercel handles env var injection

2. **Monitor Quota Usage**
   - Set alerts when quota < 20%
   - YouTube quota resets daily at 12:00 PM PST
   - YouTubei fallback activates automatically

3. **Cache Strategy**
   - Memory cache: Fast, per-function
   - Redis cache: Persistent, cross-function
   - Browser cache: Client-side fallback

4. **Error Handling**
   - Fallback gracefully to YouTubei
   - Return meaningful error messages
   - Log errors for debugging

5. **Rate Limiting**
   - Implement per-IP rate limiting
   - Monitor for suspicious activity
   - Use Vercel's built-in DDoS protection

---

**Questions?** Check server-youtube-cache.js, server-youtubei.js, or api/index.js for implementation details.