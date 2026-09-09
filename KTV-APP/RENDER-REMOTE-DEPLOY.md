# Deploy Remote Control Server on Render (Free)

## Overview

Deploy `server-remote.js` to Render.com for real-time remote control feature.

**Features:**
- ✅ WebSocket support (real-time)
- ✅ Express API endpoints
- ✅ Free tier with generous limits
- ✅ Auto-deploy from GitHub
- ✅ CORS configured

---

## Step 1: Prepare GitHub

```bash
# Commit server-remote.js
git add server-remote.js
git commit -m "Add remote control server for Render deployment"
git push origin main
```

---

## Step 2: Create Render Account & Link GitHub

1. Go to [render.com](https://render.com)
2. Sign up (or login)
3. Connect GitHub account
4. Select your KTV-APP repository

---

## Step 3: Create Web Service

1. Click **"Create new"** → **"Web Service"**
2. Select repository: `KTV-APP`
3. Configure:
   ```
   Name: ktv-remote-control (or any name)
   Environment: Node
   Build Command: npm install
   Start Command: node server-remote.js
   ```
4. Plan: **Free**
5. Click **"Create Web Service"**

---

## Step 4: Get Your Render URL

Once deployed:
- URL will be: `https://ktv-remote-control.onrender.com`
- Full WebSocket URL: `wss://ktv-remote-control.onrender.com/ws`

**Note:** Free tier instances spin down after 15 min inactivity. First request takes ~30 sec to wake up.

---

## Step 5: Update .env File

In your main app `.env`, add:

```env
# Remote control server (Render deployment)
VITE_REMOTE_SERVER_URL=https://ktv-remote-control.onrender.com
```

Replace `ktv-remote-control` with your actual Render service name.

---

## Step 6: Test Connection

### Local Test (WebSocket):
```bash
# In your app (RemoteControl.jsx)
const ws = new WebSocket('wss://ktv-remote-control.onrender.com/ws?token=TEST123')
ws.onopen = () => console.log('Connected!')
```

### API Test:
```bash
curl https://ktv-remote-control.onrender.com/health
# Should return: {"status":"ok","service":"KTV Remote Control Server",...}
```

---

## Features Available

### WebSocket (`wss://...`)
- Real-time message delivery
- Session management
- Command broadcasting

### REST API (`https://...`)
- `/api/remote/session` - Register session
- `/api/remote/commands/:token` - Get/send commands
- `/api/remote/state/:token` - Update/get state

### Polling Fallback
- Automatically falls back to HTTP polling if WebSocket unavailable
- Queries commands every 2-5 seconds

---

## Troubleshooting

### Cold Start (Free Tier)
- First request after idle → takes ~30 sec
- Solution: Render offers paid plans with no cold starts ($7/month+)

### WebSocket Disconnects
- Free tier limits connections to ~100
- Polling fallback ensures feature still works

### CORS Issues
- Check `ALLOWED_ORIGINS` in `server-remote.js`
- Add your domain if not listed

### State Not Persisting
- Free tier uses in-memory storage
- State lost on restart
- For persistence: Add PostgreSQL (free tier available)

---

## Environment Variables (Render)

Render auto-detects `PORT` from environment:
- Default: `PORT=3001`
- Render sets `PORT=10000+` automatically

No need to configure - server handles it.

---

## Performance Notes

**Free Tier Limits:**
- 750 hours/month
- ~100 concurrent WebSocket connections
- In-memory storage (clears on restart)

**For Production:**
- Upgrade to Paid: $7/month
- Add PostgreSQL: $15/month
- 99.99% uptime SLA

---

## Next Steps

1. ✅ Commit `server-remote.js` to GitHub
2. ✅ Create Render Web Service (copy-paste config above)
3. ✅ Get Render URL (e.g., `https://ktv-remote-control.onrender.com`)
4. ✅ Update `.env`: `VITE_REMOTE_SERVER_URL=https://ktv-remote-control.onrender.com`
5. ✅ Commit & push
6. ✅ Test remote control from internet!

---

## Architecture

```
┌─────────────────┐
│   Frontend      │
│ (Vercel CDN)    │
│ huy.sale        │
└────────┬────────┘
         │ WebSocket + HTTP
         ↓
┌─────────────────────────────┐
│  Render Remote Server       │
│ (ktv-remote-control)        │
│ - /ws (WebSocket)           │
│ - /api/remote/* (REST API)  │
└─────────────────────────────┘

Devices communicate via Render server
✅ LAN: Works (port 3001)
✅ Internet: Works (Render server)
✅ Real-time: WebSocket
✅ Fallback: HTTP polling
```

---

## Need Help?

- Render Docs: https://render.com/docs
- GitHub Issues: Create issue in KTV-APP
- WebSocket Test: https://www.websocket.org/echo.html