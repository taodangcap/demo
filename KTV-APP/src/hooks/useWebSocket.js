/**
 * useWebSocket - Robust WebSocket hook with auto-reconnect, heartbeat & message queuing
 * Inspired by vkara's WebSocketManager singleton pattern
 * 
 * Features:
 * - Auto-reconnect with exponential backoff
 * - Heartbeat ping/pong to detect dead connections
 * - Message queue for commands sent while disconnected
 * - Singleton pattern per session token
 * - Integrates with Zustand sessionStore
 */
import { useEffect, useRef, useCallback } from 'react'
import { useSessionStore } from '../store/sessionStore'

// ========== WebSocket Manager (Singleton per token) ==========
class WebSocketManager {
  constructor(config) {
    this.url = config.url
    this.token = config.token
    this.role = config.role // 'player' | 'remote'
    this.password = config.password || null
    this.onMessage = config.onMessage || (() => {})
    this.onStatusChange = config.onStatusChange || (() => {})

    this.socket = null
    this.messageQueue = []
    this.reconnectAttempts = 0
    this.maxReconnectAttempts = 50
    this.initialRetryDelay = 800
    this.retryDelay = this.initialRetryDelay
    this.maxRetryDelay = 30000
    this.heartbeatIntervalMs = 25000
    this.heartbeatTimeoutMs = 10000
    this.heartbeatInterval = null
    this.heartbeatTimeout = null
    this.reconnectTimeout = null
    this.intentionalClose = false
    this.isConnected = false
    this.lastConnectionTime = 0
  }

  connect() {
    if (this.socket?.readyState === WebSocket.OPEN) return
    if (this.socket?.readyState === WebSocket.CONNECTING) return
    if (this.intentionalClose) return

    this.cleanupSocket()
    this.onStatusChange('connecting')

    try {
      this.socket = new WebSocket(this.url)

      this.socket.onopen = () => {
        this.isConnected = true
        this.reconnectAttempts = 0
        this.retryDelay = this.initialRetryDelay
        this.lastConnectionTime = Date.now()
        this.onStatusChange('connected')

        // Join the session
        this.sendRaw({
          type: 'join',
          token: this.token,
          role: this.role,
          password: this.password
        })

        // Flush queued messages
        this.flushQueue()

        // Start heartbeat
        this.startHeartbeat()
      }

      this.socket.onmessage = (event) => {
        try {
          const message = JSON.parse(event.data)

          if (message.type === 'pong') {
            // Heartbeat response - connection is alive
            if (this.heartbeatTimeout) {
              clearTimeout(this.heartbeatTimeout)
              this.heartbeatTimeout = null
            }
            return
          }

          this.onMessage(message)
        } catch (e) {
          console.error('[WS] Failed to parse message:', e)
        }
      }

      this.socket.onclose = () => {
        this.isConnected = false
        this.onStatusChange('disconnected')
        this.cleanupTimers()

        if (!this.intentionalClose) {
          this.scheduleReconnect()
        }
      }

      this.socket.onerror = () => {
        this.isConnected = false
        this.cleanupSocket()

        if (!this.intentionalClose) {
          this.scheduleReconnect()
        }
      }
    } catch (e) {
      console.error('[WS] Connection error:', e)
      this.scheduleReconnect()
    }
  }

  disconnect() {
    this.intentionalClose = true
    this.isConnected = false
    this.cleanupSocket()
    this.cleanupTimers()
    this.onStatusChange('disconnected')
  }

  sendRaw(message) {
    if (this.socket?.readyState === WebSocket.OPEN) {
      try {
        this.socket.send(JSON.stringify(message))
        return true
      } catch (e) {
        console.error('[WS] Send error:', e)
        return false
      }
    }
    return false
  }

  // Send a message, queuing it if not connected
  send(message) {
    if (this.socket?.readyState === WebSocket.OPEN) {
      return this.sendRaw(message)
    }
    // Queue for later delivery
    this.messageQueue.push(message)
    if (this.messageQueue.length > 50) {
      this.messageQueue = this.messageQueue.slice(-30)
    }
    return false
  }

  // Send a remote command
  sendCommand(command, data = {}) {
    return this.send({
      type: 'command',
      command,
      data
    })
  }

  // Send state sync (player -> remotes)
  syncState(state) {
    return this.send({
      type: 'state_sync',
      state
    })
  }

  flushQueue() {
    while (this.messageQueue.length > 0) {
      const msg = this.messageQueue.shift()
      if (!this.sendRaw(msg)) {
        this.messageQueue.unshift(msg)
        break
      }
    }
  }

  startHeartbeat() {
    this.stopHeartbeat()
    this.heartbeatInterval = setInterval(() => {
      if (this.socket?.readyState !== WebSocket.OPEN) return

      this.sendRaw({ type: 'ping', timestamp: Date.now() })

      // If no pong within timeout, force reconnect
      this.heartbeatTimeout = setTimeout(() => {
        console.warn('[WS] Heartbeat timeout - reconnecting...')
        this.forceReconnect()
      }, this.heartbeatTimeoutMs)
    }, this.heartbeatIntervalMs)
  }

  stopHeartbeat() {
    if (this.heartbeatInterval) {
      clearInterval(this.heartbeatInterval)
      this.heartbeatInterval = null
    }
    if (this.heartbeatTimeout) {
      clearTimeout(this.heartbeatTimeout)
      this.heartbeatTimeout = null
    }
  }

