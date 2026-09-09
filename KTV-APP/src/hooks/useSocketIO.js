// Socket.IO client hook for remote control
// Replaces raw WebSocket with Socket.IO for better reliability

import { useEffect, useRef, useState, useCallback } from 'react'
import { io } from 'socket.io-client'

/**
 * Socket.IO hook for remote control
 * @param {string} serverUrl - Socket.IO server URL
 * @param {string} roomCode - Room code to join
 * @param {string} role - 'master' or 'client'
 * @param {object} options - Socket.IO options
 */
export function useSocketIO(serverUrl, roomCode, role = 'client', options = {}) {
  const [isConnected, setIsConnected] = useState(false)
  const [roomInfo, setRoomInfo] = useState(null)
  const [roomState, setRoomState] = useState(null)
  const [error, setError] = useState(null)
  
  const socketRef = useRef(null)
  const reconnectAttemptsRef = useRef(0)
  const commandCallbacksRef = useRef(new Map())

  // Initialize Socket.IO connection
  useEffect(() => {
    if (!serverUrl || !roomCode) return

    console.log(`[Socket.IO Hook] Connecting to ${serverUrl}...`)

    // Create socket with auto-reconnection
    const socket = io(serverUrl, {
      transports: ['websocket', 'polling'], // Auto fallback
      reconnection: true,
      reconnectionDelay: 500,
      reconnectionDelayMax: 5000,
      reconnectionAttempts: 5,
      timeout: 10000,
      ...options
    })

    socketRef.current = socket

    // Connection events
    socket.on('connect', () => {
      console.log(`[Socket.IO] Connected with id: ${socket.id}`)
      setIsConnected(true)
      setError(null)
      reconnectAttemptsRef.current = 0

      // Join room
      socket.emit('room:join', { roomCode, role })
    })

    socket.on('disconnect', (reason) => {
      console.log(`[Socket.IO] Disconnected: ${reason}`)
      setIsConnected(false)
    })

    socket.on('connect_error', (err) => {
      console.error('[Socket.IO] Connection error:', err.message)
      setError(`Connection error: ${err.message}`)
      reconnectAttemptsRef.current++
    })

    // Room events
    socket.on('room:joined', (data) => {
      console.log('[Socket.IO] Joined room:', data)
      setRoomInfo(data)
    })

    socket.on('room:state', (state) => {
      console.log('[Socket.IO] Received room state')
      setRoomState(state)
    })

    socket.on('state:sync', (state) => {
      console.log('[Socket.IO] State synced')
      setRoomState(state)
    })

    socket.on('room:user-joined', (data) => {
      console.log('[Socket.IO] User joined:', data.socketId)
    })

    socket.on('room:user-left', (data) => {
      console.log('[Socket.IO] User left:', data.socketId)
    })

    socket.on('room:master-disconnected', (data) => {
      console.warn('[Socket.IO] Master disconnected')
      setError('Master disconnected from room')
    })

    // Command events
    socket.on('command:receive', (command) => {
      console.log('[Socket.IO] Command received:', command.type)
      
      // Execute registered callbacks
      commandCallbacksRef.current.forEach(callback => {
        try {
          callback(command)
        } catch (err) {
          console.error('[Socket.IO] Command callback error:', err)
        }
      })
    })

    socket.on('command:from-client', (data) => {
      console.log('[Socket.IO] Command from client:', data.command.type)
      
      // Execute registered callbacks
      commandCallbacksRef.current.forEach(callback => {
        try {
          callback(data.command)
        } catch (err) {
          console.error('[Socket.IO] Command callback error:', err)
        }
      })
    })

    socket.on('command:ack', (data) => {
      console.log('[Socket.IO] Command acknowledged:', data.commandId)
    })

    // Error events
    socket.on('error', (data) => {
      console.error('[Socket.IO] Server error:', data.message)
      setError(data.message)
    })

    // Cleanup on unmount
    return () => {
      console.log('[Socket.IO] Cleaning up connection')
      socket.off('connect')
      socket.off('disconnect')
      socket.off('connect_error')
      socket.off('room:joined')
      socket.off('room:state')
      socket.off('state:sync')
      socket.off('room:user-joined')
      socket.off('room:user-left')
      socket.off('room:master-disconnected')
      socket.off('command:receive')
      socket.off('command:from-client')
      socket.off('command:ack')
      socket.off('error')
      socket.disconnect()
      socketRef.current = null
    }
  }, [serverUrl, roomCode, role, options])

  // Send command (master to clients)
  const sendCommand = useCallback((command) => {
    if (!socketRef.current || !isConnected) {
      console.warn('[Socket.IO] Cannot send command: not connected')
      return
    }

    socketRef.current.emit('command:send', {
      roomCode,
      command: {
        ...command,
        id: command.id || Date.now(),
        timestamp: Date.now()
      }
    })
  }, [isConnected, roomCode])

  // Send command to master (client to master)
  const sendCommandToMaster = useCallback((command) => {
    if (!socketRef.current || !isConnected) {
      console.warn('[Socket.IO] Cannot send command: not connected')
      return
    }

    socketRef.current.emit('command:to-master', {
      roomCode,
      command: {
        ...command,
        id: command.id || Date.now(),
        timestamp: Date.now()
      }
    })
  }, [isConnected, roomCode])

  // Update state (master only)
  const updateState = useCallback((state) => {
    if (!socketRef.current || !isConnected) {
      console.warn('[Socket.IO] Cannot update state: not connected')
      return
    }

    socketRef.current.emit('state:update', {
      roomCode,
      state
    })
  }, [isConnected, roomCode])

  // Request current state
  const requestState = useCallback(() => {
    if (!socketRef.current || !isConnected) {
      console.warn('[Socket.IO] Cannot request state: not connected')
      return
    }

    socketRef.current.emit('state:request', { roomCode })
  }, [isConnected, roomCode])

  // Register command listener
  const onCommand = useCallback((callback) => {
    const id = Date.now() + Math.random()
    commandCallbacksRef.current.set(id, callback)
    
    // Return cleanup function
    return () => {
      commandCallbacksRef.current.delete(id)
    }
  }, [])

  return {
    isConnected,
    roomInfo,
    roomState,
    error,
    sendCommand,
    sendCommandToMaster,
    updateState,
    requestState,
    onCommand,
    socket: socketRef.current
  }
}

export default useSocketIO