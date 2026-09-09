// Remote Control Session Management
import { useSessionStore } from '../store/sessionStore'

const STORAGE_KEY_REMOTE_SESSION = 'remote_control_session'
const STORAGE_KEY_REMOTE_ENABLED = 'remote_control_enabled'

// Helper to get API Base URL
const getApiBaseUrl = () => {
  const host = window.location.hostname;
  const isLocal = host === 'localhost' || host === '127.0.0.1';
  // Check if it's a local network IP (e.g. 192.168.x.x or 10.x.x.x or 172.x.x.x)
  const isLan = /^(192\.168\.|10\.|172\.(1[6-9]|2[0-9]|3[0-1])\.)/.test(host);
  const envUrl = import.meta.env.VITE_REMOTE_SERVER_URL;

  // 1. Ưu tiên Local/LAN cổng 3001 ở môi trường phát triển
  if (isLocal || isLan) {
    return `http://${host}:3001`;
  }

  // 2. Nếu ở môi trường production thì dùng server cấu hình trong env
  if (envUrl) {
    return envUrl.replace(/\/$/, '');
  }

  // 3. Mặc định là relative path cho production unified deployment
  return '';
}

// Generate unique session token (Short 6-character alphanumeric for easy manual entry)
export const generateSessionToken = () => {
  const chars = '0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ'
  let result = ''
  for (let i = 0; i < 6; i++) {
    result += chars.charAt(Math.floor(Math.random() * chars.length))
  }
  return result
}

// Get or create session token
export const getSessionToken = () => {
  let token = localStorage.getItem(STORAGE_KEY_REMOTE_SESSION)
  if (!token) {
    token = generateSessionToken()
    localStorage.setItem(STORAGE_KEY_REMOTE_SESSION, token)
  }
  return token
}

/**
 * Force session token (ShowCue / embed / shared dual-WebView).
 * Normalizes to 6-char uppercase A-Z0-9 when possible.
 */
export const setSessionToken = (token) => {
  if (!token || typeof token !== 'string') return getSessionToken()
  let t = token.trim().toUpperCase()
  // Allow longer tokens; prefer 6-char alnum for remote UI
  if (/^[0-9A-Z]{4,32}$/.test(t)) {
    localStorage.setItem(STORAGE_KEY_REMOTE_SESSION, t)
    return t
  }
  // Strip non-alnum
  t = t.replace(/[^0-9A-Z]/g, '')
  if (t.length >= 4) {
    localStorage.setItem(STORAGE_KEY_REMOTE_SESSION, t.slice(0, 16))
    return t.slice(0, 16)
  }
  return getSessionToken()
}

/**
 * Read ?session= or ?token= from URL and force localStorage + return token.
 * Used by /player when opened from ShowCue (separate WebView storages).
 */
export const applySessionFromUrl = (search = typeof window !== 'undefined' ? window.location.search : '') => {
  try {
    const params = new URLSearchParams(search)
    const forced = params.get('session') || params.get('token')
    if (forced) {
      return setSessionToken(forced)
    }
  } catch (e) {
    console.warn('applySessionFromUrl failed', e)
  }
  return null
}

/** True when running inside ShowCue WebView2 (or forced embed). */
export const isShowCueHost = () => {
  try {
    if (typeof window === 'undefined') return false
    if (window.chrome?.webview) return true
    const params = new URLSearchParams(window.location.search)
    return (
      params.get('embed') === '1' ||
      params.get('host') === 'showcue' ||
      params.get('master') === '1' ||
      params.get('preview') === '1'
    )
  } catch {
    return false
  }
}

/**
 * ShowCue live-preview pane: mirror only — must NOT register as remote master
 * (output secondary is the sole player master).
 */
export const isShowCuePreview = () => {
  try {
    if (typeof window === 'undefined') return false
    return new URLSearchParams(window.location.search).get('preview') === '1'
  } catch {
    return false
  }
}

/**
 * ShowCue / FOH operator remote — compact desk UI, no guest share/QR.
 * True when WebView2, or ?op=1 / host=showcue / embed=1 / operator=1.
 */
export const isShowCueOperator = () => {
  try {
    if (typeof window === 'undefined') return false
    if (window.chrome?.webview) return true
    const params = new URLSearchParams(window.location.search)
    return (
      params.get('op') === '1' ||
      params.get('operator') === '1' ||
      params.get('host') === 'showcue' ||
      params.get('embed') === '1'
    )
  } catch {
    return false
  }
}

// Reset session token
export const resetSessionToken = async () => {
  const newToken = generateSessionToken()
  localStorage.setItem(STORAGE_KEY_REMOTE_SESSION, newToken)
  // Register with server immediately after reset
  await registerSession(newToken)
  return newToken
}

// Check if remote control is enabled (default: TRUE - always on)
export const isRemoteControlEnabled = () => {
  const stored = localStorage.getItem(STORAGE_KEY_REMOTE_ENABLED)
  // If never set before, default to enabled
  if (stored === null) {
    localStorage.setItem(STORAGE_KEY_REMOTE_ENABLED, 'true')
    return true
  }
  return stored === 'true'
}

// Enable/Disable remote control
export const setRemoteControlEnabled = (enabled) => {
  localStorage.setItem(STORAGE_KEY_REMOTE_ENABLED, enabled ? 'true' : 'false')
}

// --- API INTERACTION METHODS ---