  scheduleReconnect() {
    if (this.intentionalClose) return
    if (this.reconnectAttempts >= this.maxReconnectAttempts) {
      console.error('[WS] Max reconnection attempts reached')
      this.onStatusChange('disconnected')
      return
    }

    this.onStatusChange('connecting')

    // Fast reconnect if was recently connected
    const timeSinceLastConnection = Date.now() - this.lastConnectionTime
    const isRecentDisconnect = timeSinceLastConnection < 10000
    const reconnectDelay = isRecentDisconnect
      ? Math.min(500, this.retryDelay)
      : this.retryDelay

    if (this.reconnectTimeout) clearTimeout(this.reconnectTimeout)

    this.reconnectTimeout = setTimeout(() => {
      this.reconnectTimeout = null
      this.reconnectAttempts++
      this.retryDelay = Math.min(this.retryDelay * 1.5, this.maxRetryDelay)
      this.connect()
    }, reconnectDelay)
  }

  forceReconnect() {
    this.intentionalClose = false
    this.reconnectAttempts = 0
    this.retryDelay = this.initialRetryDelay
    this.cleanupSocket()
    this.cleanupTimers()
    this.connect()
  }

  cleanupSocket() {
    if (this.socket) {
      this.socket.onopen = null
      this.socket.onclose = null
      this.socket.onerror = null
      this.socket.onmessage = null
      try { this.socket.close() } catch { /* already closed */ }
      this.socket = null
    }
  }

  cleanupTimers() {
    this.stopHeartbeat()
    if (this.reconnectTimeout) {
      clearTimeout(this.reconnectTimeout)
      this.reconnectTimeout = null
    }
  }

  destroy() {
    this.intentionalClose = true
    this.cleanupSocket()
    this.cleanupTimers()
    this.messageQueue = []
  }
}

// ========== Helper: Build WebSocket URL ==========
const getWebSocketUrl = () => {
  const host = window.location.hostname
  const isLocal = host === 'localhost' || host === '127.0.0.1'
  const isLan = /^(192\.168\.|10\.|172\.(1[6-9]|2[0-9]|3[0-1])\.)/.test(host)

  // In production, use the same host with wss://
  if (!isLocal && !isLan) {
    const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:'
    const envUrl = import.meta.env.VITE_REMOTE_SERVER_URL
    if (envUrl) {
      const parsed = new URL(envUrl)
      return `${parsed.protocol === 'https:' ? 'wss:' : 'ws:'}//${parsed.host}/ws`
    }
    return `${protocol}//${window.location.host}/ws`
  }

  // Local development - connect to server on port 3001
  return `ws://${host}:3001/ws`
}

// ========== React Hook ==========
/**
 * @param {Object} options
 * @param {'player'|'remote'} options.role - Client role
 * @param {string} [options.token] - Session token (defaults to store token)
 * @param {string} [options.password] - Session password for authentication
 * @param {function} [options.onMessage] - Message handler
 * @param {boolean} [options.enabled=true] - Whether to connect
 */
export function useWebSocket({ role, token: propToken, password, onMessage, enabled = true }) {
  const storeToken = useSessionStore((s) => s.sessionToken)
  const setConnectionStatus = useSessionStore((s) => s.setConnectionStatus)
  const setRemoteDeviceConnected = useSessionStore((s) => s.setRemoteDeviceConnected)
  
  const token = propToken || storeToken
  const managerRef = useRef(null)
  const onMessageRef = useRef(onMessage)

  // Keep callback ref fresh
  useEffect(() => {
    onMessageRef.current = onMessage
  }, [onMessage])

  // Create/destroy manager
  useEffect(() => {
    if (!enabled || !token) {
      if (managerRef.current) {
        managerRef.current.destroy()
        managerRef.current = null
      }
      setConnectionStatus('disconnected')
      return
    }

    const wsUrl = getWebSocketUrl()

    const manager = new WebSocketManager({
      url: wsUrl,
      token,
      role,
      password,
      onMessage: (msg) => {
        // Handle session-level messages
        if (msg.type === 'peer_joined') {
          if (msg.hasRemote) setRemoteDeviceConnected(true)
        } else if (msg.type === 'peer_left') {
          if (!msg.hasRemote) setRemoteDeviceConnected(false)
        } else if (msg.type === 'joined') {
          if (msg.remoteConnected || msg.hasRemote) setRemoteDeviceConnected(true)
        }

        // Forward to consumer
        if (onMessageRef.current) {
          onMessageRef.current(msg)
        }
      },
      onStatusChange: (status) => {
        setConnectionStatus(status)
      }
    })

    managerRef.current = manager
    manager.connect()

    return () => {
      manager.destroy()
      managerRef.current = null
    }
  }, [enabled, token, role, password, setConnectionStatus, setRemoteDeviceConnected])

  // Stable API functions
  const sendCommand = useCallback((command, data = {}) => {
    if (managerRef.current) {
      return managerRef.current.sendCommand(command, data)
    }
    return false
  }, [])

  const syncState = useCallback((state) => {
    if (managerRef.current) {
      return managerRef.current.syncState(state)
    }
    return false
  }, [])

  const authenticate = useCallback((pwd) => {
    if (managerRef.current) {
      return managerRef.current.authenticate(pwd)
    }
    return false
  }, [])

  const send = useCallback((message) => {
    if (managerRef.current) {
      return managerRef.current.send(message)
    }
    return false
  }, [])

  const forceReconnect = useCallback(() => {
    if (managerRef.current) {
      managerRef.current.forceReconnect()
    }
  }, [])

  return {
    sendCommand,
    syncState,
    authenticate,
    send,
    forceReconnect,
    isConnected: managerRef.current?.isConnected ?? false,
  }
}

export default useWebSocket
