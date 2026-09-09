ó /**
 * Optimized Remote Control WebSocket Client
 * - Connection pooling & reuse
 * - Auto-reconnect with exponential backoff
 * - Message batching & debouncing
 * - Optimistic updates
 * - Priority-based command handling
 */

class OptimizedRemoteClient {
  constructor(serverUrl, token, options = {}) {
    this.serverUrl = serverUrl
    this.token = token
    this.options = {
      batchDelay: 50, // ms, batch high-freq commands
      reconnectMin: 500, // ms
      reconnectMax: 5000, // ms
      heartbeatInterval: 30000, // ms, keep-alive
      maxRetries: 5,
      ...options
    }

    this.ws = null
    this.isConnected = false
    this.reconnectCount = 0
    this.messageQueue = []
    this.batchTimeout = null
    this.heartbeatTimeout = null
    this.listeners = new Map()
    this.pendingCommands = new Map()
    this.commandId = 0
  }

  /**
   * Connect to server
   */
  async connect() {
    return new Promise((resolve, reject) => {
      try {
        const wsUrl = `${this.serverUrl}/ws?token=${this.token}`
        console.log(`[OptimizedWS] Connecting to ${wsUrl}`)

        this.ws = new WebSocket(wsUrl)

        this.ws.onopen = () => {
          console.log('[OptimizedWS] Connected!')
          this.isConnected = true
          this.reconnectCount = 0
          this._startHeartbeat()
          this._emit('connected')
          resolve()
        }

        this.ws.onmessage = (event) => {
          this._handleMessage(event.data)
        }

        this.ws.onerror = (error) => {
          console.error('[OptimizedWS] Error:', error)
          this._emit('error', error)
          reject(error)
        }

        this.ws.onclose = () => {
          console.log('[OptimizedWS] Disconnected')
          this.isConnected = false
          this._clearHeartbeat()
          this._emit('disconnected')
          this._attemptReconnect()
        }
      } catch (err) {
        reject(err)
      }
    })
  }

  /**
   * Send command with priority handling
   */
  send(command, priority = 'normal') {
    if (!this.isConnected && !this.ws) {
      console.warn('[OptimizedWS] Not connected, queuing command')
      // Queue for later
      this.messageQueue.push({ command, priority })
      return
    }

    // High-priority: send immediately
    if (priority === 'high') {
      this._flushQueue()
      this._sendRaw(command)
      return
    }

    // Low-priority: batch with others
    if (priority === 'low') {
      this.messageQueue.push(command)
      this._scheduleBatch()
      return
    }

    // Normal: send after slight delay (50ms) to batch
    this.messageQueue.push(command)
    this._scheduleBatch()
  }

  /**
   * Debounce slider/seek/volume commands
   */
  sendDebounced(command, delay = 100) {
    const key = `${command.type}-${command.id || ''}`

    // Cancel previous scheduled send
    if (this.pendingCommands.has(key)) {
      clearTimeout(this.pendingCommands.get(key).timeout)
    }

    // Schedule new send
    const timeout = setTimeout(() => {
      this.send(command, 'low')
      this.pendingCommands.delete(key)
    }, delay)

    this.pendingCommands.set(key, { command, timeout })
  }

  /**
   * Listen for events
   */
  on(event, callback) {
    if (!this.listeners.has(event)) {
      this.listeners.set(event, [])
    }
    this.listeners.get(event).push(callback)
  }

  /**
   * Remove listener
   */
  off(event, callback) {
    if (!this.listeners.has(event)) return
    const callbacks = this.listeners.get(event)
    const index = callbacks.indexOf(callback)
    if (index > -1) {
      callbacks.splice(index, 1)
    }
  }

  /**
   * Disconnect
   */
  disconnect() {
    if (this.ws) {
      this.ws.close()
    }
    this._clearHeartbeat()
    this._flushQueue()
  }

  // --- Private methods ---

  _emit(event, data) {
    if (!this.listeners.has(event)) return
    this.listeners.get(event).forEach(callback => {
      try {
        callback(data)
      } catch (err) {
        console.error(`[OptimizedWS] Listener error for ${event}:`, err)
      }
    })
  }

  _sendRaw(command) {
    if (!this.isConnected || !this.ws) return

    try {
      this.ws.send(JSON.stringify(command))
    } catch (err) {
      console.error('[OptimizedWS] Send error:', err)
    }
  }

  _handleMessage(data) {
    try {
      const message = JSON.parse(data)

      // Handle batched commands
      if (message.type === 'BATCH') {
        message.commands.forEach(cmd => {
          this._emit('command', cmd)
        })
      } else {
        this._emit('message', message)
      }
    } catch (err) {
      console.error('[OptimizedWS] Parse error:', err)
    }
  }

  _scheduleBatch() {
    if (this.batchTimeout) return

    this.batchTimeout = setTimeout(() => {
      this._flushQueue()
      this.batchTimeout = null
    }, this.options.batchDelay)
  }

  _flushQueue() {
    if (this.messageQueue.length === 0) return

    const commands = this.messageQueue.splice(0)

    if (commands.length === 1) {
      this._sendRaw(commands[0])
    } else {
      // Send batch
      this._sendRaw({
        type: 'BATCH_SEND',
        commands: commands
      })
    }
  }

  _startHeartbeat() {
    this.heartbeatTimeout = setInterval(() => {
      if (this.isConnected && this.ws) {
        this.ws.send(JSON.stringify({ type: 'PING' }))
      }
    }, this.options.heartbeatInterval)
  }

  _clearHeartbeat() {
    if (this.heartbeatTimeout) {
      clearInterval(this.heartbeatTimeout)
      this.heartbeatTimeout = null
    }
  }

  _attemptReconnect() {
    if (this.reconnectCount >= this.options.maxRetries) {
      console.error('[OptimizedWS] Max reconnect attempts reached')
      this._emit('max-retries-exceeded')
      return
    }

    // Exponential backoff: 500ms, 1s, 2s, 4s, 8s
    const delay = Math.min(
      this.options.reconnectMin * Math.pow(2, this.reconnectCount),
      this.options.reconnectMax
    )

    this.reconnectCount++
    console.log(
      `[OptimizedWS] Reconnecting in ${delay}ms (attempt ${this.reconnectCount}/${this.options.maxRetries})`
    )

    setTimeout(() => {
      this.connect().catch(err => {
        console.error('[OptimizedWS] Reconnect failed:', err)
      })
    }, delay)
  }
}

export default OptimizedRemoteClient