// Register session (Called by Main Device)
export const registerSession = async (token, roomCode = null, password = null) => {
  try {
    const url = `${getApiBaseUrl()}/api/remote/session`
    const payload = { token }
    if (roomCode) payload.roomCode = roomCode
    if (password) payload.password = password
    const res = await fetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    })
    if (res.ok) {
      return await res.json()
    }
    return null
  } catch (e) {
    console.error('Failed to register session:', e)
    return null
  }
}

// Check connection (Called by Remote Device)
export const checkConnection = async (token) => {
  try {
    const url = `${getApiBaseUrl()}/api/remote/session/${token}`
    const res = await fetch(url)
    if (res.ok) {
      const data = await res.json()
      return data.connected
    }
    return false
  } catch (e) {
    // console.error('Connection check failed:', e)
    return false
  }
}

// Send command from remote to main device
export const sendRemoteCommand = async (command, data = {}, password = null) => {
  // Extract token from data if present, otherwise use local storage one
  const token = data.session || getSessionToken()
  const authPassword = password || (typeof window !== 'undefined' ? sessionStorage.getItem(`session_auth_${token}`) : null)

  try {
    const url = `${getApiBaseUrl()}/api/remote/command`
    const body = JSON.stringify({
      token,
      command,
      data,
      password: authPassword || undefined
    })
    const headers = { 'Content-Type': 'application/json' }
    if (authPassword) {
      headers['x-session-password'] = authPassword
    }

    let res = await fetch(url, {
      method: 'POST',
      headers,
      body
    })
    // Session chưa có → tạo shell rồi gửi lại (Chrome remote test trước player)
    if (res.status === 404) {
      await registerSession(token, null, authPassword)
      res = await fetch(url, {
        method: 'POST',
        headers,
        body
      })
    }
    return res.ok
  } catch (e) {
    console.error('Failed to send command:', e)
    return false
  }
}

// Get pending commands for main device
export const getRemoteCommands = async () => {
  const token = getSessionToken()
  try {
    const url = `${getApiBaseUrl()}/api/remote/commands/${token}`
    const res = await fetch(url)

    if (res.status === 404) {
      // Session lost (server restart?), try to re-register immediately
      await registerSession(token)
      return { commands: [], error: true, status: 404 }
    }

    if (res.ok) {
      const data = await res.json()
      // Robust handling: if server returns array (old format), wrap it
      if (Array.isArray(data)) {
        return { commands: data, remoteConnected: false }
      }
      return data || { commands: [] }
    }
    return { commands: [], error: true, status: res.status }
  } catch (e) {
    console.error('Failed to poll commands:', e)
    return { commands: [], error: true }
  }
}

// Clear processed commands (No longer needed with new API since GET clears them, 
// but kept for compatibility if needed, though now empty)
export const clearRemoteCommands = () => {
  // No-op: API getRemoteCommands already clears the queue on the server
}

// Get remote control URL
// Sync state (Called by Main Device)
export const syncRemoteState = async (token, state) => {
  try {
    const url = `${getApiBaseUrl()}/api/remote/state`
    const res = await fetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ token, state })
    })

    if (res.status === 404) {
      // Session lost, re-register
      await registerSession(token)
      // Try syncing once more
      await fetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ token, state })
      })
    }
    return true
  } catch (e) {
    // console.error('Failed to sync state:', e)
    return false
  }
}

// Get remote state (Called by Remote Device)
// Returns { ...state, connected, sessionExists, requiresPassword } or { error, notFound }
export const getRemoteState = async (token) => {
  try {
    const url = `${getApiBaseUrl()}/api/remote/session/${encodeURIComponent(token)}`
    const res = await fetch(url)
    if (res.ok) {
      const data = await res.json()
      const state = data?.state && typeof data.state === 'object' ? data.state : {}
      return {
        ...state,
        connected: data?.connected !== false,
        requiresPassword: !!data?.requiresPassword,
        sessionExists: true,
        error: false
      }
    }
    if (res.status === 404) {
      return { error: true, notFound: true, connected: false, sessionExists: false }
    }
    return { error: true, connected: false, status: res.status }
  } catch (e) {
    return { error: true, connected: false, networkError: true }
  }
}

// Verify session password
export const verifySessionPassword = async (token, password) => {
  try {
    const url = `${getApiBaseUrl()}/api/remote/verify`
    const res = await fetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ token, password })
    })
    if (res.ok) {
      if (typeof window !== 'undefined') {
        sessionStorage.setItem(`session_auth_${token}`, password)
      }
      return { success: true, verified: true }
    }
    const err = await res.json().catch(() => ({}))
    return { success: false, message: err.message || 'Mật khẩu không chính xác' }
  } catch (e) {
    return { success: false, message: 'Lỗi kết nối máy chủ' }
  }
}

/**
 * Ensure session exists on server (shell).
 * Player or remote can call — so Chrome remote test works even before player opens.
 */
export const ensureSession = async (token, roomCode = null, password = null) => {
  if (!token) return null
  return registerSession(token, roomCode, password)
}

export const getRemoteControlUrl = () => {
  const baseUrl = window.location.origin
  const token = getSessionToken()
  return `${baseUrl}/remote?session=${token}`
}

// Join session by 4-digit room code (vkara-style)
export const joinByRoomCode = async (code) => {
  try {
    const url = `${getApiBaseUrl()}/api/remote/room/${encodeURIComponent(code)}`
    const res = await fetch(url)
    if (res.ok) {
      const data = await res.json()
      return data // { status, token, roomCode }
    }
    return null
  } catch (e) {
    console.error('Failed to join by room code:', e)
    return null
  }
}
