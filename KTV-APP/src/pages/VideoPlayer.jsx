import { useState, useEffect, useRef, useCallback } from 'react'
import { useNavigate } from 'react-router-dom'
import { Search, Youtube, Music, Shuffle } from 'lucide-react'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import {
  faEye,
  faRedo,
  faForwardStep,
  faMicrophone,
  faMusic as faMusicSolid,
  faClock,
  faPlay,
  faTrash,
  faSearch as faSearchSolid,
  faHistory,
  faTv,
  faPlus, faPlayCircle, faArrowUp, faGear, faInfoCircle, faBroom, faListCheck,
  faMobileScreen, faQrcode, faCopy, faExpand, faCompress,
  faServer, faSync, faHandsClapping, faFaceLaughSquint, faBullhorn,
  faWind, faWaveSquare, faMagic, faCloudShowersHeavy, faFeatherPointed,
  faVolumeUp, faPause, faStop, faLock, faLockOpen
} from '@fortawesome/free-solid-svg-icons'
import { faFacebook } from '@fortawesome/free-brands-svg-icons'
import { searchVideos, getVideoDetails, formatDuration as formatYouTubeDuration, formatViewCount, fetchWithFallback, isPlayableVideo, parseISO8601Duration } from '../utils/youtube'
import { searchTracks, formatDuration as formatSoundCloudDuration } from '../utils/soundcloud'
import { getSoundEffects, saveSoundEffects, fetchSharedSoundEffects } from '../utils/storage'
import SearchAutocomplete from '../components/SearchAutocomplete'
import {
  getSessionToken,
  applySessionFromUrl,
  isShowCueHost,
  isShowCuePreview,
  isRemoteControlEnabled,
  setRemoteControlEnabled,
  getRemoteControlUrl,
  getRemoteCommands,
  getRemoteState,
  resetSessionToken,
  registerSession,
  syncRemoteState
} from '../utils/remoteControl'
import {
  isShowCueBridgeAvailable,
  onShowCueMessage,
  notifyReady,
  notifyPlaying,
  notifyProgress,
  notifyPaused,
  notifyIdle
} from '../utils/showCueBridge'
import { useWebSocket } from '../hooks/useWebSocket'
import { useSessionStore } from '../store/sessionStore'
import appLogo from '../img/logo.png'

const STORAGE_KEY_SELECTED = 'youtube_selected_videos'
const STORAGE_KEY_HISTORY = 'youtube_watch_history'
const STORAGE_KEY_AUTO_PLAY_NEXT = 'youtube_auto_play_next'
const STORAGE_KEY_AUTO_DELETE = 'youtube_auto_delete_enabled'
const STORAGE_KEY_AUTO_DELETE_DAYS = 'youtube_auto_delete_days'
const REMOTE_SESSION_LOCK_PREFIX = 'tqh_remote_session_lock_'
const SOUNDCLOUD_WIDGET_API_URL = 'https://w.soundcloud.com/player/api.js'
let soundCloudWidgetApiPromise

const loadSoundCloudWidgetApi = () => {
  if (window.SC?.Widget) return Promise.resolve(window.SC)
  if (soundCloudWidgetApiPromise) return soundCloudWidgetApiPromise

  soundCloudWidgetApiPromise = new Promise((resolve, reject) => {
    const existing = document.querySelector(`script[src="${SOUNDCLOUD_WIDGET_API_URL}"]`)
    const script = existing || document.createElement('script')
    const handleLoad = () => window.SC?.Widget
      ? resolve(window.SC)
      : reject(new Error('SoundCloud Widget API unavailable'))
    script.addEventListener('load', handleLoad, { once: true })
    script.addEventListener('error', () => reject(new Error('Cannot load SoundCloud Widget API')), { once: true })
    if (!existing) {
      script.src = SOUNDCLOUD_WIDGET_API_URL
      script.async = true
      document.head.appendChild(script)
    }
  }).catch((error) => {
    soundCloudWidgetApiPromise = undefined
    throw error
  })
  return soundCloudWidgetApiPromise
}

// Hàm decode HTML entities
const decodeHtmlEntities = (text) => {
  if (!text || typeof text !== 'string') return text
  if (typeof document === 'undefined') return text

  const textarea = document.createElement('textarea')
  textarea.innerHTML = text
  return textarea.value
}

const formatClockTime = (value) => {
  const totalSeconds = Math.max(0, Math.floor(Number(value) || 0))
  const hours = Math.floor(totalSeconds / 3600)
  const minutes = Math.floor((totalSeconds % 3600) / 60)
  const seconds = totalSeconds % 60
  if (hours > 0) {
    return `${String(hours).padStart(2, '0')}:${String(minutes).padStart(2, '0')}:${String(seconds).padStart(2, '0')}`
  }
  return `${String(minutes).padStart(2, '0')}:${String(seconds).padStart(2, '0')}`
}

const EFFECT_ICON_PALETTE = [
  { icon: faHandsClapping, color: 'text-yellow-400' },
  { icon: faFaceLaughSquint, color: 'text-green-400' },
  { icon: faBullhorn, color: 'text-blue-400' },
  { icon: faWind, color: 'text-cyan-400' },
  { icon: faWaveSquare, color: 'text-purple-400' },
  { icon: faWaveSquare, color: 'text-orange-400' },
  { icon: faCloudShowersHeavy, color: 'text-indigo-400' },
  { icon: faFeatherPointed, color: 'text-pink-400' }
]

const buildEffectUiConfig = (id, effectData = {}, index = 0) => {
  const paletteIndex = Math.abs(String(id).split('').reduce((sum, char) => sum + char.charCodeAt(0), 0) + index) % EFFECT_ICON_PALETTE.length
  return {
    label: effectData?.label || String(id),
    ...EFFECT_ICON_PALETTE[paletteIndex]
  }
}

// Helper kiểm tra video có phát được không (moved to youtube.js)

const getSelectedVideosFromStorage = () => {
  const stored = localStorage.getItem(STORAGE_KEY_SELECTED)
  const videos = stored ? JSON.parse(stored) : []
  // Tự động lọc các video không phát được ngay khi lấy từ storage
  return videos.filter(isPlayableVideo)
}

const addSelectedVideoToStorage = (video) => {
  const videos = getSelectedVideosFromStorage()
  if (!videos.find(v => v.id === video.id)) {
    // Thêm timestamp vào video
    const videoWithTimestamp = {
      ...video,
      addedAt: new Date().toISOString()
    }
    videos.push(videoWithTimestamp)
    localStorage.setItem(STORAGE_KEY_SELECTED, JSON.stringify(videos))
  }
}

const removeSelectedVideoFromStorage = (videoId) => {
  const videos = getSelectedVideosFromStorage()
  const updated = videos.filter(v => v.id !== videoId)
  localStorage.setItem(STORAGE_KEY_SELECTED, JSON.stringify(updated))
  return updated
}

const clearSelectedVideosFromStorage = () => {
  localStorage.setItem(STORAGE_KEY_SELECTED, JSON.stringify([]))
}

const addToHistory = (video) => {
  const history = getHistory()
  // Thêm timestamp vào video
  const videoWithTimestamp = {
    ...video,
    addedAt: new Date().toISOString()
  }
  const updated = [videoWithTimestamp, ...history.filter(v => v.id !== video.id)].slice(0, 50)
  localStorage.setItem(STORAGE_KEY_HISTORY, JSON.stringify(updated))
}

const getHistory = () => {
  const stored = localStorage.getItem(STORAGE_KEY_HISTORY)
  return stored ? JSON.parse(stored) : []
}

// Throttle helper with leading & trailing calls support (vkara-style)
const throttle = (func, delay) => {
  let timer = null
  let lastArgs = null
  return (...args) => {
    if (timer) {
      lastArgs = args
      return
    }
    func(...args)
    timer = setTimeout(() => {
      timer = null
      if (lastArgs) {
        func(...lastArgs)
        lastArgs = null
      }
    }, delay)
  }
}

