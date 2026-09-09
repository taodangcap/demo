/**
 * Player Store - Manages video playback state
 * Centralized state replaces scattered useState + useRef patterns
 * Inspired by vkara's youtubeStore
 */
import { create } from 'zustand'

const STORAGE_KEY_SELECTED = 'youtube_selected_videos'
const STORAGE_KEY_HISTORY = 'youtube_watch_history'
const STORAGE_KEY_AUTO_PLAY_NEXT = 'youtube_auto_play_next'
const STORAGE_KEY_AUTO_DELETE = 'youtube_auto_delete_enabled'
const STORAGE_KEY_AUTO_DELETE_DAYS = 'youtube_auto_delete_days'

// Helper to check if a video is playable
const isPlayableVideo = (video) => {
  if (!video) return false
  if (video.source !== 'youtube') return true
  if (video.status && video.status.embeddable === false) return false
  if (video.status) {
    if (video.status.uploadStatus === 'rejected' || video.status.uploadStatus === 'failed') return false
    if (video.status.privacyStatus === 'private') return false
  }
  return true
}

const getSelectedVideosFromStorage = () => {
  try {
    const stored = localStorage.getItem(STORAGE_KEY_SELECTED)
    const videos = stored ? JSON.parse(stored) : []
    return videos.filter(isPlayableVideo)
  } catch {
    return []
  }
}

const getHistoryFromStorage = () => {
  try {
    const stored = localStorage.getItem(STORAGE_KEY_HISTORY)
    return stored ? JSON.parse(stored) : []
  } catch {
    return []
  }
}

