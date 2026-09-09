# Socket.IO Migration Guide for RemoteControl.jsx

## 📋 Overview

Remote control đã được upgrade từ raw WebSocket sang **Socket.IO** (như vkara).

**Improvements:**
- ✅ Built-in auto-reconnection (exponential backoff)
- ✅ Automatic fallback (WebSocket → Long polling)
- ✅ Room-based architecture (easier pairing)
- ✅ State synchronization (bi-directional)
- ✅ Better error handling

---

## 🔄 Migration Steps

### Step 1: Update RemoteControl.jsx to Use Socket.IO Hook

**Before (raw WebSocket):**
```javascript
const [socket, setSocket] = useState(null)

useEffect(() => {
  const ws = new WebSocket(serverUrl)
  ws.onmessage = (e) => {
    const data = JSON.parse(e.data)
    // handle message
  }
  setSocket(ws)
  return () => ws.close()
}, [serverUrl])
```

**After (Socket.IO):**
```javascript
import useSocketIO from '../hooks/useSocketIO'

const { 
  isConnected, 
  roomInfo,
  roomState,
  error,
  sendCommand, 
  sendCommandToMaster,
  updateState,
  onCommand 
} = useSocketIO(serverUrl, roomCode, 'client')
```

---

## 🎯 Basic Integration Example

```javascript
import { useEffect } from 'react'
import useSocketIO from '../hooks/useSocketIO'

export function RemoteControl() {
  const roomCode = 'ABC123' // Get from props or state
  const serverUrl = process.env.REACT_APP_REMOTE_SERVER || 'http://localhost:3001'
  
  // Initialize Socket.IO connection
  const { 
    isConnected, 
    error,
    sendCommandToMaster,
    onCommand 
  } = useSocketIO(serverUrl, roomCode, 'client')
  
  // Listen for commands from master
  useEffect(() => {
    const unsubscribe = onCommand((command) => {
      console.log('Command received:', command)
      
      // Handle different command types
      switch(command.type) {
        case 'PLAY':
          // Handle play
          break
        case 'PAUSE':
          // Handle pause
          break
        case 'VOLUME':
          // Handle volume change
          handleVolumeChange(command.value)
          break
        default:
          break
      }
    })
    
    return unsubscribe
  }, [onCommand])
  
  // Send command to master
  const handlePlayClick = () => {
    sendCommandToMaster({
      type: 'PLAY',
      videoId: 'dQw4w9WgXcQ'
    })
  }
  
  // Render UI
  return (
    <div>
      <div>Status: {isConnected ? '✅ Connected' : '❌ Disconnected'}</div>
      {error && <div className="error">{error}</div>}
      
      <button onClick={handlePlayClick}>Play</button>
      {/* Other controls */}
    </div>
  )
}
```

---

## 📤 Send Commands

### Master to Clients (Broadcast)
```javascript
const { sendCommand } = useSocketIO(serverUrl, roomCode, 'master')

// Send to all clients in room
sendCommand({
  type: 'PLAY',
  videoId: 'video123',
  startTime: 0
})
```

### Client to Master (Single)
```javascript
const { sendCommandToMaster } = useSocketIO(serverUrl, roomCode, 'client')

// Send only to master
sendCommandToMaster({
  type: 'SEEK',
  position: 120
})
```

---

## 🔄 State Synchronization

### Master Updates State
```javascript
const { updateState } = useSocketIO(serverUrl, roomCode, 'master')

// Update shared state
updateState({
  isPlaying: true,
  currentVideo: { id: '123', title: 'Song' },
  volume: 75,
  position: 45
})
```

### Clients Receive State Updates
```javascript
const { roomState, onCommand } = useSocketIO(serverUrl, roomCode, 'client')

// Use roomState for display
useEffect(() => {
  if (roomState) {
    setVolume(roomState.volume)
    setPosition(roomState.position)
    setIsPlaying(roomState.isPlaying)
  }
}, [roomState])
```

---

## 🎛️ Advanced: Room Management

