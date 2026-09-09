/**
 * Session Store - Manages remote control session state
 * Inspired by vkara's websocketStore pattern using Zustand
 */
import { create } from 'zustand'

const STORAGE_KEY_REMOTE_SESSION = 'remote_control_session'
const STORAGE_KEY_REMOTE_ENABLED = 'remote_control_enabled'
const STORAGE_KEY_ROOM_CODE = 'remote_room_code'

// Generate unique session token (Short 6-character alphanumeric)
const generateSessionToken = () => {
  const chars = '0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ'
  let result = ''
  for (let i = 0; i < 6; i++) {
    result += chars.charAt(Math.floor(Math.random() * chars.length))
  }
  return result
}

// Generate 4-digit numeric room code
const generateRoomCode = () => {
  return Math.floor(1000 + Math.random() * 9000).toString()
}

// Get initial token from localStorage or create new
const getInitialToken = () => {
  let token = localStorage.getItem(STORAGE_KEY_REMOTE_SESSION)
  if (!token) {
    token = generateSessionToken()
    localStorage.setItem(STORAGE_KEY_REMOTE_SESSION, token)
  }
  return token
}

const getInitialRoomCode = () => {
  let code = localStorage.getItem(STORAGE_KEY_ROOM_CODE)
  if (!code) {
    code = generateRoomCode()
    localStorage.setItem(STORAGE_KEY_ROOM_CODE, code)
  }
  return code
}

const getInitialEnabled = () => {
  const stored = localStorage.getItem(STORAGE_KEY_REMOTE_ENABLED)
  if (stored === null) {
    localStorage.setItem(STORAGE_KEY_REMOTE_ENABLED, 'true')
    return true
  }
  return stored === 'true'
}

export const useSessionStore = create((set, get) => ({
  // State
  sessionToken: getInitialToken(),
  roomCode: getInitialRoomCode(),
  isRemoteEnabled: getInitialEnabled(),
  connectionStatus: 'disconnected', // 'disconnected' | 'connecting' | 'connected'
  isRemoteDeviceConnected: false,
  sessionConflict: false,
  connectionEpoch: 0,

  // Actions
  setConnectionStatus: (status) => set({ connectionStatus: status }),
  
  setRemoteDeviceConnected: (connected) => set({ isRemoteDeviceConnected: connected }),
  
  setSessionConflict: (conflict) => set({ sessionConflict: conflict }),

  setRemoteEnabled: (enabled) => {
    localStorage.setItem(STORAGE_KEY_REMOTE_ENABLED, enabled ? 'true' : 'false')
    set({ isRemoteEnabled: enabled })
  },

  resetSessionToken: () => {
    const newToken = generateSessionToken()
    localStorage.setItem(STORAGE_KEY_REMOTE_SESSION, newToken)
    set({ sessionToken: newToken, connectionEpoch: get().connectionEpoch + 1 })
    return newToken
  },

  /** Force session (ShowCue dual-WebView / URL ?session=) */
  setSessionToken: (token) => {
    if (!token || typeof token !== 'string') return get().sessionToken
    let t = token.trim().toUpperCase().replace(/[^0-9A-Z]/g, '')
    if (t.length < 4) return get().sessionToken
    t = t.slice(0, 16)
    localStorage.setItem(STORAGE_KEY_REMOTE_SESSION, t)
    set({ sessionToken: t, connectionEpoch: get().connectionEpoch + 1 })
    return t
  },

  resetRoomCode: () => {
    const newCode = generateRoomCode()
    localStorage.setItem(STORAGE_KEY_ROOM_CODE, newCode)
    set({ roomCode: newCode })
    return newCode
  },

  bumpConnectionEpoch: () => set((state) => ({ connectionEpoch: state.connectionEpoch + 1 })),

  getRemoteControlUrl: () => {
    const roomCode = get().roomCode
    const baseUrl = window.location.origin
    return `${baseUrl}/remote?room=${roomCode}`
  },
}))