export const usePlayerStore = create((set, get) => ({
  // Playback State
  currentVideo: null,
  isPlaying: false,
  currentTime: 0,
  duration: 0,
  volume: 100,
  isFullscreen: false,
  streamUrl: null,
  isKaraokeMode: false,

  // Queue/Playlist State
  playlist: getSelectedVideosFromStorage(),
  watchHistory: getHistoryFromStorage(),
  
  // Settings
  autoPlayNext: (() => {
    const stored = localStorage.getItem(STORAGE_KEY_AUTO_PLAY_NEXT)
    return stored ? JSON.parse(stored) : true
  })(),
  autoDeleteEnabled: (() => {
    const stored = localStorage.getItem(STORAGE_KEY_AUTO_DELETE)
    return stored !== null ? JSON.parse(stored) : true
  })(),
  autoDeleteDays: (() => {
    const stored = localStorage.getItem(STORAGE_KEY_AUTO_DELETE_DAYS)
    return stored ? parseInt(stored) : 14
  })(),

  // UI State
  expandedVideoId: null,
  showControls: true,

  // ===== Actions =====
  
  setCurrentVideo: (video) => {
    set({ currentVideo: video, streamUrl: null, currentTime: 0, duration: 0 })
    // Add to history
    if (video) {
      const history = get().watchHistory
      const videoWithTimestamp = { ...video, addedAt: new Date().toISOString() }
      const updated = [videoWithTimestamp, ...history.filter(v => v.id !== video.id)].slice(0, 50)
      localStorage.setItem(STORAGE_KEY_HISTORY, JSON.stringify(updated))
      set({ watchHistory: updated })
    }
  },

  setIsPlaying: (playing) => set({ isPlaying: playing }),
  setCurrentTime: (time) => set({ currentTime: time }),
  setDuration: (dur) => set({ duration: dur }),
  setVolume: (vol) => set({ volume: vol }),
  setStreamUrl: (url) => set({ streamUrl: url }),
  setIsFullscreen: (fs) => set({ isFullscreen: fs }),
  setShowControls: (show) => set({ showControls: show }),
  setExpandedVideoId: (id) => set({ expandedVideoId: id }),
  
  setIsKaraokeMode: (mode) => set({ isKaraokeMode: mode }),

  setAutoPlayNext: (value) => {
    localStorage.setItem(STORAGE_KEY_AUTO_PLAY_NEXT, JSON.stringify(value))
    set({ autoPlayNext: value })
  },

  setAutoDeleteEnabled: (value) => {
    localStorage.setItem(STORAGE_KEY_AUTO_DELETE, JSON.stringify(value))
    set({ autoDeleteEnabled: value })
  },

  setAutoDeleteDays: (value) => {
    localStorage.setItem(STORAGE_KEY_AUTO_DELETE_DAYS, String(value))
    set({ autoDeleteDays: value })
  },

  // Queue Management
  addToPlaylist: (video) => {
    const { currentVideo, playlist } = get()
    if (currentVideo && currentVideo.id === video.id) return false
    if (playlist.find(v => v.id === video.id)) return false
    
    const videoWithTimestamp = { ...video, addedAt: new Date().toISOString() }
    const updated = [...playlist, videoWithTimestamp]
    localStorage.setItem(STORAGE_KEY_SELECTED, JSON.stringify(updated))
    set({ playlist: updated })
    return true
  },

  removeFromPlaylist: (videoId) => {
    const playlist = get().playlist.filter(v => v.id !== videoId)
    localStorage.setItem(STORAGE_KEY_SELECTED, JSON.stringify(playlist))
    set({ playlist })
    return playlist
  },

  prioritizeVideo: (videoId) => {
    const playlist = get().playlist
    const videoIndex = playlist.findIndex(v => v.id === videoId)
    if (videoIndex > 0) {
      const video = playlist[videoIndex]
      const newPlaylist = [video, ...playlist.filter(v => v.id !== videoId)]
      localStorage.setItem(STORAGE_KEY_SELECTED, JSON.stringify(newPlaylist))
      set({ playlist: newPlaylist })
    }
  },

  clearPlaylist: () => {
    localStorage.setItem(STORAGE_KEY_SELECTED, JSON.stringify([]))
    set({ playlist: [], currentVideo: null, isPlaying: false, currentTime: 0, duration: 0, streamUrl: null })
  },

  playNext: () => {
    const { playlist, currentVideo } = get()
    const queue = playlist.filter(v => v.id !== currentVideo?.id)
    
    if (queue.length === 0) {
      set({ currentVideo: null, isPlaying: false, currentTime: 0, duration: 0, streamUrl: null, playlist: [] })
      return null
    }

    const nextVideo = queue[0]
    if (!isPlayableVideo(nextVideo)) {
      // Skip unplayable, remove and try next
      const remaining = queue.slice(1)
      localStorage.setItem(STORAGE_KEY_SELECTED, JSON.stringify(remaining))
      set({ playlist: remaining })
      if (remaining.length > 0) return get().playNext()
      set({ currentVideo: null, isPlaying: false })
      return null
    }

    // Remove played video from queue
    const remainingQueue = queue.slice(1)
    localStorage.setItem(STORAGE_KEY_SELECTED, JSON.stringify(remainingQueue))
    
    // Add to history
    const history = get().watchHistory
    const videoWithTimestamp = { ...nextVideo, addedAt: new Date().toISOString() }
    const updatedHistory = [videoWithTimestamp, ...history.filter(v => v.id !== nextVideo.id)].slice(0, 50)
    localStorage.setItem(STORAGE_KEY_HISTORY, JSON.stringify(updatedHistory))
    
    set({
      currentVideo: nextVideo,
      playlist: remainingQueue,
      watchHistory: updatedHistory,
      streamUrl: null,
      currentTime: 0,
      duration: 0,
      isPlaying: false
    })
    
    return nextVideo
  },

  playNow: (video) => {
    // Remove from playlist if present
    const playlist = get().playlist.filter(v => v.id !== video.id)
    localStorage.setItem(STORAGE_KEY_SELECTED, JSON.stringify(playlist))
    
    // Add to history
    const history = get().watchHistory
    const videoWithTimestamp = { ...video, addedAt: new Date().toISOString() }
    const updatedHistory = [videoWithTimestamp, ...history.filter(v => v.id !== video.id)].slice(0, 50)
    localStorage.setItem(STORAGE_KEY_HISTORY, JSON.stringify(updatedHistory))
    
    set({
      currentVideo: video,
      playlist,
      watchHistory: updatedHistory,
      streamUrl: null,
      currentTime: 0,
      duration: 0,
      isPlaying: false,
      expandedVideoId: null
    })
  },

  // Get serializable state for remote sync
  getStateForSync: () => {
    const state = get()
    return {
      currentVideo: state.currentVideo,
      isPlaying: state.isPlaying,
      currentTime: state.currentTime,
      duration: state.duration,
      volume: state.volume,
      isFullscreen: state.isFullscreen,
      isKaraokeMode: state.isKaraokeMode,
      autoPlayNext: state.autoPlayNext,
      playlist: state.playlist,
      watchHistory: state.watchHistory.slice(0, 10), // Only send recent history
    }
  },

  // Apply state received from remote/sync
  applyRemoteState: (remoteState) => {
    if (!remoteState) return
    const updates = {}
    
    if (remoteState.currentVideo !== undefined) updates.currentVideo = remoteState.currentVideo
    if (remoteState.isPlaying !== undefined) updates.isPlaying = remoteState.isPlaying
    if (remoteState.currentTime !== undefined) updates.currentTime = remoteState.currentTime
    if (remoteState.duration !== undefined) updates.duration = remoteState.duration
    if (remoteState.volume !== undefined) updates.volume = remoteState.volume
    if (remoteState.isFullscreen !== undefined) updates.isFullscreen = remoteState.isFullscreen
    if (remoteState.isKaraokeMode !== undefined) updates.isKaraokeMode = remoteState.isKaraokeMode
    if (remoteState.autoPlayNext !== undefined) updates.autoPlayNext = remoteState.autoPlayNext
    if (remoteState.playlist !== undefined) {
      updates.playlist = remoteState.playlist
      localStorage.setItem(STORAGE_KEY_SELECTED, JSON.stringify(remoteState.playlist))
    }
    if (remoteState.watchHistory !== undefined) updates.watchHistory = remoteState.watchHistory
    
    set(updates)
  },
}))