### Get Room Information
```javascript
const { roomInfo } = useSocketIO(serverUrl, roomCode, 'client')

// roomInfo contains:
// {
//   roomCode: 'ABC123',
//   role: 'client',
//   roomSize: 3,  // Number of connected users
//   masterConnected: true
// }
```

### Handle Disconnections
```javascript
const { isConnected, error } = useSocketIO(serverUrl, roomCode, 'client')

useEffect(() => {
  if (!isConnected) {
    console.log('Disconnected, will auto-reconnect...')
  }
}, [isConnected])

useEffect(() => {
  if (error) {
    showNotification(`Error: ${error}`)
  }
}, [error])
```

---

## 🔌 Connection Lifecycle

```
START
  ↓
useSocketIO(...) hook initializes
  ↓
Socket.IO connects to server
  ↓
Emit 'room:join' event
  ↓
Server responds with 'room:joined'
  ↓
State: isConnected = true
  ↓
READY FOR COMMUNICATION
  ↓
(On network issue)
  ↓
Auto-reconnect (exponential backoff)
  500ms → 1s → 2s → 4s → 5s
  ↓
If reconnected: resume communication
If failed: error state
  ↓
COMPONENT UNMOUNT
  ↓
Socket disconnects, cleanup
```

---

## 🔑 Key Differences from Raw WebSocket

| Feature | Raw WebSocket | Socket.IO |
|---------|---|---|
| **Auto-reconnect** | Manual | Built-in ✅ |
| **Fallback** | None | WebSocket → Polling ✅ |
| **Room management** | Manual | Built-in ✅ |
| **Broadcasting** | Manual | Built-in ✅ |
| **Error handling** | Manual | Built-in ✅ |
| **State sync** | Manual polling | Events ✅ |
| **Acknowledgment** | Custom | Built-in ✅ |

---

## 🎨 Complete Component Example

```javascript
import { useState, useEffect, useCallback } from 'react'
import useSocketIO from '../hooks/useSocketIO'

export function RemoteControlPage() {
  const [roomCode, setRoomCode] = useState('')
  const [localVolume, setLocalVolume] = useState(50)
  const [localPosition, setLocalPosition] = useState(0)
  
  const serverUrl = process.env.REACT_APP_REMOTE_SERVER || 'http://localhost:3001'
  
  const {
    isConnected,
    roomInfo,
    roomState,
    error,
    sendCommandToMaster,
    onCommand,
    requestState
  } = useSocketIO(serverUrl, roomCode, 'client')
  
  // Listen for commands
  useEffect(() => {
    const unsubscribe = onCommand((command) => {
      switch(command.type) {
        case 'VOLUME':
          setLocalVolume(command.value)
          break
        case 'SEEK':
          setLocalPosition(command.value)
          break
        case 'PLAY':
          handlePlay(command)
          break
        case 'PAUSE':
          handlePause()
          break
        default:
          break
      }
    })
    
    return unsubscribe
  }, [onCommand])
  
  // Sync state when received
  useEffect(() => {
    if (roomState) {
      setLocalVolume(roomState.volume)
      setLocalPosition(roomState.position)
    }
  }, [roomState])
  
  // Handlers
  const handlePlay = (command) => {
    console.log('Playing:', command.videoId)
    // TODO: Implement play logic
  }
  
  const handlePause = () => {
    console.log('Paused')
    // TODO: Implement pause logic
  }
  
  const handleVolumeChange = (value) => {
    setLocalVolume(value)
    sendCommandToMaster({
      type: 'VOLUME',
      value: value
    })
  }
  
  const handleSeek = (value) => {
    setLocalPosition(value)
    sendCommandToMaster({
      type: 'SEEK',
      value: value
    })
  }
  
  // Render
  if (!roomCode) {
    return (
      <div className="room-join">
        <h2>Join Room</h2>
        <input
          type="text"
          placeholder="Enter room code"
          value={roomCode}
          onChange={(e) => setRoomCode(e.target.value.toUpperCase())}
          maxLength="6"
        />
        <button onClick={() => roomCode && location.reload()}>
          Join
        </button>
      </div>
    )
  }
  
  return (
    <div className="remote-control">
      <h2>Remote Control</h2>
      
      <div className="status">
        <div className={isConnected ? 'connected' : 'disconnected'}>
          {isConnected ? '✅ Connected' : '⏳ Connecting...'}
        </div>
        {roomInfo && <div>Room: {roomInfo.roomCode}</div>}
        {roomInfo && <div>Users: {roomInfo.roomSize}</div>}
      </div>
      
      {error && <div className="error">{error}</div>}
      
      <div className="controls">
        <button onClick={handlePause}>⏸ Pause</button>
        <button onClick={handlePlay}>▶ Play</button>
        <button onClick={() => handleSeek(localPosition - 10)}>
          ⏪ -10s
        </button>
        <button onClick={() => handleSeek(localPosition + 10)}>
          ⏩ +10s
        </button>
      </div>
      
      <div className="sliders">
        <div className="slider-group">
          <label>Volume</label>
          <input
            type="range"
            min="0"
            max="100"
            value={localVolume}
            onChange={(e) => handleVolumeChange(Number(e.target.value))}
          />
          <span>{localVolume}%</span>
        </div>
        
        <div className="slider-group">
          <label>Position</label>
          <input
            type="range"
            min="0"
            max="1000"
            value={localPosition}
            onChange={(e) => handleSeek(Number(e.target.value))}
          />
          <span>{Math.floor(localPosition)}s</span>
        </div>
      </div>
      
      <button onClick={requestState}>📊 Sync State</button>
    </div>
  )
}
```

