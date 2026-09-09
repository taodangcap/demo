// Remote Control Server with Socket.IO
// Deploy on Render (free tier)
// Socket.IO for real-time remote control (better than raw WebSocket)

import express from 'express'
import { createServer } from 'http'
import { Server } from 'socket.io'
import cors from 'cors'
import dotenv from 'dotenv'

dotenv.config()

const app = express()
const server = createServer(app)

// Socket.IO server
const io = new Server(server, {
  cors: {
    origin: [
      'http://localhost:3000',
      'http://localhost:5173',
      'https://huy.sale',
      'https://www.huy.sale',
      'https://ktv-app.onrender.com',
      'https://trinhquanghuy.net',
      'https://www.trinhquanghuy.net'
    ],
    methods: ['GET', 'POST'],
    credentials: true
  },
  transports: ['websocket', 'polling'], // Auto fallback
  connectionStateRecovery: {
    maxDisconnectionDuration: 2 * 60 * 1000, // 2 minutes
    skipMiddlewares: true
  }
})

// In-memory storage (for free tier)
const rooms = new Map() // roomCode -> { master: socketId, clients: Set, state: {} }
const socketToRoom = new Map() // socket.id -> roomCode
const pendingCommands = new Map() // roomCode -> [commands]

// CORS settings for HTTP API
app.use(cors({
  origin: function(origin, callback) {
    const allowedOrigins = [
      'http://localhost:3000',
      'http://localhost:5173',
      'https://huy.sale',
      'https://www.huy.sale',
      'https://ktv-app.onrender.com',
      'https://trinhquanghuy.net',
      'https://www.trinhquanghuy.net'
    ]
    
    if (!origin || allowedOrigins.some(ao => origin.toLowerCase() === ao.toLowerCase())) {
      callback(null, true)
    } else {
      callback(new Error('Not allowed by CORS'))
    }
  }
}))

app.use(express.json())

// --- Socket.IO Event Handlers ---

io.on('connection', (socket) => {
  console.log(`[Socket.IO] Client connected: ${socket.id}`)
  
  // Client joins room
  socket.on('room:join', (data) => {
    const { roomCode, role = 'client' } = data
    
    if (!roomCode) {
      socket.emit('error', { message: 'Room code required' })
      return
    }
    
    // Create room if doesn't exist
    if (!rooms.has(roomCode)) {
      rooms.set(roomCode, {
        master: role === 'master' ? socket.id : null,
        clients: new Set(),
        state: {
          playlist: [],
          currentVideo: null,
          isPlaying: false,
          volume: 50,
          position: 0
        }
      })
    }
    
    const room = rooms.get(roomCode)
    
    // Store socket -> room mapping
    socketToRoom.set(socket.id, roomCode)
    
    // Join Socket.IO room
    socket.join(roomCode)
    
    // Update room info
    if (role === 'master') {
      room.master = socket.id
    }
    room.clients.add(socket.id)
    
    console.log(`[Socket.IO] ${socket.id} joined room ${roomCode} as ${role}`)
    
    // Notify room
    socket.to(roomCode).emit('room:user-joined', {
      socketId: socket.id,
      role: role,
      roomSize: room.clients.size
    })
    
    // Send current state to joining client
    socket.emit('room:state', room.state)
    
    // Send room info
    socket.emit('room:joined', {
      roomCode: roomCode,
      role: role,
      roomSize: room.clients.size,
      masterConnected: !!room.master
    })
  })
  
  // Master sends command to clients
  socket.on('command:send', (data) => {
    const { roomCode, command, priority = 'normal' } = data
    
    if (!roomCode || !command) {
      socket.emit('error', { message: 'Room code and command required' })
      return
    }
    
    const room = rooms.get(roomCode)
    if (!room) {
      socket.emit('error', { message: 'Room not found' })
      return
    }
    
    // Check if sender is master
    if (room.master !== socket.id) {
      socket.emit('error', { message: 'Only master can send commands' })
      return
    }
    
    // Store command for history
    if (!pendingCommands.has(roomCode)) {
      pendingCommands.set(roomCode, [])
    }
    pendingCommands.get(roomCode).push({
      ...command,
      timestamp: Date.now(),
      from: socket.id
    })
    
    console.log(`[Socket.IO] Command in room ${roomCode}:`, command.type)
    
    // Broadcast to all clients in room (except master)
    socket.to(roomCode).emit('command:receive', command)
    
    // Send acknowledgment
    socket.emit('command:ack', {
      commandId: command.id,
      success: true,
      timestamp: Date.now()
    })
  })
  
  // Client sends command to master
  socket.on('command:to-master', (data) => {
    const { roomCode, command } = data
    
    if (!roomCode || !command) {
      socket.emit('error', { message: 'Room code and command required' })
      return
    }
    
    const room = rooms.get(roomCode)
    if (!room) {
      socket.emit('error', { message: 'Room not found' })
      return
    }
    
    // Check if master is connected
    if (!room.master) {
      socket.emit('error', { message: 'Master not connected' })
      return
    }
    
    // Forward to master
    const masterSocket = io.sockets.sockets.get(room.master)
    if (masterSocket) {
      masterSocket.emit('command:from-client', {
        command: command,
        from: socket.id
      })
      console.log(`[Socket.IO] Command forwarded to master in room ${roomCode}`)
    }
  })
  
  // State synchronization
  socket.on('state:update', (data) => {
    const { roomCode, state } = data
    
    if (!roomCode || !state) {
      socket.emit('error', { message: 'Room code and state required' })
      return
    }
    
    const room = rooms.get(roomCode)
    if (!room) {
      socket.emit('error', { message: 'Room not found' })
      return
    }
    
    // Only master can update state
    if (room.master !== socket.id) {
      socket.emit('error', { message: 'Only master can update state' })
      return
    }
    
    // Update room state
    room.state = { ...room.state, ...state }
    
    console.log(`[Socket.IO] State updated in room ${roomCode}`)
    
    // Broadcast to all clients
    socket.to(roomCode).emit('state:sync', room.state)
  })
  
  // Request state
  socket.on('state:request', (data) => {
    const { roomCode } = data
    const room = rooms.get(roomCode)
    
    if (room) {
      socket.emit('state:sync', room.state)
    }
  })
  
  // Heartbeat/ping (Socket.IO handles this automatically)
  socket.on('ping', () => {
    socket.emit('pong', { timestamp: Date.now() })
  })
  
  // Disconnection handling
  socket.on('disconnect', (reason) => {
    console.log(`[Socket.IO] Client disconnected: ${socket.id} (${reason})`)
    
    const roomCode = socketToRoom.get(socket.id)
    if (!roomCode) return
    
    const room = rooms.get(roomCode)
    if (!room) return
    
    // Remove from room
    room.clients.delete(socket.id)
    socketToRoom.delete(socket.id)
    
    // If master disconnected
    if (room.master === socket.id) {
      room.master = null
      
      // Notify clients that master disconnected
      io.to(roomCode).emit('room:master-disconnected', {
        roomCode: roomCode,
        masterId: socket.id
      })
    }
    
    // Cleanup empty room
    if (room.clients.size === 0) {
      rooms.delete(roomCode)
      pendingCommands.delete(roomCode)
      console.log(`[Socket.IO] Room ${roomCode} cleaned up`)
    } else {
      // Notify remaining users
      socket.to(roomCode).emit('room:user-left', {
        socketId: socket.id,
        roomSize: room.clients.size
      })
    }
  })
})