function VideoPlayer() {
  const navigate = useNavigate()
  // ShowCue preview pane: mirror only — never remote master (output secondary is master)
  const isPreviewMode = isShowCuePreview()

  // Chỉ skip redirect khi embed/host/WebView2/preview — không skip chỉ vì ?session= (mobile)
  useEffect(() => {
    if (isShowCueHost() || isPreviewMode) return

    const isMobile = /Android|webOS|iPhone|iPad|iPod|BlackBerry|IEMobile|Opera Mini/i.test(navigator.userAgent)
    const isSmallScreen = window.innerWidth < 1024 // lg breakpoint

    if (isMobile || isSmallScreen) {
      const token = getSessionToken()
      navigate(`/remote?session=${token}`, { replace: true })
    }
  }, [navigate, isPreviewMode])

  const [activeTab, setActiveTab] = useState('search')
  const [searchQuery, setSearchQuery] = useState('')
  const [searchResults, setSearchResults] = useState([])
  const [isSearching, setIsSearching] = useState(false)
  const [isSearchFocused, setIsSearchFocused] = useState(false)
  const [selectedVideo, setSelectedVideo] = useState(null)
  const [selectedVideos, setSelectedVideos] = useState(getSelectedVideosFromStorage())
  const [watchHistory, setWatchHistory] = useState(getHistory())
  const [isKaraokeMode, setIsKaraokeMode] = useState(false)
  const [searchSource, setSearchSource] = useState('youtube') // 'youtube' hoặc 'soundcloud'
  // Lưu trữ kết quả tìm kiếm riêng biệt cho mỗi nguồn
  const [youtubeSearchState, setYoutubeSearchState] = useState({ query: '', results: [] })
  const [soundcloudSearchState, setSoundcloudSearchState] = useState({ query: '', results: [] })
  const [searchError, setSearchError] = useState(null) // Lưu lỗi tìm kiếm
  const [autoPlayNext, setAutoPlayNext] = useState(() => {
    const stored = localStorage.getItem(STORAGE_KEY_AUTO_PLAY_NEXT)
    return stored ? JSON.parse(stored) : true
  })
  // Khóa chuyển bài: chặn next / phát bài khác / auto-next (vẫn seek/pause/volume được)
  const [isTrackChangeLocked, setIsTrackChangeLocked] = useState(false)
  const [showSubtitles, setShowSubtitles] = useState(true)
  const [autoDeleteEnabled, setAutoDeleteEnabled] = useState(() => {
    const stored = localStorage.getItem(STORAGE_KEY_AUTO_DELETE)
    return stored !== null ? JSON.parse(stored) : true // Mặc định bật
  })
  const [autoDeleteDays, setAutoDeleteDays] = useState(() => {
    const stored = localStorage.getItem(STORAGE_KEY_AUTO_DELETE_DAYS)
    return stored ? parseInt(stored) : 14 // Mặc định 14 ngày
  })
  const [expandedVideoId, setExpandedVideoId] = useState(null)
  const [isPlaying, setIsPlaying] = useState(false)
  const playerRef = useRef(null) // Container cho YouTube Player
  const soundcloudRef = useRef(null) // Container cho SoundCloud
  const soundcloudIframeRef = useRef(null)
  const soundcloudWidgetRef = useRef(null)
  const fallbackVideoRef = useRef(null)
  const [youtubePlayer, setYoutubePlayer] = useState(null) // YouTube Player object
  const isLoadingNewVideoRef = useRef(false) // Flag để track khi đang load video mới
  const [remoteControlEnabled, setRemoteControlEnabledState] = useState(() => isRemoteControlEnabled())
  // ShowCue mở 2 WebView riêng localStorage → force session từ ?session=
  const [remoteSessionToken, setRemoteSessionToken] = useState(() => {
    const fromUrl = applySessionFromUrl()
    if (fromUrl) {
      try { useSessionStore.getState().setSessionToken(fromUrl) } catch { /* ignore */ }
      return fromUrl
    }
    const token = getSessionToken()
    try { useSessionStore.getState().setSessionToken(token) } catch { /* ignore */ }
    return token
  })
  const [remoteSessionConflict, setRemoteSessionConflict] = useState(false)
  const [sessionPassword] = useState(() => {
    if (typeof window === 'undefined') return ''
    const params = new URLSearchParams(window.location.search)
    return params.get('pwd') || params.get('pin') || ''
  })
  const wsConnectionStatus = useSessionStore((s) => s.connectionStatus)
  const [volume, setVolume] = useState(100)
  const [isRemoteDeviceConnected, setIsRemoteDeviceConnected] = useState(false)
  const [isFullscreen, setIsFullscreen] = useState(false)
  const [streamUrl, setStreamUrl] = useState(null) // Luồng video trực tiếp nếu YouTube bị chặn
  const [currentTime, setCurrentTime] = useState(0)
  const [duration, setDuration] = useState(0)
  const [showControls, setShowControls] = useState(true)
  const controlsTimeoutRef = useRef(null)

  const [fxDurations, setFxDurations] = useState({})
  const [activeFx, setActiveFx] = useState(null) // { id: string, audio: Audio, label: string, icon: Icon }
  const [fxProgress, setFxProgress] = useState(0)
  const [selectedFx, setSelectedFx] = useState(null)
  const [fxVolume, setFxVolume] = useState(80) // Local state for smooth sliding
  const [soundEffectsState, setSoundEffectsState] = useState(getSoundEffects())

  useEffect(() => {
    if (selectedFx) {
      const item = soundEffectsState[selectedFx.id]
      setFxVolume(typeof item === 'object' ? item.volume : 80)
    }
  }, [selectedFx?.id, soundEffectsState])

  // Tự động đồng bộ hiệu ứng âm thanh từ máy chủ
  useEffect(() => {
    const loadEffects = async () => {
      const shared = await fetchSharedSoundEffects()
      setSoundEffectsState(shared)
    }

    // Tải ngay lập tức khi mở trang
    loadEffects()

    // Sau đó cứ 10 giây lại kiểm tra cập nhật một lần (Auto-sync cho tất cả mọi người)
    const syncInterval = setInterval(loadEffects, 10000)

    return () => clearInterval(syncInterval)
  }, [])

  const [isFxPlaying, setIsFxPlaying] = useState(false)
  const syncStateRef = useRef(null)
  const remoteOwnerTabIdRef = useRef(
    typeof crypto !== 'undefined' && crypto.randomUUID
      ? crypto.randomUUID()
      : `tab-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`
  )
  const activeFxRef = useRef(null)
  const isFxPlayingRef = useRef(false)
  const selectedFxRef = useRef(null)
  const autoPlayNextRef = useRef(autoPlayNext)
  const isTrackChangeLockedRef = useRef(isTrackChangeLocked)
  const selectedVideoRef = useRef(selectedVideo)
  const watchHistoryRef = useRef(watchHistory)
  const playbackVolumeRef = useRef(volume)
  const isKaraokeModeRef = useRef(isKaraokeMode)
  const isFullscreenRef = useRef(isFullscreen)
  const isPlayingRef = useRef(isPlaying)
  const currentTimeRef = useRef(currentTime)
  const durationRef = useRef(duration)
  const youtubePlayerRef = useRef(null)

  useEffect(() => { activeFxRef.current = activeFx; isFxPlayingRef.current = isFxPlaying; selectedFxRef.current = selectedFx }, [activeFx, isFxPlaying, selectedFx])
  useEffect(() => { autoPlayNextRef.current = autoPlayNext }, [autoPlayNext])
  useEffect(() => {
    isTrackChangeLockedRef.current = isTrackChangeLocked
    if (syncStateRef.current) syncStateRef.current()
  }, [isTrackChangeLocked])
  useEffect(() => { selectedVideoRef.current = selectedVideo }, [selectedVideo])
  useEffect(() => { watchHistoryRef.current = watchHistory }, [watchHistory])
  useEffect(() => { playbackVolumeRef.current = volume }, [volume])
  useEffect(() => { isKaraokeModeRef.current = isKaraokeMode }, [isKaraokeMode])
  useEffect(() => {
    isFullscreenRef.current = isFullscreen
    if (syncStateRef.current) syncStateRef.current()
  }, [isFullscreen])
  useEffect(() => { isPlayingRef.current = isPlaying }, [isPlaying])
  useEffect(() => { currentTimeRef.current = currentTime }, [currentTime])
  useEffect(() => { durationRef.current = duration }, [duration])
  useEffect(() => { youtubePlayerRef.current = youtubePlayer }, [youtubePlayer])
  const selectedVideosRef = useRef(selectedVideos)
  useEffect(() => { selectedVideosRef.current = selectedVideos }, [selectedVideos])

  const seekPlayback = useCallback((seconds) => {
    let target = Math.max(0, Number(seconds) || 0)
    if (durationRef.current > 0) target = Math.min(target, durationRef.current)
    if (selectedVideoRef.current?.source === 'soundcloud') {
      try { soundcloudWidgetRef.current?.seekTo?.(Math.round(target * 1000)) } catch { /* ignore */ }
    } else {
      try { youtubePlayerRef.current?.seekTo?.(target, true) } catch { /* ignore */ }
    }
    currentTimeRef.current = target
    setCurrentTime(target)
  }, [])

  const setPlaybackVolume = useCallback((nextVolume) => {
    const value = Math.max(0, Math.min(100, Number(nextVolume) || 0))
    try { youtubePlayerRef.current?.setVolume?.(value) } catch { /* ignore */ }
    try { soundcloudWidgetRef.current?.setVolume?.(isPreviewMode ? 0 : value) } catch { /* ignore */ }
  }, [isPreviewMode])

  const pauseMainPlayback = useCallback(() => {
    try { youtubePlayerRef.current?.pauseVideo?.() } catch { /* ignore */ }
    try { soundcloudWidgetRef.current?.pause?.() } catch { /* ignore */ }
  }, [])

  const stopCurrentMedia = useCallback(() => {
    if (window._skipTimer) {
      clearTimeout(window._skipTimer)
      window._skipTimer = null
    }
    try {
      soundcloudWidgetRef.current?.setVolume?.(0)
      soundcloudWidgetRef.current?.pause?.()
      soundcloudWidgetRef.current?.seekTo?.(0)
    } catch { /* ignore */ }
    try {
      const player = youtubePlayerRef.current
      player?.mute?.()
      player?.pauseVideo?.()
      player?.stopVideo?.()
      player?.seekTo?.(0, true)
    } catch { /* ignore */ }
    try {
      if (fallbackVideoRef.current) {
        fallbackVideoRef.current.pause()
        fallbackVideoRef.current.currentTime = 0
        fallbackVideoRef.current.muted = true
      }
    } catch { /* ignore */ }
    isPlayingRef.current = false
    currentTimeRef.current = 0
    durationRef.current = 0
  }, [])

  const playMainPlayback = useCallback(() => {
    // A hard stop clears selectedVideoRef. Do not let PLAY revive a stale
    // YouTube/SoundCloud player that still exists behind the React view.
    if (!selectedVideoRef.current) return
    if (selectedVideoRef.current?.source === 'soundcloud') {
      try { soundcloudWidgetRef.current?.play?.() } catch { /* ignore */ }
    } else {
      try {
        youtubePlayerRef.current?.setVolume?.(playbackVolumeRef.current)
        youtubePlayerRef.current?.unMute?.()
        youtubePlayerRef.current?.playVideo?.()
      } catch { /* ignore */ }
    }
  }, [])

  const hardStopAllPlayback = useCallback(() => {
    isLoadingNewVideoRef.current = false
    stopCurrentMedia()

    const fx = activeFxRef.current
    if (fx?.audio) {
      try {
        fx.audio.pause()
        fx.audio.currentTime = 0
        fx.audio.removeAttribute('src')
        fx.audio.load()
      } catch { /* ignore */ }
    }

    activeFxRef.current = null
    selectedFxRef.current = null
    isFxPlayingRef.current = false
    selectedVideoRef.current = null
    isPlayingRef.current = false
    currentTimeRef.current = 0
    durationRef.current = 0

    setSelectedVideo(null)
    setStreamUrl(null)
    setIsPlaying(false)
    setCurrentTime(0)
    setDuration(0)
    setActiveFx(null)
    setSelectedFx(null)
    setIsFxPlaying(false)
    setFxProgress(0)
    try { localStorage.removeItem('current_playing_video_id') } catch { /* ignore */ }
    if (syncStateRef.current) syncStateRef.current()
  }, [stopCurrentMedia]) // Keep one active media source while advancing the queue.

  const armWatchdogTimer = useCallback((timeoutMs = 6000) => {
    if (window._skipTimer) {
      clearTimeout(window._skipTimer)
      window._skipTimer = null
    }
    window._skipTimer = setTimeout(() => {
      try {
        const player = youtubePlayerRef.current
        const state = player?.getPlayerState?.()
        if (state !== window.YT?.PlayerState?.PLAYING) {
          console.warn('[Watchdog] Video khong chay duoc sau timeout, tu dong chuyen bai...')
          handleNextAndRemoveRef.current?.({ force: true })
        }
      } catch (_) {
        handleNextAndRemoveRef.current?.({ force: true })
      }
    }, timeoutMs)
  }, [])

  const toggleMainPlayback = useCallback(() => {
    if (!selectedVideoRef.current) return
    if (selectedVideoRef.current?.source === 'soundcloud') {
      try {
        soundcloudWidgetRef.current?.isPaused?.((paused) => {
          if (paused) soundcloudWidgetRef.current?.play?.()
          else soundcloudWidgetRef.current?.pause?.()
        })
      } catch { /* ignore */ }
      return
    }
    try {
      const player = youtubePlayerRef.current
      const playerState = player?.getPlayerState?.()
      if (playerState === window.YT?.PlayerState?.PLAYING) player.pauseVideo?.()
      else player?.playVideo?.()
    } catch { /* ignore */ }
  }, [])

  useEffect(() => {
    if (selectedVideo?.source !== 'soundcloud' || !soundcloudIframeRef.current) {
      soundcloudWidgetRef.current = null
      return undefined
    }

    let disposed = false
    let widget
    let events
    const bindWidget = async () => {
      try {
        const SC = await loadSoundCloudWidgetApi()
        if (disposed || !soundcloudIframeRef.current) return
        widget = SC.Widget(soundcloudIframeRef.current)
        events = SC.Widget.Events
        soundcloudWidgetRef.current = widget

        widget.bind(events.READY, () => {
          if (disposed) return
          widget.setVolume(isPreviewMode ? 0 : playbackVolumeRef.current)
          widget.getDuration((milliseconds) => {
            if (disposed) return
            const seconds = Math.max(0, Number(milliseconds) || 0) / 1000
            durationRef.current = seconds
            setDuration(seconds)
          })
        })
        widget.bind(events.PLAY, () => {
          if (disposed) return
          isPlayingRef.current = true
          setIsPlaying(true)
        })
        widget.bind(events.PAUSE, () => {
          if (disposed) return
          isPlayingRef.current = false
          setIsPlaying(false)
        })
        widget.bind(events.PLAY_PROGRESS, (event) => {
          if (disposed) return
          const seconds = Math.max(0, Number(event?.currentPosition) || 0) / 1000
          currentTimeRef.current = seconds
          setCurrentTime(seconds)
        })
        widget.bind(events.FINISH, () => {
          if (disposed) return
          isPlayingRef.current = false
          setIsPlaying(false)
          if (autoPlayNextRef.current) handleNextAndRemoveRef.current?.()
        })
      } catch (error) {
        console.error('SoundCloud Widget initialization failed:', error)
      }
    }
    bindWidget()

    return () => {
      disposed = true
      if (widget && events) {
        try {
          widget.pause()
          widget.unbind(events.READY)
          widget.unbind(events.PLAY)
          widget.unbind(events.PAUSE)
          widget.unbind(events.PLAY_PROGRESS)
          widget.unbind(events.FINISH)
        } catch { /* ignore */ }
      }
      if (soundcloudWidgetRef.current === widget) soundcloudWidgetRef.current = null
    }
  }, [selectedVideo?.id, selectedVideo?.source, selectedVideo?.permalinkUrl, isPreviewMode])

  // Lắng nghe sự kiện bàn phím (phím F để phóng to, phím Esc để thu nhỏ)
  useEffect(() => {
    const handleKeyDown = (e) => {
      const activeEl = document.activeElement
      if (activeEl && (activeEl.tagName === 'INPUT' || activeEl.tagName === 'TEXTAREA' || activeEl.isContentEditable)) {
        return
      }

      if (e.key === 'f' || e.key === 'F') {
        e.preventDefault()
        setIsFullscreen(true)
      } else if (e.key === 'Escape') {
        e.preventDefault()
        setIsFullscreen(false)
      }
    }

    window.addEventListener('keydown', handleKeyDown)
    return () => {
      window.removeEventListener('keydown', handleKeyDown)
    }
  }, [])

  useEffect(() => {
    // Preview pane must not take the master session lock
    if (isPreviewMode || !remoteControlEnabled || !remoteSessionToken) {
      setRemoteSessionConflict(false)
      return undefined
    }

    const storageKey = `${REMOTE_SESSION_LOCK_PREFIX}${remoteSessionToken}`

    const readLock = () => {
      try {
        const raw = localStorage.getItem(storageKey)
        return raw ? JSON.parse(raw) : null
      } catch {
        return null
      }
    }

    const writeLock = () => {
      localStorage.setItem(storageKey, JSON.stringify({
        tabId: remoteOwnerTabIdRef.current,
        updatedAt: Date.now()
      }))
    }

    const releaseLock = () => {
      const currentLock = readLock()
      if (currentLock?.tabId === remoteOwnerTabIdRef.current) {
        localStorage.removeItem(storageKey)
      }
    }

    const refreshLock = () => {
      const currentLock = readLock()
      const isHeldByOtherTab = currentLock
        && currentLock.tabId !== remoteOwnerTabIdRef.current
        && (Date.now() - currentLock.updatedAt) < 4000

      if (isHeldByOtherTab) {
        setRemoteSessionConflict(true)
        return
      }

      writeLock()
      setRemoteSessionConflict(false)
    }

    const handleStorage = (event) => {
      if (event.key === storageKey) {
        refreshLock()
      }
    }

    refreshLock()
    const heartbeat = setInterval(refreshLock, 1500)
    window.addEventListener('storage', handleStorage)
    window.addEventListener('beforeunload', releaseLock)

    return () => {
      clearInterval(heartbeat)
      window.removeEventListener('storage', handleStorage)
      window.removeEventListener('beforeunload', releaseLock)
      releaseLock()
    }
  }, [isPreviewMode, remoteControlEnabled, remoteSessionToken])

  // Handler Refs for Polling Loop (Anti-Stale Closure)
  const handleNextAndRemoveRef = useRef(null)
  const handlePlayNowRef = useRef(null)
  const handleReplayRef = useRef(null)
  const toggleFullscreenRef = useRef(null)
  const handleClearAllRef = useRef(null)
  const handlePriorityRef = useRef(null)
  const handleRemoveSelectedRef = useRef(null)
  const handleAddToSelectedRef = useRef(null)


  const resetControlsTimer = useCallback(() => {
    setShowControls(true)
    if (controlsTimeoutRef.current) clearTimeout(controlsTimeoutRef.current)
    controlsTimeoutRef.current = setTimeout(() => {
      setShowControls(false)
    }, 5000)
  }, [])

  useEffect(() => {
    // Only auto-hide if in fullscreen
    if (!isFullscreen) {
      setShowControls(true)
      if (controlsTimeoutRef.current) clearTimeout(controlsTimeoutRef.current)
      return
    }

    const handleActivity = () => resetControlsTimer()

    // Use 'click' instead of 'mousedown' to avoid immediate toggle-off conflict with the button's onClick
    window.addEventListener('click', handleActivity)
    window.addEventListener('touchstart', handleActivity)

    resetControlsTimer()

    return () => {
      window.removeEventListener('click', handleActivity)
      window.removeEventListener('touchstart', handleActivity)
      if (controlsTimeoutRef.current) clearTimeout(controlsTimeoutRef.current)
    }
  }, [isFullscreen, resetControlsTimer])


  const toggleFullscreen = () => {
    // Sử dụng Web-Fullscreen (CSS-based)
    setIsFullscreen(prev => !prev)
  }


  useEffect(() => {
    // Reset controls visibility when toggling fullscreen
    setShowControls(true)
    if (isFullscreen) {
      resetControlsTimer()
    }
  }, [isFullscreen])

  const handleSearch = async (e) => {
    e.preventDefault()
    if (!searchQuery.trim()) return

    setIsSearching(true)
    setSearchError(null)
    const query = isKaraokeMode ? `${searchQuery} karaoke` : searchQuery
    let results = []

    try {
      if (searchSource === 'youtube') {
        results = await searchVideos(query, 15)
        if (results.length > 0) {
          // Kiểm tra xem kết quả đã có đủ thông tin chi tiết chưa (duration, viewCount)
          // Nếu đã dùng endpoint search-full thì sẽ có sẵn, đỡ tốn thêm 1 lần gọi API
          const alreadyHasDetails = results.every(r => r.duration)

          if (!alreadyHasDetails) {
            const videoIds = results.map(r => r.id)
            const details = await getVideoDetails(videoIds)
            const enriched = results.map(result => {
              const detail = details.find(d => d.id === result.id)
              return detail ? { ...result, ...detail, source: 'youtube' } : { ...result, source: 'youtube' }
            })
            results = enriched.filter(isPlayableVideo)
          } else {
            results = results.map(r => ({ ...r, source: 'youtube' })).filter(isPlayableVideo)
          }
        }
      } else if (searchSource === 'soundcloud') {
        results = await searchTracks(query, 15)
        results = results.map(result => ({ ...result, source: 'soundcloud' }))
      }
    } catch (error) {
      console.error('Search error:', error)
      if (searchSource === 'soundcloud') {
        if (error.message && error.message.includes('Proxy server')) {
          setSearchError('Proxy server chưa chạy. Vui lòng mở terminal và chạy lệnh: npm run server')
        } else {
          setSearchError('Lỗi khi tìm kiếm SoundCloud.')
        }
      } else {
        setSearchError('Lỗi khi tìm kiếm.')
      }
    }
    setSearchResults(results)
    // Lưu lại vào state riêng biệt
    if (searchSource === 'youtube') {
      setYoutubeSearchState({ query: searchQuery, results: results })
    } else {
      setSoundcloudSearchState({ query: searchQuery, results: results })
    }
    setIsSearching(false)
    setActiveTab('search')
    setExpandedVideoId(null)
  }

  const switchSearchSource = (newSource) => {
    if (newSource === searchSource) return

    // 1. Lưu trạng thái hiện tại trước khi chuyển
    if (searchSource === 'youtube') {
      setYoutubeSearchState({ query: searchQuery, results: searchResults })
    } else {
      setSoundcloudSearchState({ query: searchQuery, results: searchResults })
    }

    // 2. Lấy trạng thái của nguồn mới
    const targetState = newSource === 'youtube' ? youtubeSearchState : soundcloudSearchState

    // 3. Cập nhật giao diện
    setSearchQuery(targetState.query)
    setSearchResults(targetState.results)
    setSearchSource(newSource)
    setSearchError(null) // Clear error khi chuyển nguồn
  }

  const handleVideoSelect = (video) => {
    stopCurrentMedia()
    selectedVideoRef.current = video
    setStreamUrl(null)
    setCurrentTime(0)
    setDuration(0)
    setIsPlaying(false)
    setSelectedVideo(video)
    // Nếu chọn phát ngay thì phải xóa khỏi hàng chờ
    removeSelectedVideoFromStorage(video.id)
    const newPlaylist = getSelectedVideosFromStorage()
    selectedVideosRef.current = newPlaylist
    setSelectedVideos(newPlaylist)
    setActiveTab('selected')
    if (syncStateRef.current) syncStateRef.current()
  }
  void handleVideoSelect

  const handlePlayNow = (video, sourceTab = null) => {
    if (!video) return
    // Đang khóa chuyển bài → không cho phát bài khác (giữ bài hiện tại)
    if (isTrackChangeLockedRef.current && selectedVideoRef.current) {
      console.log('[TrackLock] playVideo blocked')
      return
    }

    // Enforce a single active source before mounting the next YouTube/SoundCloud player.
    stopCurrentMedia()

    // Update ref IMMEDIATELY so syncStateRef sees the new video right away
    selectedVideoRef.current = video

    // Đặt video hiện tại
    setStreamUrl(null)
    setCurrentTime(0)
    setDuration(0)
    setIsPlaying(false)
    setSelectedVideo(video)
    setExpandedVideoId(null)

    // Xóa khỏi playlist (Phát tới đâu xóa tới đó)
    removeSelectedVideoFromStorage(video.id)
    const newPlaylist = getSelectedVideosFromStorage()
    selectedVideosRef.current = newPlaylist
    setSelectedVideos(newPlaylist)

    // Nếu phát từ tab tìm kiếm, chúng ta giữ nguyên tab để người dùng tiếp tục tìm kiếm
    // Đã xóa setActiveTab('selected') theo yêu cầu người dùng
    if (sourceTab === 'search') {
      // Giữ nguyên tab hiện tại
    }

    // Track history is handled by useEffect on selectedVideo change
    // Trigger immediate sync to remote (using fresh ref values)
    if (syncStateRef.current) syncStateRef.current()
  }

  const handleAddToSelected = (video, autoPlayIfIdle = false) => {
    // Nếu bài này đang phát thì không được thêm vào playlist
    if (selectedVideo && selectedVideo.id === video.id) {
      return
    }

    // ONLY auto-play if explicitly requested AND nothing is currently playing
    if (autoPlayIfIdle && !selectedVideoRef.current) {
      // Play it directly instead of queuing
      handlePlayNow(video, 'search')
      return
    }

    addSelectedVideoToStorage(video)
    const newList = getSelectedVideosFromStorage()
    // Update ref IMMEDIATELY so next sync sends fresh playlist
    selectedVideosRef.current = newList
    setSelectedVideos(newList)
    setExpandedVideoId(null)
    // Trigger immediate sync so Remote sees the new playlist right away
    if (syncStateRef.current) syncStateRef.current()
  }

  const handlePriority = (videoId, videoData = null) => {
    const videos = getSelectedVideosFromStorage()
    const videoIndex = videos.findIndex(v => v.id === videoId)
    if (videoIndex > 0) {
      const video = videos[videoIndex]
      const newVideos = [video, ...videos.filter(v => v.id !== videoId)]
      localStorage.setItem(STORAGE_KEY_SELECTED, JSON.stringify(newVideos))
      // Update ref IMMEDIATELY
      selectedVideosRef.current = newVideos
      setSelectedVideos(newVideos)
      setExpandedVideoId(null)
      // Trigger immediate sync
      if (syncStateRef.current) syncStateRef.current()
    } else if (videoIndex === -1 && videoData) {
      // Nếu bài hát chưa có trong hàng chờ, chèn lên vị trí đầu tiên
      const newVideos = [videoData, ...videos]
      localStorage.setItem(STORAGE_KEY_SELECTED, JSON.stringify(newVideos))
      // Update ref IMMEDIATELY
      selectedVideosRef.current = newVideos
      setSelectedVideos(newVideos)
      setExpandedVideoId(null)
      // Trigger immediate sync
      if (syncStateRef.current) syncStateRef.current()
    }
  }

  const handleRemoveSelected = (videoId) => {
    // Khóa: không xóa bài đang phát (sẽ kích hoạt chuyển bài)
    if (isTrackChangeLockedRef.current && selectedVideoRef.current?.id === videoId) {
      console.log('[TrackLock] remove current blocked')
      return
    }
    removeSelectedVideoFromStorage(videoId)
    const newList = getSelectedVideosFromStorage()
    // Update ref IMMEDIATELY
    selectedVideosRef.current = newList
    setSelectedVideos(newList)
    if (selectedVideo?.id === videoId) {
      stopCurrentMedia()
      const remaining = newList
      const nextVideo = remaining.length > 0 ? remaining[0] : null
      selectedVideoRef.current = nextVideo
      setSelectedVideo(nextVideo)
    }
    // Trigger immediate sync
    if (syncStateRef.current) syncStateRef.current()
  }

  const handleClearAll = () => {
    if (isTrackChangeLockedRef.current) {
      console.log('[TrackLock] clearAll blocked')
      return
    }
    isLoadingNewVideoRef.current = true
    stopCurrentMedia()
    clearSelectedVideosFromStorage()
    selectedVideosRef.current = []
    setSelectedVideos([])
    if (youtubePlayer) {
      try { youtubePlayer.stopVideo() } catch { /* no-op */ }
    }
    if (playerRef.current) {
      try {
        const iframe = playerRef.current.querySelector('iframe')
        if (iframe) {
          iframe.style.display = 'none'
          iframe.style.visibility = 'hidden'
          iframe.style.opacity = '0'
        }
      } catch { /* no-op */ }
    }
    setSelectedVideo(null)
    selectedVideoRef.current = null
    setStreamUrl(null)
    setIsPlaying(false)
    setCurrentTime(0)
    setDuration(0)
    if (syncStateRef.current) syncStateRef.current()
    setTimeout(() => { isLoadingNewVideoRef.current = false }, 200)
  }

  const handleReplay = () => {
    const currentPlayer = youtubePlayerRef.current
    const currentVideo = selectedVideoRef.current

    if (currentVideo?.source === 'soundcloud') {
      seekPlayback(0)
      playMainPlayback()
    } else if (currentPlayer && currentVideo) {
      try {
        currentPlayer.seekTo(0)
        currentPlayer.playVideo()
      } catch { /* no-op */ }
    }
  }

  // Hàm chuyển sang bài tiếp theo và xóa bài đang phát khỏi danh sách
  // opts.force = true: bỏ qua khóa (lỗi video / không phát được)
  const handleNextAndRemove = useCallback((opts = {}) => {
    if (isTrackChangeLockedRef.current && !opts.force) {
      console.log('[TrackLock] next blocked')
      return
    }
    // Luôn lấy player từ ref để tránh stale closure
    const currentPlayer = youtubePlayerRef.current;

    // Đảm bảo không bị gọi nhiều lần đồng thời
    if (isLoadingNewVideoRef.current) {
      return
    }


    // Lấy danh sách video mới nhất từ storage
    const currentVideos = getSelectedVideosFromStorage()

    // Lọc bỏ bài đang phát ra khỏi danh sách hàng chờ thực tế
    const queue = currentVideos.filter(v => v.id !== selectedVideoRef.current?.id)

    // Nếu danh sách hàng chờ rỗng, dừng phát
    if (queue.length === 0) {
      isLoadingNewVideoRef.current = false
      stopCurrentMedia()
      selectedVideoRef.current = null
      setSelectedVideo(null)
      setStreamUrl(null)
      setIsPlaying(false)
      setCurrentTime(0)
      setDuration(0)
      selectedVideosRef.current = []
      setSelectedVideos([])
      try { localStorage.removeItem('current_playing_video_id') } catch { /* ignore */ }
      if (currentPlayer) {
        try {
          currentPlayer.stopVideo()
        } catch (error) {
          console.error('Error stopping video:', error)
        }
      }
      if (syncStateRef.current) syncStateRef.current()
      return
    }

    // Luôn lấy video đứng đầu hàng chờ để phát
    const nextVideo = queue[0]

    // Kiểm tra tính khả dụng
    if (nextVideo && !isPlayableVideo(nextVideo)) {
      removeSelectedVideoFromStorage(nextVideo.id)
      const updatedQueue = getSelectedVideosFromStorage()
      selectedVideosRef.current = updatedQueue
      setSelectedVideos(updatedQueue)
      // Chuyển sang bài tiếp theo sau một khoảng thời gian ngắn (force: video hỏng)
      setTimeout(() => handleNextAndRemove({ force: true }), 200)
      return
    }

    // Bắt đầu xử lý phát video mới
    stopCurrentMedia()
    isLoadingNewVideoRef.current = true

    if (nextVideo.source !== 'soundcloud' && currentPlayer) {
      try {
        setStreamUrl(null) // Reset luồng dự phòng

        // Load video mới vào player
        currentPlayer.loadVideoById(nextVideo.id)

        // Update refs immediately before React flushes state
        selectedVideoRef.current = nextVideo

        // Cập nhật state video hiện tại
        setSelectedVideo(nextVideo)

        // XÓA NGAY LẬP TỨC khỏi danh sách hàng chờ (Phát tới đâu xóa tới đó)
        removeSelectedVideoFromStorage(nextVideo.id)
        const updatedQueue = getSelectedVideosFromStorage()
        selectedVideosRef.current = updatedQueue
        setSelectedVideos(updatedQueue)

        // Watchdog: tu dong skip neu video khong chay duoc sau 6 giay
        armWatchdogTimer(6000)

        // Reset flag sau khi video đã load
        setTimeout(() => {
          isLoadingNewVideoRef.current = false
        }, 800)
      } catch (error) {
        console.error('Error loading next video:', error)
        isLoadingNewVideoRef.current = false
        // Nếu lỗi, vẫn set selected video để UI cập nhật, nhưng bài đó đã bị xóa rồi
        setSelectedVideo(nextVideo)
        removeSelectedVideoFromStorage(nextVideo.id)
        setSelectedVideos(getSelectedVideosFromStorage())
      }
    } else {
      // SoundCloud hoặc không có YouTube player
      selectedVideoRef.current = nextVideo
      setSelectedVideo(nextVideo)
      removeSelectedVideoFromStorage(nextVideo.id)
      const updatedQueue2 = getSelectedVideosFromStorage()
      selectedVideosRef.current = updatedQueue2
      setSelectedVideos(updatedQueue2)

      // Reset flag
      setTimeout(() => {
        isLoadingNewVideoRef.current = false
      }, 500)
    }

    // Trigger immediate sync
    if (syncStateRef.current) syncStateRef.current()
  }, [stopCurrentMedia]) // Keep one active media source while advancing the queue.

  // Keep handler refs in sync with latest definitions (placed AFTER all handlers for safety)
  useEffect(() => {
    handleNextAndRemoveRef.current = handleNextAndRemove
    handlePlayNowRef.current = handlePlayNow
    handleReplayRef.current = handleReplay
    toggleFullscreenRef.current = toggleFullscreen
    handleClearAllRef.current = handleClearAll
    handlePriorityRef.current = handlePriority
    handleRemoveSelectedRef.current = handleRemoveSelected
    handleAddToSelectedRef.current = handleAddToSelected
  }, [
    handleNextAndRemove, handlePlayNow, handleReplay, toggleFullscreen,
    handleClearAll, handlePriority, handleRemoveSelected, handleAddToSelected
  ])



  // Tự động next khi hết bài - đơn giản hóa (chỉ cho YouTube)
  const onPlayerStateChange = useCallback((event) => {
    // Chỉ xử lý cho YouTube videos
    if (selectedVideoRef.current?.source === 'soundcloud') {
      return
    }

    // Cập nhật trạng thái phát
    if (event.data === window.YT.PlayerState.PLAYING) {
      // A delayed YouTube onReady/play event can arrive after the queue was
      // cleared. Never resurrect audio without an active song.
      if (!selectedVideoRef.current) {
        try {
          event.target?.mute?.()
          event.target?.stopVideo?.()
        } catch { /* ignore */ }
        isPlayingRef.current = false
        setIsPlaying(false)
        return
      }
      if (!isPreviewMode) {
        try {
          event.target?.setVolume?.(playbackVolumeRef.current)
          event.target?.unMute?.()
        } catch { /* ignore */ }
      }
      setIsPlaying(true)
      isLoadingNewVideoRef.current = false
    } else if (event.data === window.YT.PlayerState.PAUSED) {
      setIsPlaying(false)
    } else if (event.data === window.YT.PlayerState.ENDED) {
      setIsPlaying(false)

      const autoPlayValue = autoPlayNextRef.current

      // Video kết thúc - tự động chuyển bài (bỏ qua nếu đang khóa chuyển bài)
      if (autoPlayValue && !isTrackChangeLockedRef.current) {
        // Reset flag ngay lập tức
        isLoadingNewVideoRef.current = false

        // Gọi trực tiếp không cần setTimeout để đảm bảo được gọi
        const currentVideos = getSelectedVideosFromStorage()
        if (currentVideos.length > 0) {
          // Sử dụng setTimeout nhỏ để đảm bảo state đã cập nhật
          setTimeout(() => {
            if (!isLoadingNewVideoRef.current && !isTrackChangeLockedRef.current) {
              handleNextAndRemove()
            }
          }, 100)
        }
      }
    }
  }, [handleNextAndRemove, selectedVideo, isPreviewMode])

  // Kiểm tra trạng thái ENDED và tự động chuyển bài (backup cho onPlayerStateChange) - chỉ cho YouTube
  useEffect(() => {
    if (!youtubePlayer || !selectedVideoRef.current || selectedVideoRef.current.source === 'soundcloud') return

    const checkEndedState = setInterval(() => {
      if (isLoadingNewVideoRef.current) return

      try {
        const playerState = youtubePlayer.getPlayerState?.()
        // Kiểm tra nếu video đã kết thúc
        if (playerState === window.YT.PlayerState.ENDED) {
          // Luôn lấy trạng thái mới nhất từ ref
          if (autoPlayNextRef.current && !isTrackChangeLockedRef.current) {
            const currentVideos = getSelectedVideosFromStorage()
            if (currentVideos.length > 0) {
              isLoadingNewVideoRef.current = false
              handleNextAndRemove()
            }
          }
        }
      } catch (error) {
        // Ignore errors
      }
    }, 500) // Kiểm tra mỗi 0.5 giây

    return () => clearInterval(checkEndedState)
  }, [youtubePlayer, selectedVideo, handleNextAndRemove])

  // Kiểm tra khi người dùng tua đến cuối video - chỉ cho YouTube
  useEffect(() => {
    if (!youtubePlayer || !selectedVideoRef.current || selectedVideoRef.current.source === 'soundcloud') return

    const checkVideoEnd = setInterval(() => {
      if (isLoadingNewVideoRef.current) return

      try {
        const playerState = youtubePlayer.getPlayerState?.()
        // Chỉ xử lý khi đang phát, không xử lý khi đã ENDED (onPlayerStateChange sẽ xử lý)
        if (playerState === window.YT.PlayerState.PLAYING) {
          const currentTime = youtubePlayer.getCurrentTime?.()
          const duration = youtubePlayer.getDuration?.()

          // Nếu đã tua đến gần cuối video (còn 0.3 giây), tự động chuyển bài
          // Chỉ khi đang phát và chưa kết thúc
          if (currentTime && duration && duration > 0 && (duration - currentTime) <= 0.3 && (duration - currentTime) > 0) {
            if (autoPlayNextRef.current && !isTrackChangeLockedRef.current && !isLoadingNewVideoRef.current) {
              isLoadingNewVideoRef.current = true
              handleNextAndRemove()
            }
          }
        }
      } catch (error) {
        // Ignore errors
      }
    }, 300) // Kiểm tra mỗi 0.3 giây

    return () => clearInterval(checkVideoEnd)
  }, [youtubePlayer, selectedVideo, handleNextAndRemove])

  // Khởi tạo YouTube Player
  const initPlayer = () => {
    if (!window.YT || !window.YT.Player || !selectedVideo || !playerRef.current) {
      return
    }

    // Không khởi tạo YouTube player cho SoundCloud
    if (selectedVideo.source === 'soundcloud') {
      return
    }

    // Nếu player đã tồn tại, chỉ load video mới (KHÔNG BAO GIỜ destroy)
    if (youtubePlayer) {
      try {
        const currentVideoId = youtubePlayer.getVideoData?.()?.video_id
        if (currentVideoId === selectedVideo.id) {
          // Video giống nhau, không cần làm gì
          return
        }
        // Video khác, load video mới vào player hiện có
        try {
          youtubePlayer.loadVideoById(selectedVideo.id)
          armWatchdogTimer(6000)
          return
        } catch (loadError) {
          console.error('Error loading video:', loadError)
          // Nếu loadVideoById thất bại, vẫn không destroy - chỉ log error
          // Player sẽ tự xử lý hoặc user có thể thử lại
          return
        }
      } catch (error) {
        // Nếu không thể lấy video ID, thử load video mới
        try {
          youtubePlayer.loadVideoById(selectedVideo.id)
          armWatchdogTimer(6000)
          return
        } catch (loadError) {
          console.error('Error loading video (fallback):', loadError)
          return
        }
      }
    }

    try {
      // Đảm bảo container có kích thước
      if (playerRef.current.offsetWidth === 0 || playerRef.current.offsetHeight === 0) {
        setTimeout(() => {
          if (playerRef.current && selectedVideo) {
            initPlayer()
          }
        }, 500)
        return
      }

      // Kiểm tra xem container đã có iframe chưa (từ player cũ)
      const existingIframe = playerRef.current.querySelector('iframe')
      if (existingIframe) {
        // Container đã có iframe, hiển thị lại nếu đang ẩn
        existingIframe.style.display = ''
        existingIframe.style.visibility = ''
        existingIframe.style.opacity = ''
        // Chỉ load video mới nếu player object chưa có
        if (!youtubePlayer) {
          // Đợi một chút rồi thử lại
          setTimeout(() => {
            if (playerRef.current && selectedVideo && !youtubePlayer) {
              initPlayer()
            }
          }, 500)
        }
        return
      }

      // Tạo player mới chỉ khi container chưa có iframe
      const expectedVideoId = selectedVideo.id
      const newPlayer = new window.YT.Player(playerRef.current, {
        videoId: selectedVideo.id,
        host: 'https://www.youtube.com',
        playerVars: {
          autoplay: 1,
          controls: 1,
          modestbranding: 1,
          rel: 0,
          showinfo: 0,
          playsinline: 1,
          enablejsapi: 1,
          mute: 0,
          origin: window.location.origin,
          widget_referrer: window.location.origin,
          fs: 1
        },
        events: {
          onReady: (event) => {
            if (selectedVideoRef.current?.id !== expectedVideoId) {
              try {
                event.target.mute()
                event.target.stopVideo()
              } catch { /* ignore stale player teardown */ }
              return
            }
            // Player sẵn sàng, đảm bảo iframe được hiển thị
            if (playerRef.current) {
              const iframe = playerRef.current.querySelector('iframe')
              if (iframe) {
                iframe.style.display = ''
                iframe.style.visibility = ''
                iframe.style.opacity = ''
              }
            }
            try {
              event.target.setVolume(isPreviewMode ? 0 : playbackVolumeRef.current)
              event.target.playVideo()
              if (isPreviewMode) event.target.mute()
              else event.target.unMute()
            } catch {
              // Ignore autoplay/unmute failures from the embed.
            }
          },
          onStateChange: (event) => {
            onPlayerStateChange(event)
            // Nếu bắt đầu phát, hủy bộ đếm watchdog
            if (event.data === window.YT.PlayerState.PLAYING) {
              if (window._skipTimer) {
                clearTimeout(window._skipTimer)
                window._skipTimer = null
              }
            }
          },
          onError: (event) => {
            console.error('YouTube Player Error:', event.data)
            console.warn(`[Error] Video khong the phat (Ma loi ${event.data}). Tu dong chuyen bai ngay...`)
            if (window._skipTimer) {
              clearTimeout(window._skipTimer)
              window._skipTimer = null
            }
            handleNextAndRemove({ force: true })
          }
        }
      })

      youtubePlayerRef.current = newPlayer
      setYoutubePlayer(newPlayer)
      armWatchdogTimer(6000)
    } catch (error) {
      console.error('Error initializing YouTube player:', error)
    }
  }

  // Hàm xóa dữ liệu cũ dựa trên số ngày
  const deleteOldData = useCallback(() => {
    if (!autoDeleteEnabled) return

    const daysInMs = autoDeleteDays * 24 * 60 * 60 * 1000
    const cutoffDate = new Date(Date.now() - daysInMs)

    // Xóa lịch sử cũ
    const history = getHistory()
    const filteredHistory = history.filter(video => {
      if (!video.addedAt) return true // Giữ video không có timestamp (backward compatibility)
      return new Date(video.addedAt) > cutoffDate
    })
    if (filteredHistory.length !== history.length) {
      localStorage.setItem(STORAGE_KEY_HISTORY, JSON.stringify(filteredHistory))
      setWatchHistory(filteredHistory)
    }

    // Xóa video đã chọn cũ
    const selected = getSelectedVideosFromStorage()
    const filteredSelected = selected.filter(video => {
      if (!video.addedAt) return true // Giữ video không có timestamp
      return new Date(video.addedAt) > cutoffDate
    })
    if (filteredSelected.length !== selected.length) {
      localStorage.setItem(STORAGE_KEY_SELECTED, JSON.stringify(filteredSelected))
      setSelectedVideos(filteredSelected)
      // Removed: Logic that resets selectedVideo if it's not in the storage list.
      // In this app, the playing video is intentionally removed from the storage list,
      // so checking for its presence here causes it to be reset to null incorrectly.
    }
  }, [autoDeleteEnabled, autoDeleteDays])

  useEffect(() => {
    const videos = getSelectedVideosFromStorage()
    setSelectedVideos(videos)
    if (videos.length > 0 && !selectedVideo) {
      const firstVideo = videos[0]
      setSelectedVideo(firstVideo)
      addToHistory(firstVideo)

      // Khởi tạo là xóa luôn khỏi hàng chờ
      removeSelectedVideoFromStorage(firstVideo.id)
      setSelectedVideos(getSelectedVideosFromStorage())
    }

    // Kiểm tra và xóa dữ liệu cũ khi component mount
    deleteOldData()
  }, [])

  // Register session with server — only OUTPUT master (never ShowCue preview)
  useEffect(() => {
    if (isPreviewMode || !remoteControlEnabled || !remoteSessionToken || remoteSessionConflict) return undefined

    registerSession(remoteSessionToken, null, sessionPassword)

    // Re-register every minute to keep alive
    const interval = setInterval(() => {
      registerSession(remoteSessionToken, null, sessionPassword)
    }, 60000)

    return () => clearInterval(interval)
  }, [isPreviewMode, remoteControlEnabled, remoteSessionToken, remoteSessionConflict, sessionPassword])

  // Remote Control Command Handler (Long Polling - Fallback when WebSocket unavailable)
  useEffect(() => {
    if (isPreviewMode || !remoteControlEnabled || remoteSessionConflict) return
    // Skip HTTP polling entirely when WebSocket is connected
    if (wsConnectionStatus === 'connected') return

    let isMounted = true
    let isPolling = false
    let consecutiveErrors = 0

    const pollCommands = async () => {
      if (isPolling || !isMounted) return
      isPolling = true

      let hasError = false

      try {
        const data = await getRemoteCommands()

        if (data && data.error) {
          hasError = true
        } else {
          consecutiveErrors = 0 // Reset on success
          const commands = data ? (data.commands || []) : []

          if (isMounted) {
            setIsRemoteDeviceConnected(!!data?.remoteConnected)
          }

          if (isMounted && commands && commands.length > 0) {
            commands.forEach(cmd => {
            switch (cmd.command) {
              case 'playPause':
                toggleMainPlayback()
                break
              case 'next':
                handleNextAndRemoveRef.current?.()
                break
              case 'seek':
                if (cmd.data?.seconds !== undefined) {
                  seekPlayback(cmd.data.seconds)
                }
                break
              case 'seekBackward':
                seekPlayback(currentTimeRef.current - 10)
                break
              case 'seekForward':
                seekPlayback(currentTimeRef.current + 10)
                break
              case 'replay':
                handleReplayRef.current?.()
                break
              case 'volume':
                if (cmd.data?.value !== undefined) {
                  const newVol = cmd.data.value
                  setVolume(newVol)
                  setPlaybackVolume(newVol)
                }
                if (syncStateRef.current) syncStateRef.current()
                break
              case 'search':
                setActiveTab('search')
                break
              case 'openSearchTab':
                setActiveTab('search')
                break
              case 'karaoke':
                if (cmd.data?.value !== undefined) {
                  setIsKaraokeMode(cmd.data.value)
                } else {
                  setIsKaraokeMode(prev => !prev)
                }
                if (syncStateRef.current) syncStateRef.current()
                break
              case 'fullscreenVideo':
                toggleFullscreenRef.current?.()
                break
              case 'shuffle': {
                if (isTrackChangeLockedRef.current) break
                const vids = getSelectedVideosFromStorage()
                const shuffledVids = [...vids].sort(() => Math.random() - 0.5)
                setSelectedVideos(shuffledVids)
                localStorage.setItem(STORAGE_KEY_SELECTED, JSON.stringify(shuffledVids))
                if (shuffledVids.length > 0) {
                  handlePlayNowRef.current?.(shuffledVids[0])
                }
                break
              }
              case 'toggleAutoPlay':
                setAutoPlayNext(prev => {
                  const newVal = cmd.data?.value !== undefined ? !!cmd.data.value : !prev
                  autoPlayNextRef.current = newVal
                  localStorage.setItem(STORAGE_KEY_AUTO_PLAY_NEXT, JSON.stringify(newVal))
                  return newVal
                })
                if (syncStateRef.current) syncStateRef.current()
                break
              case 'toggleTrackLock':
                setIsTrackChangeLocked(prev => {
                  const newVal = cmd.data?.value !== undefined ? !!cmd.data.value : !prev
                  isTrackChangeLockedRef.current = newVal
                  return newVal
                })
                if (syncStateRef.current) syncStateRef.current()
                break
              case 'toggleSubtitles':
                setShowSubtitles(prev => !prev)
                if (youtubePlayerRef.current && youtubePlayerRef.current.loadModule) {
                  try {
                    youtubePlayerRef.current.setOption('captions', 'fontSize', !showSubtitles ? 0 : -1)
                  } catch (e) {
                    console.log('Subtitle toggle error:', e)
                  }
                }
                break
              case 'setAutoPlay':
                if (cmd.data && cmd.data.value !== undefined) {
                  const newValue = cmd.data.value
                  setAutoPlayNext(newValue)
                  localStorage.setItem(STORAGE_KEY_AUTO_PLAY_NEXT, JSON.stringify(newValue))
                }
                break
              case 'clearAll':
                handleClearAllRef.current?.()
                break
              case 'playVideo':
                if (cmd.data?.video) {
                  // Use source tab from remote if provided, default to 'search'
                  const sourceTab = cmd.data.from || 'search'
                  handlePlayNowRef.current?.(cmd.data.video, sourceTab)
                } else if (cmd.data?.videoId) {
                  const videos = getSelectedVideosFromStorage()
                  const video = videos.find(v => v.id === cmd.data.videoId)
                  if (video) {
                    handlePlayNowRef.current?.(video, 'selected')
                  }
                }
                break
              case 'priority':
                if (cmd.data?.videoId) {
                  handlePriorityRef.current?.(cmd.data.videoId, cmd.data.video)
                }
                break
              case 'playSoundEffect':
                if (cmd.data?.id) {
                  // STOP & CLEAN OLD ONES
                  if (activeFxRef.current?.audio) {
                    try {
                      activeFxRef.current.audio.pause();
                      activeFxRef.current.audio.src = '';
                      activeFxRef.current.audio.load(); // Full reset
                    } catch {
                      // Ignore effect cleanup errors.
                    }
                  }

                  const effects = getSoundEffects()
                  const item = effects[cmd.data.id]
                  if (item) {
                    const url = typeof item === 'object' ? item.url : item
                    const rawVol = typeof item === 'object' ? (item.volume ?? 80) : 80
                    const targetVol = rawVol / 100
                    const id = cmd.data.id

                    const audioFx = new Audio(url)
                    audioFx.volume = targetVol
                    audioFx.muted = (rawVol === 0)
                    audioFx.dataset.targetVol = targetVol.toString()

                    // Register events to keep volume sticky
                    const enforceVol = () => {
                      const v = parseFloat(audioFx.dataset.targetVol || "0.8");
                      audioFx.volume = v;
                      audioFx.muted = (v === 0);
                    };

                    audioFx.onplay = enforceVol;
                    audioFx.onplaying = enforceVol;

                    const effectLabels = {
                      applause: 'Vỗ tay', laughter: 'Cười', cheering: 'Hò reo',
                      whistle: 'Huýt sáo', intro: 'Nhạc dạo', drumroll: 'Trống',
                      rain: 'Mưa', magical: 'Ma mị'
                    }
                    const effectIcons = {
                      applause: faHandsClapping, laughter: faFaceLaughSquint, cheering: faBullhorn,
                      whistle: faWind, intro: faWaveSquare, drumroll: faWaveSquare,
                      rain: faCloudShowersHeavy, magical: faFeatherPointed
                    }

                    const newFx = {
                      id,
                      audio: audioFx,
                      label: effectLabels[id] || id,
                      icon: effectIcons[id] || faVolumeUp
                    }

                    setActiveFx(newFx)
                    setIsFxPlaying(true)

                    audioFx.onended = () => {
                      activeFxRef.current = null
                      setIsFxPlaying(false)
                      setActiveFx(null)
                    }

                    audioFx.play().then(() => {
                      enforceVol();
                    }).catch(e => console.error('FAILED TO PLAY REMOTE EFFECT:', e))
                  }
                }
                break;
              case 'selectEffect':
                if (cmd.data) {
                  const effectId = typeof cmd.data === 'string' ? cmd.data : cmd.data.id
                  if (effectId) {
                    setSelectedFx({ id: effectId, ...buildEffectUiConfig(effectId, getSoundEffects()[effectId]) });
                  }
                }
                break;
              case 'controlEffect':
                if (activeFxRef.current && activeFxRef.current.audio) {
                  const { action, value } = cmd.data
                  if (action === 'toggle') {
                    if (isFxPlayingRef.current) activeFxRef.current.audio.pause()
                    else activeFxRef.current.audio.play().catch(() => undefined)
                    setIsFxPlaying(!isFxPlayingRef.current)
                  } else if (action === 'stop') {
                    activeFxRef.current.audio.pause()
                    activeFxRef.current.audio.currentTime = 0
                    activeFxRef.current = null
                    setActiveFx(null)
                    setIsFxPlaying(false)
                  } else if (action === 'seek') {
                    activeFxRef.current.audio.currentTime = value
                    setFxProgress(value)
                  } else if (action === 'volume') {
                    const newVol = value / 100;
                    if (activeFxRef.current && activeFxRef.current.audio) {
                      activeFxRef.current.audio.volume = newVol;
                      activeFxRef.current.audio.muted = (newVol === 0);
                      if (newVol === 0) {
                        activeFxRef.current.audio.pause();
                      } else if (activeFxRef.current.audio.paused && isFxPlayingRef.current) {
                        activeFxRef.current.audio.play().catch(() => { });
                      }
                      activeFxRef.current.audio.dataset.targetVol = newVol.toString();
                    }
                    setFxVolume(value)
                  }
                }
                break;
              case 'updateSoundEffectVolume':
                if (cmd.data?.id && cmd.data?.volume !== undefined) {
                  const volVal = cmd.data.volume;
                  const targetVol = volVal / 100;

                  // Global apply to active sound
                  if (activeFxRef.current) {
                    activeFxRef.current.audio.volume = targetVol;
                    activeFxRef.current.audio.muted = (volVal === 0);
                    if (volVal === 0) {
                      activeFxRef.current.audio.pause();
                    } else if (activeFxRef.current.audio.paused && isFxPlayingRef.current && activeFxRef.current.id === cmd.data.id) {
                      activeFxRef.current.audio.play().catch(() => { });
                    }
                    activeFxRef.current.audio.dataset.targetVol = targetVol.toString();
                  }

                  setSoundEffectsState(prev => {
                    const currentState = prev || getSoundEffects() || {}
                    const item = currentState[cmd.data.id]
                    const next = {
                      ...currentState,
                      [cmd.data.id]: typeof item === 'object' ? { ...item, volume: cmd.data.volume } : { url: item, volume: cmd.data.volume }
                    }
                    saveSoundEffects(next)
                    return next
                  })
                  if (selectedFx?.id === cmd.data.id) {
                    setFxVolume(cmd.data.volume)
                  }
                }
                break;
              case 'removeFromSelected':
                if (cmd.data?.videoId) {
                  handleRemoveSelectedRef.current?.(cmd.data.videoId)
                  // sync is now handled inside handleRemoveSelected
                }
                break
              case 'stopPlayback':
                hardStopAllPlayback()
                break
              case 'addToSelected':
                if (cmd.data?.video) {
                  // Pass autoPlayIfIdle=false: ONLY add to list, never auto-play even if idle
                  handleAddToSelectedRef.current?.(cmd.data.video, false)
                }
                break
            }
          })
        }
        } // Đóng khối else
      } catch (error) {
        hasError = true
      } finally {
        isPolling = false
        // Continue polling if still mounted
        if (isMounted) {
          if (hasError) {
            consecutiveErrors++
            // Exponential backoff: 1s, 1.5s, 2.25s, ... up to 10s (10000ms)
            const delay = Math.min(10000, 1000 * Math.pow(1.5, consecutiveErrors - 1))
            setTimeout(pollCommands, delay)
          } else {
            // Small delay to prevent tight loop in case of immediate returns
            setTimeout(pollCommands, 50)
          }
        }
      }
    }

    // Start polling loop
    pollCommands()

    return () => {
      isMounted = false
    }
  }, [remoteControlEnabled, remoteSessionToken, remoteSessionConflict, wsConnectionStatus,
    hardStopAllPlayback, seekPlayback, setPlaybackVolume, toggleMainPlayback])

  // Tracking Sound Effect Progress
  useEffect(() => {
    let interval;
    if (activeFx && activeFx.audio && isFxPlaying) {
      interval = setInterval(() => {
        setFxProgress(activeFx.audio.currentTime)
      }, 200)
    }
    return () => clearInterval(interval)
  }, [activeFx, isFxPlaying])

  // Throttled WebSocket state sync to avoid network spam (vkara-style)
  // Dual-WebView: WS sync càng dày càng bám (giữ WebView, không DWM)
  const throttledWsSync = useCallback(
    throttle((state) => {
      if (wsSyncRef.current) {
        wsSyncRef.current(state)
      }
    }, 80),
    []
  )

  /** Gửi time-lock nhẹ (chỉ WS) — preview bám timeline OUTPUT */
  const pushLightTimeSync = useCallback(() => {
    if (isPreviewMode || !remoteControlEnabled || remoteSessionConflict) return
    if (!wsSyncRef.current || !selectedVideoRef.current) return
    const currentVid = selectedVideoRef.current
    const queueVids = selectedVideosRef.current || []
    const fullPlaylist = currentVid
      ? [{ ...currentVid, isPlaying: true }, ...queueVids.filter(v => v.id !== currentVid.id).map(v => ({ ...v, isPlaying: false }))]
      : queueVids.map(v => ({ ...v, isPlaying: false }))
    wsSyncRef.current({
      currentVideo: currentVid,
      playlist: fullPlaylist,
      isPlaying: isPlayingRef.current,
      currentTime: currentTimeRef.current,
      duration: durationRef.current,
      syncedAt: Date.now(),
      volume: playbackVolumeRef.current,
      isKaraokeMode: isKaraokeModeRef.current,
      autoPlayNext: autoPlayNextRef.current,
      isTrackChangeLocked: isTrackChangeLockedRef.current
    })
  }, [isPreviewMode, remoteControlEnabled, remoteSessionConflict])

  // Sync State Function Reference
  const wsSyncRef = useRef(null) // Will be set by WebSocket hook

  useEffect(() => {
    syncStateRef.current = async () => {
      if (isPreviewMode || !remoteControlEnabled || !remoteSessionToken || remoteSessionConflict) return

      // Get volume safely from player if possible, else use state
      let currentVolValue = playbackVolumeRef.current
      if (selectedVideoRef.current?.source !== 'soundcloud' && youtubePlayer && youtubePlayer.getVolume) {
        try {
          currentVolValue = youtubePlayer.getVolume()
        } catch {
          // Ignore player volume access errors.
        }
      }

      // Build full playlist: currentVideo first, then queue (dedup by ID)
      const currentVid = selectedVideoRef.current
      const queueVids = selectedVideosRef.current
      const fullPlaylist = currentVid
        ? [{ ...currentVid, isPlaying: true }, ...queueVids.filter(v => v.id !== currentVid.id).map(v => ({ ...v, isPlaying: false }))]
        : queueVids.map(v => ({ ...v, isPlaying: false }))

      const stateToSync = {
        // Use refs to ALWAYS get the LATEST value even if called mid-render
        currentVideo: currentVid,
        playlist: fullPlaylist,
        watchHistory: watchHistoryRef.current,
        volume: currentVolValue,
        isKaraokeMode: isKaraokeModeRef.current,
        isFullscreen: isFullscreenRef.current,
        autoPlayNext: autoPlayNextRef.current,
        isTrackChangeLocked: isTrackChangeLockedRef.current,
        isPlaying: isPlayingRef.current,
        currentTime: currentTimeRef.current,
        duration: durationRef.current,
        syncedAt: Date.now(), // ← vkara-style: timestamp for playback interpolation on remote
        soundEffects: getSoundEffects(),
        fxDurations: fxDurations,
        selectedFx: selectedFxRef.current ? {
          id: selectedFxRef.current.id,
          label: selectedFxRef.current.label
        } : null,
        activeFx: activeFxRef.current ? {
          id: activeFxRef.current.id,
          label: activeFxRef.current.label,
          progress: fxProgress,
          isPlaying: isFxPlayingRef.current,
          maxTime: activeFxRef.current.audio?.duration || 0,
          volume: (activeFxRef.current.audio?.volume || 0.8) * 100
        } : null
      }

      // Push via WebSocket (throttled ~120ms) — preview/remote mirror realtime
      throttledWsSync(stateToSync)

      // HTTP state luôn cập nhật (preview fallback + remote HTTP)
      // Không chờ WS drop — giảm lệch 2 màn
      try {
        await syncRemoteState(remoteSessionToken, stateToSync)
      } catch (err) {
        if (wsConnectionStatus !== 'connected') {
          console.warn('[HTTP Sync] state sync failed:', err)
        }
      }
    }
  }, [remoteControlEnabled, remoteSessionToken, remoteSessionConflict, youtubePlayer, fxProgress, fxDurations, wsConnectionStatus])

  // ===== WebSocket Integration for Player =====

  const handleWsPlayerMessage = useCallback((msg) => {
    if (!msg) return

    // Handle commands received from remote via WebSocket (replaces polling)
    if (msg.type === 'command') {
      const cmd = msg
      switch (cmd.command) {
        case 'playPause':
          toggleMainPlayback()
          break
        case 'next':
          handleNextAndRemoveRef.current?.()
          break
        case 'seek':
          if (cmd.data?.seconds !== undefined) {
            seekPlayback(cmd.data.seconds)
          }
          break
        case 'replay':
          handleReplayRef.current?.()
          break
        case 'volume':
          if (cmd.data?.value !== undefined) {
            const newVol = cmd.data.value
            setVolume(newVol)
            setPlaybackVolume(newVol)
          }
          if (syncStateRef.current) syncStateRef.current()
          break
        case 'karaoke':
          if (cmd.data?.value !== undefined) setIsKaraokeMode(cmd.data.value)
          else setIsKaraokeMode(prev => !prev)
          if (syncStateRef.current) syncStateRef.current()
          break
        case 'fullscreenVideo':
          toggleFullscreenRef.current?.()
          break
        case 'toggleAutoPlay':
          setAutoPlayNext(prev => {
            const newVal = cmd.data?.value !== undefined ? !!cmd.data.value : !prev
            autoPlayNextRef.current = newVal
            localStorage.setItem(STORAGE_KEY_AUTO_PLAY_NEXT, JSON.stringify(newVal))
            return newVal
          })
          if (syncStateRef.current) syncStateRef.current()
          break
        case 'toggleTrackLock':
          setIsTrackChangeLocked(prev => {
            const newVal = cmd.data?.value !== undefined ? !!cmd.data.value : !prev
            isTrackChangeLockedRef.current = newVal
            return newVal
          })
          if (syncStateRef.current) syncStateRef.current()
          break
        case 'toggleSubtitles':
          setShowSubtitles(prev => !prev)
          if (youtubePlayerRef.current && youtubePlayerRef.current.setOption) {
            try {
              youtubePlayerRef.current.setOption('captions', 'fontSize', !showSubtitles ? 0 : -1)
            } catch (e) {
              console.log('Subtitle toggle error:', e)
            }
          }
          break
        case 'clearAll':
          handleClearAllRef.current?.()
          break
        case 'playVideo':
          if (cmd.data?.video) {
            handlePlayNowRef.current?.(cmd.data.video, cmd.data.from || 'search')
          } else if (cmd.data?.videoId) {
            const videos = getSelectedVideosFromStorage()
            const video = videos.find(v => v.id === cmd.data.videoId)
            if (video) handlePlayNowRef.current?.(video, 'selected')
          }
          break
        case 'priority':
          if (cmd.data?.videoId) handlePriorityRef.current?.(cmd.data.videoId, cmd.data.video)
          break
        case 'removeFromSelected':
          if (cmd.data?.videoId) handleRemoveSelectedRef.current?.(cmd.data.videoId)
          break
        case 'stopPlayback':
          hardStopAllPlayback()
          break
        case 'addToSelected':
          if (cmd.data?.video) handleAddToSelectedRef.current?.(cmd.data.video, false)
          break
        case 'importPlaylist':
          if (cmd.data?.videos && Array.isArray(cmd.data.videos)) {
            const currentList = getSelectedVideosFromStorage()
            const newVideos = cmd.data.videos.filter(v => !currentList.some(existing => existing.id === v.id))
            if (newVideos.length > 0) {
              const updatedList = [...currentList, ...newVideos]
              localStorage.setItem(STORAGE_KEY_SELECTED, JSON.stringify(updatedList))
              selectedVideosRef.current = updatedList
              setSelectedVideos(updatedList)
              if (syncStateRef.current) syncStateRef.current()
            }
          }
          break
        case 'shuffle': {
          if (isTrackChangeLockedRef.current) break
          const vids = getSelectedVideosFromStorage()
          const shuffledVids = [...vids].sort(() => Math.random() - 0.5)
          setSelectedVideos(shuffledVids)
          localStorage.setItem(STORAGE_KEY_SELECTED, JSON.stringify(shuffledVids))
          if (shuffledVids.length > 0) handlePlayNowRef.current?.(shuffledVids[0])
          break
        }
        case 'playSoundEffect':
          if (cmd.data?.id) {
            if (activeFxRef.current?.audio) {
              try { activeFxRef.current.audio.pause(); activeFxRef.current.audio.src = ''; activeFxRef.current.audio.load(); } catch { /* ignore */ }
            }
            const effects = getSoundEffects()
            const item = effects[cmd.data.id]
            if (item) {
              const url = typeof item === 'object' ? item.url : item
              const rawVol = typeof item === 'object' ? (item.volume ?? 80) : 80
              const targetVol = rawVol / 100
              const id = cmd.data.id
              const audioFx = new Audio(url)
              audioFx.volume = targetVol
              audioFx.muted = (rawVol === 0)
              audioFx.dataset.targetVol = targetVol.toString()
              const enforceVol = () => { const v = parseFloat(audioFx.dataset.targetVol || "0.8"); audioFx.volume = v; audioFx.muted = (v === 0); }
              audioFx.onplay = enforceVol
              audioFx.onplaying = enforceVol
              const newFx = { id, audio: audioFx, label: id, icon: faVolumeUp }
              setActiveFx(newFx)
              setIsFxPlaying(true)
              audioFx.onended = () => {
                activeFxRef.current = null
                setIsFxPlaying(false)
                setActiveFx(null)
                if (syncStateRef.current) syncStateRef.current()
              }
              audioFx.play().then(() => {
                enforceVol()
                if (syncStateRef.current) syncStateRef.current()
              }).catch(e => console.error('FAILED TO PLAY REMOTE EFFECT:', e))
            }
          }
          break
        case 'selectEffect':
          if (cmd.data) {
            const effectId = typeof cmd.data === 'string' ? cmd.data : cmd.data.id
            if (effectId) setSelectedFx({ id: effectId, ...buildEffectUiConfig(effectId, getSoundEffects()[effectId]) })
          }
          break
        case 'controlEffect':
          if (activeFxRef.current && activeFxRef.current.audio) {
            const { action, value } = cmd.data
            if (action === 'toggle') {
              if (isFxPlayingRef.current) activeFxRef.current.audio.pause()
              else activeFxRef.current.audio.play().catch(() => undefined)
              setIsFxPlaying(!isFxPlayingRef.current)
            } else if (action === 'stop') {
              activeFxRef.current.audio.pause()
              activeFxRef.current.audio.currentTime = 0
              activeFxRef.current = null
              setActiveFx(null)
              setIsFxPlaying(false)
            } else if (action === 'seek') {
              activeFxRef.current.audio.currentTime = value
              setFxProgress(value)
            } else if (action === 'volume') {
              const newVol = value / 100
              activeFxRef.current.audio.volume = newVol
              activeFxRef.current.audio.muted = (newVol === 0)
              activeFxRef.current.audio.dataset.targetVol = newVol.toString()
              setFxVolume(value)
            }
            if (syncStateRef.current) syncStateRef.current()
          }
          break
        case 'updateSoundEffectVolume':
          if (cmd.data?.id && cmd.data?.volume !== undefined) {
            const volVal = cmd.data.volume
            const targetVol = volVal / 100
            if (activeFxRef.current) {
              activeFxRef.current.audio.volume = targetVol
              activeFxRef.current.audio.muted = (volVal === 0)
              activeFxRef.current.audio.dataset.targetVol = targetVol.toString()
            }
            setSoundEffectsState(prev => {
              const currentState = prev || getSoundEffects() || {}
              const sItem = currentState[cmd.data.id]
              const next = { ...currentState, [cmd.data.id]: typeof sItem === 'object' ? { ...sItem, volume: cmd.data.volume } : { url: sItem, volume: cmd.data.volume } }
              saveSoundEffects(next)
              return next
            })
            if (selectedFx?.id === cmd.data.id) setFxVolume(cmd.data.volume)
            if (syncStateRef.current) syncStateRef.current()
          }
          break
        default:
          break
      }
    }

    // Handle peer connection events
    if (msg.type === 'peer_joined' || msg.type === 'peer_left') {
      setIsRemoteDeviceConnected(msg.hasRemote || false)
    }
  }, [hardStopAllPlayback, seekPlayback, setPlaybackVolume, toggleMainPlayback])

  // Connect player via WebSocket — output master only
  const { syncState: wsSyncState } = useWebSocket({
    role: 'player',
    token: remoteSessionToken,
    onMessage: handleWsPlayerMessage,
    enabled: remoteControlEnabled && !remoteSessionConflict && !isPreviewMode
  })

  // Keep wsSyncRef updated with latest syncState function
  useEffect(() => {
    wsSyncRef.current = wsSyncState
  }, [wsSyncState])

  // Đảm bảo session được đăng ký ngay khi bật hoặc đổi token
  useEffect(() => {
    if (isPreviewMode || !remoteControlEnabled || !remoteSessionToken || remoteSessionConflict) return undefined

    const initRemote = async () => {
      try {
        await registerSession(remoteSessionToken, null, sessionPassword)
        if (syncStateRef.current) syncStateRef.current()
      } catch (e) {
        console.error('Failed to init remote session:', e)
      }
    }
    initRemote()
    return undefined
  }, [isPreviewMode, remoteControlEnabled, remoteSessionToken, remoteSessionConflict, sessionPassword])

  /** Pause YouTube + SoundCloud + FX while keeping the current song resumable. */
  const pauseAllPlayback = useCallback(() => {
    pauseMainPlayback()
    try {
      if (activeFxRef.current?.audio) {
        activeFxRef.current.audio.pause()
        setIsFxPlaying(false)
      }
    } catch { /* ignore */ }
    setIsPlaying(false)
  }, [pauseMainPlayback])

  // ShowCue WebView2 bridge: host → player (pause/blackout) + player → host (ready/NOW)
  useEffect(() => {
    const inHost = isShowCueBridgeAvailable() || isShowCueHost()
    if (!inHost) return undefined

    // Only master reports ready as player; preview reports ready-preview
    notifyReady(remoteSessionToken)

    if (!isShowCueBridgeAvailable()) return undefined

    const unsub = onShowCueMessage((data) => {
      const action = (data.action || data.type || '').toString().toLowerCase()
      if (['esc', 'escape', 'pause', 'blackout'].includes(action)) {
        pauseAllPlayback()
        notifyPaused(remoteSessionToken)
      } else if (action === 'stop') {
        hardStopAllPlayback()
        notifyIdle(remoteSessionToken)
      } else if (action === 'play' || action === 'resume') {
        if (isPreviewMode) return // preview stays muted / follows master
        playMainPlayback()
      } else if (action === 'seek') {
        if (isPreviewMode) return
        const seconds = Number(data.seconds ?? data.position ?? data.time)
        if (Number.isFinite(seconds)) seekPlayback(seconds)
      } else if (action === 'volume') {
        if (isPreviewMode) return
        const raw = Number(data.value ?? data.volume)
        if (Number.isFinite(raw)) {
          const percent = Math.max(0, Math.min(100, raw <= 1 ? raw * 100 : raw))
          setVolume(percent)
          try { youtubePlayerRef.current?.unMute?.() } catch { /* ignore */ }
          setPlaybackVolume(percent)
        }
      } else if (action === 'mute') {
        if (isPreviewMode) return
        const muted = data.muted !== false
        try {
          if (muted) youtubePlayerRef.current?.mute?.()
          else {
            youtubePlayerRef.current?.unMute?.()
          }
        } catch { /* ignore */ }
        try { soundcloudWidgetRef.current?.setVolume?.(muted ? 0 : playbackVolumeRef.current) } catch { /* ignore */ }
      } else if (action === 'fullscreen' || action === 'fullscreenvideo' || action === 'phongto') {
        // ShowCue toolbar "Phóng to" → toggle player fullscreen (master only)
        if (isPreviewMode) return
        try { toggleFullscreenRef.current?.() } catch { /* ignore */ }
      }
    })

    return unsub
  }, [remoteSessionToken, pauseAllPlayback, hardStopAllPlayback, playMainPlayback, seekPlayback, setPlaybackVolume, isPreviewMode])

  // Đồng hồ phát chính xác cho thanh tiến trình bên ShowCue.
  useEffect(() => {
    if (isPreviewMode || !isShowCueBridgeAvailable()) return undefined
    const timer = window.setInterval(() => {
      notifyProgress({
        session: remoteSessionToken,
        position: Number(currentTimeRef.current) || 0,
        duration: Number(durationRef.current) || 0
      })
    }, 500)
    return () => window.clearInterval(timer)
  }, [remoteSessionToken, isPreviewMode])

  // Báo NOW title cho ShowCue — chỉ từ master (tránh spam preview)
  useEffect(() => {
    if (isPreviewMode) return
    if (!isShowCueBridgeAvailable() && !isShowCueHost()) return
    const title = selectedVideo?.title || selectedVideo?.name || ''
    const artist = selectedVideo?.channelTitle || selectedVideo?.artist || ''
    if (!selectedVideo) {
      notifyIdle(remoteSessionToken)
      return
    }
    if (isPlaying) {
      notifyPlaying({
        session: remoteSessionToken,
        title,
        artist,
        remaining: duration > 0 ? Math.max(0, duration - currentTime) : null
      })
    } else {
      notifyPaused(remoteSessionToken)
    }
  }, [selectedVideo?.id, isPlaying, remoteSessionToken, isPreviewMode]) // eslint-disable-line react-hooks/exhaustive-deps

  /** Áp state master lên preview (mute, cùng bài/seek) — gọi từ WS + HTTP */
  const applyPreviewMirrorState = useCallback((state) => {
    if (!state || state.error || isPreviewMode !== true) return

    const applyMute = () => {
      try {
        youtubePlayerRef.current?.mute?.()
        youtubePlayerRef.current?.setVolume?.(0)
      } catch { /* ignore */ }
      try { soundcloudWidgetRef.current?.setVolume?.(0) } catch { /* ignore */ }
    }

    const cv = state.currentVideo
    if (cv?.id && cv.id !== selectedVideoRef.current?.id) {
      stopCurrentMedia()
      selectedVideoRef.current = cv
      setSelectedVideo(cv)
    } else if (!cv && selectedVideoRef.current) {
      selectedVideoRef.current = null
      setSelectedVideo(null)
    }

    if (Array.isArray(state.playlist)) {
      const queue = state.playlist.filter((v) => v && v.id && v.id !== cv?.id)
      selectedVideosRef.current = queue
      setSelectedVideos(queue)
    }

    // Interpolation theo syncedAt + buffer nhẹ (dual WebView không frame-lock)
    let targetTime = Number(state.currentTime)
    if (state.isPlaying && state.syncedAt && Number.isFinite(targetTime)) {
      const drift = (Date.now() - Number(state.syncedAt)) / 1000
      // Bù latency mạng ~80ms
      if (drift > 0 && drift < 5) targetTime += drift + 0.08
    }

    if (cv?.id) {
      try {
        if (state.isPlaying === true) {
          playMainPlayback()
        } else if (state.isPlaying === false) {
          pauseMainPlayback()
        }
        if (Number.isFinite(targetTime) && Math.abs(currentTimeRef.current - targetTime) > 0.4) {
          seekPlayback(targetTime)
        }
      } catch { /* ignore */ }
    }

    applyMute()
    setVolume(0)
    setIsPlaying(!!state.isPlaying)
    if (Number.isFinite(targetTime)) setCurrentTime(targetTime)
    if (Number.isFinite(Number(state.duration))) setDuration(Number(state.duration))
  }, [isPreviewMode, pauseMainPlayback, playMainPlayback, seekPlayback, stopCurrentMedia])

  // Preview join WS role=remote → nhận state_sync realtime từ master
  const handlePreviewWsMessage = useCallback((msg) => {
    if (!msg || !isPreviewMode) return
    if (msg.type === 'state_sync' && msg.state) {
      applyPreviewMirrorState(msg.state)
    }
  }, [isPreviewMode, applyPreviewMirrorState])

  useWebSocket({
    role: 'remote',
    token: remoteSessionToken,
    onMessage: handlePreviewWsMessage,
    enabled: isPreviewMode && !!remoteSessionToken
  })

  // Master: pulse time-lock ~200ms khi đang phát (chỉ WS, dual-WebView mượt hơn)
  useEffect(() => {
    if (isPreviewMode || !remoteControlEnabled || !remoteSessionToken || remoteSessionConflict) {
      return undefined
    }
    const id = setInterval(() => {
      if (!isPlayingRef.current || !selectedVideoRef.current) return
      pushLightTimeSync()
    }, 200)
    return () => clearInterval(id)
  }, [isPreviewMode, remoteControlEnabled, remoteSessionToken, remoteSessionConflict, pushLightTimeSync])

  // HTTP fallback thưa (WS + time-pulse là chính)
  useEffect(() => {
    if (!isPreviewMode || !remoteSessionToken) return undefined

    setVolume(0)
    setIsFullscreen(true)
    let cancelled = false

    const tick = async () => {
      if (cancelled) return
      try {
        const state = await getRemoteState(remoteSessionToken)
        if (cancelled || !state || state.error) return
        applyPreviewMirrorState(state)
      } catch { /* ignore */ }
    }

    tick()
    const id = setInterval(tick, 2000)
    return () => {
      cancelled = true
      clearInterval(id)
    }
  }, [isPreviewMode, remoteSessionToken, applyPreviewMirrorState])

  // Keep preview muted + full frame
  useEffect(() => {
    if (!isPreviewMode) return
    setVolume(0)
    setIsFullscreen(true)
    try {
      youtubePlayerRef.current?.mute?.()
      youtubePlayerRef.current?.setVolume?.(0)
    } catch { /* ignore */ }
    try { soundcloudWidgetRef.current?.setVolume?.(0) } catch { /* ignore */ }
  }, [isPreviewMode, selectedVideo?.id, youtubePlayer])

  // Robust Sync Interval - Less frequent since WebSocket provides instant push
  useEffect(() => {
    if (isPreviewMode || !remoteControlEnabled || !remoteSessionToken || remoteSessionConflict) return

    let syncTimer = null
    let isMounted = true

    const runSync = async () => {
      if (!isMounted) return

      try {
        if (syncStateRef.current) {
          await syncStateRef.current()
        }
      } catch (e) {
        // console.error('Sync failed', e)
      } finally {
        if (isMounted) {
          // Full state (playlist…) — time-lock lo pulse riêng 200ms
          const interval = wsConnectionStatus === 'connected' ? 500 : 300
          syncTimer = setTimeout(runSync, interval)
        }
      }
    }

    runSync()

    return () => {
      isMounted = false
      if (syncTimer) clearTimeout(syncTimer)
    }
  }, [remoteControlEnabled, remoteSessionToken, remoteSessionConflict, wsConnectionStatus])

  // Cập nhật currentTime và duration định kỳ
  useEffect(() => {
    const interval = setInterval(() => {
      if (!selectedVideoRef.current) {
        if (currentTimeRef.current !== 0) {
          currentTimeRef.current = 0
          setCurrentTime(0)
        }
        if (durationRef.current !== 0) {
          durationRef.current = 0
          setDuration(0)
        }
        return
      }
      if (selectedVideoRef.current?.source === 'soundcloud') {
        const widget = soundcloudWidgetRef.current
        try {
          widget?.getPosition?.((milliseconds) => {
            const seconds = Math.max(0, Number(milliseconds) || 0) / 1000
            currentTimeRef.current = seconds
            setCurrentTime(seconds)
          })
          widget?.getDuration?.((milliseconds) => {
            const seconds = Math.max(0, Number(milliseconds) || 0) / 1000
            if (seconds > 0) {
              durationRef.current = seconds
              setDuration(seconds)
            }
          })
        } catch { /* ignore */ }
        return
      }
      if (youtubePlayer && youtubePlayer.getCurrentTime) {
        try {
          const time = youtubePlayer.getCurrentTime()
          const dur = youtubePlayer.getDuration()
          if (time !== currentTimeRef.current) setCurrentTime(time)
          if (dur !== durationRef.current) setDuration(dur)
        } catch {
          // Ignore transient player timing errors.
        }
      }
    }, 1000)
    return () => clearInterval(interval)
  }, [youtubePlayer])

  // Kiểm tra và xóa dữ liệu cũ mỗi khi cài đặt thay đổi
  useEffect(() => {
    deleteOldData()
  }, [autoDeleteEnabled, autoDeleteDays, deleteOldData])


  useEffect(() => {
    if (activeTab === 'effects') {
      const soundEffects = getSoundEffects()
      Object.keys(soundEffects).forEach(id => {
        const item = soundEffects[id]
        const url = typeof item === 'object' ? item.url : item
        if (url && !fxDurations[id]) {
          const audio = new Audio(url)
          audio.addEventListener('loadedmetadata', () => {
            setFxDurations(prev => ({ ...prev, [id]: audio.duration }))
          })
        }
      })
    }
  }, [activeTab])

  useEffect(() => {
    if (selectedVideo) {
      addToHistory(selectedVideo)

      // Pre-calculate duration from metadata if available
      if (selectedVideo.source === 'soundcloud' && Number(selectedVideo.duration) > 0) {
        const soundCloudDuration = Number(selectedVideo.duration) / 1000
        durationRef.current = soundCloudDuration
        setDuration(soundCloudDuration)
        setCurrentTime(0)
      } else if (selectedVideo.contentDetails?.duration) {
        const parsedDuration = parseISO8601Duration(selectedVideo.contentDetails.duration)
        if (parsedDuration > 0) {
          setDuration(parsedDuration)
          setCurrentTime(0)
          // Trigger immediate sync to show duration on remote
          if (syncStateRef.current) syncStateRef.current()
        }
      } else {
        // Reset progress for new video
        setDuration(0)
        setCurrentTime(0)
      }
      setWatchHistory(getHistory())

      // Store current playing video ID for remote control
      if (remoteControlEnabled) {
        localStorage.setItem('current_playing_video_id', selectedVideo.id)
      }

      // Nếu là SoundCloud, không khởi tạo YouTube player
      if (selectedVideo.source === 'soundcloud') {
        // Cleanup YouTube player nếu có
        if (youtubePlayer && playerRef.current) {
          try {
            youtubePlayer.stopVideo()
            // Đợi một chút trước khi ẩn iframe để tránh lỗi DOM
            setTimeout(() => {
              if (playerRef.current) {
                const iframe = playerRef.current.querySelector('iframe')
                if (iframe && iframe.parentNode === playerRef.current) {
                  iframe.style.display = 'none'
                  iframe.style.visibility = 'hidden'
                }
              }
            }, 100)
          } catch (error) {
            console.error('Error stopping YouTube player:', error)
          }
        }
        return
      }

      // Chỉ khởi tạo YouTube player cho YouTube videos
      // Khởi tạo player khi video thay đổi
      const initializePlayer = () => {
        if (!window.YT || !window.YT.Player || !playerRef.current || !selectedVideo) {
          return
        }

        // Nếu là SoundCloud, không khởi tạo YouTube player
        if (selectedVideo.source === 'soundcloud') {
          return
        }

        // Nếu đang load video mới (từ handleNextAndRemove), không làm gì
        if (isLoadingNewVideoRef.current) {
          return
        }

        // Nếu player đã tồn tại, kiểm tra xem video có khác không
        if (youtubePlayer) {
          try {
            const currentVideoId = youtubePlayer.getVideoData?.()?.video_id
            // Nếu video giống nhau, hiển thị iframe và return
            if (currentVideoId === selectedVideo.id) {
              // Hiển thị iframe nếu đang ẩn
              if (playerRef.current) {
                const iframe = playerRef.current.querySelector('iframe')
                if (iframe) {
                  iframe.style.display = ''
                  iframe.style.visibility = ''
                  iframe.style.opacity = ''
                }
              }
              return
            }
            // Nếu video khác, thử load video mới (không destroy)
            // Nhưng chỉ nếu player đã sẵn sàng (state >= 0)
            try {
              const playerState = youtubePlayer.getPlayerState?.()
              if (playerState !== undefined && playerState >= 0) {
                // Đánh dấu đang load video mới
                isLoadingNewVideoRef.current = true
                // Hiển thị iframe trước khi load video
                if (playerRef.current) {
                  const iframe = playerRef.current.querySelector('iframe')
                  if (iframe) {
                    iframe.style.display = ''
                    iframe.style.visibility = ''
                    iframe.style.opacity = ''
                  }
                }
                // Load video moi vao player hien co
                youtubePlayer.loadVideoById(selectedVideo.id)
                armWatchdogTimer(6000)
                setTimeout(() => {
                  try {
                    // Kiểm tra xem video đã load thành công chưa
                    const newVideoId = youtubePlayer.getVideoData?.()?.video_id
                    if (newVideoId === selectedVideo.id) {
                      // Video đã load thành công, phát video
                      youtubePlayer.playVideo()
                    }
                  } catch (e) {
                    console.error('Error checking video load:', e)
                  } finally {
                    isLoadingNewVideoRef.current = false
                  }
                }, 300)
                return
              }
            } catch (loadError) {
              // Nếu loadVideoById thất bại, tiếp tục tạo player mới
              console.error('Error loading video:', loadError)
              isLoadingNewVideoRef.current = false
            }
          } catch (error) {
            // Nếu không thể lấy video ID, tiếp tục tạo player mới
            console.error('Error getting video data:', error)
            isLoadingNewVideoRef.current = false
          }
        }

        // Đợi một chút để đảm bảo container đã render và có kích thước
        const timer = setTimeout(() => {
          if (playerRef.current && selectedVideo) {
            // Kiểm tra container có kích thước chưa
            const rect = playerRef.current.getBoundingClientRect()
            if (rect.width > 0 && rect.height > 0) {
              initPlayer()
            } else {
              // Nếu chưa có kích thước, thử lại sau
              setTimeout(() => {
                if (playerRef.current && selectedVideo) {
                  initPlayer()
                }
              }, 500)
            }
          }
        }, 100)
        return () => clearTimeout(timer)
      }

      // Nếu API chưa sẵn sàng, đợi nó load
      if (window.YT && window.YT.Player) {
        return initializePlayer()
      } else {
        // Đợi API load xong
        const checkAPI = setInterval(() => {
          if (window.YT && window.YT.Player) {
            clearInterval(checkAPI)
            initializePlayer()
          }
        }, 100)

        return () => {
          clearInterval(checkAPI)
        }
      }
    } else {
      // Dừng video nếu không có video, nhưng KHÔNG destroy player
      // Chỉ dừng nếu không đang load video mới
      isLoadingNewVideoRef.current = false
      stopCurrentMedia()
      currentTimeRef.current = 0
      durationRef.current = 0
      setCurrentTime(0)
      setDuration(0)
      setIsPlaying(false)
      try { localStorage.removeItem('current_playing_video_id') } catch { /* ignore */ }
      if (youtubePlayer) {
        try {
          youtubePlayer.stopVideo()
          // Ẩn iframe để không hiển thị video phía sau
          setTimeout(() => {
            if (playerRef.current) {
              const iframe = playerRef.current.querySelector('iframe')
              if (iframe && iframe.parentNode === playerRef.current) {
                iframe.style.display = 'none'
              }
            }
          }, 100)
        } catch (error) {
          console.error('Error stopping video:', error)
        }
      } else if (!youtubePlayer && playerRef.current) {
        // Nếu không có player nhưng có container, ẩn iframe nếu có
        const iframe = playerRef.current.querySelector('iframe')
        if (iframe && iframe.parentNode === playerRef.current) {
          iframe.style.display = 'none'
        }
      }
    }
  }, [selectedVideo?.id, selectedVideo?.source, stopCurrentMedia])

  // Cleanup: chỉ destroy player khi component unmount (không phải khi video thay đổi)
  useEffect(() => {
    return () => {
      // Chỉ destroy player khi component bị unmount hoàn toàn
      if (youtubePlayer) {
        try {
          // Đánh dấu đang cleanup để tránh xung đột
          isLoadingNewVideoRef.current = true
          // Dừng video trước
          youtubePlayer.stopVideo()
          // Đợi một chút rồi mới destroy để tránh xung đột với React
          setTimeout(() => {
            try {
              if (youtubePlayer && playerRef.current) {
                // Kiểm tra xem container vẫn còn trong DOM
                if (document.body.contains(playerRef.current)) {
                  // Kiểm tra xem iframe vẫn còn là child của container
                  const iframe = playerRef.current.querySelector('iframe')
                  if (iframe && iframe.parentNode === playerRef.current) {
                    youtubePlayer.destroy()
                  }
                }
              }
            } catch (destroyError) {
              // Ignore destroy errors khi component đã unmount
              console.warn('Error destroying player during cleanup:', destroyError)
            }
          }, 200)
        } catch (error) {
          // Ignore errors khi component đang unmount
          console.warn('Error stopping video during cleanup:', error)
        }
      }
    }
  }, []) // Empty deps - chỉ chạy khi component unmount


  const getFilteredVideos = (videos, forceAll = false) => {
    // 1. Lọc theo tính khả dụng (copyright, region, embeddable)
    let filtered = videos.filter(isPlayableVideo)
    // 2. Lọc theo chế độ Karaoke nếu bật (Chỉ áp dụng cho tìm kiếm, không áp dụng cho danh sách chọn/lịch sử)
    if (isKaraokeMode && !forceAll) {
      filtered = filtered.filter(v =>
        v.title?.toLowerCase().includes('karaoke') ||
        v.title?.toLowerCase().includes('karaok')
      )
    }
    return filtered
  }

  // Lọc video đang phát ra khỏi danh sách "Đã chọn" để tránh hiển thị trùng, và lọc theo tính khả dụng
  const filteredSelectedVideos = getFilteredVideos(selectedVideos, true).filter(video => video.id !== selectedVideo?.id)
  const filteredHistory = getFilteredVideos(watchHistory, true)
  const filteredSearchResults = getFilteredVideos(searchResults, false)
  const remoteControlUrl = `${window.location.origin}/remote?session=${remoteSessionToken}`

  return (
    <div className={`flex flex-col h-screen bg-gradient-to-br from-[#020617] via-[#040a1c] to-[#020617] text-white overflow-hidden transition-all duration-500`}>
      <div className={`flex flex-col lg:flex-row flex-1 overflow-hidden transition-all duration-500 ${isFullscreen ? 'p-0' : ''}`}>
        {/* Main Video Player Area - Enhanced */}
        <div className={`flex-1 flex flex-col bg-gradient-to-br from-[#020617] via-[#040a1c] to-[#020617] transition-all duration-500 ${isFullscreen ? 'p-0' : 'lg:pr-2'} min-h-0`}>
          <div className={`flex-1 relative group flex items-start justify-start transition-all duration-500 ${isFullscreen ? 'p-0' : 'p-2 sm:p-3 lg:p-4 lg:pr-2'} min-h-0 overflow-auto scrollbar-hide`}>
            <div className={`w-full transition-all duration-500 ${isFullscreen ? 'lg:mr-0 space-y-0 h-full' : 'lg:mr-4 space-y-2'}`}>
              <div className={`relative overflow-hidden shadow-[0_25px_80px_-30px_rgba(15,23,42,0.8)] border-slate-700/60 bg-black/80 w-full transition-all duration-500 ${isFullscreen ? 'h-screen rounded-none border-0' : 'aspect-video rounded-lg sm:rounded-xl border'}`}>
                {isPreviewMode && (
                  <div className="absolute top-3 left-3 z-30 pointer-events-none">
                    <span className="px-2.5 py-1 rounded-lg bg-black/75 border border-amber-500/40 text-amber-300 text-[10px] font-black uppercase tracking-wider shadow-lg">
                      PREVIEW · MUTE
                    </span>
                  </div>
                )}
                {/* YouTube Player Container - Luôn tồn tại, chỉ ẩn/hiện */}
                <div
                  ref={playerRef}
                  className={`absolute inset-0 w-full h-full rounded-2xl ${selectedVideo?.source === 'soundcloud' || streamUrl ? 'hidden' : ''
                    }`}
                />

                {/* Direct Stream Player - Dùng khi YouTube bị chặn vùng lãnh thổ */}
                {streamUrl && selectedVideo?.source !== 'soundcloud' && (
                  <div className="absolute inset-0 w-full h-full bg-black rounded-2xl overflow-hidden flex items-center justify-center">
                    <video
                      ref={fallbackVideoRef}
                      src={streamUrl}
                      controls
                      autoPlay
                      className="w-full h-full object-contain"
                      onEnded={() => handleNextAndRemove()}
                      onError={() => handleNextAndRemove()}
                    />
                    <div className="absolute top-4 left-4 px-3 py-1.5 bg-blue-600/90 text-white text-[10px] font-bold rounded-lg shadow-xl backdrop-blur-sm animate-pulse flex items-center gap-2">
                      <span className="w-2 h-2 bg-white rounded-full"></span>
                      LINK DỰ PHÒNG (KHÔNG QUẢNG CÁO)
                    </div>
                  </div>
                )}

                {/* SoundCloud Container - Luôn tồn tại, chỉ ẩn/hiện */}
                <div
                  ref={soundcloudRef}
                  className={`absolute inset-0 w-full h-full rounded-2xl ${selectedVideo?.source !== 'soundcloud' ? 'hidden' : ''
                    }`}
                >
                  {selectedVideo?.source === 'soundcloud' && (
                    <>
                      {/* Thumbnail background */}
                      {selectedVideo.thumbnail && (
                        <img
                          src={selectedVideo.thumbnail}
                          alt={selectedVideo.title}
                          className="w-full h-full object-cover"
                        />
                      )}
                      {/* SoundCloud Embed */}
                      {selectedVideo.permalinkUrl ? (
                        <div className="absolute inset-0 flex items-center justify-center bg-black/30">
                          <iframe
                            ref={soundcloudIframeRef}
                            key={`sc-embed-${selectedVideo.id}`}
                            width="100%"
                            height="100%"
                            scrolling="no"
                            frameBorder="no"
                            allow="autoplay; fullscreen"
                            src={`https://w.soundcloud.com/player/?url=${encodeURIComponent(selectedVideo.permalinkUrl)}&color=%23ff5500&auto_play=${isPreviewMode ? 'false' : 'true'}&hide_related=false&show_comments=true&show_user=true&show_reposts=false&show_teaser=true&visual=true`}
                            className="rounded-2xl"
                            title={selectedVideo.title}
                          />
                        </div>
                      ) : (
                        <div className="absolute inset-0 flex items-center justify-center bg-slate-900">
                          <div className="text-center px-6">
                            <p className="text-sm text-gray-400">Không thể phát SoundCloud track</p>
                          </div>
                        </div>
                      )}
                      <div className="absolute inset-0 bg-gradient-to-t from-black/20 to-transparent pointer-events-none rounded-2xl z-10" />
                    </>
                  )}
                </div>

                {/* Empty state - vkara-style with QR overlay */}
                {!selectedVideo && (
                  <div className="absolute inset-0 flex items-center justify-center bg-gradient-to-br from-slate-950 via-slate-900 to-slate-950 z-20">
                    <div className="flex flex-col items-center justify-center gap-4 sm:gap-6 px-4 w-full max-w-lg">
                      {/* App logo + title */}
                      <div className="flex flex-col items-center gap-2">
                        <img src={appLogo} alt="App Logo" className="w-10 h-10 sm:w-14 sm:h-14 object-contain drop-shadow-[0_12px_24px_rgba(0,0,0,0.55)]" />
                        <p className="text-base sm:text-xl font-bold bg-gradient-to-r from-white to-gray-400 bg-clip-text text-transparent">
                          Chọn video để phát
                        </p>
                      </div>

                      {/* Session only (không room id) */}
                      {remoteControlEnabled && remoteSessionToken && (
                        <div className="flex flex-col items-center gap-2 w-full">
                          <p className="text-[10px] text-slate-500 font-medium uppercase tracking-widest">Session</p>
                          <code className="px-4 py-2 rounded-xl bg-slate-800/80 border border-slate-600/50 text-lg font-mono font-black tracking-widest text-white">
                            {remoteSessionToken}
                          </code>
                          <p className="text-[10px] text-slate-500 text-center">
                            Remote: /remote?session={remoteSessionToken}
                          </p>
                        </div>
                      )}

                      {/* Hint */}
                      <p className="text-xs text-slate-600 text-center">Tìm kiếm và chọn video từ sidebar để bắt đầu</p>
                    </div>
                  </div>
                )}

                {/* Gradient overlay - chỉ hiển thị khi có video */}
                {selectedVideo && (
                  <div className="absolute inset-0 bg-gradient-to-t from-black/20 to-transparent pointer-events-none rounded-2xl z-10" />
                )}
              </div>

              {/* Bottom Controls - Floating higher to avoid overlapping YouTube UI, made larger for visibility */}
              <div className={`transition-all duration-500 ${isFullscreen
                ? `fixed bottom-20 sm:bottom-24 left-1/2 -translate-x-1/2 z-[100] p-2.5 sm:p-3 px-3 sm:px-4 rounded-2xl sm:rounded-3xl w-[calc(100%-1rem)] sm:w-[96%] max-w-6xl ${showControls ? 'opacity-100 translate-y-0' : 'opacity-0 translate-y-20 pointer-events-none'}`
                : 'relative mt-4 p-3 sm:p-4 md:p-5'
                } bg-slate-900/95 border border-white/10 ${!isFullscreen ? 'rounded-xl sm:rounded-2xl' : ''} backdrop-blur-2xl shadow-[0_20px_60px_rgba(0,0,0,0.7)]`}>
                <div className={`flex ${isFullscreen ? 'flex-col gap-2 xl:flex-row xl:items-center xl:gap-4' : 'flex-col gap-4'}`}>
                  {/* Metadata Section */}
                  <div className={`min-w-0 ${isFullscreen ? 'w-full px-1 xl:flex-1 xl:border-r xl:border-white/10 xl:pr-4' : 'px-1'}`}>
                    {selectedVideo ? (
                      <>
                        <div className={`flex items-center gap-3 ${isFullscreen ? 'flex-1' : ''}`}>
                          <h3 className={`font-bold text-white line-clamp-1 break-words tracking-tight ${isFullscreen ? 'text-sm md:text-base' : 'text-sm sm:text-base font-bold mb-1'}`}>
                            {decodeHtmlEntities(selectedVideo.title)}
                          </h3>
                          {isFullscreen && selectedVideo.channelTitle && (
                            <span className="text-[10px] text-slate-500 font-medium truncate hidden md:inline shrink-0">
                              • {decodeHtmlEntities(selectedVideo.channelTitle)}
                            </span>
                          )}
                        </div>
                        {!isFullscreen && (
                          <div className="flex flex-wrap items-center gap-3 text-xs text-slate-400 font-medium">
                            {selectedVideo.viewCount && (
                              <span className="flex items-center space-x-2">
                                <FontAwesomeIcon icon={faEye} className="w-3 h-3 text-slate-500" />
                                <span>{formatViewCount(selectedVideo.viewCount)} lượt xem</span>
                              </span>
                            )}
                            <div className="w-1 h-1 rounded-full bg-slate-700" />
                            {selectedVideo.channelTitle && (
                              <span className="flex items-center space-x-2">
                                <FontAwesomeIcon icon={faTv} className="w-3 h-3 text-slate-500" />
                                <span className="truncate max-w-[150px] sm:max-w-none">{decodeHtmlEntities(selectedVideo.channelTitle)}</span>
                              </span>
                            )}
                          </div>
                        )}
                      </>
                    ) : (
                      <>
                        <h3 className="text-xs sm:text-sm md:text-base font-semibold mb-1 text-slate-400">
                          Chưa có bài hát
                        </h3>
                        <div className="text-[10px] sm:text-xs text-slate-500">
                          <span>Chọn bài hát từ sidebar để phát</span>
                        </div>
                      </>
                    )}
                  </div>

                  {/* Responsive controls: keep labels readable in narrow WebViews. */}
                  <div className={`grid w-full items-center ${isFullscreen ? 'grid-cols-4 gap-2 sm:grid-cols-7 xl:flex-[1.8]' : 'grid-cols-2 gap-3 xs:grid-cols-4 lg:grid-cols-7'}`}>
                    <button
                        onClick={handleReplay}
                        disabled={!selectedVideo}
                        className={`w-full min-w-0 px-2 ${isFullscreen ? 'h-10' : 'h-11'} bg-slate-800/80 hover:bg-slate-700 active:bg-slate-600 rounded-xl text-xs sm:text-sm font-bold text-slate-200 transition-all flex items-center justify-center gap-2 border border-white/5 disabled:opacity-30 active:scale-95 shadow-lg`}
                      >
                        <FontAwesomeIcon icon={faRedo} className={`${isFullscreen ? 'text-sm' : 'text-sm'}`} />
                        <span className="hidden xs:inline">Phát lại</span>
                        <span className="xs:hidden">Lại</span>
                    </button>

                    <button
                      onClick={() => handleNextAndRemove()}
                      disabled={!selectedVideo || isTrackChangeLocked}
                      title={isTrackChangeLocked ? 'Đang khóa chuyển bài' : 'Bài tiếp'}
                      className={`w-full min-w-0 px-2 ${isFullscreen ? 'h-10' : 'h-11'} bg-gradient-to-r from-blue-600 to-blue-500 hover:from-blue-500 hover:to-blue-400 active:from-blue-700 rounded-xl text-xs sm:text-sm font-bold text-white transition-all flex items-center justify-center gap-2 shadow-[0_4px_15px_rgba(37,99,235,0.4)] disabled:opacity-30 active:scale-95`}
                    >
                      <FontAwesomeIcon icon={isTrackChangeLocked ? faLock : faForwardStep} className={`${isFullscreen ? 'text-sm' : 'text-sm'}`} />
                      <span>{isTrackChangeLocked ? 'Khóa' : 'Tiếp'}</span>
                    </button>

                    <button
                      onClick={() => setShowSubtitles(!showSubtitles)}
                      disabled={!selectedVideo || selectedVideo?.source === 'soundcloud'}
                      className={`w-full min-w-0 px-2 ${isFullscreen ? 'h-10' : 'h-11'} rounded-xl text-xs sm:text-sm font-bold transition-all flex items-center justify-center gap-2 border shadow-lg active:scale-95 disabled:opacity-30 ${showSubtitles ? 'bg-purple-600/80 hover:bg-purple-500 active:bg-purple-700 border-purple-500/50 text-white' : 'bg-slate-800/80 hover:bg-slate-700 active:bg-slate-600 border-white/5 text-slate-200'}`}
                    >
                      <FontAwesomeIcon icon={faClock} className={`${isFullscreen ? 'text-sm' : 'text-sm'}`} />
                      <span className="hidden xs:inline">{showSubtitles ? 'CC On' : 'CC Off'}</span>
                      <span className="xs:hidden">{showSubtitles ? 'On' : 'Off'}</span>
                    </button>

                    <div className={`w-full min-w-0 ${isFullscreen ? 'h-10' : 'h-11'} flex items-center justify-center gap-2 bg-slate-800/80 border border-white/5 rounded-xl px-2 shadow-lg`}>
                      <FontAwesomeIcon icon={faMicrophone} className={`${isFullscreen ? 'text-sm' : 'text-sm'} ${isKaraokeMode ? 'text-green-500' : 'text-gray-400'}`} />
                      <span className="text-xs sm:text-sm font-bold text-slate-300 hidden sm:inline">{isFullscreen ? 'KTV' : 'Karaoke'}</span>
                      <button
                        onClick={() => setIsKaraokeMode(!isKaraokeMode)}
                        className={`relative shrink-0 ${isFullscreen ? 'w-8 h-4' : 'w-8 h-4'} rounded-full transition-all ${isKaraokeMode ? 'bg-green-500' : 'bg-slate-600'}`}
                      >
                        <span className={`absolute top-0.5 left-0.5 ${isFullscreen ? 'w-3 h-3' : 'w-3 h-3'} bg-white rounded-full transition-transform ${isKaraokeMode ? (isFullscreen ? 'translate-x-4' : 'translate-x-4') : ''}`} />
                      </button>
                    </div>

                    <a
                      href="https://www.facebook.com/taodangcap"
                      target="_blank"
                      rel="noopener noreferrer"
                      className={`w-full min-w-0 px-2 ${isFullscreen ? 'h-10' : 'h-11'} bg-blue-600/10 hover:bg-blue-600/20 text-blue-400 rounded-xl text-xs sm:text-sm font-bold transition-all flex items-center justify-center gap-2 border border-blue-500/20 shadow-lg active:scale-95`}
                    >
                      <FontAwesomeIcon icon={faFacebook} className={`${isFullscreen ? 'text-sm' : 'text-sm'}`} />
                      <span>FB</span>
                    </a>

                    <div className={`w-full min-w-0 px-2 ${isFullscreen ? 'h-10' : 'h-11'} flex items-center justify-center gap-2 border rounded-xl transition-all shadow-lg ${remoteControlEnabled && isRemoteDeviceConnected ? 'bg-green-600/20 border-green-500/30 text-green-400' : 'bg-red-600/20 border-red-500/30 text-red-400'
                      }`}>
                      <div className={`w-1.5 h-1.5 rounded-full ${remoteControlEnabled && isRemoteDeviceConnected ? 'bg-green-500 animate-pulse' : 'bg-red-500'}`} />
                      <span className={`font-black tracking-widest uppercase truncate ${isFullscreen ? 'text-[10px]' : 'text-[10px]'}`}>
                        {isRemoteDeviceConnected ? 'ONLINE' : 'OFFLINE'}
                      </span>
                    </div>


                    <button
                      onClick={toggleFullscreen}
                      className={`w-full min-w-0 px-2 ${isFullscreen ? 'h-10' : 'h-11'} bg-slate-800/80 hover:bg-slate-700 text-slate-200 rounded-xl text-xs sm:text-sm font-bold transition-all flex items-center justify-center gap-2 border border-white/5 shadow-lg active:scale-95`}
                    >
                      <FontAwesomeIcon icon={isFullscreen ? faCompress : faExpand} className={`${isFullscreen ? 'text-sm' : 'text-sm'}`} />
                      <span className="hidden sm:inline">{isFullscreen ? "Thu nhỏ" : "Phóng to"}</span>
                      <span className="sm:hidden">{isFullscreen ? "Thu" : "To"}</span>
                    </button>
                  </div>
                </div>
              </div>

              {/* Floating Toggle Button for Fullscreen */}
              {isFullscreen && (
                <div className="fixed bottom-3 right-3 z-[110] sm:bottom-4 sm:right-4">
                  <button
                    onClick={(e) => {
                      e.stopPropagation();
                      if (showControls) {
                        setShowControls(false);
                        if (controlsTimeoutRef.current) clearTimeout(controlsTimeoutRef.current);
                      } else {
                        resetControlsTimer();
                      }
                    }}
                    onPointerDown={(e) => {
                      // Fix for air mice and sensitive TV remotes
                      e.stopPropagation();
                    }}
                    onKeyDown={(e) => {
                      // Support TV Remote 'OK' or 'Enter' key
                      if (e.key === 'Enter' || e.key === ' ') {
                        e.preventDefault();
                        e.stopPropagation();
                        if (showControls) {
                          setShowControls(false);
                          if (controlsTimeoutRef.current) clearTimeout(controlsTimeoutRef.current);
                        } else {
                          resetControlsTimer();
                        }
                      }
                    }}
                    className={`w-11 h-11 sm:w-12 sm:h-12 rounded-full bg-blue-600/30 hover:bg-blue-600/60 focus:bg-blue-600/70 focus:outline-none focus:ring-4 focus:ring-blue-500/50 text-white/70 hover:text-white focus:text-white shadow-[0_10px_30px_rgba(0,0,0,0.35)] transition-all duration-200 flex items-center justify-center active:scale-110 border-2 border-white/10 hover:border-white/30 focus:border-white/50 backdrop-blur-md cursor-pointer`}
                    aria-label="Toggle Controls"
                  >
                    <FontAwesomeIcon
                      icon={faArrowUp}
                      className={`transition-transform duration-500 ${showControls ? 'rotate-180' : ''} text-lg md:text-xl`}
                    />
                  </button>
                </div>
              )}
            </div>
          </div>
        </div>

        {/* Sidebar - Enhanced, hidden in fullscreen */}
        <div className={`${isFullscreen ? 'hidden' : 'flex'} w-full lg:w-[420px] bg-slate-800/95 backdrop-blur-md border-t lg:border-t-0 lg:border-l border-slate-700/50 flex flex-col shadow-2xl h-[50vh] sm:h-[60vh] lg:h-auto lg:max-h-none transition-all duration-500`}>
          {/* Logo Header */}
          <div className="p-3 border-b border-slate-700/50 bg-slate-900/50 flex items-center justify-between px-4">
            <div className="flex items-center gap-2">
              <img src={appLogo} alt="Logo" className="w-6 h-6 object-contain" />
              <span className="font-bold text-base bg-gradient-to-r from-purple-400 to-blue-400 bg-clip-text text-transparent">Trịnh Quang Huy</span>
            </div>
            <button
              onClick={() => setActiveTab('settings')}
              className={`flex items-center gap-2 group transition-all ${activeTab === 'settings' ? 'text-blue-400' : 'text-gray-500 hover:text-white'}`}
            >
              <FontAwesomeIcon icon={faGear} className={`text-sm ${activeTab === 'settings' ? 'animate-spin-slow' : ''}`} />
              <div className="text-[10px] font-bold uppercase tracking-wider">Cài đặt</div>
            </button>
          </div>

          {/* Tabs - Enhanced */}
          <div className="p-2 sm:p-3 bg-slate-800/50 border-b border-slate-700/50 flex-shrink-0">
            <div className="flex gap-1.5 sm:gap-2 overflow-x-auto scrollbar-hide items-center">
              {[
                { id: 'search', label: 'Tìm kiếm', shortLabel: 'Tìm', icon: faSearchSolid },
                { id: 'history', label: 'Lịch sử', shortLabel: 'Sử', icon: faHistory },
                { id: 'selected', label: `Đã chọn (${filteredSelectedVideos.length})`, shortLabel: `Chọn (${filteredSelectedVideos.length})`, icon: faListCheck },
                { id: 'settings', label: 'Cài đặt', shortLabel: 'Đặt', icon: faGear },
              ].map((tab) => (
                <button
                  key={tab.id}
                  onClick={() => setActiveTab(tab.id)}
                  title={tab.label}
                  className={`px-2.5 py-1.5 sm:px-3 sm:py-2 text-[10px] sm:text-xs font-bold transition-all duration-300 rounded-lg sm:rounded-xl whitespace-nowrap flex-shrink-0 touch-manipulation flex items-center gap-1.5 ${activeTab === tab.id
                    ? 'bg-gradient-to-r from-blue-600 via-blue-500 to-blue-600 text-white shadow-lg shadow-blue-500/20'
                    : 'text-gray-400 hover:text-gray-200 hover:bg-slate-700/50 active:scale-95'
                    }`}
                >
                  <FontAwesomeIcon icon={tab.icon} className="text-[10px] sm:text-xs" />
                  <span className="hidden xl:inline">{tab.label}</span>
                  <span className="xl:hidden inline">{tab.shortLabel}</span>
                </button>
              ))}
            </div>
          </div>

          {/* Tab Content - Enhanced */}
          <div className="flex-1 overflow-y-auto p-2 sm:p-3 md:p-4 scrollbar-thin scrollbar-thumb-slate-600 scrollbar-track-slate-800 min-h-0">
            {activeTab === 'search' && (
              <div>
                <form onSubmit={handleSearch} className="mb-3">
                  {/* Chọn nguồn tìm kiếm */}
                  <div className="mb-2 flex gap-2">
                    <button
                      type="button"
                      onClick={() => switchSearchSource('youtube')}
                      className={`flex-1 px-3 py-2 rounded-lg text-xs sm:text-sm font-medium transition-all duration-200 ${searchSource === 'youtube'
                        ? 'bg-gradient-to-r from-red-600 to-red-500 text-white shadow-lg shadow-red-500/30'
                        : 'bg-slate-700/50 text-gray-400 hover:bg-slate-700/70'
                        }`}
                    >
                      <Youtube size={16} className="inline mr-1.5" />
                      YouTube
                    </button>
                    <button
                      type="button"
                      onClick={() => switchSearchSource('soundcloud')}
                      className={`flex-1 px-3 py-2 rounded-lg text-xs sm:text-sm font-medium transition-all duration-200 ${searchSource === 'soundcloud'
                        ? 'bg-gradient-to-r from-orange-600 to-orange-500 text-white shadow-lg shadow-orange-500/30'
                        : 'bg-slate-700/50 text-gray-400 hover:bg-slate-700/70'
                        }`}
                    >
                      <Music size={16} className="inline mr-1.5" />
                      SoundCloud
                    </button>
                  </div>

                  <div className={`relative flex items-center transition-all duration-300 ${isSearchFocused ? 'gap-2' : 'space-x-1.5 sm:space-x-2'}`}>
                    <div className={`transition-all duration-300 ${isSearchFocused ? 'flex-1' : 'flex-1'}`}>
                      <SearchAutocomplete
                        searchQuery={searchQuery}
                        setSearchQuery={setSearchQuery}
                        onSearch={handleSearch}
                        searchSource={searchSource}
                        isKaraokeMode={isKaraokeMode}
                        onSearchComplete={() => setIsSearchFocused(false)}
                        className="w-full px-2.5 py-2 sm:px-3 sm:py-2 bg-slate-700/50 text-white rounded-lg border border-slate-600/50 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent text-xs sm:text-sm placeholder-gray-400 transition-all duration-200 backdrop-blur-sm min-h-[36px] focus:bg-slate-700/70 focus:border-blue-500/50"
                        onFocus={() => setIsSearchFocused(true)}
                        onBlur={() => setTimeout(() => setIsSearchFocused(false), 150)}
                      />
                    </div>
                    <button
                      type="submit"
                      disabled={isSearching}
                      className={`px-3 py-2 bg-gradient-to-r from-blue-600 to-blue-500 hover:from-blue-500 hover:to-blue-400 active:from-blue-400 active:to-blue-300 rounded-lg transition-all duration-300 shadow-lg shadow-blue-500/30 active:scale-95 disabled:opacity-50 disabled:cursor-not-allowed disabled:active:scale-100 touch-manipulation min-h-[36px] flex items-center justify-center ${isSearchFocused ? 'opacity-0 w-0 px-0 overflow-hidden' : 'opacity-100'}`}
                    >
                      <Search size={16} className="sm:w-5 sm:h-5" />
                    </button>
                  </div>
                  {isKaraokeMode && (
                    <div className="mt-2 px-3 py-1.5 bg-green-500/10 border border-green-500/30 rounded-lg">
                      <p className="text-[10px] sm:text-xs text-green-400 flex items-center space-x-1.5">
                        <FontAwesomeIcon icon={faMicrophone} className="text-xs" />
                        <span>Chế độ Karaoke: Chỉ hiển thị bài hát karaoke</span>
                      </p>
                    </div>
                  )}
                </form>

                {isSearching ? (
                  <div className="text-center py-12">
                    <div className="inline-block animate-spin rounded-full h-8 w-8 border-b-2 border-blue-500"></div>
                    <p className="text-gray-400 mt-4 text-sm">Đang tìm kiếm...</p>
                  </div>
                ) : (
                  <div className="space-y-2">
                    {searchError && (
                      <div className="px-4 py-3 bg-red-500/10 border border-red-500/30 rounded-lg">
                        <p className="text-xs text-red-400 flex items-start space-x-2">
                          <FontAwesomeIcon icon={faInfoCircle} className="mt-0.5 flex-shrink-0" />
                          <span>{searchError}</span>
                        </p>
                      </div>
                    )}
                    {filteredSearchResults.length > 0 ? (
                      filteredSearchResults.map((video) => (
                        <div
                          key={video.id}
                          className="group rounded-lg sm:rounded-xl active:bg-slate-700/50 transition-all duration-200 border border-transparent active:border-slate-600/50 overflow-hidden touch-manipulation"
                        >
                          <div
                            onClick={() => setExpandedVideoId(expandedVideoId === video.id ? null : video.id)}
                            className="flex space-x-1.5 sm:space-x-2 p-1.5 sm:p-2 cursor-pointer"
                          >
                            <div className="relative flex-shrink-0">
                              <img
                                src={video.thumbnail}
                                alt={decodeHtmlEntities(video.title)}
                                className="w-20 h-14 sm:w-24 sm:h-16 object-cover rounded-md shadow-lg"
                              />
                              {video.duration && (
                                <div className="absolute bottom-1 right-1 px-1 py-0.5 bg-black/80 text-[8px] sm:text-[10px] text-white font-bold rounded">
                                  {(() => {
                                    if (video.source === 'soundcloud') return formatSoundCloudDuration(video.duration)
                                    try { return formatYouTubeDuration(video.duration) } catch (e) { return '0:00' }
                                  })()}
                                </div>
                              )}
                            </div>
                            <div className="flex-1 min-w-0">
                              <div className="text-[11px] sm:text-xs font-semibold line-clamp-2 text-white break-words">
                                {decodeHtmlEntities(video.title)}
                              </div>
                              <div className="text-[10px] sm:text-xs text-gray-400 mt-0.5 truncate">
                                {decodeHtmlEntities(video.channelTitle)}
                              </div>
                              {video.viewCount && (
                                <div className="text-[10px] sm:text-xs text-gray-500 mt-0.5 flex items-center space-x-1">
                                  <FontAwesomeIcon icon={faEye} className="w-2.5 h-2.5 sm:w-3 sm:h-3" />
                                  <span className="truncate">{formatViewCount(video.viewCount)} lượt xem</span>
                                </div>
                              )}
                            </div>
                          </div>

                          {/* Buttons hiển thị khi click vào video */}
                          {expandedVideoId === video.id && (
                            <div className="px-1.5 sm:px-2 pb-1.5 sm:pb-2 pt-0 border-t border-slate-700/50 mt-1 animate-fadeIn bg-slate-700/30">
                              <div className="flex space-x-1.5 pt-1.5">
                                <button
                                  onClick={(e) => {
                                    e.stopPropagation()
                                    handlePlayNow(video, 'search')
                                  }}
                                  className="flex-1 px-2 sm:px-3 py-1.5 sm:py-2 bg-gradient-to-r from-blue-600 to-blue-500 active:from-blue-400 active:to-blue-300 rounded-lg text-[10px] sm:text-xs font-medium transition-all duration-200 shadow-lg shadow-blue-500/30 active:scale-95 flex items-center justify-center space-x-1 touch-manipulation min-h-[32px]"
                                >
                                  <FontAwesomeIcon icon={faPlayCircle} className="text-xs sm:text-sm" />
                                  <span className="whitespace-nowrap hidden xs:inline">Phát Ngay</span>
                                  <span className="whitespace-nowrap xs:hidden">Phát</span>
                                </button>
                                <button
                                  onClick={(e) => {
                                    e.stopPropagation()
                                    handleAddToSelected(video)
                                  }}
                                  className="flex-1 px-2 sm:px-3 py-1.5 sm:py-2 bg-gradient-to-r from-green-600 to-green-500 active:from-green-400 active:to-green-300 rounded-lg text-[10px] sm:text-xs font-medium transition-all duration-200 shadow-lg shadow-green-500/30 active:scale-95 flex items-center justify-center space-x-1 touch-manipulation min-h-[32px]"
                                >
                                  <FontAwesomeIcon icon={faPlus} className="text-xs sm:text-sm" />
                                  <span>Thêm</span>
                                </button>
                              </div>
                            </div>
                          )}
                        </div>
                      ))
                    ) : searchQuery ? (
                      <div className="text-center py-12">
                        <FontAwesomeIcon icon={faSearchSolid} className="text-5xl text-gray-500 mb-3" />
                        <p className="text-gray-400 text-sm">
                          {isKaraokeMode ? 'Không tìm thấy bài hát karaoke' : 'Không tìm thấy kết quả'}
                        </p>
                      </div>
                    ) : (
                      <div className="text-center py-12">
                        <FontAwesomeIcon icon={faMusicSolid} className="text-5xl text-gray-500 mb-3" />
                        <p className="text-gray-400 text-sm">Nhập từ khóa để tìm kiếm</p>
                      </div>
                    )}
                  </div>
                )}
              </div>
            )}

            {activeTab === 'history' && (
              <div className="space-y-2">
                {filteredHistory.length === 0 ? (
                  <div className="text-center py-12">
                    <FontAwesomeIcon icon={faHistory} className="text-5xl text-gray-500 mb-3" />
                    <p className="text-gray-400 text-sm">
                      {isKaraokeMode ? 'Chưa có lịch sử karaoke' : 'Chưa có lịch sử'}
                    </p>
                  </div>
                ) : (
                  filteredHistory.map((video) => (
                    <div
                      key={video.id}
                      className="group rounded-lg sm:rounded-xl active:bg-slate-700/50 transition-all duration-200 border border-transparent active:border-slate-600/50 overflow-hidden touch-manipulation"
                    >
                      <div
                        onClick={() => setExpandedVideoId(expandedVideoId === video.id ? null : video.id)}
                        className="flex space-x-1.5 sm:space-x-2 p-1.5 sm:p-2 cursor-pointer"
                      >
                        <div className="relative flex-shrink-0">
                          <img
                            src={video.thumbnail}
                            alt={decodeHtmlEntities(video.title)}
                            className="w-20 h-14 sm:w-24 sm:h-16 object-cover rounded-md shadow-lg"
                          />
                          {video.duration && (
                            <div className="absolute bottom-1 right-1 px-1 py-0.5 bg-black/80 text-[8px] sm:text-[10px] text-white font-bold rounded">
                              {(() => {
                                if (video.source === 'soundcloud') return formatSoundCloudDuration(video.duration)
                                try { return formatYouTubeDuration(video.duration) } catch (e) { return '0:00' }
                              })()}
                            </div>
                          )}
                        </div>
                        <div className="flex-1 min-w-0">
                          <div className="text-[11px] sm:text-xs font-semibold line-clamp-2 text-white break-words">
                            {decodeHtmlEntities(video.title)}
                          </div>
                          <div className="text-[10px] sm:text-xs text-gray-400 mt-0.5 truncate">
                            {decodeHtmlEntities(video.channelTitle)}
                          </div>
                        </div>
                      </div>

                      {/* Buttons hiển thị khi click vào video */}
                      {expandedVideoId === video.id && (
                        <div className="px-1.5 sm:px-2 pb-1.5 sm:pb-2 pt-0 border-t border-slate-700/50 mt-1 animate-fadeIn bg-slate-700/30">
                          <div className="flex space-x-1.5 pt-1.5">
                            <button
                              onClick={(e) => {
                                e.stopPropagation()
                                handlePlayNow(video, 'history')
                              }}
                              className="flex-1 px-2 sm:px-3 py-1.5 sm:py-2 bg-gradient-to-r from-blue-600 to-blue-500 active:from-blue-400 active:to-blue-300 rounded-lg text-[10px] sm:text-xs font-medium transition-all duration-200 shadow-lg shadow-blue-500/30 active:scale-95 flex items-center justify-center space-x-1 touch-manipulation min-h-[32px]"
                            >
                              <FontAwesomeIcon icon={faPlayCircle} className="text-xs sm:text-sm" />
                              <span className="whitespace-nowrap hidden xs:inline">Phát Ngay</span>
                              <span className="whitespace-nowrap xs:hidden">Phát</span>
                            </button>
                            <button
                              onClick={(e) => {
                                e.stopPropagation()
                                handleAddToSelected(video)
                              }}
                              className="flex-1 px-2 sm:px-3 py-1.5 sm:py-2 bg-gradient-to-r from-green-600 to-green-500 active:from-green-400 active:to-green-300 rounded-lg text-[10px] sm:text-xs font-medium transition-all duration-200 shadow-lg shadow-green-500/30 active:scale-95 flex items-center justify-center space-x-1 touch-manipulation min-h-[32px]"
                            >
                              <FontAwesomeIcon icon={faPlus} className="text-xs sm:text-sm" />
                              <span>Thêm</span>
                            </button>
                          </div>
                        </div>
                      )}
                    </div>
                  ))
                )}
              </div>
            )}

            {activeTab === 'selected' && (
              <div>
                {filteredSelectedVideos.length > 0 && (
                  <div className="flex justify-between items-center mb-2 p-1.5 sm:p-2 bg-slate-700/30 rounded-lg border border-slate-600/30">
                    <button
                      onClick={() => {
                        const shuffled = [...filteredSelectedVideos].sort(() => Math.random() - 0.5)
                        setSelectedVideos(shuffled)
                        localStorage.setItem(STORAGE_KEY_SELECTED, JSON.stringify(shuffled))
                        if (shuffled.length > 0) {
                          setSelectedVideo(shuffled[0])
                        }
                      }}
                      className="flex items-center space-x-1 sm:space-x-1.5 text-xs text-blue-400 active:text-blue-300 transition-colors font-medium touch-manipulation"
                    >
                      <Shuffle size={12} className="sm:w-4 sm:h-4" />
                      <span>Xáo trộn</span>
                    </button>
                    <button
                      onClick={handleClearAll}
                      className="text-xs text-red-400 active:text-red-300 transition-colors font-medium flex items-center space-x-1 sm:space-x-1.5 touch-manipulation"
                    >
                      <FontAwesomeIcon icon={faTrash} className="text-xs" />
                      <span>Xóa hết</span>
                    </button>
                  </div>
                )}

                <div className="space-y-2">
                  {filteredSelectedVideos.length === 0 ? (
                    <div className="text-center py-12">
                      <FontAwesomeIcon icon={faMusicSolid} className="text-5xl text-gray-500 mb-3" />
                      <p className="text-gray-400 text-sm">
                        {isKaraokeMode ? 'Chưa có bài hát karaoke được chọn' : 'Chưa có video được chọn'}
                      </p>
                    </div>
                  ) : (
                    filteredSelectedVideos.map((video, index) => (
                      <div
                        key={video.id}
                        className={`group rounded-lg sm:rounded-xl transition-all duration-200 border overflow-hidden touch-manipulation ${selectedVideo?.id === video.id
                          ? 'bg-gradient-to-r from-blue-600/20 to-blue-500/20 border-blue-500/50 shadow-lg shadow-blue-500/20'
                          : 'active:bg-slate-700/50 border-transparent active:border-slate-600/50'
                          }`}
                      >
                        <div
                          onClick={() => setExpandedVideoId(expandedVideoId === video.id ? null : video.id)}
                          className="flex items-center space-x-1.5 sm:space-x-2 p-1.5 sm:p-2 cursor-pointer"
                        >
                          <div className="relative flex-shrink-0">
                            <img
                              src={video.thumbnail}
                              alt={decodeHtmlEntities(video.title)}
                              className="w-20 h-14 sm:w-24 sm:h-16 object-cover rounded-md shadow-lg"
                            />
                            {video.duration && (
                              <div className="absolute bottom-1 right-1 px-1 py-0.5 bg-black/80 text-[8px] sm:text-[10px] text-white font-bold rounded z-10">
                                {(() => {
                                  if (video.source === 'soundcloud') return formatSoundCloudDuration(video.duration)
                                  try { return formatYouTubeDuration(video.duration) } catch (e) { return '0:00' }
                                })()}
                              </div>
                            )}
                            {selectedVideo?.id === video.id && (
                              <div className="absolute inset-0 bg-blue-500/30 rounded-lg flex items-center justify-center z-20">
                                <div className="w-6 h-6 sm:w-7 sm:h-7 bg-blue-500 rounded-full flex items-center justify-center">
                                  <FontAwesomeIcon icon={faPlay} className="text-white text-[8px] sm:text-[10px] ml-0.5" />
                                </div>
                              </div>
                            )}
                          </div>
                          <div className="flex-1 min-w-0">
                            <div className="text-[11px] sm:text-xs font-semibold line-clamp-2 text-white break-words">
                              {decodeHtmlEntities(video.title)}
                            </div>
                            <div className="text-[10px] sm:text-xs text-gray-400 mt-0.5 truncate">
                              {decodeHtmlEntities(video.channelTitle)}
                            </div>
                            {video.duration && (
                              <div className="text-[10px] sm:text-xs text-gray-500 mt-0.5 flex items-center space-x-1">
                                <FontAwesomeIcon icon={faClock} className="w-2.5 h-2.5 sm:w-3 sm:h-3" />
                                <span className="truncate">
                                  {(() => {
                                    // Kiểm tra nếu duration là số (milliseconds) -> SoundCloud
                                    if (typeof video.duration === 'number') {
                                      return formatSoundCloudDuration(video.duration)
                                    }
                                    // Nếu có source field, dùng source
                                    if (video.source === 'soundcloud') {
                                      return formatSoundCloudDuration(video.duration)
                                    }
                                    // Mặc định dùng YouTube format
                                    try {
                                      return formatYouTubeDuration(video.duration)
                                    } catch (error) {
                                      // Nếu format YouTube thất bại, thử SoundCloud format
                                      if (typeof video.duration === 'number') {
                                        return formatSoundCloudDuration(video.duration)
                                      }
                                      return '0:00'
                                    }
                                  })()}
                                </span>
                              </div>
                            )}
                          </div>
                        </div>

                        {/* Buttons hiển thị khi click vào video */}
                        {expandedVideoId === video.id && (
                          <div className="px-1.5 sm:px-2 pb-1.5 sm:pb-2 pt-0 border-t border-slate-700/50 mt-1 animate-fadeIn bg-slate-700/30">
                            <div className="flex flex-wrap gap-1.5 pt-1.5">
                              <button
                                onClick={(e) => {
                                  e.stopPropagation()
                                  handlePlayNow(video, 'selected')
                                }}
                                className="flex-1 min-w-[calc(50%-4px)] sm:min-w-0 px-2 sm:px-3 py-1.5 sm:py-2 bg-gradient-to-r from-blue-600 to-blue-500 active:from-blue-400 active:to-blue-300 rounded-lg text-[10px] sm:text-xs font-medium transition-all duration-200 shadow-lg shadow-blue-500/30 active:scale-95 flex items-center justify-center space-x-1 touch-manipulation min-h-[32px]"
                              >
                                <FontAwesomeIcon icon={faPlayCircle} className="text-xs sm:text-sm" />
                                <span className="whitespace-nowrap hidden xs:inline">Phát Ngay</span>
                                <span className="whitespace-nowrap xs:hidden">Phát</span>
                              </button>
                              <button
                                onClick={(e) => {
                                  e.stopPropagation()
                                  handlePriority(video.id)
                                }}
                                disabled={index === 0}
                                className="flex-1 min-w-[calc(50%-4px)] sm:min-w-0 px-2 sm:px-3 py-1.5 sm:py-2 bg-gradient-to-r from-yellow-600 to-yellow-500 active:from-yellow-400 active:to-yellow-300 rounded-lg text-[10px] sm:text-xs font-medium transition-all duration-200 shadow-lg shadow-yellow-500/30 active:scale-95 flex items-center justify-center space-x-1 disabled:opacity-50 disabled:cursor-not-allowed disabled:active:scale-100 touch-manipulation min-h-[32px]"
                              >
                                <FontAwesomeIcon icon={faArrowUp} className="text-xs sm:text-sm" />
                                <span>Ưu tiên</span>
                              </button>
                              <button
                                onClick={(e) => {
                                  e.stopPropagation()
                                  handleRemoveSelected(video.id)
                                }}
                                className="flex-1 min-w-[calc(50%-4px)] sm:min-w-0 px-2 sm:px-3 py-1.5 sm:py-2 bg-gradient-to-r from-red-600 to-red-500 active:from-red-400 active:to-red-300 rounded-lg text-[10px] sm:text-xs font-medium transition-all duration-200 shadow-lg shadow-red-500/30 active:scale-95 flex items-center justify-center space-x-1 touch-manipulation min-h-[32px]"
                              >
                                <FontAwesomeIcon icon={faTrash} className="text-xs sm:text-sm" />
                                <span>Xóa</span>
                              </button>
                            </div>
                          </div>
                        )}
                      </div>
                    ))
                  )}
                </div>
              </div>
            )}

            {activeTab === 'settings' && (
              <div className="space-y-4">
                <div className="mb-6">
                  <h2 className="text-xl font-bold text-white mb-2 flex items-center space-x-2">
                    <FontAwesomeIcon icon={faGear} />
                    <span>Cài đặt</span>
                  </h2>
                  <p className="text-sm text-gray-400">Quản lý cài đặt và dữ liệu ứng dụng</p>
                </div>

                {/* Xóa dữ liệu */}
                <div className="bg-slate-700/30 rounded-xl border border-slate-600/30 p-4">
                  <h3 className="text-sm font-semibold text-white mb-3 flex items-center space-x-2">
                    <FontAwesomeIcon icon={faBroom} />
                    <span>Xóa dữ liệu</span>
                  </h3>
                  <div className="space-y-3">
                    <button
                      onClick={() => {
                        if (window.confirm('Bạn có chắc chắn muốn xóa tất cả lịch sử xem?')) {
                          localStorage.removeItem(STORAGE_KEY_HISTORY)
                          setWatchHistory([])
                          alert('Đã xóa lịch sử xem')
                        }
                      }}
                      className="w-full px-4 py-3 bg-slate-600/50 hover:bg-slate-600/70 rounded-lg text-sm font-medium transition-all duration-200 flex items-center justify-between border border-slate-500/30"
                    >
                      <div className="flex items-center space-x-2">
                        <FontAwesomeIcon icon={faHistory} className="text-gray-300" />
                        <span className="text-white">Xóa lịch sử xem</span>
                      </div>
                      <span className="text-xs text-gray-400">{watchHistory.length} video</span>
                    </button>
                  </div>
                </div>

                {/* Thông tin ứng dụng */}
                <div className="bg-slate-700/30 rounded-xl border border-slate-600/30 p-4">
                  <h3 className="text-sm font-semibold text-white mb-3 flex items-center space-x-2">
                    <FontAwesomeIcon icon={faInfoCircle} />
                    <span>Thông tin</span>
                  </h3>
                  <div className="space-y-2 text-sm">
                    <div className="flex justify-between items-center">
                      <span className="text-gray-400">Phiên bản</span>
                      <span className="text-white font-medium">1.0.2</span>
                    </div>
                    <div className="flex justify-between items-center">
                      <span className="text-gray-400">Video đã chọn</span>
                      <span className="text-white font-medium">{selectedVideos.length}</span>
                    </div>
                    <div className="flex justify-between items-center">
                      <span className="text-gray-400">Lịch sử xem</span>
                      <span className="text-white font-medium">{watchHistory.length}</span>
                    </div>
                    <div className="pt-2 border-t border-slate-600/30">
                      <p className="text-xs text-gray-500 text-center">
                        Copyright By Trịnh Quang Huy
                      </p>
                    </div>
                  </div>
                </div>

                {/* Cài đặt phát lại */}
                <div className="bg-slate-700/30 rounded-xl border border-slate-600/30 p-4">
                  <h3 className="text-sm font-semibold text-white mb-3 flex items-center space-x-2">
                    <FontAwesomeIcon icon={faPlay} />
                    <span>Phát lại</span>
                  </h3>
                  <div className="space-y-3">
                    <div className="flex items-center justify-between">
                      <div>
                        <p className="text-sm text-white font-medium">Khóa chuyển bài</p>
                        <p className="text-xs text-gray-400">Không cho next / phát bài khác / auto-next</p>
                      </div>
                      <button
                        type="button"
                        onClick={() => {
                          setIsTrackChangeLocked(prev => {
                            const newVal = !prev
                            isTrackChangeLockedRef.current = newVal
                            return newVal
                          })
                          if (syncStateRef.current) setTimeout(() => syncStateRef.current?.(), 0)
                        }}
                        className={`relative w-12 h-6 rounded-full transition-all duration-300 shadow-inner ${isTrackChangeLocked ? 'bg-gradient-to-r from-amber-500 to-orange-500' : 'bg-slate-600'}`}
                      >
                        <span className={`absolute top-0.5 left-0.5 w-5 h-5 bg-white rounded-full shadow-lg transition-transform duration-300 ${isTrackChangeLocked ? 'translate-x-6' : ''}`} />
                      </button>
                    </div>
                    <div className="flex items-center justify-between">
                      <div>
                        <p className="text-sm text-white font-medium">Chế độ Karaoke</p>
                        <p className="text-xs text-gray-400">Chỉ hiển thị bài hát karaoke khi tìm kiếm</p>
                      </div>
                      <button
                        onClick={() => setIsKaraokeMode(!isKaraokeMode)}
                        className={`relative w-12 h-6 rounded-full transition-all duration-300 shadow-inner ${isKaraokeMode ? 'bg-gradient-to-r from-green-500 to-emerald-500' : 'bg-slate-600'}`}
                      >
                        <span
                          className={`absolute top-0.5 left-0.5 w-5 h-5 bg-white rounded-full shadow-lg transition-transform duration-300 ${isKaraokeMode ? 'translate-x-6' : ''}`}
                        />
                      </button>
                    </div>
                    <div className="flex items-center justify-between pt-2 border-t border-slate-600/30">
                      <div>
                        <p className="text-sm text-white font-medium">Tự động chuyển bài</p>
                        <p className="text-xs text-gray-400">Tự động phát video tiếp theo khi video hiện tại kết thúc</p>
                      </div>
                      <button
                        onClick={() => {
                          const newValue = !autoPlayNext
                          setAutoPlayNext(newValue)
                          localStorage.setItem(STORAGE_KEY_AUTO_PLAY_NEXT, JSON.stringify(newValue))
                        }}
                        className={`relative w-12 h-6 rounded-full transition-all duration-300 shadow-inner ${autoPlayNext ? 'bg-gradient-to-r from-blue-500 to-blue-400' : 'bg-slate-600'
                          }`}
                      >
                        <span
                          className={`absolute top-0.5 left-0.5 w-5 h-5 bg-white rounded-full shadow-lg transition-transform duration-300 ${autoPlayNext ? 'translate-x-6' : ''
                            }`}
                        />
                      </button>
                    </div>
                  </div>
                </div>

                {/* Remote Control */}
                <div className="bg-slate-700/30 rounded-xl border border-slate-600/30 p-4">
                  <h3 className="text-sm font-semibold text-white mb-3 flex items-center space-x-2">
                    <FontAwesomeIcon icon={faMobileScreen} />
                    <span>Điều khiển từ xa</span>
                  </h3>
                  <div className="space-y-3">
                    <div className="flex items-center justify-between">
                      <div>
                        <p className="text-sm text-white font-medium">Bật điều khiển từ xa</p>
                        <p className="text-xs text-gray-400">Điều khiển bằng điện thoại qua trình duyệt</p>
                      </div>
                      <button
                        onClick={() => {
                          const newValue = !remoteControlEnabled
                          setRemoteControlEnabledState(newValue)
                          setRemoteControlEnabled(newValue)
                          if (newValue) {
                            setRemoteSessionToken(getSessionToken())
                          }
                        }}
                        className={`relative w-12 h-6 rounded-full transition-all duration-300 shadow-inner ${remoteControlEnabled ? 'bg-gradient-to-r from-blue-500 to-blue-400' : 'bg-slate-600'
                          }`}
                      >
                        <span
                          className={`absolute top-0.5 left-0.5 w-5 h-5 bg-white rounded-full shadow-lg transition-transform duration-300 ${remoteControlEnabled ? 'translate-x-6' : ''
                            }`}
                        />
                      </button>
                    </div>
                    {remoteControlEnabled && (
                      <div className="pt-3 border-t border-slate-600/30 space-y-3">
                        {remoteSessionConflict && (
                          <div className="bg-red-500/10 border border-red-500/20 rounded-lg px-3 py-2">
                            <p className="text-xs font-semibold text-red-300">Session này đang được tab khác giữ.</p>
                            <p className="text-[11px] text-red-200/80 mt-1">Mỗi token chỉ cho phép 1 tab player hoạt động để tránh nhảy trạng thái.</p>
                          </div>
                        )}
                        <div className="bg-slate-800/50 rounded-lg p-3">
                          <div className="flex items-center justify-between mb-2">
                            <span className="text-xs text-gray-400">Session Token</span>
                            <button
                              onClick={async () => {
                                const newToken = await resetSessionToken()
                                setRemoteSessionToken(newToken)
                                alert('Đã tạo session token mới')
                              }}
                              className="text-xs text-blue-400 hover:text-blue-300"
                            >
                              Tạo mới
                            </button>
                          </div>
                          <div className="flex items-center gap-2">
                            <code className="flex-1 text-xs font-mono text-white bg-slate-900/50 px-2 py-1.5 rounded break-all">
                              {remoteSessionToken}
                            </code>
                            <button
                              onClick={() => {
                                navigator.clipboard.writeText(remoteSessionToken)
                                alert('Đã copy session token!')
                              }}
                              className="p-2 bg-slate-700/50 hover:bg-slate-700 rounded-lg transition-colors"
                            >
                              <FontAwesomeIcon icon={faCopy} className="text-sm" />
                            </button>
                          </div>
                        </div>
                        <div className="bg-slate-800/50 rounded-lg p-3">
                          <p className="text-xs text-gray-400 mb-2">URL điều khiển</p>
                          <div className="flex items-center gap-2">
                            <input
                              type="text"
                              readOnly
                              value={getRemoteControlUrl()}
                              className="flex-1 text-xs font-mono text-white bg-slate-900/50 px-2 py-1.5 rounded"
                            />
                            <button
                              onClick={() => {
                                navigator.clipboard.writeText(getRemoteControlUrl())
                                alert('Đã copy URL!')
                              }}
                              className="p-2 bg-slate-700/50 hover:bg-slate-700 rounded-lg transition-colors"
                            >
                              <FontAwesomeIcon icon={faCopy} className="text-sm" />
                            </button>
                          </div>
                        </div>
                        <div className="bg-slate-800/50 rounded-lg p-3">
                          <p className="text-xs text-gray-400 mb-2">Mã QR Code</p>
                          <div className="flex flex-col items-center gap-2">
                            <img
                              src={`https://api.qrserver.com/v1/create-qr-code/?size=200x200&data=${encodeURIComponent(getRemoteControlUrl())}`}
                              alt="QR Code"
                              className="w-40 h-40 border-2 border-slate-600 rounded-lg bg-white p-2"
                            />
                            <p className="text-xs text-gray-400 text-center">
                              Quét mã QR này bằng điện thoại để kết nối
                            </p>
                          </div>
                        </div>
                        <div className="bg-blue-500/10 border border-blue-500/30 rounded-lg p-3">
                          <div className="flex items-start gap-2">
                            <FontAwesomeIcon icon={faQrcode} className="text-blue-400 mt-0.5" />
                            <div className="flex-1">
                              <p className="text-xs text-blue-300 font-medium mb-1">Hướng dẫn sử dụng</p>
                              <ol className="text-xs text-blue-200/80 space-y-1 list-decimal list-inside">
                                <li>Quét QR code bằng điện thoại</li>
                                <li>Hoặc mở URL trên điện thoại</li>
                                <li>Điều khiển từ xa sẽ hoạt động</li>
                              </ol>
                            </div>
                          </div>
                        </div>
                      </div>
                    )}
                  </div>
                </div>

                {/* System Monitoring */}
                <div className="bg-slate-700/30 rounded-xl border border-slate-600/30 p-4">
                  <h3 className="text-sm font-semibold text-white mb-3 flex items-center space-x-2">
                    <FontAwesomeIcon icon={faServer} />
                    <span>Hệ thống & Máy chủ</span>
                  </h3>
                  <div className="space-y-3">
                    <p className="text-xs text-gray-400 leading-relaxed">
                      Kiểm tra trạng thái kết nối của các máy chủ tìm kiếm và điều khiển từ xa.
                    </p>
                    <button
                      onClick={() => navigate('/status')}
                      className="w-full py-2.5 bg-blue-600 hover:bg-blue-500 text-white rounded-lg text-xs font-bold transition-all shadow-lg active:scale-95 flex items-center justify-center space-x-2"
                    >
                      <FontAwesomeIcon icon={faSync} />
                      <span>Kiểm tra trạng thái hệ thống</span>
                    </button>
                  </div>
                </div>

                {/* Cài đặt tự động xóa */}
                <div className="bg-slate-700/30 rounded-xl border border-slate-600/30 p-4">
                  <h3 className="text-sm font-semibold text-white mb-3 flex items-center space-x-2">
                    <FontAwesomeIcon icon={faBroom} />
                    <span>Xóa dữ liệu tự động</span>
                  </h3>
                  <div className="space-y-3">
                    <div className="flex items-center justify-between">
                      <div>
                        <p className="text-sm text-white font-medium">Bật xóa tự động</p>
                        <p className="text-xs text-gray-400">Tự động xóa dữ liệu cũ sau số ngày chỉ định</p>
                      </div>
                      <button
                        onClick={() => {
                          const newValue = !autoDeleteEnabled
                          setAutoDeleteEnabled(newValue)
                          localStorage.setItem(STORAGE_KEY_AUTO_DELETE, JSON.stringify(newValue))
                          if (newValue) {
                            deleteOldData()
                          }
                        }}
                        className={`relative w-12 h-6 rounded-full transition-all duration-300 shadow-inner ${autoDeleteEnabled ? 'bg-gradient-to-r from-orange-500 to-orange-400' : 'bg-slate-600'
                          }`}
                      >
                        <span
                          className={`absolute top-0.5 left-0.5 w-5 h-5 bg-white rounded-full shadow-lg transition-transform duration-300 ${autoDeleteEnabled ? 'translate-x-6' : ''
                            }`}
                        />
                      </button>
                    </div>
                    {autoDeleteEnabled && (
                      <div className="pt-2 border-t border-slate-600/30">
                        <div className="flex items-center justify-between mb-2">
                          <p className="text-sm text-white font-medium">Số ngày</p>
                          <span className="text-xs text-gray-400">{autoDeleteDays} ngày</span>
                        </div>
                        <input
                          type="range"
                          min="1"
                          max="30"
                          value={autoDeleteDays}
                          onChange={(e) => {
                            const days = parseInt(e.target.value)
                            setAutoDeleteDays(days)
                            localStorage.setItem(STORAGE_KEY_AUTO_DELETE_DAYS, days.toString())
                            deleteOldData()
                          }}
                          className="w-full h-2 bg-slate-600 rounded-lg appearance-none cursor-pointer accent-orange-500"
                        />
                        <div className="flex justify-between text-xs text-gray-500 mt-1">
                          <span>1 ngày</span>
                          <span>30 ngày</span>
                        </div>
                        <p className="text-xs text-gray-400 mt-2">
                          Dữ liệu cũ hơn {autoDeleteDays} ngày sẽ tự động bị xóa
                        </p>
                      </div>
                    )}
                  </div>
                </div>
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  )
}

export default VideoPlayer