---

## 🧪 Testing

### Test Connection
```javascript
// In browser console
const socket = io('http://localhost:3001')
socket.on('connect', () => console.log('Connected!'))
socket.emit('room:join', { roomCode: 'TEST123', role: 'client' })
socket.on('room:joined', (data) => console.log('Room joined:', data))
```

### Test Command Flow
```javascript
// In master console
socket.emit('command:send', {
  roomCode: 'TEST123',
  command: { type: 'PLAY', videoId: 'test' }
})

// In client console
socket.on('command:receive', (cmd) => console.log('Received:', cmd))
```

---

## 🚀 Deployment

### Environment Variables
```env
# .env
REACT_APP_REMOTE_SERVER=https://ktv-remote-control.onrender.com
REACT_APP_REMOTE_ROOM_CODE=ABC123
```

### Production URL
```javascript
const serverUrl = process.env.REACT_APP_REMOTE_SERVER || 'http://localhost:3001'
```

---

## ✅ Migration Checklist

- [ ] Install `socket.io-client` (done in package.json)
- [ ] Create `useSocketIO.js` hook (done)
- [ ] Update `RemoteControl.jsx` to use hook
- [ ] Test connection with localhost
- [ ] Test commands (play, pause, volume, seek)
- [ ] Test reconnection (disable network, enable)
- [ ] Test with multiple devices
- [ ] Deploy server to Render
- [ ] Test with Render URL
- [ ] Update documentation

---

## 🔗 Resources

- Socket.IO Docs: https://socket.io/docs/v4/
- Socket.IO Client: https://socket.io/docs/v4/client-api/
- Room Concepts: https://socket.io/docs/v4/rooms/
- Events: https://socket.io/docs/v4/emitting-events/

---

## 💡 Tips

1. **Always check `isConnected`** before sending commands
2. **Use `onCommand()` hook** instead of direct socket.on()
3. **Handle disconnections gracefully** (show UI feedback)
4. **Test with lossy networks** (throttle in DevTools)
5. **Monitor console logs** for [Socket.IO] messages

---

## 📝 What's Next

After migration:
1. Add QR code generation for room codes
2. Add visual feedback for room state
3. Add command acknowledgment feedback
4. Add offline queue (store commands when disconnected)
5. Add analytics/logging