// --- HTTP API Endpoints (Fallback/Compatibility) ---

// Health check
app.get('/health', (req, res) => {
  res.json({ 
    status: 'ok', 
    service: 'KTV Remote Control Server (Socket.IO)',
    socketio: true,
    timestamp: new Date().toISOString(),
    rooms: rooms.size,
    clients: Array.from(io.sockets.sockets.values()).length
  })
})

// Create room (HTTP API)
app.post('/api/room/create', (req, res) => {
  const { userId } = req.body
  
  // Generate 6-character room code
  const characters = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789'
  let roomCode = ''
  for (let i = 0; i < 6; i++) {
    roomCode += characters.charAt(Math.floor(Math.random() * characters.length))
  }
  
  // Initialize room
  rooms.set(roomCode, {
    master: userId || null,
    clients: new Set(),
    state: {
      playlist: [],
      currentVideo: null,
      isPlaying: false,
      volume: 50,
      position: 0
    }
  })
  
  console.log(`[API] Room created: ${roomCode}`)
  
  res.json({
    success: true,
    roomCode: roomCode,
    message: 'Room created. Connect via Socket.IO to join.'
  })
})

// Get room info
app.get('/api/room/:roomCode', (req, res) => {
  const { roomCode } = req.params
  const room = rooms.get(roomCode)
  
  if (!room) {
    return res.status(404).json({ error: 'Room not found' })
  }
  
  res.json({
    success: true,
    roomCode: roomCode,
    masterConnected: !!room.master,
    clientCount: room.clients.size,
    state: room.state
  })
})

// HTTP command fallback (for devices that can't use WebSocket)
app.post('/api/command/:roomCode', (req, res) => {
  const { roomCode } = req.params
  const { command, sender } = req.body
  
  console.log(`[API-HTTP] Command via HTTP: ${command.type} in ${roomCode}`)
  
  const room = rooms.get(roomCode)
  if (!room) {
    return res.status(404).json({ error: 'Room not found' })
  }
  
  // Store command
  if (!pendingCommands.has(roomCode)) {
    pendingCommands.set(roomCode, [])
  }
  pendingCommands.get(roomCode).push({
    ...command,
    timestamp: Date.now(),
    from: sender || 'http-api'
  })
  
  // If master is connected, forward via Socket.IO
  if (room.master) {
    const masterSocket = io.sockets.sockets.get(room.master)
    if (masterSocket) {
      masterSocket.emit('command:from-client', {
        command: command,
        from: sender || 'http-api'
      })
    }
  }
  
  res.json({ success: true, received: true })
})

// Get pending commands (polling fallback)
app.get('/api/commands/:roomCode', (req, res) => {
  const { roomCode } = req.params
  
  if (!pendingCommands.has(roomCode)) {
    return res.json({ commands: [] })
  }
  
  const commands = pendingCommands.get(roomCode)
  pendingCommands.set(roomCode, []) // Clear after reading
  
  res.json({ commands: commands })
})

// Start server
const PORT = process.env.PORT || 3001

server.listen(PORT, () => {
  console.log(`🚀 Remote Control Server (Socket.IO) running on port ${PORT}`)
  console.log(`📝 HTTP API: http://localhost:${PORT}/api/*`)
  console.log(`📡 Socket.IO: ws://localhost:${PORT}`)
  console.log(`✅ Transports: ${io.opts.transports.join(', ')}`)
})

// Cleanup on exit
process.on('SIGTERM', () => {
  console.log('SIGTERM received, shutting down...')
  server.close(() => {
    console.log('Server closed')
    process.exit(0)
  })
})

export default { app, server, io }