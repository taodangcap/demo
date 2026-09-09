import { useState, useEffect, useRef, useCallback } from 'react'
import { useSearchParams, useNavigate } from 'react-router-dom'
import { fetchSearchSuggestions } from '../utils/youtube'
import { useSpeechRecognition } from '../hooks/useSpeechRecognition'
import {
  faPlus, faTrash, faArrowLeft, faMusic, faHandsClapping, faFaceLaughSquint,
  faBullhorn, faWind, faWaveSquare, faMagic, faCloudShowersHeavy, faFeatherPointed,
  faPlay, faPause, faForwardStep, faVolumeUp, faVolumeDown,
  faRedo, faMicrophone, faSearch, faExpand,
  faArrowUp, faClock, faBolt, faCog, faListCheck, faCheck,
  faCompress, faShuffle, faPowerOff,
  faStop, faLock, faLockOpen,
  faUndo, faCheckCircle, faSlidersH, faEllipsisH, faTv, faClosedCaptioning
} from '@fortawesome/free-solid-svg-icons'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { sendRemoteCommand, getRemoteState, isShowCueOperator, ensureSession, verifySessionPassword } from '../utils/remoteControl'
import { searchVideos, getVideoDetails, formatDuration as formatYouTubeDuration, formatViewCount, isPlayableVideo, parseISO8601Duration } from '../utils/youtube'
import { searchTracks, formatDuration as formatSoundCloudDuration } from '../utils/soundcloud'
import { Youtube, Music, Search } from 'lucide-react'
import appLogo from '../img/logo.png'
import Swal from 'sweetalert2'
import { useWebSocket } from '../hooks/useWebSocket'
import { useSessionStore } from '../store/sessionStore'

const STARTER_PLAYLISTS = {
  karaoke: [
    {
      id: 'sp_random_sings',
      title: 'Random sings - Kara list',
      songs: '26 songs',
      thumbnail: 'https://images.unsplash.com/photo-1511671782779-c97d3d27a1d4?w=150&auto=format&fit=crop&q=60',
      label: 'Random sings'
    },
    {
      id: 'sp_89x_nhac_tre',
      title: '89x nhạc trẻ - Kara list',
      songs: '36 songs',
      thumbnail: 'https://images.unsplash.com/photo-1518837695005-2083093ee35b?w=150&auto=format&fit=crop&q=60',
      label: '89x nhạc trẻ'
    },
    {
      id: 'sp_nhac_tre_mix',
      title: 'Nhạc trẻ mix linh tinh - Kara list',
      songs: '68 songs',
      thumbnail: 'https://images.unsplash.com/photo-1470225620780-dba8ba36b745?w=150&auto=format&fit=crop&q=60',
      label: 'Nhạc trẻ mix'
    },
    {
      id: 'sp_nhac_tre_remix',
      title: 'Nhạc trẻ nhưng remix - Kara list',
      songs: '11 songs',
      thumbnail: 'https://images.unsplash.com/photo-1514525253161-7a46d19cd819?w=150&auto=format&fit=crop&q=60',
      label: 'Nhạc trẻ remix'
    }
  ],
  music: [
    {
      id: 'sp_tu_hao_vn',
      title: 'Tự hào Việt Nam',
      songs: '32 songs',
      thumbnail: 'https://images.unsplash.com/photo-1555939594-58d7cb561ad1?w=150&auto=format&fit=crop&q=60',
      label: 'Tự hào VN'
    }
  ]
}

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
  const minutes = Math.floor(totalSeconds / 60)
  const minutesInHour = Math.floor((totalSeconds % 3600) / 60)
  const seconds = totalSeconds % 60
  if (hours > 0) {
    return `${String(hours).padStart(2, '0')}:${String(minutesInHour).padStart(2, '0')}:${String(seconds).padStart(2, '0')}`
  }
  return `${String(minutes).padStart(2, '0')}:${String(seconds).padStart(2, '0')}`
}

const EFFECT_ICON_PALETTE = [
  { icon: faHandsClapping, color: 'bg-yellow-500', textColor: 'text-yellow-500' },
  { icon: faFaceLaughSquint, color: 'bg-green-500', textColor: 'text-green-500' },
  { icon: faBullhorn, color: 'bg-blue-500', textColor: 'text-blue-500' },
  { icon: faWind, color: 'bg-cyan-500', textColor: 'text-cyan-500' },
  { icon: faWaveSquare, color: 'bg-purple-500', textColor: 'text-purple-500' },
  { icon: faWaveSquare, color: 'bg-orange-500', textColor: 'text-orange-500' },
  { icon: faCloudShowersHeavy, color: 'bg-indigo-500', textColor: 'text-indigo-500' },
  { icon: faFeatherPointed, color: 'bg-pink-500', textColor: 'text-pink-500' }
]

const buildEffectUiConfig = (id, effectData = {}, index = 0) => {
  const paletteIndex = Math.abs(String(id).split('').reduce((sum, char) => sum + char.charCodeAt(0), 0) + index) % EFFECT_ICON_PALETTE.length
  return {
    label: effectData?.label || String(id),
    ...EFFECT_ICON_PALETTE[paletteIndex]
  }
}

function RemoteControl() {
  const [searchParams, setSearchParams] = useSearchParams()
  const navigate = useNavigate()
  const sessionToken = searchParams.get('session')
  // ShowCue / FOH: compact operator desk — không UI khách (QR/share)
  const isOperatorDesk = isShowCueOperator()
  // Force layout: ?ui=phone|pc|mobile|desktop (ShowCue nút Phone/PC)
  const uiParam = (searchParams.get('ui') || searchParams.get('layout') || '').toLowerCase()
  const forcePhone = uiParam === 'phone' || uiParam === 'mobile'
  const forcePc = uiParam === 'pc' || uiParam === 'desktop'
  const [inputToken, setInputToken] = useState('')
  const [isConnected, setIsConnected] = useState(false)
  const [currentVideo, setCurrentVideo] = useState(null)
  const [volume, setVolume] = useState(50)
  const [fxVolume, setFxVolume] = useState(80)
  const [playlist, setPlaylist] = useState([])
  const [searchQuery, setSearchQuery] = useState('')
  const [searchResults, setSearchResults] = useState([])
  const [isSearching, setIsSearching] = useState(false)
  const [isSearchFocused, setIsSearchFocused] = useState(false)
  const [showSuggestions, setShowSuggestions] = useState(false)
  const justSelectedSuggestionRef = useRef(false) // Track if user just picked a suggestion
  const suggestionRequestRef = useRef(0)
  const [suggestions, setSuggestions] = useState([])
  const [suggestionIndex, setSuggestionIndex] = useState(-1)
  const [isLoadingSuggestions, setIsLoadingSuggestions] = useState(false)
  const [searchHistory, setSearchHistory] = useState([])
  const [requiresPassword, setRequiresPassword] = useState(false)
  const [isPasswordUnlocked, setIsPasswordUnlocked] = useState(false)
  const [inputPassword, setInputPassword] = useState('')
  const [passwordError, setPasswordError] = useState('')
  const [isVerifyingPassword, setIsVerifyingPassword] = useState(false)
  const [sessionPassword, setSessionPassword] = useState(() => {
    return searchParams.get('pwd') || searchParams.get('pin') || (typeof window !== 'undefined' ? (sessionStorage.getItem(`session_auth_${sessionToken}`) || '') : '') || ''
  })

  // Kiểm tra trạng thái mật khẩu của session
  useEffect(() => {
    if (!sessionToken) {
      setRequiresPassword(false)
      setIsPasswordUnlocked(false)
      return
    }

    let isMounted = true
    const checkPassword = async () => {
      try {
        const state = await getRemoteState(sessionToken)
        if (!isMounted) return

        if (state?.requiresPassword) {
          setRequiresPassword(true)
          const urlPwd = searchParams.get('pwd') || searchParams.get('pin')
          const cachedPwd = typeof window !== 'undefined' ? sessionStorage.getItem(`session_auth_${sessionToken}`) : null
          const candidate = urlPwd || cachedPwd

          if (candidate) {
            setIsVerifyingPassword(true)
            const res = await verifySessionPassword(sessionToken, candidate)
            if (isMounted) {
              setIsVerifyingPassword(false)
              if (res.success) {
                setSessionPassword(candidate)
                setIsPasswordUnlocked(true)
                setPasswordError('')
              } else {
                setIsPasswordUnlocked(false)
                if (urlPwd) setPasswordError('Mật khẩu trong URL không hợp lệ')
              }
            }
          } else {
            setIsPasswordUnlocked(false)
          }
        } else {
          setRequiresPassword(false)
          setIsPasswordUnlocked(true)
        }
      } catch (err) {
        console.error('Password check error:', err)
      }
    }

    checkPassword()
    return () => { isMounted = false }
  }, [sessionToken, searchParams])

  const handlePasswordSubmit = async (e) => {
    e?.preventDefault?.()
    if (!inputPassword.trim()) {
      setPasswordError('Vui lòng nhập mật khẩu')
      return
    }

    setIsVerifyingPassword(true)
    setPasswordError('')

    try {
      const trimmedPwd = inputPassword.trim()
      const res = await verifySessionPassword(sessionToken, trimmedPwd)
      if (res.success) {
        setSessionPassword(trimmedPwd)
        wsAuthenticate?.(trimmedPwd)
        setIsPasswordUnlocked(true)
        setPasswordError('')
        showToast('Mở khóa thành công!')
      } else {
        setPasswordError(res.message || 'Mật khẩu không chính xác')
      }
    } catch (err) {
      setPasswordError('Lỗi kết nối máy chủ')
    } finally {
      setIsVerifyingPassword(false)
    }
  }

  const handleExitSession = () => {
    if (typeof window !== 'undefined') {
      sessionStorage.removeItem(`session_auth_${sessionToken}`)
    }
    setSearchParams({}, { replace: true })
    setRequiresPassword(false)
    setIsPasswordUnlocked(false)
    setInputPassword('')
    setPasswordError('')
  }

  // Load search history on mount
  useEffect(() => {
    try {
      const stored = localStorage.getItem('search_history')
      if (stored) setSearchHistory(JSON.parse(stored))
    } catch (e) {
      console.error('Failed to load search history:', e)
    }
  }, [])

  const saveToSearchHistory = (query) => {
    if (!query || query.trim().length < 2) return
    try {
      const newHistory = [query, ...searchHistory.filter(h => h !== query)].slice(0, 10)
      setSearchHistory(newHistory)
      localStorage.setItem('search_history', JSON.stringify(newHistory))
    } catch (e) {
      console.error('Failed to save search history:', e)
    }
  }

  const clearSearchHistory = () => {
    setSearchHistory([])
    localStorage.removeItem('search_history')
  }

  const deleteSearchHistoryItem = (itemToRemove) => {
    try {
      const newHistory = searchHistory.filter(h => h !== itemToRemove)
      setSearchHistory(newHistory)
      localStorage.setItem('search_history', JSON.stringify(newHistory))
    } catch (e) {
      console.error('Failed to delete search history item:', e)
    }
  }
  const searchInputRef = useRef(null)
  const desktopInputRef = useRef(null)
  const mobileSearchContainerRef = useRef(null)
  const desktopSearchContainerRef = useRef(null)
  const [searchSource, setSearchSource] = useState('youtube')
  // Lưu trữ kết quả tìm kiếm riêng biệt cho mỗi nguồn
  const [youtubeSearchState, setYoutubeSearchState] = useState({ query: '', results: [] })
  const [soundcloudSearchState, setSoundcloudSearchState] = useState({ query: '', results: [] })
  const [searchError, setSearchError] = useState(null)
  const [isConnecting, setIsConnecting] = useState(false)
  const [tokenError, setTokenError] = useState(null)
  const [activeTab, setActiveTab] = useState('search') // 'search', 'queue', 'controls', 'more'
  const [isKaraokeMode, setIsKaraokeMode] = useState(false)
  const [isFullscreen, setIsFullscreen] = useState(false)
  const [watchHistory, setWatchHistory] = useState([])
  const [currentTime, setCurrentTime] = useState(0)
  const [duration, setDuration] = useState(0)
  const [showVolumeSheet, setShowVolumeSheet] = useState(false)
  const [isListening, setIsListening] = useState(false)
  const [interimTranscript, setInterimTranscript] = useState('')
  const [trendingSongs, setTrendingSongs] = useState([])
  const [isLoadingTrending, setIsLoadingTrending] = useState(false)
  const [expandedResultId, setExpandedResultId] = useState(null)

  const [autoPlayNext, setAutoPlayNext] = useState(true)
  const [isTrackChangeLocked, setIsTrackChangeLocked] = useState(false)
  const [remoteState, setRemoteState] = useState(null)
  const [optimisticSelectedFxId, setOptimisticSelectedFxId] = useState(null)
  const [optimisticActiveFx, setOptimisticActiveFx] = useState(null)
  const [toasts, setToasts] = useState([]) // Feedback for user actions
  const lastAutoPlayChangeRef = useRef(0)
  const lastTrackLockChangeRef = useRef(0)
  const lastVolumeChangeRef = useRef(0)
  const lastVolCommandRef = useRef(0)
  const lastSeekCommandRef = useRef(0)
  const lastKaraokeChangeRef = useRef(0)
  const lastPlayCommandRef = useRef(0)
  const lastPlaylistCommandRef = useRef(0) // Guard against stale playlist poll
  const lastSystemToggleRef = useRef(0) // Guard for isFullscreen, autoPlayNext, etc.
  const commandCooldownsRef = useRef({}) // Throttle rapid command firing
  const isSeekingRef = useRef(false)
  const volumeRef = useRef(volume)
  const currentVideoRef = useRef(currentVideo)
  const currentTimeRef = useRef(currentTime)
  const durationRef = useRef(duration)
  const playlistRef = useRef(playlist)
  const pollingTimeoutRef = useRef(null)
  const fxUiLockRef = useRef(0)
  const playbackSyncRef = useRef({ serverTime: 0, syncedAt: Date.now(), isPlaying: false })

  useEffect(() => { volumeRef.current = volume }, [volume])
  useEffect(() => { currentVideoRef.current = currentVideo }, [currentVideo])
  useEffect(() => { currentTimeRef.current = currentTime }, [currentTime])
  useEffect(() => { durationRef.current = duration }, [duration])
  useEffect(() => { playlistRef.current = playlist }, [playlist])

  useEffect(() => {
    if (!remoteState) return

    const isFxUiLocked = Date.now() - fxUiLockRef.current < 1200
    if (isFxUiLocked) return

    setOptimisticSelectedFxId(remoteState.selectedFx?.id || null)
    setOptimisticActiveFx(remoteState.activeFx || null)
  }, [remoteState])

  useEffect(() => {
    if (!sessionToken) {
      setIsConnected(false)
      return
    }

    // Nothing else needed here - WebSocket handles real-time sync
    // The useWebSocket hook below manages connection state
  }, [sessionToken])

  // ===== WebSocket Integration (replaces HTTP polling) =====
  const connectionStatus = useSessionStore((s) => s.connectionStatus)

  const handleWsMessage = useCallback((msg) => {
    if (!msg) return

    if (msg.type === 'joined') {
      setIsConnected(true)
      return
    }

    if (msg.type === 'peer_joined' || msg.type === 'peer_left') {
      // Connection status updates handled by hook
      return
    }

    if (msg.type === 'state_sync' && msg.state) {
      const state = msg.state

      const now = Date.now()
      const isRecentlyToggled = (now - lastSystemToggleRef.current) < 2000

      if (!isRecentlyToggled) {
        if (state.isFullscreen !== undefined) {
          setIsFullscreen(prev => prev === !!state.isFullscreen ? prev : !!state.isFullscreen)
        }
        if (state.autoPlayNext !== undefined) {
          setAutoPlayNext(prev => prev === !!state.autoPlayNext ? prev : !!state.autoPlayNext)
        }
        if (state.isTrackChangeLocked !== undefined) {
          setIsTrackChangeLocked(prev => prev === !!state.isTrackChangeLocked ? prev : !!state.isTrackChangeLocked)
        }
      }

      // Sync Playlist & History
      const isPlaylistRecentlyChanged = Date.now() - lastPlaylistCommandRef.current < 4000
      if (state.playlist && !isPlaylistRecentlyChanged) {
        setPlaylist(prev => {
          const isPlaylistEqual = (arr1, arr2) => {
            if (!arr1 || !arr2) return false
            if (arr1.length !== arr2.length) return false
            return arr1.every((v, i) => v.id === arr2[i].id)
          }
          if (isPlaylistEqual(prev, state.playlist)) return prev
          playlistRef.current = state.playlist
          return state.playlist
        })
      }

      if (state.watchHistory) {
        setWatchHistory(prev => {
          const isHistoryEqual = (arr1, arr2) => {
            if (!arr1 || !arr2) return false
            if (arr1.length !== arr2.length) return false
            return arr1.every((v, i) => v.id === arr2[i].id)
          }
          if (isHistoryEqual(prev, state.watchHistory)) return prev
          return state.watchHistory
        })
      }

      // Sync Current Video
      if (state.currentVideo) {
        setCurrentVideo(prev => {
          if (prev && prev.id === state.currentVideo.id && prev.source === state.currentVideo.source) return prev
          currentVideoRef.current = state.currentVideo
          return state.currentVideo
        })
      } else if (playlistRef.current.length > 0 && currentVideoRef.current) {
        // Do nothing
      } else {
        setCurrentVideo(prev => {
          if (prev === null) return prev
          currentVideoRef.current = null
          return null
        })
      }

      // Sync playback progress
      const nextDuration = Math.max(0, Number(state.duration) || 0)
      const nextCurrentTime = Math.max(0, Number(state.currentTime) || 0)
      // Extend seek lock: 3000ms to prevent jumps during user dragging
      const isPlaybackSeeking = Date.now() - lastSeekCommandRef.current < 3000

      if (state.currentTime !== undefined) {
        playbackSyncRef.current = {
          serverTime: nextCurrentTime,
          syncedAt: Date.now(),
          isPlaying: !!state.isPlaying
        }
      }

      // Only sync currentTime if NOT actively seeking or within grace period
      // This prevents the "jump back" effect when server sends old state during seek
      if (state.currentTime !== undefined && !isSeekingRef.current && !isPlaybackSeeking) {
        const clampedServerTime = nextDuration > 0 ? Math.min(nextCurrentTime, nextDuration) : nextCurrentTime
        // Only update if server time is significantly different (avoid micro-sync)
        if (!state.isPlaying || Math.abs(clampedServerTime - currentTimeRef.current) > 1.25) {
          setCurrentTime(prev => prev === clampedServerTime ? prev : clampedServerTime)
        }
      }
      if (state.duration !== undefined) {
        setDuration(prev => prev === nextDuration ? prev : nextDuration)
      }

      const isRecentlyChanged = Date.now() - lastVolumeChangeRef.current < 5000
      if (state.volume !== undefined && !isRecentlyChanged && Math.abs(state.volume - volumeRef.current) > 5) {
        setVolume(prev => prev === state.volume ? prev : state.volume)
      }

      // Sync fx volume
      const currentRemoteFxVol = (state.soundEffects || {})[state.selectedFx?.id]?.volume || 80
      if (Date.now() - lastVolCommandRef.current > 1000) {
        setFxVolume(prev => prev === currentRemoteFxVol ? prev : currentRemoteFxVol)
      }

      if (Date.now() - fxUiLockRef.current > 1200) {
        setOptimisticSelectedFxId(prev => prev === (state.selectedFx?.id || null) ? prev : (state.selectedFx?.id || null))
        setOptimisticActiveFx(prev => {
          if (!prev && !state.activeFx) return prev
          if (prev && state.activeFx && prev.id === state.activeFx.id && prev.volume === state.activeFx.volume && prev.progress === state.activeFx.progress && prev.isPlaying === state.activeFx.isPlaying) return prev
          return state.activeFx || null
        })
      }

      const isKaraokeRecentlyChanged = Date.now() - lastKaraokeChangeRef.current < 3000
      if (state.isKaraokeMode !== undefined && !isKaraokeRecentlyChanged) {
        setIsKaraokeMode(prev => prev === !!state.isKaraokeMode ? prev : !!state.isKaraokeMode)
      }

      const isAutoPlayRecentlyChanged = Date.now() - lastAutoPlayChangeRef.current < 2000
      if (state.autoPlayNext !== undefined && !isAutoPlayRecentlyChanged) {
        setAutoPlayNext(prev => prev === !!state.autoPlayNext ? prev : !!state.autoPlayNext)
      }

      const isTrackLockRecentlyChanged = Date.now() - lastTrackLockChangeRef.current < 2000
      if (state.isTrackChangeLocked !== undefined && !isTrackLockRecentlyChanged) {
        setIsTrackChangeLocked(prev => prev === !!state.isTrackChangeLocked ? prev : !!state.isTrackChangeLocked)
      }

      setRemoteState(prev => {
        if (!prev) return state
        const isEq = prev.isPlaying === state.isPlaying &&
          prev.isFullscreen === state.isFullscreen &&
          prev.autoPlayNext === state.autoPlayNext &&
          prev.isTrackChangeLocked === state.isTrackChangeLocked &&
          prev.isKaraokeMode === state.isKaraokeMode &&
          prev.volume === state.volume &&
          prev.selectedFx?.id === state.selectedFx?.id &&
          prev.activeFx?.id === state.activeFx?.id &&
          prev.activeFx?.progress === state.activeFx?.progress &&
          prev.currentTime === state.currentTime &&
          prev.duration === state.duration &&
          (prev.soundEffects === state.soundEffects || JSON.stringify(prev.soundEffects) === JSON.stringify(state.soundEffects)) &&
          (prev.fxDurations === state.fxDurations || JSON.stringify(prev.fxDurations) === JSON.stringify(state.fxDurations))
        return isEq ? prev : state
      })
    }

    if (msg.type === 'command') {
      // Handle commands sent to this device (if acting as player)
      // Most remotes only send commands, but this allows bi-directional
    }
  }, [])

  // Connect via WebSocket for real-time communication
  const { sendCommand: wsSendCommand, authenticate: wsAuthenticate } = useWebSocket({
    role: 'remote',
    token: sessionToken,
    password: sessionPassword,
    onMessage: handleWsMessage,
    enabled: !!sessionToken
  })

  // Update isConnected based on WebSocket status
  useEffect(() => {
    if (connectionStatus === 'connected') {
      setIsConnected(true)
    } else if (connectionStatus === 'disconnected') {
      // Only mark disconnected after a brief grace period
      const timeout = setTimeout(() => setIsConnected(false), 3000)
      return () => clearTimeout(timeout)
    }
  }, [connectionStatus])

  // Đảm bảo session tồn tại trên server (kể cả khi chỉ mở remote Chrome, chưa bật player)
  useEffect(() => {
    if (!sessionToken) return undefined
    let cancelled = false
    ;(async () => {
      try {
        await ensureSession(sessionToken)
      } catch { /* ignore */ }
      if (cancelled) return
    })()
    return () => { cancelled = true }
  }, [sessionToken])

  // Fallback: HTTP polling for initial state if WS not yet connected
  useEffect(() => {
    if (!sessionToken || connectionStatus === 'connected') return

    let isMounted = true
    const fetchInitialState = async () => {
      if (!isMounted || connectionStatus === 'connected') return
      try {
        // Tạo shell session nếu player chưa register
        await ensureSession(sessionToken)
        const state = await getRemoteState(sessionToken)
        if (!isMounted || !state || state.error || connectionStatus === 'connected') return
        // Có session trên server = remote đã join được (player có thể chưa online)
        setIsConnected(true)
        if (state.currentVideo) setCurrentVideo(state.currentVideo)
        if (state.playlist) setPlaylist(state.playlist)
        if (state.watchHistory) setWatchHistory(state.watchHistory)
        if (state.volume !== undefined) setVolume(state.volume)
        setRemoteState(state)
      } catch { /* ignore */ }
    }
    fetchInitialState()
    const poll = setInterval(() => {
      if (connectionStatus === 'connected') {
        clearInterval(poll)
        return
      }
      fetchInitialState()
    }, 2500)
    return () => {
      isMounted = false
      clearInterval(poll)
    }
  }, [sessionToken, connectionStatus])

  // Playback time interpolation using RAF for ALL devices (vkara-style)
  // Mobile uses slower 500ms effective rate to save CPU
  useEffect(() => {
    if (!sessionToken) return undefined

    let rafId = null
    let lastUpdate = 0
    // Mobile: update every 500ms; Desktop: every 250ms
    const updateRate = window.innerWidth < 1024 ? 500 : 250

    const tick = (timestamp) => {
      rafId = requestAnimationFrame(tick)
      if (timestamp - lastUpdate < updateRate) return
      lastUpdate = timestamp

      if (isSeekingRef.current) return
      if (!currentVideoRef.current) return

      const { serverTime, syncedAt, isPlaying } = playbackSyncRef.current
      if (!isPlaying || durationRef.current <= 0) return

      const predictedTime = Math.min(
        durationRef.current,
        serverTime + ((Date.now() - syncedAt) / 1000)
      )

      if (Math.abs(predictedTime - currentTimeRef.current) >= 0.35) {
        setCurrentTime(predictedTime)
      }
    }

    rafId = requestAnimationFrame(tick)
    return () => { if (rafId) cancelAnimationFrame(rafId) }
  }, [sessionToken])

  const lockFxUi = () => {
    fxUiLockRef.current = Date.now()
  }

  const getOptimisticFxVolume = (effectId) => {
    if (!effectId) return fxVolume
    const item = (remoteState?.soundEffects || {})[effectId]
    return typeof item === 'object' ? item.volume : fxVolume
  }

  const handleCommand = (command, data = {}, event = null) => {
    const now = Date.now()
    const lastTime = commandCooldownsRef.current[command] || 0
    // Different cooldowns for different commands - optimized for mobile
    let cooldown = 150 // Default
    
    if (['fullscreenVideo', 'playPause', 'toggleAutoPlay', 'toggleTrackLock', 'karaoke', 'replay'].includes(command)) {
      cooldown = 300 // Buttons: faster
    } else if (['seek', 'volume', 'updateSoundEffectVolume', 'controlEffect'].includes(command)) {
      cooldown = 20 // Slider controls: very fast for mobile
    }

    if (now - lastTime < cooldown) {
      // Don't log for slider commands (too noisy)
      if (!['seek', 'volume', 'updateSoundEffectVolume', 'controlEffect'].includes(command)) {
        console.log(`Command ${command} throttled...`)
      }
      return
    }
    commandCooldownsRef.current[command] = now

    // Đang khóa chuyển bài → chặn next / phát ngay / clearAll / shuffle
    if (isTrackChangeLocked && ['next', 'playVideo', 'clearAll', 'shuffle'].includes(command)) {
      const toastId = Date.now()
      setToasts([{ id: toastId, message: '🔒 Đang khóa chuyển bài' }])
      setTimeout(() => setToasts(prev => prev.filter(t => t.id !== toastId)), 2200)
      return
    }

    if (command === 'playVideo' && data.video) {
      lastPlayCommandRef.current = Date.now()
      currentVideoRef.current = data.video
      setCurrentVideo(data.video)
      playbackSyncRef.current = { serverTime: 0, syncedAt: Date.now(), isPlaying: false }

      const toastId = Date.now()
      const title = data.video.title || "Bài hát"
      setToasts([{ id: toastId, message: `Phát ngay: ${title.slice(0, 25)}...` }])
      setTimeout(() => setToasts(prev => prev.filter(t => t.id !== toastId)), 3000)

      if (data.video.duration) {
        setDuration(data.video.source === 'soundcloud'
          ? Math.max(0, Number(data.video.duration) || 0) / 1000
          : data.video.duration)
      } else if (data.video.contentDetails?.duration) {
        setDuration(0)
      }
      setCurrentTime(0)
    } else if (command === 'seek' && typeof data.seconds === 'number') {
      const nextTime = Math.max(0, data.seconds)
      lastSeekCommandRef.current = Date.now()
      playbackSyncRef.current = {
        serverTime: nextTime,
        syncedAt: Date.now(),
        isPlaying: !!remoteState?.isPlaying
      }
      setCurrentTime(durationRef.current > 0 ? Math.min(nextTime, durationRef.current) : nextTime)
    } else if (command === 'addToSelected' && data.video) {
      lastPlaylistCommandRef.current = Date.now()
      setPlaylist(prev => {
        if (prev.some(v => v.id === data.video.id)) return prev;
        return [...prev, { ...data.video, isPlaying: false }];
      });

      const toastId = Date.now()
      const title = data.video.title || "Bài hát"
      setToasts([{ id: toastId, message: `Hàng chờ: ${title.slice(0, 25)}...` }])
      setTimeout(() => setToasts(prev => prev.filter(t => t.id !== toastId)), 3000)
    } else if (command === 'removeFromSelected' && data.videoId) {
      lastPlaylistCommandRef.current = Date.now()
      setPlaylist(prev => prev.filter(v => v.id !== data.videoId))
    } else if (command === 'next') {
      if (filteredPlaylist.length > 0) {
        currentVideoRef.current = filteredPlaylist[0]
        setCurrentVideo(filteredPlaylist[0])
      }
    } else if (command === 'stopPlayback') {
      currentVideoRef.current = null
      playbackSyncRef.current = { serverTime: 0, syncedAt: Date.now(), isPlaying: false }
      setCurrentVideo(null)
      setDuration(0)
      setCurrentTime(0)
      setRemoteState(prev => prev ? {
        ...prev,
        currentVideo: null,
        isPlaying: false,
        currentTime: 0,
        duration: 0,
        playlist: (prev.playlist || []).map(v => ({ ...v, isPlaying: false }))
      } : prev)
      const toastId = Date.now()
      setToasts([{ id: toastId, message: 'Đã dừng phát' }])
      setTimeout(() => setToasts(prev => prev.filter(t => t.id !== toastId)), 2000)
    } else if (command === 'fullscreenVideo') {
      setIsFullscreen(prev => !prev)
      lastSystemToggleRef.current = Date.now()
    } else if (command === 'toggleAutoPlay') {
      setAutoPlayNext(prev => data?.value !== undefined ? !!data.value : !prev)
      lastSystemToggleRef.current = Date.now()
    } else if (command === 'toggleTrackLock') {
      setIsTrackChangeLocked(prev => data?.value !== undefined ? !!data.value : !prev)
      lastSystemToggleRef.current = Date.now()
      lastTrackLockChangeRef.current = Date.now()
    }

    if (command === 'selectEffect' && data.id) {
      lockFxUi()
      setOptimisticSelectedFxId(data.id)
      setFxVolume(getOptimisticFxVolume(data.id))
    } else if (command === 'playSoundEffect' && data.id) {
      lockFxUi()
      const ui = buildEffectUiConfig(data.id, (remoteState?.soundEffects || {})[data.id])
      setOptimisticSelectedFxId(data.id)
      setOptimisticActiveFx({
        id: data.id,
        label: ui.label,
        progress: 0,
        maxTime: remoteState?.fxDurations?.[data.id] || optimisticActiveFx?.maxTime || 0,
        isPlaying: true,
        volume: fxVolume
      })
    } else if (command === 'controlEffect') {
      lockFxUi()
      setOptimisticActiveFx(prev => {
        if (!prev) return prev

        if (data.action === 'toggle') {
          return { ...prev, isPlaying: !prev.isPlaying }
        }
        if (data.action === 'stop') {
          return null
        }
        if (data.action === 'seek') {
          return { ...prev, progress: data.value ?? prev.progress }
        }
        if (data.action === 'volume') {
          setFxVolume(data.value ?? fxVolume)
          return { ...prev, volume: data.value ?? prev.volume }
        }

        return prev
      })
    } else if (command === 'updateSoundEffectVolume' && data.id) {
      lockFxUi()
      setFxVolume(data.volume)
      setOptimisticActiveFx(prev => prev && prev.id === data.id ? { ...prev, volume: data.volume } : prev)
    }

    if (event && event.currentTarget) {
      const btn = event.currentTarget
      btn.style.opacity = '0.7'
      setTimeout(() => { if (btn) btn.style.opacity = '' }, 100)
    }

    // Lệnh quan trọng: gửi cả WS + HTTP (tránh WebView WS "connected" nhưng player không nhận)
    const critical = ['stopPlayback', 'playVideo', 'playPause', 'next', 'fullscreenVideo', 'addToSelected', 'removeFromSelected', 'clearAll', 'priority', 'toggleTrackLock', 'toggleAutoPlay', 'karaoke']
    if (critical.includes(command)) {
      try { wsSendCommand(command, { session: sessionToken, ...data }) } catch { /* ignore */ }
      try { sendRemoteCommand(command, { session: sessionToken, ...data }) } catch { /* ignore */ }
    } else if (connectionStatus === 'connected') {
      wsSendCommand(command, { session: sessionToken, ...data })
    } else {
      sendRemoteCommand(command, { session: sessionToken, ...data })
    }
  }

  const handleImportPlaylistPrompt = () => {
    Swal.fire({
      title: 'Nhập Playlist YouTube',
      input: 'text',
      inputPlaceholder: 'Dán link hoặc ID playlist YouTube vào đây...',
      showCancelButton: true,
      confirmButtonText: 'Đồng ý',
      cancelButtonText: 'Hủy',
      background: '#0B1224',
      color: '#fff',
      confirmButtonColor: '#4F7CFF',
      preConfirm: (value) => {
        if (!value || !value.trim()) {
          Swal.showValidationMessage('Vui lòng nhập đường dẫn playlist!')
          return false
        }
        return value.trim()
      }
    }).then(async (result) => {
      if (result.isConfirmed && result.value) {
        const query = result.value
        
        Swal.fire({
          title: 'Đang import playlist...',
          html: 'Vui lòng chờ trong giây lát',
          allowOutsideClick: false,
          didOpen: () => {
            Swal.showLoading()
          },
          background: '#0B1224',
          color: '#fff'
        })

        try {
          let playlistId = query
          const match = query.match(/[?&]list=([^#&?]+)/)
          if (match && match[1]) {
            playlistId = match[1]
          }

          const playlistUrl = `https://www.youtube.com/playlist?list=${playlistId}`
          const playlistItems = await searchVideos(playlistUrl, 100)
          
          if (!playlistItems || playlistItems.length === 0) {
            throw new Error('Không tìm thấy bài hát nào trong playlist hoặc playlist riêng tư.')
          }

          const videosWithSource = playlistItems.map(v => ({
            ...v,
            source: 'youtube'
          }))

          setPlaylist(prev => {
            const newVideos = videosWithSource.filter(v => !prev.some(existing => existing.id === v.id))
            return [...prev, ...newVideos]
          })

          handleCommand('importPlaylist', { videos: videosWithSource })

          Swal.fire({
            title: 'Import thành công!',
            text: `Đã thêm ${videosWithSource.length} bài hát vào hàng chờ.`,
            icon: 'success',
            background: '#0B1224',
            color: '#fff',
            confirmButtonColor: '#4F7CFF'
          })
        } catch (err) {
          console.error('Import playlist failed:', err)
          Swal.fire({
            title: 'Import thất bại',
            text: err.message || 'Có lỗi xảy ra khi tải danh sách bài hát.',
            icon: 'error',
            background: '#0B1224',
            color: '#fff',
            confirmButtonColor: '#EF4444'
          })
        }
      }
    })
  }

  const confirmPlayVideo = (video, from, e) => {
    if (e) e.stopPropagation()
    // Operator desk / ShowCue: phát ngay, không Swal (WebView hay kẹt popup)
    if (isOperatorDesk) {
      handleCommand('playVideo', { video, from }, e)
      return
    }
    Swal.fire({
      title: 'Phát ngay bài này?',
      html: `<div class="text-left space-y-2 text-slate-300">
        <p>Bạn có chắc chắn muốn phát ngay:</p>
        <p class="text-[#EF4444] font-black uppercase text-sm">${decodeHtmlEntities(video.title)}</p>
        <p class="text-[10px] text-slate-500 font-bold uppercase tracking-wider">${decodeHtmlEntities(video.channelTitle || '')}</p>
        <p class="text-[10px] text-yellow-500 font-bold mt-1">⚠ Bài đang phát sẽ bị ngắt</p>
      </div>`,
      icon: 'warning',
      showCancelButton: true,
      confirmButtonColor: '#EF4444',
      cancelButtonColor: '#374151',
      confirmButtonText: 'Phát ngay',
      cancelButtonText: 'Hủy',
      background: '#0B1224',
      color: '#ffffff',
      customClass: {
        popup: 'rounded-3xl border border-white/5 shadow-2xl p-6 max-w-[90%]',
        title: 'text-sm font-black uppercase tracking-wider text-white border-b border-white/5 pb-2 text-left',
        confirmButton: 'px-4 py-2 text-xs font-black uppercase tracking-widest rounded-xl hover:scale-95 transition-transform',
        cancelButton: 'px-4 py-2 text-xs font-black uppercase tracking-widest rounded-xl hover:scale-95 transition-transform'
      }
    }).then((result) => {
      if (result.isConfirmed) {
        handleCommand('playVideo', { video, from }, e)
        setActiveTab('controls')
      }
    })
  }

  const confirmAddToSelected = (video, e) => {
    if (e) e.stopPropagation()
    Swal.fire({
      title: 'Thêm vào hàng chờ?',
      html: `<div class="text-left space-y-2 text-slate-300">
        <p>Thêm bài hát sau vào danh sách chờ:</p>
        <p class="text-[#4F7CFF] font-black uppercase text-sm">${decodeHtmlEntities(video.title)}</p>
        <p class="text-[10px] text-slate-500 font-bold uppercase tracking-wider">${decodeHtmlEntities(video.channelTitle)}</p>
      </div>`,
      icon: 'question',
      showCancelButton: true,
      confirmButtonColor: '#4F7CFF',
      cancelButtonColor: '#374151',
      confirmButtonText: 'Thêm bài',
      cancelButtonText: 'Hủy',
      background: '#0B1224',
      color: '#ffffff',
      customClass: {
        popup: 'rounded-3xl border border-white/5 shadow-2xl p-6 max-w-[90%]',
        title: 'text-sm font-black uppercase tracking-wider text-white border-b border-white/5 pb-2 text-left',
        confirmButton: 'px-4 py-2 text-xs font-black uppercase tracking-widest rounded-xl hover:scale-95 transition-transform',
        cancelButton: 'px-4 py-2 text-xs font-black uppercase tracking-widest rounded-xl hover:scale-95 transition-transform'
      }
    }).then((result) => {
      if (result.isConfirmed) {
        handleCommand('addToSelected', { video }, e)
      }
    })
  }

  const formatTime = (seconds) => {
    if (!seconds || isNaN(seconds)) return '0:00'
    const mins = Math.floor(seconds / 60)
    const secs = Math.floor(seconds % 60)
    return `${mins}:${secs.toString().padStart(2, '0')}`
  }

  const switchSearchSource = (newSource) => {
    if (newSource === searchSource) return

    if (searchSource === 'youtube') {
      setYoutubeSearchState({ query: searchQuery, results: searchResults })
    } else {
      setSoundcloudSearchState({ query: searchQuery, results: searchResults })
    }

    const targetState = newSource === 'youtube' ? youtubeSearchState : soundcloudSearchState

    setSearchQuery(targetState.query)
    setSearchResults(targetState.results)
    setSearchSource(newSource)
    setSearchError(null)
  }

  const toggleKaraoke = () => {
    const newValue = !isKaraokeMode
    setIsKaraokeMode(newValue)
    lastKaraokeChangeRef.current = Date.now()
    handleCommand('karaoke', { value: newValue })
  }

  const toggleAutoPlay = () => {
    const newValue = !autoPlayNext
    setAutoPlayNext(newValue)
    lastAutoPlayChangeRef.current = Date.now()
    lastSystemToggleRef.current = Date.now()
    handleCommand('toggleAutoPlay', { value: newValue })
    const toastId = Date.now()
    setToasts([{
      id: toastId,
      message: newValue
        ? '⏭ Tự động chuyển bài: BẬT'
        : '⏹ Tự động chuyển bài: TẮT'
    }])
    setTimeout(() => setToasts(prev => prev.filter(t => t.id !== toastId)), 2200)
  }

  const toggleTrackLock = () => {
    const newValue = !isTrackChangeLocked
    setIsTrackChangeLocked(newValue)
    lastTrackLockChangeRef.current = Date.now()
    lastSystemToggleRef.current = Date.now()
    handleCommand('toggleTrackLock', { value: newValue })
    const toastId = Date.now()
    setToasts([{ id: toastId, message: newValue ? '🔒 Đã khóa chuyển bài' : '🔓 Đã mở khóa chuyển bài' }])
    setTimeout(() => setToasts(prev => prev.filter(t => t.id !== toastId)), 2200)
  }

  const handleSearchWithQuery = async (queryText) => {
    if (!queryText.trim()) return

    setIsSearchFocused(false)

    // Save query to search history
    saveToSearchHistory(queryText)

    // Close suggestions and blur inputs to hide keyboards
    setShowSuggestions(false)
    if (searchInputRef.current) searchInputRef.current.blur()
    if (desktopInputRef.current) desktopInputRef.current.blur()

    setIsSearching(true)
    setSearchError(null)

    try {
      const query = isKaraokeMode ? `${queryText} karaoke` : queryText
      let results = []

      if (searchSource === 'youtube') {
        results = await searchVideos(query, 30)
        if (results && results.length > 0) {
          const videoIds = results.map(r => r.id)
          const details = await getVideoDetails(videoIds)
          results = results.map(result => {
            const detail = details.find(d => d.id === result.id)
            return detail ? { ...result, ...detail, source: 'youtube' } : { ...result, source: 'youtube' }
          })

          results = results.filter(r => {
            if (!isPlayableVideo(r)) return false
            if (r.source === 'youtube' && r.contentDetails?.duration) {
              const seconds = parseISO8601Duration(r.contentDetails.duration)
              if (seconds < 60) {
                console.log(`Bỏ qua video Short: ${r.title} (${seconds}s)`)
                return false
              }
            }
            return true
          })
        }
      } else {
        results = await searchTracks(query, 30)
        if (results && results.length > 0) {
          results = results.map(r => ({ ...r, source: 'soundcloud' }))
        } else if (!results || results.length === 0) {
          setSearchError('Không tìm thấy bài hát nào trên SoundCloud.')
        }
      }

      setSearchResults(results || [])

      if (searchSource === 'youtube') {
        setYoutubeSearchState({ query: queryText, results: results })
      } else {
        setSoundcloudSearchState({ query: queryText, results: results })
      }

      if (isConnected) {
        handleCommand('search', { query: queryText })
      }
    } catch (err) {
      console.error('Search error:', err)
      if (searchSource === 'soundcloud' && err.message?.includes('Proxy server')) {
        setSearchError('Máy chủ SoundCloud chưa chạy.')
      } else {
        setSearchError(err.message || 'Lỗi khi tìm kiếm. Vui lòng thử lại.')
      }
    } finally {
      setIsSearching(false)
    }
  }

  const handleSearchSubmit = (e) => {
    e.preventDefault()
    handleSearchWithQuery(searchQuery)
  }

  const { isSupported: isSpeechSupported, isListening: isVoiceListening, startListening, stopListening } = useSpeechRecognition({
    lang: 'vi-VN',
    enabled: true,
    onTranscriptAction: useCallback((transcript, isFinal) => {
      if (isFinal) {
        setSearchQuery(transcript)
        setInterimTranscript('')
      } else {
        setInterimTranscript(transcript)
      }
    }, []),
    onListeningEndAction: useCallback((transcript) => {
      if (transcript) {
        setSearchQuery(transcript)

        const toastId = Date.now()
        setToasts([{ id: toastId, message: `🎙️ Nhận diện: "${transcript}"` }])
        setTimeout(() => setToasts(prev => prev.filter(t => t.id !== toastId)), 3000)

        setTimeout(() => {
          handleSearchWithQuery(transcript)
        }, 500)
      }
      setInterimTranscript('')
      setIsListening(false)
    }, []),
    onErrorAction: useCallback((error) => {
      console.error('Speech recognition error:', error)
      setIsListening(false)
      const toastId = Date.now()
      
      let message = `⚠️ Lỗi giọng nói: ${error}`
      // Check for permission block or non-secure origin restrictions on mobile
      if (error === 'not-allowed') {
        const isSecure = window.location.protocol === 'https:' || 
                         window.location.hostname === 'localhost' || 
                         window.location.hostname === '127.0.0.1'
        if (!isSecure) {
          message = '⚠️ Yêu cầu kết nối HTTPS để sử dụng Microphone trên điện thoại!'
        } else {
          message = '⚠️ Vui lòng cấp quyền truy cập Microphone trong cài đặt trình duyệt!'
        }
      }
      
      setToasts([{ id: toastId, message }])
      setTimeout(() => setToasts(prev => prev.filter(t => t.id !== toastId)), 3000)
    }, [])
  })

  useEffect(() => {
    setIsListening(isVoiceListening)
  }, [isVoiceListening])

  // Fetch search suggestions when typing (YouTube only)
  useEffect(() => {
    const requestId = ++suggestionRequestRef.current

    if (searchSource !== 'youtube') {
      setSuggestions([])
      setShowSuggestions(false)
      setIsLoadingSuggestions(false)
      return
    }

    if (searchQuery.length < 2) {
      setSuggestions([])
      setIsLoadingSuggestions(false)
      // Giữ panel mở khi focus để hiện lịch sử (query ngắn); blur/click-outside sẽ đóng
      if (!isSearchFocused) setShowSuggestions(false)
      return
    }

    setIsLoadingSuggestions(true)
    const timer = setTimeout(async () => {
      try {
        const results = await fetchSearchSuggestions(searchQuery)
        if (requestId !== suggestionRequestRef.current) return
        setSuggestions(results)
        // Only show suggestions if focused AND user didn't just pick one
        if (isSearchFocused && results.length > 0 && !justSelectedSuggestionRef.current) {
          setShowSuggestions(true)
        }
        justSelectedSuggestionRef.current = false // Reset after fetch
      } catch (error) {
        if (requestId !== suggestionRequestRef.current) return
        console.error('Failed to fetch suggestions:', error)
        setSuggestions([])
      } finally {
        if (requestId === suggestionRequestRef.current) {
          setIsLoadingSuggestions(false)
        }
      }
    }, 300) // 300ms debounce

    return () => clearTimeout(timer)
  }, [searchQuery, searchSource, isSearchFocused])

  // Close suggestions when clicking outside
  useEffect(() => {
    const handleClickOutside = (e) => {
      const isOutsideMobile = !mobileSearchContainerRef.current || !mobileSearchContainerRef.current.contains(e.target)
      const isOutsideDesktop = !desktopSearchContainerRef.current || !desktopSearchContainerRef.current.contains(e.target)
      
      if (isOutsideMobile && isOutsideDesktop) {
        setShowSuggestions(false)
      }
    }

    document.addEventListener('mousedown', handleClickOutside)
    return () => document.removeEventListener('mousedown', handleClickOutside)
  }, [])

  // Reset suggestionIndex when query or suggestions change
  useEffect(() => {
    setSuggestionIndex(-1)
  }, [searchQuery, suggestions])

  // Fetch trending songs on component mount
  useEffect(() => {
    const loadTrendingSongs = async () => {
      setIsLoadingTrending(true)
      try {
        const response = await fetch('/api/youtube/trending-suggestions?maxResults=10')
        if (response.ok) {
          const data = await response.json()
          if (Array.isArray(data)) {
            const formatted = data.map(song => ({
              id: song.id,
              title: song.title || 'Untitled',
              thumbnail: song.thumbnail || 'https://images.unsplash.com/photo-1511671782779-c97d3d27a1d4?w=150&auto=format&fit=crop&q=60',
              channelTitle: song.channel || song.channelTitle || 'YouTube Music',
              duration: song.duration || '0:00',
              source: 'youtube'
            }))
            setTrendingSongs(formatted)
          }
        }
      } catch (err) {
        console.error('Failed to load trending songs:', err)
      } finally {
        setIsLoadingTrending(false)
      }
    }
    loadTrendingSongs()
  }, [])

  const handleKeyDownSuggestions = (e, inputRef) => {
    if (suggestions.length === 0) return

    if (e.key === 'ArrowDown') {
      e.preventDefault()
      setSuggestionIndex(prev => (prev < suggestions.length - 1 ? prev + 1 : 0))
    } else if (e.key === 'ArrowUp') {
      e.preventDefault()
      setSuggestionIndex(prev => (prev > 0 ? prev - 1 : suggestions.length - 1))
    } else if (e.key === 'Enter') {
      if (suggestionIndex > -1 && suggestionIndex < suggestions.length) {
        e.preventDefault()
        const text = typeof suggestions[suggestionIndex] === 'string'
          ? suggestions[suggestionIndex]
          : suggestions[suggestionIndex].title
        setSearchQuery(text)
        setShowSuggestions(false)
        handleSearchWithQuery(text)
      }
    } else if (e.key === 'Escape') {
      setShowSuggestions(false)
      inputRef.current?.blur()
    }
  }

  const startVoiceRecognition = () => {
    if (!isSpeechSupported) {
      Swal.fire({
        title: 'Không hỗ trợ',
        text: 'Trình duyệt không hỗ trợ nhận diện giọng nói. Hãy thử Chrome hoặc Safari gốc.',
        icon: 'warning',
        confirmButtonColor: '#4F7CFF',
        background: '#0B1224',
        color: '#fff'
      })
      return
    }

    // SpeechRecognition on mobile browsers requires a secure context (HTTPS) or localhost
    const isSecureContext = window.location.protocol === 'https:' || 
                            window.location.hostname === 'localhost' || 
                            window.location.hostname === '127.0.0.1'
    
    if (!isSecureContext) {
      Swal.fire({
        title: 'Yêu cầu kết nối bảo mật',
        html: `<div class="text-left space-y-2 text-slate-300 text-sm">
          <p>Tính năng giọng nói yêu cầu kết nối <strong>HTTPS</strong> để truy cập Microphone trên điện thoại.</p>
          <p class="text-xs text-yellow-500 font-semibold">💡 Cách khắc phục khi test local:</p>
          <ul class="list-disc pl-4 text-xs space-y-1 text-slate-400">
            <li>Sử dụng ngrok để tạo link HTTPS: <code class="bg-black/40 px-1 py-0.5 rounded text-white font-mono">npx ngrok http 3000</code></li>
            <li>Hoặc deploy ứng dụng lên các nền tảng hỗ trợ HTTPS như Vercel/Netlify.</li>
          </ul>
        </div>`,
        icon: 'info',
        confirmButtonColor: '#4F7CFF',
        background: '#0B1224',
        color: '#fff'
      })
      return
    }

    const toastId = Date.now()
    setToasts([{ id: toastId, message: '🎙️ Đang nghe... Hãy nói tên bài hát' }])
    setTimeout(() => setToasts(prev => prev.filter(t => t.id !== toastId)), 3000)

    startListening()
    // Keep on current tab, don't auto-switch to controls
  }

  const getFilteredResults = (results) => {
    if (!isKaraokeMode) return results
    return results.filter(v =>
      v.title?.toLowerCase().includes('karaoke') ||
      v.title?.toLowerCase().includes('karaok')
    )
  }

  const handleStarterPlaylistClick = async (playlistItem) => {
    const query = playlistItem.label
    setSearchQuery(query)
    setIsSearching(true)
    setSearchError(null)

    try {
      const searchQueryString = isKaraokeMode ? `${query} karaoke` : query
      let results = []

      if (searchSource === 'youtube') {
        results = await searchVideos(searchQueryString, 30)
        if (results && results.length > 0) {
          const videoIds = results.map(r => r.id)
          const details = await getVideoDetails(videoIds)
          results = results.map(result => {
            const detail = details.find(d => d.id === result.id)
            return detail ? { ...result, ...detail, source: 'youtube' } : { ...result, source: 'youtube' }
          })
          results = results.filter(r => {
            if (!isPlayableVideo(r)) return false
            if (r.source === 'youtube' && r.contentDetails?.duration) {
              const seconds = parseISO8601Duration(r.contentDetails.duration)
              if (seconds < 60) return false
            }
            return true
          })
        }
      } else {
        results = await searchTracks(searchQueryString, 30)
        if (results && results.length > 0) {
          results = results.map(r => ({ ...r, source: 'soundcloud' }))
        }
      }

      setSearchResults(results || [])
      if (searchSource === 'youtube') {
        setYoutubeSearchState({ query, results })
      } else {
        setSoundcloudSearchState({ query, results })
      }

      if (isConnected) {
        handleCommand('search', { query })
      }
    } catch (err) {
      console.error('Search error:', err)
      setSearchError(err.message || 'Lỗi khi tìm kiếm.')
    } finally {
      setIsSearching(false)
    }
  }

  const filteredPlaylist = playlist.filter(v => v.id !== currentVideo?.id)

  const renderMiniPlayer = () => {
    if (!sessionToken) return null
    if (activeTab === 'controls') return null

    return (
      <div className="bg-[#0B1224]/95 backdrop-blur-md border-t border-white/5 shrink-0 shadow-[0_-10px_30px_rgba(0,0,0,0.5)] w-full">
        {/* Seek progress bar - slim, always visible at top of player */}
        {currentVideo && duration > 0 && (
          <div className="px-4 pt-2 pb-0">
            <div className="flex items-center gap-2">
              <span className="text-[8px] font-bold text-slate-600 font-mono w-7 text-right shrink-0">{formatClockTime(currentTime)}</span>
              <input
                type="range"
                min="0"
                max={duration || 100}
                value={currentTime}
                onChange={(e) => {
                  isSeekingRef.current = true
                  setCurrentTime(parseFloat(e.target.value))
                }}
                onPointerUp={(e) => {
                  isSeekingRef.current = false
                  const targetTime = parseFloat(e.target.value)
                  handleCommand('seek', { seconds: targetTime }, e)
                }}
                className="spotify-slider flex-1 cursor-pointer"
                style={{ height: '3px' }}
              />
              <span className="text-[8px] font-bold text-slate-600 font-mono w-7 shrink-0">{formatClockTime(duration)}</span>
            </div>
          </div>
        )}

        {/* Main mini player row */}
        <div className="px-4 py-2.5 flex items-center justify-between gap-3 h-[60px]">
          {/* Left: Thumbnail & Info - Clickable to open controls */}
          <div
            className="flex items-center gap-2.5 min-w-0 flex-1 cursor-pointer active:scale-[0.98] transition-transform"
            onClick={() => setActiveTab('controls')}
          >
            <div className="w-9 h-9 rounded-lg overflow-hidden bg-slate-950 shrink-0 border border-white/10 relative">
              {currentVideo ? (
                <img
                  src={currentVideo.source === 'youtube' ? `https://i.ytimg.com/vi/${currentVideo.id}/default.jpg` : currentVideo.thumbnail}
                  className="w-full h-full object-cover"
                  alt=""
                  onError={(e) => {
                    if (currentVideo.source === 'youtube' && !e.target.src.includes('mqdefault')) {
                      e.target.src = `https://i.ytimg.com/vi/${currentVideo.id}/default.jpg`
                    }
                  }}
                />
              ) : (
                <div className="w-full h-full flex items-center justify-center text-slate-700">
                  <FontAwesomeIcon icon={faMusic} className="text-xs opacity-40" />
                </div>
              )}
            </div>
            <div className="min-w-0 flex-1">
              <h4 className="text-xs font-black text-white truncate uppercase tracking-tight leading-snug">
                {currentVideo ? decodeHtmlEntities(currentVideo.title) : 'Không có bài đang phát'}
              </h4>
              <p className="text-[9px] font-bold text-slate-500 uppercase tracking-wider truncate mt-0.5">
                {currentVideo ? decodeHtmlEntities(currentVideo.channelTitle) : 'Player Ready'}
              </p>
            </div>
          </div>

          {/* Right: Actions */}
          <div className="flex items-center gap-1.5 shrink-0">
            <button
              onPointerDown={() => setShowVolumeSheet(true)}
              className="w-8 h-8 flex items-center justify-center bg-white/5 hover:bg-white/10 text-slate-300 rounded-full transition-all active:scale-90"
              title="Âm lượng"
            >
              <FontAwesomeIcon icon={faVolumeUp} className="text-xs" />
            </button>

            <button
              onPointerDown={(e) => handleCommand('playPause', {}, e)}
              className="w-9 h-9 flex items-center justify-center bg-[#4F7CFF] text-white rounded-full shadow-lg shadow-[#4F7CFF]/20 active:scale-90 transition-all"
            >
              <FontAwesomeIcon icon={remoteState?.isPlaying ? faPause : faPlay} className="text-xs ml-0.5" />
            </button>

            <button
              onPointerDown={(e) => !isTrackChangeLocked && handleCommand('next', {}, e)}
              disabled={isTrackChangeLocked}
              className={`w-8 h-8 flex items-center justify-center rounded-full transition-all active:scale-90 ${isTrackChangeLocked ? 'bg-amber-500/20 text-amber-300 opacity-80' : 'bg-white/5 hover:bg-white/10 text-slate-300'}`}
              title={isTrackChangeLocked ? 'Đang khóa chuyển bài' : 'Chuyển bài'}
            >
              <FontAwesomeIcon icon={isTrackChangeLocked ? faLock : faForwardStep} className="text-xs" />
            </button>
          </div>
        </div>
      </div>
    )
  }


  const renderVolumeSheet = () => {
    if (!showVolumeSheet) return null

    return (
      <div className="fixed inset-0 z-50 flex flex-col justify-end">
        {/* Background Overlay */}
        <div
          className="absolute inset-0 bg-black/60 backdrop-blur-sm"
          onClick={() => setShowVolumeSheet(false)}
        />

        {/* Sheet Content */}
        <div className="relative w-full max-w-md mx-auto bg-[#0B1224] rounded-t-3xl p-5 pb-8 border-t border-white/10 animate-slideUp z-10 shadow-[0_-20px_50px_rgba(0,0,0,0.8)]">
          {/* Header handle */}
          <div className="w-12 h-1 bg-white/10 rounded-full mx-auto mb-5" />

          <div className="flex items-center justify-between mb-4">
            <h3 className="text-xs font-black text-white uppercase tracking-widest flex items-center gap-2">
              <FontAwesomeIcon icon={faVolumeUp} className="text-[#4F7CFF]" />
              <span>Âm lượng hệ thống</span>
            </h3>
            <span className="text-xs font-black text-[#4F7CFF] font-mono">{volume}%</span>
          </div>

          <div className="flex items-center gap-4 py-3 bg-black/25 px-4 rounded-2xl border border-white/5 mb-5">
            <FontAwesomeIcon icon={faVolumeDown} className="text-xs text-slate-500" />
            <input
              type="range"
              min="0"
              max="100"
              value={volume}
              onChange={(e) => {
                const newVolume = parseInt(e.target.value)
                setVolume(newVolume)
              }}
              onPointerUp={(e) => {
                const newVolume = parseInt(e.target.value)
                lastVolumeChangeRef.current = Date.now()
                handleCommand('volume', { value: newVolume }, e)
              }}
              className="spotify-slider flex-1 cursor-pointer"
            />
            <FontAwesomeIcon icon={faVolumeUp} className="text-xs text-slate-500" />
          </div>

          <button
            onClick={() => setShowVolumeSheet(false)}
            className="w-full py-3 bg-white/5 hover:bg-white/10 text-white font-black text-xs uppercase tracking-widest rounded-xl transition-all active:scale-[0.98]"
          >
            Đóng
          </button>
        </div>
      </div>
    )
  }

  const renderTabs = () => (
    <div className="flex bg-[#0B1224] border-b border-white/5 px-2 py-1.5 gap-1 shrink-0 z-20">
      <button
        onClick={() => setActiveTab('search')}
        className={`flex-1 py-2 text-[9px] font-black uppercase tracking-wider flex flex-col items-center justify-center gap-1 rounded-xl transition-all ${activeTab === 'search' ? 'bg-white/5 text-[#4F7CFF]' : 'text-slate-500 hover:text-slate-300'}`}
      >
        <FontAwesomeIcon icon={faSearch} className="text-xs" />
        <span>Tìm bài</span>
      </button>
      <button
        onClick={() => setActiveTab('selected')}
        className={`flex-1 py-2 text-[9px] font-black uppercase tracking-wider flex flex-col items-center justify-center gap-1 rounded-xl transition-all relative ${activeTab === 'selected' ? 'bg-white/5 text-[#4F7CFF]' : 'text-slate-500 hover:text-slate-300'}`}
      >
        <FontAwesomeIcon icon={faListCheck} className="text-xs" />
        <span>Đã chọn</span>
        {filteredPlaylist.length > 0 && (
          <span className="absolute top-1 right-2 min-w-[14px] h-[14px] px-1 bg-[#EF4444] text-white text-[8px] font-mono rounded-full flex items-center justify-center shadow-lg border border-[#0B1224]">
            {filteredPlaylist.length}
          </span>
        )}
      </button>
      <button
        onClick={() => setActiveTab('history')}
        className={`flex-1 py-2 text-[9px] font-black uppercase tracking-wider flex flex-col items-center justify-center gap-1 rounded-xl transition-all ${activeTab === 'history' ? 'bg-white/5 text-slate-300' : 'text-slate-500 hover:text-slate-300'}`}
      >
        <FontAwesomeIcon icon={faClock} className="text-xs" />
        <span>Lịch sử</span>
      </button>
      <button
        onClick={() => setActiveTab('settings')}
        className={`flex-1 py-2 text-[9px] font-black uppercase tracking-wider flex flex-col items-center justify-center gap-1 rounded-xl transition-all ${activeTab === 'settings' ? 'bg-white/5 text-slate-300' : 'text-slate-500 hover:text-slate-300'}`}
      >
        <FontAwesomeIcon icon={faCog} className="text-xs" />
        <span>Cài đặt</span>
      </button>
    </div>
  )

  const renderSearchContent = () => {
    if (isSearchFocused) {
      return (
        <div className="flex-1 flex flex-col min-h-0 w-full px-4 pt-3 pb-4 gap-4 bg-black animate-fadeIn">
          {/* Header tìm kiếm gồm nút Back và Input */}
          <div className="flex items-center gap-3 shrink-0">
            <button
              type="button"
              onMouseDown={(e) => {
                e.preventDefault()
                setIsSearchFocused(false)
                setShowSuggestions(false)
                if (searchInputRef.current) searchInputRef.current.blur()
              }}
              onTouchStart={(e) => {
                e.preventDefault()
                setIsSearchFocused(false)
                setShowSuggestions(false)
                if (searchInputRef.current) searchInputRef.current.blur()
              }}
              className="w-9 h-9 rounded-full flex items-center justify-center bg-white/5 text-slate-300 hover:text-white transition-all active:scale-95 shrink-0"
            >
              <svg className="w-5 h-5" fill="none" stroke="currentColor" strokeWidth={2.5} viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" d="M15 19l-7-7 7-7" />
              </svg>
            </button>

            <div className="flex-1 relative">
              <form onSubmit={handleSearchSubmit} className="relative flex items-center w-full">
                {/* Icon kính lúp trang trí ở đầu ô input */}
                <div className="absolute left-3 text-slate-500 pointer-events-none flex items-center justify-center">
                  <Search size={14} />
                </div>
                
                <input
                  ref={searchInputRef}
                  autoFocus={true}
                  className="w-full bg-white/5 border border-white/10 focus:border-[#4F7CFF]/50 text-white text-xs rounded-xl py-2.5 pl-9 pr-18 placeholder:text-slate-500 font-bold focus:outline-none transition-all focus:bg-[#0B1224]/80"
                  placeholder="Tìm kiếm video..."
                  value={searchQuery}
                  onChange={e => setSearchQuery(e.target.value)}
                  onKeyDown={(e) => handleKeyDownSuggestions(e, searchInputRef)}
                />

                <div className="absolute right-1.5 flex items-center gap-1.5">
                  {/* Nút Clear (X) để xóa nhanh chữ đã nhập */}
                  {searchQuery && (
                    <button
                      type="button"
                      onClick={() => setSearchQuery('')}
                      className="w-7 h-7 rounded-lg flex items-center justify-center text-slate-400 hover:text-white transition-all active:scale-90"
                      title="Xóa nội dung"
                    >
                      <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" strokeWidth={2.5} viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
                      </svg>
                    </button>
                  )}

                  {/* Nút Tìm kiếm thủ công */}
                  <button 
                    type="submit" 
                    className="w-7 h-7 bg-[#4F7CFF] rounded-lg flex items-center justify-center text-white shadow hover:bg-[#4F7CFF]/90 transition-all active:scale-95"
                  >
                    {isSearching ? <div className="animate-spin w-3.5 h-3.5 border-2 border-white rounded-full border-t-transparent" /> : <Search size={13} />}
                  </button>
                </div>
              </form>
            </div>

            {/* Nút Mic to, tròn, dễ dùng và nổi bật ở bên ngoài */}
            <button
              type="button"
              onClick={startVoiceRecognition}
              className={`w-9 h-9 rounded-full flex items-center justify-center transition-all active:scale-90 shrink-0 ${
                isListening 
                  ? 'bg-red-500 text-white animate-pulse shadow-[0_0_15px_rgba(239,68,68,0.7)]' 
                  : 'bg-white/5 text-slate-300 hover:text-white hover:bg-white/10 border border-white/10'
              }`}
              title="Tìm kiếm bằng giọng nói"
            >
              <FontAwesomeIcon icon={faMicrophone} className="text-sm" />
            </button>
          </div>

          {/* Toggle Nguồn Tìm Kiếm trong chế độ gõ */}
          <div className="flex bg-black/40 p-1 rounded-xl border border-white/5 shrink-0">
            <button
              type="button"
              onClick={() => switchSearchSource('youtube')}
              className={`flex-1 py-1.5 rounded-lg text-[9px] font-black flex items-center justify-center gap-1.5 transition-all uppercase tracking-widest ${searchSource === 'youtube' ? 'bg-[#EF4444] text-white shadow-md' : 'text-slate-500 hover:text-slate-300'}`}
            >
              <Youtube size={10} /> YouTube
            </button>
            <button
              type="button"
              onClick={() => switchSearchSource('soundcloud')}
              className={`flex-1 py-1.5 rounded-lg text-[9px] font-black flex items-center justify-center gap-1.5 transition-all uppercase tracking-widest ${searchSource === 'soundcloud' ? 'bg-[#FF5500] text-white shadow-md' : 'text-slate-500 hover:text-slate-300'}`}
            >
              <FontAwesomeIcon icon={faMusic} className="text-[9px]" /> SoundCloud
            </button>
          </div>

          {/* Vùng gợi ý/lịch sử hiển thị phẳng (inline) lấp đầy phần bên dưới */}
          <div className="flex-1 overflow-y-auto custom-scrollbar overflow-x-hidden p-1 min-h-0">
            {/* Lịch sử tìm kiếm */}
            {searchQuery.length < 2 && searchHistory.length > 0 && (
              <div className="py-1">
                <div className="px-4 py-2.5 text-[10px] font-black text-slate-500 uppercase tracking-widest flex items-center justify-between border-b border-white/5 mb-1">
                  <span>Lịch sử tìm kiếm gần đây</span>
                  <button
                    type="button"
                    onMouseDown={(e) => {
                      e.preventDefault()
                      clearSearchHistory()
                    }}
                    onTouchStart={(e) => {
                      e.preventDefault()
                      clearSearchHistory()
                    }}
                    className="text-red-400 hover:text-red-300 transition-colors uppercase text-[9px] font-bold px-2 py-0.5 rounded hover:bg-red-500/10"
                  >
                    Xóa hết
                  </button>
                </div>
                {searchHistory.map((item, index) => (
                  <div
                    key={index}
                    onMouseDown={(e) => {
                      e.preventDefault()
                      justSelectedSuggestionRef.current = true
                      setSearchQuery(item)
                      setIsSearchFocused(false)
                      setShowSuggestions(false)
                      handleSearchWithQuery(item)
                    }}
                    onTouchStart={(e) => {
                      e.preventDefault()
                      justSelectedSuggestionRef.current = true
                      setSearchQuery(item)
                      setIsSearchFocused(false)
                      setShowSuggestions(false)
                      handleSearchWithQuery(item)
                    }}
                    className="px-4 py-3.5 cursor-pointer hover:bg-white/5 text-slate-300 hover:text-white transition-all duration-150 flex items-center justify-between gap-3 border-b border-white/5 last:border-b-0"
                  >
                    <div className="flex items-center gap-3.5 flex-1 min-w-0">
                      <svg className="w-4 h-4 text-slate-500 shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 8v4l3 3m6-3a9 9 0 11-18 0 9 9 0 0118 0z" />
                      </svg>
                      <span className="text-sm font-semibold truncate tracking-wide leading-relaxed">{item}</span>
                    </div>
                    <button
                      type="button"
                      onMouseDown={(e) => {
                        e.preventDefault()
                        e.stopPropagation()
                        deleteSearchHistoryItem(item)
                      }}
                      onTouchStart={(e) => {
                        e.preventDefault()
                        e.stopPropagation()
                        deleteSearchHistoryItem(item)
                      }}
                      className="w-9 h-9 rounded-full flex items-center justify-center bg-white/0 hover:bg-white/10 text-slate-500 hover:text-red-400 transition-all shrink-0 active:scale-90"
                      title="Xóa lịch sử"
                    >
                      <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2.5} d="M6 18L18 6M6 6l12 12" />
                      </svg>
                    </button>
                  </div>
                ))}
              </div>
            )}

            {/* Đang tìm gợi ý API */}
            {searchQuery.length >= 2 && isLoadingSuggestions && (
              <div className="px-4 py-6 text-center text-slate-400 text-sm font-medium flex items-center justify-center gap-2">
                <span className="inline-block animate-spin text-base">⏳</span>
                Đang tìm gợi ý...
              </div>
            )}

            {/* Gợi ý từ API */}
            {searchQuery.length >= 2 && !isLoadingSuggestions && suggestions.length > 0 && suggestions.map((item, index) => {
              const text = typeof item === 'string' ? item : item.title
              return (
                <div
                  key={index}
                  onMouseDown={(e) => {
                    e.preventDefault()
                    justSelectedSuggestionRef.current = true
                    setSearchQuery(text)
                    setIsSearchFocused(false)
                    setShowSuggestions(false)
                    handleSearchWithQuery(text)
                  }}
                  onTouchStart={(e) => {
                    e.preventDefault()
                    justSelectedSuggestionRef.current = true
                    setSearchQuery(text)
                    setIsSearchFocused(false)
                    setShowSuggestions(false)
                    handleSearchWithQuery(text)
                  }}
                  className={`px-4 py-3.5 cursor-pointer transition-all duration-150 border-b border-white/5 last:border-b-0 flex items-center gap-3.5 ${
                    index === suggestionIndex 
                      ? 'bg-[#4F7CFF] text-white font-bold shadow-[0_4px_12px_rgba(79,124,255,0.25)] rounded-xl' 
                      : 'hover:bg-white/5 text-slate-100'
                  }`}
                >
                  <svg 
                    className={`w-4 h-4 flex-shrink-0 transition-colors ${
                      index === suggestionIndex ? 'text-white' : 'text-[#4F7CFF]'
                    }`} 
                    fill="none" 
                    stroke="currentColor" 
                    viewBox="0 0 24 24"
                  >
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2.5} d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
                  </svg>
                  
                  <span className="text-sm font-semibold truncate flex-1 tracking-wide leading-relaxed">
                    {text}
                  </span>

                  <svg 
                    className={`w-4 h-4 flex-shrink-0 transition-colors ${
                      index === suggestionIndex ? 'text-white/80' : 'text-slate-500'
                    }`} 
                    fill="none" 
                    stroke="currentColor" 
                    viewBox="0 0 24 24"
                  >
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2.5} d="M10 6H6a2 2 0 00-2 2v10a2 2 0 002 2h10a2 2 0 002-2v-4M14 4h6m0 0v6m0-6L10 14" />
                  </svg>
                </div>
              )
            })}

            {/* Trống */}
            {searchQuery.length >= 2 && !isLoadingSuggestions && suggestions.length === 0 && (
              <div className="px-4 py-12 text-center text-slate-500 text-xs font-semibold">
                Không tìm thấy gợi ý nào cho "{searchQuery}"
              </div>
            )}
          </div>
        </div>
      )
    }

    return (
      <div className="flex-1 flex flex-col min-h-0 w-full px-4 pt-3 pb-4 gap-3 bg-[#050816] animate-fadeIn">
        {/* Search Header Container */}
        <div className="bg-[#0B1224]/80 backdrop-blur-xl p-3 rounded-2xl border border-white/5 shadow-lg space-y-3 shrink-0">
          <div className="flex flex-col gap-2.5">
            {/* Source Toggle */}
            <div className="flex bg-black/40 p-1 rounded-xl border border-white/5">
              <button
                type="button"
                onClick={() => switchSearchSource('youtube')}
                className={`flex-1 py-1.5 rounded-lg text-[9px] font-black flex items-center justify-center gap-1.5 transition-all uppercase tracking-widest ${searchSource === 'youtube' ? 'bg-[#EF4444] text-white shadow-md' : 'text-slate-500 hover:text-slate-300'}`}
              >
                <Youtube size={10} /> YouTube
              </button>
              <button
                type="button"
                onClick={() => switchSearchSource('soundcloud')}
                className={`flex-1 py-1.5 rounded-lg text-[9px] font-black flex items-center justify-center gap-1.5 transition-all uppercase tracking-widest ${searchSource === 'soundcloud' ? 'bg-[#FF5500] text-white shadow-md' : 'text-slate-500 hover:text-slate-300'}`}
              >
                <FontAwesomeIcon icon={faMusic} className="text-[9px]" /> SoundCloud
              </button>
            </div>

            {/* Ô tìm kiếm dạng Tĩnh - Nhấp vào để kích hoạt Chế độ gõ toàn màn hình */}
            <div 
              onClick={() => {
                setIsSearchFocused(true)
                setShowSuggestions(true)
                setTimeout(() => {
                  if (searchInputRef.current) searchInputRef.current.focus()
                }, 100)
              }}
              className="relative cursor-pointer"
            >
              <div className="w-full bg-black/35 border border-white/5 text-slate-400 text-xs rounded-xl py-2 pl-4 pr-3 font-bold flex items-center justify-between min-h-[40px] hover:border-white/10 transition-colors">
                <span className="truncate mr-4 flex-1 text-left">{searchQuery || (searchSource === 'youtube' ? 'Tìm video YouTube...' : 'Tìm nhạc SoundCloud...')}</span>
                <div className="flex items-center gap-1.5 shrink-0">
                  {/* Nút Mic bấm trực tiếp để thu âm giọng nói */}
                  <button
                    type="button"
                    onClick={(e) => {
                      e.stopPropagation()
                      startVoiceRecognition()
                    }}
                    onTouchStart={(e) => {
                      e.stopPropagation()
                      startVoiceRecognition()
                    }}
                    className={`w-7 h-7 rounded-lg flex items-center justify-center transition-all active:scale-90 ${
                      isListening 
                        ? 'bg-red-500 text-white animate-pulse shadow-[0_0_10px_rgba(239,68,68,0.6)]' 
                        : 'bg-white/5 text-slate-400 hover:text-white border border-white/10'
                    }`}
                    title="Tìm kiếm giọng nói"
                  >
                    <FontAwesomeIcon icon={faMicrophone} className="text-[11px]" />
                  </button>

                  {/* Icon Search (Kính lúp) chỉ hiển thị để minh họa */}
                  <div className="w-7 h-7 rounded-lg flex items-center justify-center text-slate-500">
                    <Search size={13} />
                  </div>
                </div>
              </div>
            </div>
          </div>

          {/* Badges / Errors */}
          {(searchError || isKaraokeMode || isListening) && (
            <div className="flex items-center gap-2 px-0.5">
              {searchError && (
                <div className="text-[8px] text-[#EF4444] font-black uppercase tracking-wider truncate flex items-center gap-1.5">
                  <span className="w-1.5 h-1.5 rounded-full bg-[#EF4444] animate-pulse" />
                  Lỗi: {searchError}
                </div>
              )}
              {isListening && (
                <div className="text-[8px] text-[#EF4444] font-black uppercase tracking-wider flex items-center gap-1.5 bg-[#EF4444]/15 px-2 py-0.5 rounded-full border border-[#EF4444]/25">
                  <span className="w-1.5 h-1.5 rounded-full bg-[#EF4444] animate-pulse" />
                  Đang nghe...
                </div>
              )}
              {isKaraokeMode && (
                <div className="text-[8px] text-[#10B981] font-black uppercase tracking-wider flex items-center gap-1.5 bg-[#10B981]/15 px-2 py-0.5 rounded-full border border-[#10B981]/25">
                  <span className="w-1.5 h-1.5 rounded-full bg-[#10B981] animate-pulse" />
                  Đang tìm Karaoke
                </div>
              )}
            </div>
          )}
        </div>

        {/* Grid Results - Redesigned vkara list-style (Horizontal Thumbnail + Horizontal triple button action card) */}
        <div className="flex-1 overflow-y-auto overflow-x-hidden custom-scrollbar pr-0.5 pb-44">
          {searchResults.length > 0 ? (
            <div className="flex flex-col gap-3">
              {getFilteredResults(searchResults).map((video) => (
                <div
                  key={video.id}
                  onClick={() => setExpandedResultId(prev => prev === video.id ? null : video.id)}
                  className="bg-[#0B1224]/40 border border-white/5 rounded-2xl p-3 space-y-3 hover:border-[#4F7CFF]/20 transition-all relative cursor-pointer"
                >
                  {/* Top Row: Thumbnail & Info side-by-side */}
                  <div className="flex gap-3">
                    {/* Thumbnail left */}
                    <div className="relative w-28 aspect-video rounded-xl overflow-hidden bg-slate-950 shrink-0 border border-white/5">
                      <img
                        src={video.source === 'youtube' ? `https://i.ytimg.com/vi/${video.id}/mqdefault.jpg` : video.thumbnail}
                        className="w-full h-full object-cover"
                        alt=""
                        onError={(e) => {
                          if (video.source === 'youtube' && !e.target.src.includes('mqdefault')) {
                            e.target.src = `https://i.ytimg.com/vi/${video.id}/mqdefault.jpg`
                          }
                        }}
                      />
                      {video.duration && (
                        <div className="absolute bottom-1 right-1 px-1 py-0.5 bg-black/85 backdrop-blur-md text-[8px] text-white font-mono rounded border border-white/5">
                          {video.source === 'soundcloud' ? formatSoundCloudDuration(video.duration) : formatYouTubeDuration(video.duration)}
                        </div>
                      )}
                    </div>

                    {/* Text details right */}
                    <div className="flex-1 min-w-0 flex flex-col justify-between py-0.5">
                      <h3 className="text-[11px] font-black text-white line-clamp-2 leading-tight uppercase tracking-tight">
                        {decodeHtmlEntities(video.title)}
                      </h3>
                      <div className="space-y-0.5 mt-1">
                        <div className="flex items-center gap-1 text-[8px] font-black text-slate-400 uppercase tracking-wider">
                          <span className="truncate max-w-[120px]">{decodeHtmlEntities(video.channelTitle)}</span>
                          <FontAwesomeIcon icon={faCheckCircle} className="text-[#4F7CFF] text-[8px] shrink-0" />
                        </div>
                        <span className="text-[8px] font-bold text-slate-500 uppercase tracking-wider block">
                          {video.viewCount ? `${formatViewCount(video.viewCount)} views` : 'SOUNDCLOUD'}
                        </span>
                      </div>
                    </div>
                  </div>

                  {/* Bottom Row: Triple Actions buttons layout (Play, Add, Play next) - shown when card expanded */}
                  {expandedResultId === video.id && (
                    <div className="grid grid-cols-3 gap-2 pt-2 border-t border-white/5">
                      <button
                        onClick={(e) => { e.stopPropagation(); confirmPlayVideo(video, 'search', e) }}
                        className="py-2 bg-[#10B981]/5 hover:bg-[#10B981]/15 text-[#10B981] border border-[#10B981]/25 rounded-xl flex items-center justify-center gap-1.5 active:scale-95 transition-all text-[9px] font-black uppercase tracking-wider"
                      >
                        <FontAwesomeIcon icon={faPlay} className="text-[8px]" />
                        <span>Play</span>
                      </button>

                      <button
                        onClick={(e) => { e.stopPropagation(); confirmAddToSelected(video, e) }}
                        className="py-2 bg-white/5 hover:bg-white/10 text-white border border-white/10 rounded-xl flex items-center justify-center gap-1.5 active:scale-95 transition-all text-[9px] font-black uppercase tracking-wider"
                      >
                        <FontAwesomeIcon icon={faPlus} className="text-[8px]" />
                        <span>Add</span>
                      </button>

                      <button
                        onClick={(e) => {
                          e.stopPropagation()
                          handleCommand('priority', { videoId: video.id }, e)
                          const toastId = Date.now()
                          setToasts([{ id: toastId, message: `Play next: ${video.title?.slice(0, 18)}...` }])
                          setTimeout(() => setToasts(prev => prev.filter(t => t.id !== toastId)), 3000)
                        }}
                        className="py-2 bg-[#F59E0B]/5 hover:bg-[#F59E0B]/15 text-[#F59E0B] border border-[#F59E0B]/25 rounded-xl flex items-center justify-center gap-1.5 active:scale-95 transition-all text-[9px] font-black uppercase tracking-wider"
                      >
                        <FontAwesomeIcon icon={faArrowUp} className="text-[8px]" />
                        <span>Play next</span>
                      </button>
                    </div>
                  )}
                </div>
              ))}
            </div>
          ) : (
            /* Giao diện vkara-style Search Empty/Starter (Hình 1) */
            <div className="flex flex-col gap-4 py-4 px-1">
              {/* Lịch sử đã hát (Mobile) */}
              <div className="space-y-2.5">
                <span className="text-[9px] font-black text-[#8B5CF6] uppercase tracking-wider block text-left">Lịch sử đã hát 🕒</span>
                
                {watchHistory.length > 0 ? (
                  <div className="space-y-3 animate-fadeIn">
                    {watchHistory.slice(0, 7).map((song) => (
                      <div
                        key={song.id}
                        onClick={() => setExpandedResultId(prev => prev === song.id ? null : song.id)}
                        className="bg-[#0B1224]/40 border border-white/5 rounded-2xl p-3 space-y-3 hover:border-[#4F7CFF]/20 transition-all relative cursor-pointer"
                      >
                        {/* Top Row: Thumbnail & Info side-by-side */}
                        <div className="flex gap-3">
                          {/* Thumbnail left */}
                          <div className="relative w-28 aspect-video rounded-xl overflow-hidden bg-slate-950 shrink-0 border border-white/5">
                            <img
                              src={song.thumbnail}
                              className="w-full h-full object-cover"
                              alt=""
                            />
                            {song.duration && (
                              <div className="absolute bottom-1 right-1 px-1 py-0.5 bg-black/85 backdrop-blur-md text-[8px] text-white font-mono rounded border border-white/5">
                                {song.duration}
                              </div>
                            )}
                          </div>

                          {/* Text details right */}
                          <div className="flex-1 min-w-0 flex flex-col justify-between py-0.5">
                            <h3 className="text-[11px] font-black text-white line-clamp-2 leading-tight uppercase tracking-tight text-left">
                              {song.title}
                            </h3>
                            <div className="space-y-0.5 mt-1">
                              <div className="flex items-center gap-1 text-[8px] font-black text-slate-400 uppercase tracking-wider text-left">
                                <span className="truncate max-w-[120px]">{song.channelTitle}</span>
                                <FontAwesomeIcon icon={faCheckCircle} className="text-[#4F7CFF] text-[8px] shrink-0" />
                              </div>
                              <span className="text-[8px] font-bold text-slate-500 uppercase tracking-wider block text-left">
                                LỊCH SỬ ĐÃ PHÁT
                              </span>
                            </div>
                          </div>
                        </div>

                        {/* Bottom Row: Triple Actions (Play, Add, Play next) */}
                        {expandedResultId === song.id && (
                          <div className="grid grid-cols-3 gap-2 pt-2 border-t border-white/5">
                            <button
                              type="button"
                              onPointerDown={(e) => { e.stopPropagation(); confirmPlayVideo(song, 'history', e) }}
                              onClick={(e) => { e.stopPropagation(); confirmPlayVideo(song, 'history', e) }}
                              className="py-2 bg-[#10B981]/5 hover:bg-[#10B981]/15 text-[#10B981] border border-[#10B981]/25 rounded-xl flex items-center justify-center gap-1.5 active:scale-95 transition-all text-[9px] font-black uppercase tracking-wider"
                            >
                              <FontAwesomeIcon icon={faPlay} className="text-[8px]" />
                              <span>Play</span>
                            </button>

                            <button
                              type="button"
                              onPointerDown={(e) => { e.stopPropagation(); confirmAddToSelected(song, e) }}
                              onClick={(e) => { e.stopPropagation(); confirmAddToSelected(song, e) }}
                              className="py-2 bg-white/5 hover:bg-white/10 text-white border border-white/10 rounded-xl flex items-center justify-center gap-1.5 active:scale-95 transition-all text-[9px] font-black uppercase tracking-wider"
                            >
                              <FontAwesomeIcon icon={faPlus} className="text-[8px]" />
                              <span>Add</span>
                            </button>

                            <button
                              type="button"
                              onPointerDown={(e) => {
                                e.stopPropagation()
                                handleCommand('priority', { videoId: song.id, video: song }, e)
                                const toastId = Date.now()
                                setToasts([{ id: toastId, message: `Phát tiếp theo: ${song.title?.slice(0, 18)}...` }])
                                setTimeout(() => setToasts(prev => prev.filter(t => t.id !== toastId)), 3000)
                              }}
                              onClick={(e) => {
                                e.stopPropagation()
                                handleCommand('priority', { videoId: song.id, video: song }, e)
                                const toastId = Date.now()
                                setToasts([{ id: toastId, message: `Phát tiếp theo: ${song.title?.slice(0, 18)}...` }])
                                setTimeout(() => setToasts(prev => prev.filter(t => t.id !== toastId)), 3000)
                              }}
                              className="py-2 bg-[#F59E0B]/5 hover:bg-[#F59E0B]/15 text-[#F59E0B] border border-[#F59E0B]/25 rounded-xl flex items-center justify-center gap-1.5 active:scale-95 transition-all text-[9px] font-black uppercase tracking-wider"
                            >
                              <FontAwesomeIcon icon={faArrowUp} className="text-[8px]" />
                              <span>Play next</span>
                            </button>
                          </div>
                        )}
                      </div>
                    ))}
                  </div>
                ) : (
                  <div className="text-center py-6 text-slate-500 text-[8px] font-black uppercase tracking-wider border border-white/5 border-dashed rounded-xl">
                    Chưa có bài hát nào trong lịch sử phát.
                  </div>
                )}
              </div>
            </div>
          )}
        </div>
      </div>
    )
  }

  const renderSelectedContent = () => {
    const isMobile = window.innerWidth < 1024;

    if (isMobile) {
      return (
        <div className="space-y-4 pb-44 px-4 pt-3">
          {/* Header row */}
          <div className="flex items-center justify-between pb-3 border-b border-white/5">
            <button
              onClick={handleImportPlaylistPrompt}
              className="px-2.5 py-1 bg-white/5 hover:bg-white/10 text-[9px] font-black uppercase tracking-widest border border-white/5 rounded-lg transition-all"
            >
              Import playlist
            </button>

            {filteredPlaylist.length > 0 && (
              <div className="flex gap-1.5">
                <button
                  onClick={() => {
                    const shuffled = [...playlist].sort(() => Math.random() - 0.5)
                    setPlaylist(shuffled)
                  }}
                  className="px-2.5 py-1 rounded-lg bg-white/5 hover:bg-white/10 text-[9px] font-black uppercase tracking-widest transition-all active:scale-95"
                >
                  Xáo trộn
                </button>
                <button
                  onClick={() => handleCommand('clearAll', {})}
                  className="px-2.5 py-1 rounded-lg bg-[#EF4444]/10 hover:bg-[#EF4444]/20 text-[#EF4444] text-[9px] font-black uppercase tracking-widest transition-all active:scale-95"
                >
                  Xóa hết
                </button>
              </div>
            )}
          </div>

          {filteredPlaylist.length === 0 ? (
            /* Giao diện vkara-style Queue Empty (Hình 2) */
            <div className="flex flex-col gap-6 py-4">
              {/* Queue empty box */}
              <div className="bg-[#0B1224] border border-white/5 rounded-2xl p-6 text-center flex flex-col items-center gap-4 shadow-xl">
                <div className="w-12 h-12 bg-white/5 rounded-2xl flex items-center justify-center text-slate-400">
                  <FontAwesomeIcon icon={faListCheck} className="text-lg" />
                </div>
                <div className="space-y-1.5">
                  <h3 className="text-xs font-black text-white uppercase tracking-widest">Queue is empty</h3>
                  <p className="text-[9px] font-bold text-slate-500 uppercase tracking-wide leading-relaxed">
                    Search for a song, then choose Add last or Play next. You can also import a YouTube playlist from the menu above.
                  </p>
                </div>
                <button
                  type="button"
                  onClick={() => setActiveTab('search')}
                  className="w-full bg-white text-black text-xs font-black uppercase tracking-wider py-3 rounded-xl hover:bg-slate-200 transition-all active:scale-98 shadow-md"
                >
                  Search songs
                </button>
              </div>

              {/* Nhạc Hot Xu Hướng (Queue Mobile) */}
              <div className="space-y-2.5">
                <span className="text-[9px] font-black text-[#8B5CF6] uppercase tracking-wider block text-left">Lịch sử đã hát 🕒</span>
                
                {watchHistory.length > 0 ? (
                  <div className="space-y-3 animate-fadeIn">
                    {watchHistory.slice(0, 7).map((song) => (
                      <div
                        key={song.id}
                        onClick={() => setExpandedResultId(prev => prev === song.id ? null : song.id)}
                        className="bg-[#0B1224]/40 border border-white/5 rounded-2xl p-3 space-y-3 hover:border-[#4F7CFF]/20 transition-all relative cursor-pointer"
                      >
                        {/* Top Row: Thumbnail & Info side-by-side */}
                        <div className="flex gap-3">
                          {/* Thumbnail left */}
                          <div className="relative w-28 aspect-video rounded-xl overflow-hidden bg-slate-950 shrink-0 border border-white/5">
                            <img
                              src={song.thumbnail}
                              className="w-full h-full object-cover"
                              alt=""
                            />
                            {song.duration && (
                              <div className="absolute bottom-1 right-1 px-1 py-0.5 bg-black/85 backdrop-blur-md text-[8px] text-white font-mono rounded border border-white/5">
                                {song.duration}
                              </div>
                            )}
                          </div>

                          {/* Text details right */}
                          <div className="flex-1 min-w-0 flex flex-col justify-between py-0.5">
                            <h3 className="text-[11px] font-black text-white line-clamp-2 leading-tight uppercase tracking-tight text-left">
                              {song.title}
                            </h3>
                            <div className="space-y-0.5 mt-1">
                              <div className="flex items-center gap-1 text-[8px] font-black text-slate-400 uppercase tracking-wider text-left">
                                <span className="truncate max-w-[120px]">{song.channelTitle}</span>
                                <FontAwesomeIcon icon={faCheckCircle} className="text-[#4F7CFF] text-[8px] shrink-0" />
                              </div>
                              <span className="text-[8px] font-bold text-slate-500 uppercase tracking-wider block text-left">
                                LỊCH SỬ ĐÃ PHÁT
                              </span>
                            </div>
                          </div>
                        </div>

                        {/* Bottom Row: Triple Actions (Play, Add, Play next) */}
                        {expandedResultId === song.id && (
                          <div className="grid grid-cols-3 gap-2 pt-2 border-t border-white/5">
                            <button
                              type="button"
                              onPointerDown={(e) => { e.stopPropagation(); confirmPlayVideo(song, 'history', e) }}
                              onClick={(e) => { e.stopPropagation(); confirmPlayVideo(song, 'history', e) }}
                              className="py-2 bg-[#10B981]/5 hover:bg-[#10B981]/15 text-[#10B981] border border-[#10B981]/25 rounded-xl flex items-center justify-center gap-1.5 active:scale-95 transition-all text-[9px] font-black uppercase tracking-wider"
                            >
                              <FontAwesomeIcon icon={faPlay} className="text-[8px]" />
                              <span>Play</span>
                            </button>

                            <button
                              type="button"
                              onPointerDown={(e) => { e.stopPropagation(); confirmAddToSelected(song, e) }}
                              onClick={(e) => { e.stopPropagation(); confirmAddToSelected(song, e) }}
                              className="py-2 bg-white/5 hover:bg-white/10 text-white border border-white/10 rounded-xl flex items-center justify-center gap-1.5 active:scale-95 transition-all text-[9px] font-black uppercase tracking-wider"
                            >
                              <FontAwesomeIcon icon={faPlus} className="text-[8px]" />
                              <span>Add</span>
                            </button>

                            <button
                              type="button"
                              onPointerDown={(e) => {
                                e.stopPropagation()
                                handleCommand('priority', { videoId: song.id, video: song }, e)
                                const toastId = Date.now()
                                setToasts([{ id: toastId, message: `Phát tiếp theo: ${song.title?.slice(0, 18)}...` }])
                                setTimeout(() => setToasts(prev => prev.filter(t => t.id !== toastId)), 3000)
                              }}
                              onClick={(e) => {
                                e.stopPropagation()
                                handleCommand('priority', { videoId: song.id, video: song }, e)
                                const toastId = Date.now()
                                setToasts([{ id: toastId, message: `Phát tiếp theo: ${song.title?.slice(0, 18)}...` }])
                                setTimeout(() => setToasts(prev => prev.filter(t => t.id !== toastId)), 3000)
                              }}
                              className="py-2 bg-[#F59E0B]/5 hover:bg-[#F59E0B]/15 text-[#F59E0B] border border-[#F59E0B]/25 rounded-xl flex items-center justify-center gap-1.5 active:scale-95 transition-all text-[9px] font-black uppercase tracking-wider"
                            >
                              <FontAwesomeIcon icon={faArrowUp} className="text-[8px]" />
                              <span>Play next</span>
                            </button>
                          </div>
                        )}
                      </div>
                    ))}
                  </div>
                ) : (
                  <div className="text-center py-6 text-slate-500 text-[8px] font-black uppercase tracking-wider border border-white/5 border-dashed rounded-xl">
                    Chưa có bài hát nào trong lịch sử phát.
                  </div>
                )}
              </div>
            </div>
          ) : (
            /* Danh sách Queue - Giống search layout */
            <div className="space-y-3">
              {filteredPlaylist.map((video, idx) => (
                <div
                  key={`${video.id}-${idx}`}
                  onClick={() => setExpandedResultId(prev => prev === video.id ? null : video.id)}
                  className="bg-[#0B1224]/40 border border-white/5 rounded-2xl p-3 space-y-3 hover:border-[#4F7CFF]/20 transition-all relative cursor-pointer"
                >
                  {/* Top Row: Thumbnail & Info side-by-side */}
                  <div className="flex gap-3">
                    {/* Thumbnail left */}
                    <div className="relative w-28 aspect-video rounded-xl overflow-hidden bg-slate-950 shrink-0 border border-white/5">
                      <img
                        src={video.source === 'youtube' ? `https://i.ytimg.com/vi/${video.id}/mqdefault.jpg` : video.thumbnail}
                        className="w-full h-full object-cover"
                        alt=""
                        onError={(e) => {
                          if (video.source === 'youtube' && !e.target.src.includes('mqdefault')) {
                            e.target.src = `https://i.ytimg.com/vi/${video.id}/mqdefault.jpg`
                          }
                        }}
                      />
                      {video.duration && (
                        <div className="absolute bottom-1 right-1 px-1 py-0.5 bg-black/85 backdrop-blur-md text-[8px] text-white font-mono rounded border border-white/5">
                          {video.source === 'soundcloud' ? formatSoundCloudDuration(video.duration) : formatYouTubeDuration(video.duration)}
                        </div>
                      )}
                    </div>

                    {/* Text details right */}
                    <div className="flex-1 min-w-0 flex flex-col justify-between py-0.5">
                      <h3 className="text-[11px] font-black text-white line-clamp-2 leading-tight uppercase tracking-tight">
                        {decodeHtmlEntities(video.title)}
                      </h3>
                      <div className="space-y-0.5 mt-1">
                        <div className="flex items-center gap-1 text-[8px] font-black text-slate-400 uppercase tracking-wider">
                          <span className="truncate max-w-[120px]">{decodeHtmlEntities(video.channelTitle)}</span>
                          <FontAwesomeIcon icon={faCheckCircle} className="text-[#4F7CFF] text-[8px] shrink-0" />
                        </div>
                        <span className="text-[8px] font-bold text-slate-500 uppercase tracking-wider block">
                          {video.viewCount ? `${formatViewCount(video.viewCount)} views` : 'SOUNDCLOUD'}
                        </span>
                      </div>
                    </div>
                  </div>

                  {/* Bottom Row: Triple Actions buttons layout - shown when card expanded */}
                  {expandedResultId === video.id && (
                    <div className="grid grid-cols-3 gap-2 pt-2 border-t border-white/5">
                      <button
                        onClick={(e) => { e.stopPropagation(); confirmPlayVideo(video, 'selected', e) }}
                        className="py-2 bg-[#10B981]/5 hover:bg-[#10B981]/15 text-[#10B981] border border-[#10B981]/25 rounded-xl flex items-center justify-center gap-1.5 active:scale-95 transition-all text-[9px] font-black uppercase tracking-wider"
                      >
                        <FontAwesomeIcon icon={faPlay} className="text-[8px]" />
                        <span>Play</span>
                      </button>

                      <button
                        onClick={(e) => {
                          e.stopPropagation()
                          handleCommand('priority', { videoId: video.id }, e)
                        }}
                        className="py-2 bg-[#4F7CFF]/5 hover:bg-[#4F7CFF]/15 text-[#4F7CFF] border border-[#4F7CFF]/25 rounded-xl flex items-center justify-center gap-1.5 active:scale-95 transition-all text-[9px] font-black uppercase tracking-wider"
                      >
                        <FontAwesomeIcon icon={faArrowUp} className="text-[8px]" />
                        <span>Priority</span>
                      </button>

                      <button
                        onClick={(e) => {
                          e.stopPropagation()
                          handleCommand('removeFromSelected', { videoId: video.id }, e)
                        }}
                        className="py-2 bg-[#EF4444]/5 hover:bg-[#EF4444]/15 text-[#EF4444] border border-[#EF4444]/25 rounded-xl flex items-center justify-center gap-1.5 active:scale-95 transition-all text-[9px] font-black uppercase tracking-wider"
                      >
                        <FontAwesomeIcon icon={faTrash} className="text-[8px]" />
                        <span>Remove</span>
                      </button>
                    </div>
                  )}
                </div>
              ))}
            </div>
          )}
        </div>
      )
    }

    return (
      <div className="space-y-4 pb-16">
        <div className="flex items-center justify-between pb-3 border-b border-white/5">
          <h2 className="text-sm font-black text-white flex items-center gap-2">
            <FontAwesomeIcon icon={faListCheck} className="text-[#4F7CFF]" />
            <span>Danh sách chờ ({filteredPlaylist.length})</span>
          </h2>
          <div className="flex gap-1.5">
            <button
              onClick={handleImportPlaylistPrompt}
              className="px-2.5 py-1 bg-white/5 hover:bg-white/10 text-[9px] font-black uppercase tracking-widest border border-white/5 rounded-lg transition-all"
            >
              Import playlist
            </button>
            <button
              onClick={() => {
                const shuffled = [...playlist].sort(() => Math.random() - 0.5)
                setPlaylist(shuffled)
              }}
              className="px-2.5 py-1 rounded-lg bg-white/5 hover:bg-white/10 text-[9px] font-black uppercase tracking-widest transition-all active:scale-95"
            >
              Xáo trộn
            </button>
            <button
              onClick={() => handleCommand('clearAll', {})}
              className="px-2.5 py-1 rounded-lg bg-[#EF4444]/10 hover:bg-[#EF4444]/20 text-[#EF4444] text-[9px] font-black uppercase tracking-widest transition-all active:scale-95"
            >
              Xóa hết
            </button>
          </div>
        </div>

        {filteredPlaylist.length === 0 ? (
          <div className="py-16 text-center flex flex-col items-center gap-3 opacity-20">
            <FontAwesomeIcon icon={faListCheck} className="text-4xl" />
            <p className="font-black text-sm uppercase tracking-[0.15em]">Hàng chờ trống</p>
          </div>
        ) : (
          <div className="space-y-2">
            {filteredPlaylist.map((video, idx) => (
              <div key={`${video.id}-${idx}`} className="group flex gap-3 p-2 bg-[#0B1224] border border-white/5 rounded-xl hover:border-white/10 transition-all items-center">
                <div className="w-5 text-center text-xs font-black text-slate-600 shrink-0 font-mono">
                  {idx + 1}
                </div>
                <div className="relative w-16 aspect-video rounded-lg overflow-hidden bg-slate-950 shrink-0">
                  <img
                    src={video.source === 'youtube' ? `https://i.ytimg.com/vi/${video.id}/default.jpg` : video.thumbnail}
                    className="w-full h-full object-cover"
                    alt=""
                    onError={(e) => {
                      if (video.source === 'youtube' && !e.target.src.includes('mqdefault')) {
                        e.target.src = `https://i.ytimg.com/vi/${video.id}/default.jpg`
                      }
                    }}
                  />
                </div>
                <div className="flex-1 min-w-0">
                  <h3 className="text-xs font-black text-white truncate uppercase tracking-tight">{decodeHtmlEntities(video.title)}</h3>
                  <p className="text-[9px] font-bold text-slate-500 uppercase tracking-wider truncate mt-0.5">{decodeHtmlEntities(video.channelTitle)}</p>
                </div>
                <div className="flex items-center gap-1 shrink-0">
                  <button
                    onPointerDown={(e) => confirmPlayVideo(video, 'selected', e)}
                    className="w-8 h-8 flex items-center justify-center bg-[#EF4444]/10 hover:bg-[#EF4444]/20 text-[#EF4444] border border-white/5 rounded-lg active:scale-90 transition-all"
                    title="Phát ngay"
                  >
                    <FontAwesomeIcon icon={faBolt} className="text-xs" />
                  </button>
                  <button
                    onPointerDown={(e) => handleCommand('priority', { videoId: video.id }, e)}
                    className="w-8 h-8 flex items-center justify-center bg-white/5 hover:bg-[#4F7CFF]/20 text-[#4F7CFF] border border-white/5 rounded-lg active:scale-90 transition-all"
                    title="Ưu tiên"
                  >
                    <FontAwesomeIcon icon={faArrowUp} className="text-xs" />
                  </button>
                  <button
                    onPointerDown={(e) => handleCommand('removeFromSelected', { videoId: video.id }, e)}
                    className="w-8 h-8 flex items-center justify-center bg-white/5 hover:bg-red-600/10 text-slate-500 hover:text-[#EF4444] border border-white/5 rounded-lg active:scale-90 transition-all"
                    title="Xóa"
                  >
                    <FontAwesomeIcon icon={faTrash} className="text-xs" />
                  </button>
                </div>
              </div>
            ))}
          </div>
        )}
      </div>
    )
  }

  const renderHistoryContent = (isSubPanel = false) => {
    const itemsToShow = isSubPanel ? watchHistory.slice(0, 7) : watchHistory

    return (
      <div className={`space-y-4 ${isSubPanel ? '' : 'pb-16'}`}>
        {!isSubPanel && (
          <div className="flex items-center justify-between pb-3 border-b border-white/5">
            <h2 className="text-sm font-black text-white flex items-center gap-2">
              <FontAwesomeIcon icon={faClock} className="text-[#8B5CF6]" />
              <span>Lịch sử phát ({watchHistory.length})</span>
            </h2>
          </div>
        )}

        {itemsToShow.length === 0 ? (
          <div className="py-16 text-center flex flex-col items-center gap-3 opacity-20">
            <FontAwesomeIcon icon={faClock} className="text-4xl" />
            <p className="font-black text-sm uppercase tracking-[0.15em]">Lịch sử trống</p>
          </div>
        ) : (
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-2">
            {itemsToShow.map((video, idx) => (
              <div key={`${video.id}-${video.timestamp || idx}`} className="group flex gap-3 p-2 bg-[#0B1224] border border-white/5 rounded-xl hover:border-white/10 transition-all items-center">
                <div
                  onPointerDown={(e) => { e.stopPropagation(); confirmPlayVideo(video, 'history', e) }}
                  onClick={(e) => { e.stopPropagation(); confirmPlayVideo(video, 'history', e) }}
                  className="relative w-16 aspect-video rounded-lg overflow-hidden bg-slate-950 shrink-0 cursor-pointer"
                >
                  <img
                    src={video.source === 'youtube' ? `https://i.ytimg.com/vi/${video.id}/default.jpg` : video.thumbnail}
                    className="w-full h-full object-cover"
                    alt=""
                    onError={(e) => {
                      if (video.source === 'youtube' && !e.target.src.includes('mqdefault')) {
                        e.target.src = `https://i.ytimg.com/vi/${video.id}/default.jpg`
                      }
                    }}
                  />
                  <div className="absolute inset-0 bg-black/40 opacity-0 group-hover:opacity-100 flex items-center justify-center transition-opacity">
                    <FontAwesomeIcon icon={faPlay} className="text-white text-xs" />
                  </div>
                </div>
                <div className="flex-1 min-w-0">
                  <h3 className="text-xs font-black text-white truncate uppercase tracking-tight">{decodeHtmlEntities(video.title)}</h3>
                  <p className="text-[9px] font-bold text-slate-500 uppercase tracking-wider truncate mt-0.5">{decodeHtmlEntities(video.channelTitle)}</p>
                </div>
                <div className="flex items-center gap-1 shrink-0">
                  <button
                    type="button"
                    onPointerDown={(e) => { e.stopPropagation(); confirmPlayVideo(video, 'history', e) }}
                    onClick={(e) => { e.stopPropagation(); confirmPlayVideo(video, 'history', e) }}
                    className="w-7 h-7 flex items-center justify-center bg-[#10B981]/10 hover:bg-[#10B981]/25 text-[#10B981] border border-white/5 rounded-lg active:scale-90 transition-all"
                    title="Phát ngay"
                  >
                    <FontAwesomeIcon icon={faPlay} className="text-[10px]" />
                  </button>
                  <button
                    type="button"
                    onPointerDown={(e) => {
                      e.stopPropagation()
                      handleCommand('priority', { videoId: video.id, video: video }, e)
                      const toastId = Date.now()
                      setToasts([{ id: toastId, message: `Phát tiếp theo: ${video.title?.slice(0, 18)}...` }])
                      setTimeout(() => setToasts(prev => prev.filter(t => t.id !== toastId)), 3000)
                    }}
                    onClick={(e) => {
                      e.stopPropagation()
                      handleCommand('priority', { videoId: video.id, video: video }, e)
                      const toastId = Date.now()
                      setToasts([{ id: toastId, message: `Phát tiếp theo: ${video.title?.slice(0, 18)}...` }])
                      setTimeout(() => setToasts(prev => prev.filter(t => t.id !== toastId)), 3000)
                    }}
                    className="w-7 h-7 flex items-center justify-center bg-[#F59E0B]/10 hover:bg-[#F59E0B]/25 text-[#F59E0B] border border-white/5 rounded-lg active:scale-90 transition-all"
                    title="Phát tiếp theo"
                  >
                    <FontAwesomeIcon icon={faArrowUp} className="text-[10px]" />
                  </button>
                  <button
                    type="button"
                    onPointerDown={(e) => { e.stopPropagation(); confirmAddToSelected(video, e) }}
                    onClick={(e) => { e.stopPropagation(); confirmAddToSelected(video, e) }}
                    className="w-7 h-7 flex items-center justify-center bg-white/5 hover:bg-[#4F7CFF]/20 text-[#4F7CFF] border border-white/5 rounded-lg active:scale-90 transition-all"
                    title="Thêm hàng chờ"
                  >
                    <FontAwesomeIcon icon={faPlus} className="text-[10px]" />
                  </button>
                </div>
              </div>
            ))}
          </div>
        )}
      </div>
    )
  }

  const renderSoundEffectsContent = (isSubPanel = false) => {
    const soundEffects = remoteState?.soundEffects || {}
    const effectsFromConfig = Object.entries(soundEffects).map(([id, item], index) => ({
      id,
      ...buildEffectUiConfig(id, typeof item === 'object' ? item : {}, index)
    }))
    const selectedFxId = optimisticSelectedFxId || remoteState?.selectedFx?.id || null
    const selectedFxConfig = selectedFxId ? buildEffectUiConfig(selectedFxId, soundEffects[selectedFxId]) : null
    const activeFxState = optimisticActiveFx || remoteState?.activeFx || null

    return (
      <div className={`space-y-4 ${isSubPanel ? '' : 'pb-16'}`}>
        {!isSubPanel && (
          <div className="flex items-center justify-between pb-3 border-b border-white/5">
            <h2 className="text-sm font-black text-white flex items-center gap-2">
              <FontAwesomeIcon icon={faMagic} className="text-[#8B5CF6]" />
              <span>Hiệu ứng âm thanh ({effectsFromConfig.length})</span>
            </h2>
          </div>
        )}

        <div className="grid grid-cols-2 gap-2.5">
          {effectsFromConfig.map((fx) => {
            const item = (remoteState?.soundEffects || {})[fx.id]
            const currentVol = typeof item === 'object' ? item.volume : 80

            return (
              <div
                key={fx.id}
                onPointerDown={() => {
                  handleCommand('selectEffect', { id: fx.id })
                  setFxVolume(currentVol)
                }}
                className={`group flex items-center gap-3 p-3 bg-[#0B1224] border rounded-xl active:scale-98 transition-all cursor-pointer ${selectedFxId === fx.id ? 'border-[#8B5CF6]/50 shadow-lg shadow-[#8B5CF6]/5' : 'border-white/5 hover:border-white/10'
                  }`}
              >
                <div className={`w-10 h-10 rounded-lg flex items-center justify-center shrink-0 text-white ${fx.color}`}>
                  <FontAwesomeIcon icon={fx.icon} className="text-sm" />
                </div>
                <div className="min-w-0 flex-1">
                  <span className="text-[10px] font-black uppercase tracking-wider block text-white truncate">{fx.label}</span>
                  {remoteState?.fxDurations?.[fx.id] && (
                    <span className="text-[8px] font-bold text-slate-500 block mt-0.5">
                      {formatClockTime(remoteState.fxDurations[fx.id])}
                    </span>
                  )}
                </div>
              </div>
            )
          })}
        </div>

        {/* Selected Effect persistent card */}
        {selectedFxId && selectedFxConfig && (
          <div className="p-4 bg-[#0B1224] border border-white/5 rounded-2xl space-y-4 shadow-xl">
            <div className="flex items-center gap-3">
              <div className={`w-10 h-10 rounded-lg flex items-center justify-center text-white shrink-0 ${selectedFxConfig.color}`}>
                <FontAwesomeIcon icon={selectedFxConfig.icon} />
              </div>
              <div className="min-w-0 flex-1">
                <h4 className="text-xs font-black text-white uppercase tracking-widest truncate">{selectedFxConfig.label}</h4>
                <p className="text-[8px] font-bold text-slate-500 uppercase tracking-wider mt-0.5">
                  {activeFxState?.id === selectedFxId ? 'Đang chạy' : 'Sẵn sàng'}
                </p>
              </div>
              {activeFxState?.id === selectedFxId && (
                <button
                  onPointerDown={() => handleCommand('controlEffect', { action: 'stop' })}
                  className="w-8 h-8 bg-[#EF4444]/15 border border-[#EF4444]/25 text-[#EF4444] rounded-lg flex items-center justify-center active:scale-90 transition-all"
                  title="Dừng phát"
                >
                  <FontAwesomeIcon icon={faStop} className="text-xs" />
                </button>
              )}
            </div>

            {/* Volume FX slider */}
            <div className="space-y-1.5 p-3 bg-black/20 rounded-xl border border-white/5">
              <div className="flex justify-between items-center">
                <span className="text-[9px] font-black text-slate-500 uppercase tracking-wider">Âm lượng FX</span>
                <span className="text-[9px] font-black text-[#8B5CF6] font-mono">{fxVolume}%</span>
              </div>
              <input
                type="range"
                min="0"
                max="100"
                value={fxVolume}
                onChange={(e) => {
                  setFxVolume(parseInt(e.target.value))
                  lastVolCommandRef.current = Date.now()
                }}
                onPointerUp={(e) => {
                  const val = parseInt(e.target.value)
                  lastVolCommandRef.current = Date.now()
                  handleCommand('updateSoundEffectVolume', { id: selectedFxId, volume: val })
                }}
                className="spotify-slider spotify-slider-purple w-full cursor-pointer"
              />
            </div>

            {/* Progress FX slider if active */}
            {activeFxState?.id === selectedFxId && (
              <div className="space-y-1.5 px-1">
                <div className="flex justify-between items-center text-[8px] font-bold text-slate-500 font-mono">
                  <span>{formatClockTime(activeFxState.progress)}</span>
                  <span>{formatClockTime(activeFxState.maxTime)}</span>
                </div>
                <input
                  type="range"
                  min="0"
                  max={activeFxState.maxTime || 100}
                  step="0.1"
                  value={activeFxState.progress || 0}
                  onChange={(e) => {
                    handleCommand('controlEffect', { action: 'seek', value: parseFloat(e.target.value) })
                  }}
                  className="spotify-slider spotify-slider-purple w-full cursor-pointer"
                />
              </div>
            )}

            {/* Play FX button */}
            <button
              onPointerDown={() => handleCommand(
                activeFxState?.id === selectedFxId ? 'controlEffect' : 'playSoundEffect',
                activeFxState?.id === selectedFxId ? { action: 'toggle' } : { id: selectedFxId }
              )}
              className={`w-full py-3 rounded-xl flex items-center justify-center gap-2 text-white font-black text-xs uppercase tracking-widest shadow-md transition-all active:scale-98 ${activeFxState?.id === selectedFxId && activeFxState.isPlaying
                ? 'bg-gradient-to-r from-[#EF4444] to-orange-500 shadow-red-950/20'
                : 'bg-gradient-to-r from-[#8B5CF6] to-[#4F7CFF] shadow-[#8B5CF6]/10'
                }`}
            >
              <FontAwesomeIcon icon={activeFxState?.id === selectedFxId && activeFxState.isPlaying ? faPause : faPlay} className="text-xs" />
              <span>{activeFxState?.id === selectedFxId && activeFxState.isPlaying ? 'Tạm dừng FX' : 'Phát FX'}</span>
            </button>
          </div>
        )}
      </div>
    )
  }

  const renderSettingsContent = (isSubPanel = false) => (
    <div className={`space-y-4 ${isSubPanel ? '' : 'pb-16'}`}>
      {!isSubPanel && (
        <div className="flex items-center justify-between pb-3 border-b border-white/5">
          <h2 className="text-sm font-black text-white flex items-center gap-2">
            <FontAwesomeIcon icon={faCog} className="text-slate-400" />
            <span>{isOperatorDesk ? 'Cài đặt operator' : 'Cài đặt hệ thống'}</span>
          </h2>
        </div>
      )}

      <div className="space-y-3">
        {/* Token Info — operator: không khuyến khích share */}
        <div className="bg-[#0B1224] border border-white/5 p-4 rounded-2xl flex items-center justify-between gap-3">
          <div>
            <span className="text-[10px] font-black text-slate-500 uppercase tracking-widest block">
              {isOperatorDesk ? 'Session (operator)' : 'Mã phiên (Token)'}
            </span>
            <span className="text-[8px] font-bold text-slate-600 block mt-0.5">
              {isOperatorDesk ? 'Chỉ bạn · không share khách' : 'Dùng để kết nối thêm tay điều khiển'}
            </span>
          </div>
          <span className="px-3 py-1.5 bg-[#4F7CFF]/15 text-[#4F7CFF] text-xs font-mono font-bold rounded-lg border border-[#4F7CFF]/20 select-text">
            {sessionToken}
          </span>
        </div>
        {isOperatorDesk && (
          <div className="bg-amber-500/10 border border-amber-500/25 rounded-xl px-3 py-2">
            <p className="text-[10px] font-black text-amber-300 uppercase tracking-wider">Không share link · chỉ session operator</p>
            <p className="text-[9px] text-amber-200/70 mt-0.5">FOH desk — chỉ operator điều khiển</p>
          </div>
        )}

        {/* System toggles */}
        <div className="bg-[#0B1224] border border-white/5 p-4 rounded-2xl space-y-4">
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-3">
              <div className={`w-8 h-8 rounded-lg flex items-center justify-center shrink-0 ${isKaraokeMode ? 'bg-[#10B981]/15 text-[#10B981]' : 'bg-white/5 text-slate-500'}`}>
                <FontAwesomeIcon icon={faMicrophone} className="text-xs" />
              </div>
              <div>
                <span className="text-[10px] font-black text-white uppercase tracking-wider block">Chế độ Karaoke</span>
                <span className="text-[8px] font-bold text-slate-500 block mt-0.5">Luôn tìm bài Karaoke trước</span>
              </div>
            </div>
            <button
              onClick={toggleKaraoke}
              className={`w-10 h-5.5 rounded-full transition-all relative ${isKaraokeMode ? 'bg-[#10B981]' : 'bg-slate-800'}`}
            >
              <div className={`absolute top-0.75 left-0.75 w-4 h-4 bg-white rounded-full transition-all ${isKaraokeMode ? 'translate-x-4.5' : ''}`} />
            </button>
          </div>

          <div className="flex items-center justify-between pt-4 border-t border-white/5">
            <div className="flex items-center gap-3">
              <div className={`w-8 h-8 rounded-lg flex items-center justify-center shrink-0 ${autoPlayNext ? 'bg-[#8B5CF6]/15 text-[#8B5CF6]' : 'bg-white/5 text-slate-500'}`}>
                <FontAwesomeIcon icon={faShuffle} className="text-xs" />
              </div>
              <div>
                <span className="text-[10px] font-black text-white uppercase tracking-wider block">Tự động chuyển</span>
                <span className="text-[8px] font-bold text-slate-500 block mt-0.5">Tự động phát bài tiếp theo</span>
              </div>
            </div>
            <button
              onClick={toggleAutoPlay}
              className={`w-10 h-5.5 rounded-full transition-all relative ${autoPlayNext ? 'bg-[#8B5CF6]' : 'bg-slate-800'}`}
            >
              <div className={`absolute top-0.75 left-0.75 w-4 h-4 bg-white rounded-full transition-all ${autoPlayNext ? 'translate-x-4.5' : ''}`} />
            </button>
          </div>

          <div className="flex items-center justify-between pt-4 border-t border-white/5">
            <div className="flex items-center gap-3">
              <div className={`w-8 h-8 rounded-lg flex items-center justify-center shrink-0 ${isTrackChangeLocked ? 'bg-amber-500/15 text-amber-400' : 'bg-white/5 text-slate-500'}`}>
                <FontAwesomeIcon icon={isTrackChangeLocked ? faLock : faLockOpen} className="text-xs" />
              </div>
              <div>
                <span className="text-[10px] font-black text-white uppercase tracking-wider block">Khóa chuyển bài</span>
                <span className="text-[8px] font-bold text-slate-500 block mt-0.5">Chặn next / phát bài khác / auto-next</span>
              </div>
            </div>
            <button
              onClick={toggleTrackLock}
              className={`w-10 h-5.5 rounded-full transition-all relative ${isTrackChangeLocked ? 'bg-amber-500' : 'bg-slate-800'}`}
            >
              <div className={`absolute top-0.75 left-0.75 w-4 h-4 bg-white rounded-full transition-all ${isTrackChangeLocked ? 'translate-x-4.5' : ''}`} />
            </button>
          </div>
        </div>

        {/* Logout — ẩn khi operator desk (ShowCue giữ session) */}
        {!isOperatorDesk && (
          <div className="bg-[#EF4444]/5 border border-[#EF4444]/15 p-4 rounded-2xl space-y-3">
            <span className="text-[9px] font-black text-[#EF4444] uppercase tracking-wider block">Vùng nguy hiểm</span>
            <button
              onClick={() => {
                if (window.confirm('Bạn có chắc muốn ngắt kết nối với thiết bị chính?')) {
                  setSearchParams({})
                  navigate('/')
                }
              }}
              className="w-full py-2.5 bg-[#EF4444]/10 hover:bg-[#EF4444]/20 border border-[#EF4444]/25 text-[#EF4444] rounded-xl text-[9px] font-black uppercase tracking-widest transition-all active:scale-[0.98]"
            >
              Đăng xuất / Ngắt kết nối
            </button>
          </div>
        )}
      </div>
    </div>
  )

  const renderControlsTab = () => {
    const isPlaying = remoteState?.isPlaying
    const hasCurrent = !!currentVideo

    return (
      <div className="flex-1 flex flex-col justify-between items-center px-6 py-6 pb-24 text-center max-w-md mx-auto w-full h-full min-h-0 overflow-y-auto custom-scrollbar">
        <div className="w-full text-center mb-4 shrink-0">
          <span className="text-[10px] font-black text-slate-500 uppercase tracking-widest">Đang phát</span>
        </div>

        <div className="w-full flex justify-center my-auto shrink-0">
          <div className="w-64 h-64 sm:w-72 sm:h-72 mobile-controls-art aspect-square rounded-3xl overflow-hidden bg-slate-950/80 relative shadow-[0_20px_50px_rgba(0,0,0,0.5)] border border-white/5 flex items-center justify-center">
            {hasCurrent ? (
              <img
                src={currentVideo.source === 'youtube' ? `https://i.ytimg.com/vi/${currentVideo.id}/hqdefault.jpg` : currentVideo.thumbnail}
                className="w-full h-full object-cover"
                alt=""
                onError={(e) => {
                  e.target.src = currentVideo.source === 'youtube'
                    ? `https://i.ytimg.com/vi/${currentVideo.id}/mqdefault.jpg`
                    : currentVideo.thumbnail
                }}
              />
            ) : (
              <div className="w-full h-full flex flex-col items-center justify-center text-slate-700 bg-gradient-to-b from-slate-900 to-slate-950">
                <FontAwesomeIcon icon={faMusic} className="text-4xl text-slate-800 animate-pulse" />
                <span className="text-[9px] font-black text-slate-600 uppercase tracking-widest mt-4">Không có bài hát</span>
              </div>
            )}
          </div>
        </div>

        <div className="w-full my-4 mobile-controls-spacing shrink-0 space-y-1">
          <h2 className="text-sm font-black text-white uppercase tracking-tight line-clamp-2 px-4 leading-snug mobile-controls-title">
            {hasCurrent ? decodeHtmlEntities(currentVideo.title) : 'KTV APP'}
          </h2>
          <div className="flex items-center justify-center gap-1.5 text-[10px] font-bold text-slate-400 uppercase tracking-wider">
            <span>{hasCurrent ? decodeHtmlEntities(currentVideo.channelTitle) : 'Sẵn sàng kết nối'}</span>
            {hasCurrent && (
              <FontAwesomeIcon icon={faCheckCircle} className="text-[#4F7CFF] text-[10px]" />
            )}
          </div>
        </div>

        <div className="w-full shrink-0 px-2 space-y-1.5 my-2 mobile-controls-spacing">
          <input
            type="range"
            min="0"
            max={duration || 100}
            value={currentTime}
            onChange={(e) => {
              isSeekingRef.current = true
              setCurrentTime(parseFloat(e.target.value))
            }}
            onPointerUp={(e) => {
              isSeekingRef.current = false
              const targetTime = parseFloat(e.target.value)
              handleCommand('seek', { seconds: targetTime }, e)
            }}
            disabled={!hasCurrent || duration <= 0}
            className="spotify-slider w-full cursor-pointer disabled:opacity-40"
          />
          <div className="flex justify-between items-center text-[10px] font-black text-slate-500 font-mono">
            <span>{formatClockTime(currentTime)}</span>
            <span>{formatClockTime(duration)}</span>
          </div>
        </div>

        <div className={`w-full flex items-center justify-center gap-4 my-4 mobile-controls-spacing shrink-0 ${isOperatorDesk ? 'gap-6' : 'gap-5'}`}>
          <button
            onPointerDown={(e) => {
              if (hasCurrent) handleCommand('seek', { seconds: currentTime - 10 }, e)
            }}
            disabled={!hasCurrent}
            className={`${isOperatorDesk ? 'w-14 h-14' : 'w-10 h-10'} mobile-controls-btn rounded-full bg-white/5 hover:bg-white/10 border border-white/5 flex flex-col items-center justify-center text-slate-400 hover:text-white transition-all active:scale-90 disabled:opacity-30 disabled:pointer-events-none`}
            title="Tua lùi 10s"
          >
            <FontAwesomeIcon icon={faUndo} className={isOperatorDesk ? 'text-sm' : 'text-xs'} />
            <span className="text-[7px] font-black font-sans -mt-0.5">10s</span>
          </button>

          <button
            onPointerDown={(e) => {
              if (hasCurrent) handleCommand('replay', {}, e)
            }}
            disabled={!hasCurrent}
            className={`${isOperatorDesk ? 'w-14 h-14' : 'w-10 h-10'} mobile-controls-btn rounded-full bg-white/5 hover:bg-white/10 border border-white/5 flex items-center justify-center text-slate-400 hover:text-white transition-all active:scale-90 disabled:opacity-30 disabled:pointer-events-none`}
            title="Phát lại"
          >
            <FontAwesomeIcon icon={faRedo} className={isOperatorDesk ? 'text-sm' : 'text-xs'} />
          </button>

          <button
            onPointerDown={(e) => handleCommand('playPause', {}, e)}
            disabled={!hasCurrent}
            className={`${isOperatorDesk ? 'w-20 h-20' : 'w-16 h-16'} mobile-controls-play-btn rounded-full bg-white text-black flex items-center justify-center shadow-xl hover:scale-105 transition-all active:scale-95 disabled:bg-slate-700 disabled:text-slate-500 disabled:pointer-events-none`}
            title={isPlaying ? "Tạm dừng" : "Phát"}
          >
            <FontAwesomeIcon icon={isPlaying ? faPause : faPlay} className={`${isOperatorDesk ? 'text-2xl' : 'text-lg'} ml-0.5`} />
          </button>

          <button
            onPointerDown={(e) => !isTrackChangeLocked && handleCommand('next', {}, e)}
            disabled={isTrackChangeLocked || filteredPlaylist.length === 0}
            className={`${isOperatorDesk ? 'w-14 h-14' : 'w-10 h-10'} mobile-controls-btn rounded-full border flex items-center justify-center transition-all active:scale-90 disabled:opacity-30 disabled:pointer-events-none ${isTrackChangeLocked ? 'bg-amber-500/15 border-amber-500/30 text-amber-300' : 'bg-white/5 hover:bg-white/10 border-white/5 text-slate-400 hover:text-white'}`}
            title={isTrackChangeLocked ? 'Đang khóa chuyển bài' : 'Bài tiếp theo'}
          >
            <FontAwesomeIcon icon={isTrackChangeLocked ? faLock : faForwardStep} className={isOperatorDesk ? 'text-sm' : 'text-xs'} />
          </button>

          <button
            onPointerDown={(e) => {
              if (hasCurrent) handleCommand('seek', { seconds: currentTime + 10 }, e)
            }}
            disabled={!hasCurrent}
            className={`${isOperatorDesk ? 'w-14 h-14' : 'w-10 h-10'} mobile-controls-btn rounded-full bg-white/5 hover:bg-white/10 border border-white/5 flex flex-col items-center justify-center text-slate-400 hover:text-white transition-all active:scale-90 disabled:opacity-30 disabled:pointer-events-none`}
            title="Tua tiến 10s"
          >
            <FontAwesomeIcon icon={faUndo} className={`${isOperatorDesk ? 'text-sm' : 'text-xs'} scale-x-[-1]`} />
            <span className="text-[7px] font-black font-sans -mt-0.5">10s</span>
          </button>
        </div>

        <div className="w-full bg-black/20 border border-white/5 rounded-2xl p-4 flex items-center gap-3 shrink-0 my-2 mobile-controls-spacing">
          <FontAwesomeIcon icon={faVolumeDown} className="text-slate-500 text-xs shrink-0" />
          <input
            type="range"
            min="0"
            max="100"
            value={volume}
            onChange={(e) => {
              setVolume(parseInt(e.target.value))
            }}
            onPointerUp={(e) => {
              const val = parseInt(e.target.value)
              lastVolumeChangeRef.current = Date.now()
              handleCommand('volume', { value: val }, e)
            }}
            className="spotify-slider flex-1 cursor-pointer"
          />
          <div className="flex items-center gap-2.5 shrink-0">
            <span className="text-[10px] font-mono font-black text-[#4F7CFF] w-8 text-right">{volume}%</span>
            <div className="w-[1px] h-3 bg-white/10" />
            <button
              onPointerDown={() => handleCommand('fullscreenVideo')}
              className={`w-6 h-6 flex items-center justify-center rounded-lg transition-colors ${isFullscreen ? 'text-orange-500 bg-orange-500/10' : 'text-slate-400 hover:text-white'}`}
              title="Toàn màn hình TV"
            >
              <FontAwesomeIcon icon={faTv} className="text-xs" />
            </button>
          </div>
        </div>
      </div>
    )
  }

  const renderMoreContent = () => (
    <div className="space-y-6 pb-24">
      <div className="space-y-3">
        <h3 className="text-xs font-black text-white uppercase tracking-widest flex items-center gap-2 pb-2 border-b border-white/5">
          <FontAwesomeIcon icon={faCog} className="text-slate-400" />
          <span>Cài đặt hệ thống</span>
        </h3>
        {renderSettingsContent(true)}
      </div>
    </div>
  )

  const renderMobileTabs = () => (
    <div className="h-[64px] bg-[#0B1224]/85 backdrop-blur-xl border-t border-white/5 px-2 py-1 flex items-center justify-around gap-1 w-full">
      <button
        onClick={() => setActiveTab('search')}
        className={`flex-1 py-1 text-[9px] font-black uppercase tracking-wider flex flex-col items-center justify-center gap-1 rounded-xl transition-all ${activeTab === 'search' ? 'text-[#4F7CFF]' : 'text-slate-500 hover:text-slate-300'}`}
      >
        <FontAwesomeIcon icon={faSearch} className="text-sm" />
        <span className="text-[8px]">Tìm kiếm</span>
      </button>
      <button
        onClick={() => setActiveTab('queue')}
        className={`flex-1 py-1 text-[9px] font-black uppercase tracking-wider flex flex-col items-center justify-center gap-1 rounded-xl transition-all relative ${activeTab === 'queue' ? 'text-[#4F7CFF]' : 'text-slate-500 hover:text-slate-300'}`}
      >
        <FontAwesomeIcon icon={faListCheck} className="text-sm" />
        <span className="text-[8px]">Hàng chờ</span>
        {filteredPlaylist.length > 0 && (
          <span className="absolute top-0 right-6 min-w-[14px] h-[14px] px-1 bg-[#EF4444] text-white text-[8px] font-mono rounded-full flex items-center justify-center shadow-lg border border-[#0B1224]">
            {filteredPlaylist.length}
          </span>
        )}
      </button>
      <button
        onClick={() => setActiveTab('controls')}
        className={`flex-1 py-1 text-[9px] font-black uppercase tracking-wider flex flex-col items-center justify-center gap-1 rounded-xl transition-all ${activeTab === 'controls' ? 'text-[#4F7CFF]' : 'text-slate-500 hover:text-slate-300'}`}
      >
        <FontAwesomeIcon icon={faSlidersH} className="text-sm" />
        <span className="text-[8px]">Điều khiển</span>
      </button>
      <button
        onClick={() => setActiveTab('more')}
        className={`flex-1 py-1 text-[9px] font-black uppercase tracking-wider flex flex-col items-center justify-center gap-1 rounded-xl transition-all ${activeTab === 'more' ? 'text-[#4F7CFF]' : 'text-slate-500 hover:text-slate-300'}`}
      >
        <FontAwesomeIcon icon={faEllipsisH} className="text-sm" />
        <span className="text-[8px]">Khác</span>
      </button>
    </div>
  )

  /** Lấy session từ chuỗi thuần hoặc full URL remote/player */
  const parseSessionInput = (raw) => {
    if (!raw || typeof raw !== 'string') return ''
    const s = raw.trim()
    try {
      if (s.includes('session=') || s.startsWith('http')) {
        const url = s.startsWith('http') ? new URL(s) : new URL(s, window.location.origin)
        const fromQuery = url.searchParams.get('session') || url.searchParams.get('token')
        if (fromQuery) return fromQuery.trim().toUpperCase().replace(/[^0-9A-Z]/g, '').slice(0, 16)
      }
    } catch { /* not a URL */ }
    return s.toUpperCase().replace(/[^0-9A-Z]/g, '').slice(0, 16)
  }

  const handleTokenSubmit = async (e) => {
    e?.preventDefault?.()
    const token = parseSessionInput(inputToken)
    if (!token) {
      setTokenError('Nhập session ID (vd: ABC123)')
      return
    }
    if (token.length < 4) {
      setTokenError('Session quá ngắn (tối thiểu 4 ký tự)')
      return
    }
    if (!/^[0-9A-Z]{4,16}$/.test(token)) {
      setTokenError('Session chỉ gồm A–Z và 0–9')
      return
    }

    setIsConnecting(true)
    setTokenError(null)
    setInputToken(token)

    try {
      // Kiểm tra server (nếu lỗi mạng thì báo; 404 vẫn cho join)
      const state = await getRemoteState(token)
      if (state?.networkError) {
        setTokenError('Không kết nối được máy chủ. Kiểm tra mạng / server remote.')
        return
      }
      // Tạo shell session nếu player chưa register
      await ensureSession(token)
      setSearchParams({ session: token }, { replace: true })
    } catch (err) {
      console.error(err)
      setTokenError('Lỗi kết nối máy chủ')
    } finally {
      setIsConnecting(false)
    }
  }

  const renderTokenEntry = () => (
    <div className="flex-1 flex flex-col items-center justify-center p-4 sm:p-6 bg-[#1C1C1C]">
      <div className="w-full max-w-md bg-[#222222] border border-[#3C3C3C] rounded-2xl p-6 sm:p-8 shadow-2xl">
        <div className="space-y-6">
          <div className="flex flex-col items-center gap-3 text-center">
            <div className="w-14 h-14 bg-[#2478C8]/15 rounded-2xl flex items-center justify-center border border-[#2478C8]/35">
              <FontAwesomeIcon icon={faBolt} className="text-2xl text-[#2478C8]" />
            </div>
            <div>
              <h2 className="text-base font-bold text-white tracking-wide">Kết nối điều khiển</h2>
              <p className="text-[12px] text-[#A0A0A0] mt-1.5 leading-relaxed">
                Nhập <span className="text-[#7EC8FF] font-semibold">Session ID</span> từ ShowCue / player
                <br />
                <span className="text-[#707070]">Chỉ session — không dùng room id</span>
              </p>
            </div>
          </div>

          <form onSubmit={handleTokenSubmit} className="space-y-4">
            <div>
              <label className="block text-[10px] font-bold text-[#A0A0A0] uppercase tracking-wider mb-2">
                Session ID
              </label>
              <input
                type="text"
                maxLength={200}
                autoFocus
                disabled={isConnecting}
                autoComplete="off"
                spellCheck={false}
                className={`w-full bg-[#1C1C1C] border ${tokenError ? 'border-[#EF4444]' : 'border-[#4E4E4E] focus:border-[#2478C8]'
                  } text-white text-xl sm:text-2xl font-mono font-bold tracking-[0.25em] text-center rounded-xl py-4 px-3 outline-none transition-colors placeholder:text-[#555] placeholder:tracking-normal placeholder:text-sm disabled:opacity-50`}
                placeholder="ABC123"
                value={inputToken}
                onChange={(e) => {
                  const v = e.target.value
                  // Cho dán URL; khi gõ tay thì lọc alnum
                  if (v.includes('session=') || v.includes('http') || v.includes('?') || v.includes('/')) {
                    setInputToken(v)
                  } else {
                    setInputToken(v.toUpperCase().replace(/[^0-9A-Z]/g, '').slice(0, 16))
                  }
                  setTokenError(null)
                }}
                onPaste={(e) => {
                  const text = e.clipboardData?.getData('text') || ''
                  if (text.includes('session=') || text.includes('http')) {
                    e.preventDefault()
                    const parsed = parseSessionInput(text)
                    setInputToken(parsed)
                    setTokenError(null)
                  }
                }}
                onKeyDown={(e) => {
                  if (e.key === 'Enter') handleTokenSubmit(e)
                }}
              />
              <p className="mt-2 text-[10px] text-[#707070] text-center">
                Có thể dán full link: <span className="text-[#A0A0A0]">…/remote?session=ABC123</span>
              </p>
            </div>

            {tokenError && (
              <div className="px-3 py-2 rounded-lg bg-[#EF4444]/10 border border-[#EF4444]/30 text-center">
                <p className="text-[11px] text-[#FCA5A5] font-semibold">{tokenError}</p>
              </div>
            )}

            <button
              type="submit"
              disabled={isConnecting || parseSessionInput(inputToken).length < 4}
              className="w-full bg-[#2478C8] hover:bg-[#2B87D7] disabled:bg-[#333] disabled:text-[#707070] text-white font-bold text-sm py-3.5 rounded-xl active:scale-[0.98] transition-all flex items-center justify-center gap-2"
            >
              {isConnecting ? (
                <>
                  <div className="animate-spin w-4 h-4 border-2 border-white rounded-full border-t-transparent" />
                  <span>Đang kết nối…</span>
                </>
              ) : (
                <>
                  <FontAwesomeIcon icon={faCheck} />
                  <span>Kết nối remote</span>
                </>
              )}
            </button>
          </form>

          <div className="pt-4 border-t border-[#3C3C3C] space-y-2 text-[11px] text-[#A0A0A0] leading-relaxed">
            <p className="font-semibold text-[#C8C8C8]">Cách lấy session</p>
            <ol className="list-decimal list-inside space-y-1 text-[#888]">
              <li>ShowCue → tab Karaoke → ô Session (vd. <code className="text-[#7EC8FF]">4MHXYW</code>)</li>
              <li>Hoặc bật <b className="text-[#C8C8C8]">KARAOKE ON</b> rồi copy session</li>
              <li>Dán vào đây → Kết nối remote</li>
            </ol>
            <p className="text-[10px] text-[#666] pt-1">
              Player master phải cùng session: <code className="text-[#888]">/player?session=…</code>
            </p>
          </div>
        </div>
      </div>

      {!isOperatorDesk && (
        <button
          type="button"
          onClick={() => navigate('/')}
          className="mt-5 text-[11px] font-semibold text-[#707070] hover:text-white flex items-center gap-2 transition-colors"
        >
          <FontAwesomeIcon icon={faArrowLeft} />
          <span>Trang chủ</span>
        </button>
      )}
    </div>
  )

  const renderPasswordLockScreen = () => (
    <div className="flex-1 flex flex-col items-center justify-center p-4 sm:p-6 bg-[#1C1C1C]">
      <div className="w-full max-w-md bg-[#222222] border border-[#3C3C3C] rounded-2xl p-6 sm:p-8 shadow-2xl">
        <div className="space-y-6">
          <div className="flex flex-col items-center gap-3 text-center">
            <div className="w-16 h-16 bg-amber-500/15 rounded-2xl flex items-center justify-center border border-amber-500/35 shadow-lg shadow-amber-500/10">
              <FontAwesomeIcon icon={faLock} className="text-3xl text-amber-400 animate-pulse" />
            </div>
            <div>
              <h2 className="text-lg font-bold text-white tracking-wide">Phiên được bảo vệ</h2>
              <p className="text-[12px] text-[#A0A0A0] mt-1.5 leading-relaxed">
                Session <span className="text-[#7EC8FF] font-mono font-bold">{sessionToken}</span> yêu cầu mật khẩu để điều khiển
              </p>
            </div>
          </div>

          <form onSubmit={handlePasswordSubmit} className="space-y-4">
            <div>
              <label className="block text-[10px] font-bold text-[#A0A0A0] uppercase tracking-wider mb-2 text-center">
                Nhập Mật khẩu / Mã PIN
              </label>
              <input
                type="password"
                maxLength={32}
                autoFocus
                disabled={isVerifyingPassword}
                autoComplete="current-password"
                className={`w-full bg-[#1C1C1C] border ${passwordError ? 'border-[#EF4444]' : 'border-[#4E4E4E] focus:border-amber-400'
                  } text-white text-xl font-mono font-bold tracking-[0.25em] text-center rounded-xl py-3.5 px-3 outline-none transition-colors placeholder:text-[#555] placeholder:tracking-normal placeholder:text-sm disabled:opacity-50`}
                placeholder="••••"
                value={inputPassword}
                onChange={(e) => {
                  setInputPassword(e.target.value)
                  setPasswordError('')
                }}
              />
              {passwordError && (
                <p className="text-[#EF4444] text-[11px] font-medium text-center mt-2 animate-shake">
                  {passwordError}
                </p>
              )}
            </div>

            <button
              type="submit"
              disabled={isVerifyingPassword || !inputPassword.trim()}
              className="w-full py-3.5 rounded-xl bg-gradient-to-r from-amber-500 to-amber-600 hover:from-amber-400 hover:to-amber-500 active:scale-95 text-black font-black uppercase text-xs tracking-wider transition-all disabled:opacity-50 shadow-lg shadow-amber-500/20"
            >
              {isVerifyingPassword ? 'Đang kiểm tra…' : 'Mở khóa điều khiển'}
            </button>

            <button
              type="button"
              onClick={handleExitSession}
              className="w-full py-2.5 rounded-xl bg-[#2A2A2A] hover:bg-[#333] active:scale-95 text-[#A0A0A0] text-[11px] font-semibold transition-all"
            >
              Đổi Session khác
            </button>
          </form>
        </div>
      </div>
    </div>
  )

  const renderMobileLayout = () => {
    const isSearchActiveAndFocused = activeTab === 'search' && isSearchFocused

    return (
      <div className="flex-1 flex flex-col min-h-0 w-full relative bg-[#050816]">
        {/* Top Header */}
        {!isSearchActiveAndFocused && (
          <header className="h-[56px] bg-[#0B1224]/80 backdrop-blur-md border-b border-white/5 flex items-center justify-between px-4 sticky top-0 z-40 shadow-lg shrink-0">
            <div
              className={`flex items-center gap-2 ${isOperatorDesk ? '' : 'cursor-pointer'}`}
              onClick={isOperatorDesk ? undefined : () => navigate('/')}
            >
              <img src={appLogo} alt="Logo" className="w-7 h-7 object-contain" />
              <span className="font-black text-sm tracking-tight text-white uppercase hidden sm:inline">
                {isOperatorDesk ? 'OPERATOR' : 'KTV APP'}
              </span>
            </div>
            <div className="text-xs font-black text-slate-300 truncate max-w-[200px] text-center">
              {isOperatorDesk ? (
                <span className="inline-flex items-center gap-1.5 px-2 py-0.5 rounded-full bg-amber-500/15 border border-amber-500/30 text-amber-300 text-[10px]">
                  OPERATOR · KHÔNG SHARE
                </span>
              ) : (
                'ĐIỀU KHIỂN TỪ XA'
              )}
            </div>
            <div className="flex items-center gap-2">
              <div className={`w-2 h-2 rounded-full ${isConnected ? 'bg-green-500 animate-pulse shadow-[0_0_8px_rgba(34,197,94,0.5)]' : 'bg-[#EF4444]'}`} />
              <span className="text-[9px] font-black tracking-wider text-slate-400 uppercase">{isConnected ? 'ONLINE' : 'OFFLINE'}</span>
            </div>
          </header>
        )}

        {/* Quick Actions Bar */}
        {!isSearchActiveAndFocused && (
          <div className="flex items-center justify-between gap-1.5 px-3 py-2 bg-[#0B1224]/60 border-b border-white/5 h-12 shrink-0">
            <button
              onClick={toggleKaraoke}
              className={`flex-1 py-1.5 px-2.5 rounded-full text-[9px] font-black uppercase tracking-wider flex items-center justify-center gap-1 transition-all border ${isKaraokeMode ? 'bg-[#4F7CFF] border-[#4F7CFF]/20 text-white shadow-lg shadow-[#4F7CFF]/20' : 'bg-white/5 border-transparent text-slate-400'}`}
            >
              <FontAwesomeIcon icon={faMicrophone} className="text-[8px]" />
              <span>Karaoke</span>
            </button>
            <button
              onClick={toggleAutoPlay}
              className={`flex-1 py-1.5 px-2.5 rounded-full text-[9px] font-black uppercase tracking-wider flex items-center justify-center gap-1 transition-all border ${autoPlayNext ? 'bg-[#8B5CF6] border-[#8B5CF6]/20 text-white shadow-lg shadow-[#8B5CF6]/20' : 'bg-white/5 border-transparent text-slate-400'}`}
              title={autoPlayNext ? 'Tự động chuyển bài: BẬT' : 'Tự động chuyển bài: TẮT'}
            >
              <FontAwesomeIcon icon={faShuffle} className="text-[8px]" />
              <span>{autoPlayNext ? 'Auto ON' : 'Auto OFF'}</span>
            </button>
            <button
              onClick={toggleTrackLock}
              className={`flex-1 py-1.5 px-2.5 rounded-full text-[9px] font-black uppercase tracking-wider flex items-center justify-center gap-1 transition-all border ${isTrackChangeLocked ? 'bg-amber-500 border-amber-500/30 text-white shadow-lg shadow-amber-500/25' : 'bg-white/5 border-transparent text-slate-400'}`}
              title={isTrackChangeLocked ? 'Mở khóa chuyển bài' : 'Khóa không cho chuyển bài'}
            >
              <FontAwesomeIcon icon={isTrackChangeLocked ? faLock : faLockOpen} className="text-[8px]" />
              <span>{isTrackChangeLocked ? 'Khóa' : 'Khóa bài'}</span>
            </button>
            <button
              onClick={() => handleCommand('fullscreenVideo')}
              className={`flex-1 py-1.5 px-2.5 rounded-full text-[9px] font-black uppercase tracking-wider flex items-center justify-center gap-1 transition-all border ${isFullscreen ? 'bg-orange-500 border-orange-500/20 text-white shadow-lg shadow-orange-500/20' : 'bg-white/5 border-transparent text-slate-400'}`}
            >
              <FontAwesomeIcon icon={isFullscreen ? faCompress : faExpand} className="text-[8px]" />
              <span>{isFullscreen ? 'Thu nhỏ' : 'Phóng to'}</span>
            </button>
            <button
              onClick={() => handleCommand('stopPlayback')}
              className="py-1.5 px-3 rounded-full text-[9px] font-black uppercase tracking-wider flex items-center justify-center gap-1 bg-[#EF4444]/10 hover:bg-[#EF4444]/20 border border-[#EF4444]/20 text-[#EF4444] transition-all"
            >
              <FontAwesomeIcon icon={faPowerOff} className="text-[8px]" />
              <span>Tắt bài</span>
            </button>
          </div>
        )}

        {/* Scrollable Content */}
        <div className={activeTab === 'search' || activeTab === 'controls' ? 'flex-1 min-h-0 flex flex-col overflow-hidden bg-[#050816]' : 'flex-1 overflow-y-auto overflow-x-hidden custom-scrollbar px-4 pt-3 pb-44 bg-[#050816]'}>
          {activeTab === 'search' && renderSearchContent()}
          {activeTab === 'queue' && renderSelectedContent()}
          {activeTab === 'controls' && renderControlsTab()}
          {activeTab === 'more' && renderMoreContent()}
        </div>

        {/* Mini Player & Bottom Tab Navigation Bar Container */}
        {!isSearchActiveAndFocused && (
          <div className="fixed bottom-0 left-0 right-0 z-40 flex flex-col pointer-events-none shadow-[0_-10px_30px_rgba(0,0,0,0.5)]">
            <div className="pointer-events-auto">
              {!isListening && renderMiniPlayer()}
            </div>
            <div className="pointer-events-auto">
              {renderMobileTabs()}
            </div>
          </div>
        )}

        {/* Voice Search Overlay (vkara style) */}
        {isListening && (
          <div className="fixed inset-0 bottom-[64px] z-30 bg-[#050816]/95 backdrop-blur-md flex flex-col justify-between p-6 animate-fadeIn">
            {/* Header with close button */}
            <div className="relative pt-6">
              <button
                onClick={stopListening}
                className="w-9 h-9 rounded-full bg-white/5 border border-white/10 flex items-center justify-center text-slate-400 hover:text-white transition-all active:scale-90"
                title="Đóng"
              >
                <svg className="w-4 h-4" fill="none" stroke="currentColor" strokeWidth={2.5} viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
                </svg>
              </button>
              <h2 className="text-2xl font-black text-white uppercase tracking-wider text-left mt-8 pl-1">
                Đang nghe...
              </h2>
            </div>

            {/* Pulsing microphone stop button at center */}
            <div className="flex-1 flex flex-col items-center justify-center gap-8">
              <div className="relative flex items-center justify-center">
                {/* Outer pulsing wave */}
                <div className="absolute w-32 h-32 bg-red-500/10 rounded-full animate-ping duration-1000" />
                {/* Inner pulsing wave */}
                <div className="absolute w-24 h-24 bg-red-500/20 rounded-full animate-pulse duration-700" />
                
                {/* Stop button */}
                <button
                  onClick={stopListening}
                  className="relative w-20 h-20 bg-gradient-to-br from-red-500 to-red-600 rounded-full flex items-center justify-center shadow-[0_0_30px_rgba(239,68,68,0.4)] active:scale-95 transition-all z-10"
                >
                  <div className="w-6 h-6 bg-white rounded-md" />
                </button>
              </div>

              {/* Interim realtime transcript */}
              {interimTranscript && (
                <p className="text-sm font-bold text-slate-300 text-center px-6 max-w-[280px] line-clamp-3 bg-white/5 py-2 px-4 rounded-xl border border-white/5 animate-pulse">
                  "{interimTranscript}"
                </p>
              )}
            </div>

            {/* Instruction text at bottom */}
            <div className="pb-8 text-center">
              <p className="text-[10px] font-black text-slate-500 uppercase tracking-widest leading-relaxed">
                Chạm dừng để tìm với nội dung đã nói
              </p>
            </div>
          </div>
        )}

        {/* Volume Bottom Sheet */}
        {renderVolumeSheet()}
      </div>
    )
  }

  /**
   * Layout ShowCue desk:
   *  Trái = NOW + CONTROL to · Phải = HÀNG CHỜ (cạnh nhau, dễ nhìn)
   *  Dưới = Tìm kiếm
   */
  const renderShowCueDeskLayout = () => {
    const isPlaying = !!remoteState?.isPlaying
    const hasCurrent = !!currentVideo
    const results = getFilteredResults(searchResults) || []
    const thumb = currentVideo
      ? (currentVideo.source === 'youtube'
        ? `https://i.ytimg.com/vi/${currentVideo.id}/mqdefault.jpg`
        : currentVideo.thumbnail)
      : null

    return (
      <div className="flex-1 flex flex-col min-h-0 w-full bg-[#1C1C1C] text-[#F5F5F5] overflow-hidden">
        {/* Status strip */}
        <div className="shrink-0 h-9 px-3 flex items-center gap-2 bg-[#222222] border-b border-[#3C3C3C]">
          <div className={`w-2 h-2 rounded-full shrink-0 ${isConnected || connectionStatus === 'connected' ? 'bg-[#00E676]' : 'bg-[#E8A838]'}`} />
          <span className="text-[10px] font-semibold text-[#A0A0A0] uppercase shrink-0">
            {connectionStatus === 'connected' || isConnected ? 'Online' : 'Chờ player…'}
          </span>
          <span className="text-[10px] font-mono text-[#2478C8] bg-[#2478C8]/15 px-2 py-0.5 rounded border border-[#2478C8]/30 shrink-0">
            {sessionToken}
          </span>
          <div className="flex-1" />
          <button type="button" onClick={toggleKaraoke}
            className={`h-7 px-2 rounded text-[9px] font-bold border ${isKaraokeMode ? 'border-[#2478C8] text-[#7EC8FF] bg-[#2478C8]/15' : 'border-[#3C3C3C] text-[#A0A0A0]'}`}>
            Karaoke
          </button>
          <button type="button" onClick={toggleAutoPlay}
            className={`h-7 px-2.5 rounded text-[9px] font-bold border flex items-center gap-1 ${autoPlayNext ? 'border-[#8B5CF6] text-white bg-[#8B5CF6]/30' : 'border-[#3C3C3C] text-[#A0A0A0] bg-[#1C1C1C]'}`}
            title={autoPlayNext ? 'Tự động chuyển bài: BẬT — bấm để tắt' : 'Tự động chuyển bài: TẮT — bấm để bật'}>
            <FontAwesomeIcon icon={faShuffle} className="text-[9px]" />
            {autoPlayNext ? 'Tự động: ON' : 'Tự động: OFF'}
          </button>
          <button type="button" onClick={toggleTrackLock}
            className={`h-7 px-2 rounded text-[9px] font-bold border flex items-center gap-1 ${isTrackChangeLocked ? 'border-amber-500 text-amber-300 bg-amber-500/20' : 'border-[#3C3C3C] text-[#A0A0A0]'}`}
            title={isTrackChangeLocked ? 'Đang khóa chuyển bài — bấm để mở' : 'Khóa không cho chuyển bài'}>
            <FontAwesomeIcon icon={isTrackChangeLocked ? faLock : faLockOpen} className="text-[9px]" />
            {isTrackChangeLocked ? 'Khóa' : 'Mở'}
          </button>
        </div>

        {/* Hàng giữa: NOW+CONTROL | HÀNG CHỜ (cạnh nhau) */}
        <div className="shrink-0 grid grid-cols-1 md:grid-cols-12 border-b border-[#3C3C3C] min-h-[200px] max-h-[46%]">
          {/* NOW + CONTROL */}
          <div className="md:col-span-7 flex flex-col gap-3 p-3 bg-[#222222] border-b md:border-b-0 md:border-r border-[#3C3C3C]">
            <div className="flex gap-3 min-h-0">
              <div className="w-[120px] h-[68px] sm:w-[148px] sm:h-[84px] rounded-lg overflow-hidden bg-black border border-[#3C3C3C] shrink-0">
                {thumb ? (
                  <img src={thumb} alt="" className="w-full h-full object-cover" />
                ) : (
                  <div className="w-full h-full flex items-center justify-center text-[#555]">
                    <FontAwesomeIcon icon={faMusic} className="text-2xl" />
                  </div>
                )}
              </div>
              <div className="flex-1 min-w-0 flex flex-col justify-center gap-1">
                <div className="flex items-center gap-2 flex-wrap">
                  <span className="text-[9px] font-bold text-[#2478C8] uppercase tracking-wider">Đang phát</span>
                  {isTrackChangeLocked && (
                    <span className="text-[9px] font-bold text-amber-300 bg-amber-500/15 border border-amber-500/30 px-1.5 py-0.5 rounded">
                      🔒 Khóa bài
                    </span>
                  )}
                  <span className={`text-[9px] font-bold px-1.5 py-0.5 rounded border ${autoPlayNext ? 'text-[#C4B5FD] bg-[#8B5CF6]/15 border-[#8B5CF6]/35' : 'text-[#888] bg-[#2B2B2B] border-[#3C3C3C]'}`}>
                    {autoPlayNext ? '⏭ Tự động ON' : '⏹ Tự động OFF'}
                  </span>
                </div>
                <p className="text-[13px] sm:text-[14px] font-bold text-white leading-snug line-clamp-2">
                  {hasCurrent ? decodeHtmlEntities(currentVideo.title) : 'Chưa có bài — tìm & phát bên dưới'}
                </p>
                <p className="text-[11px] text-[#A0A0A0] truncate">
                  {hasCurrent ? decodeHtmlEntities(currentVideo.channelTitle || '') : '—'}
                </p>
              </div>
            </div>

            {/* Seek */}
            <div className="flex items-center gap-2">
              <span className="text-[9px] font-mono text-[#707070] w-8 text-right">{formatClockTime(currentTime)}</span>
              <input
                type="range"
                min="0"
                max={duration || 100}
                value={currentTime}
                disabled={!hasCurrent || duration <= 0}
                onChange={(e) => {
                  isSeekingRef.current = true
                  setCurrentTime(parseFloat(e.target.value))
                }}
                onPointerUp={(e) => {
                  isSeekingRef.current = false
                  handleCommand('seek', { seconds: parseFloat(e.target.value) }, e)
                }}
                className="spotify-slider flex-1 cursor-pointer disabled:opacity-40"
              />
              <span className="text-[9px] font-mono text-[#707070] w-8">{formatClockTime(duration)}</span>
            </div>

            {/* Auto-next control */}
            <div className="flex items-center gap-2 flex-wrap">
              <button type="button" onClick={toggleAutoPlay}
                className={`h-9 px-3 rounded-lg text-[11px] font-bold border flex items-center gap-2 active:scale-95 transition-all ${
                  autoPlayNext
                    ? 'bg-[#8B5CF6] border-[#8B5CF6] text-white shadow-md shadow-purple-900/30'
                    : 'bg-[#2B2B2B] border-[#4E4E4E] text-[#A0A0A0]'
                }`}
                title="Hết bài → tự sang bài kế trong hàng chờ">
                <FontAwesomeIcon icon={faShuffle} className="text-[11px]" />
                <span>{autoPlayNext ? 'Tự động chuyển: BẬT' : 'Tự động chuyển: TẮT'}</span>
              </button>
            </div>

            {/* CONTROL to */}
            <div className="flex items-center justify-between gap-2 flex-wrap">
              <div className="flex items-center gap-2">
                <button type="button"
                  disabled={!hasCurrent}
                  onPointerDown={(e) => hasCurrent && handleCommand('seek', { seconds: Math.max(0, currentTime - 10) }, e)}
                  className="w-10 h-10 rounded-lg bg-[#2B2B2B] border border-[#4E4E4E] text-[#C8C8C8] text-[10px] font-bold disabled:opacity-30 active:scale-95"
                  title="−10s">
                  −10
                </button>
                <button type="button"
                  disabled={!hasCurrent}
                  onPointerDown={(e) => hasCurrent && handleCommand('replay', {}, e)}
                  className="w-10 h-10 rounded-lg bg-[#2B2B2B] border border-[#4E4E4E] text-[#C8C8C8] disabled:opacity-30 active:scale-95"
                  title="Phát lại">
                  <FontAwesomeIcon icon={faRedo} />
                </button>
                <button type="button"
                  onPointerDown={(e) => handleCommand('playPause', {}, e)}
                  className="w-14 h-14 rounded-full bg-white text-black shadow-lg active:scale-95 flex items-center justify-center"
                  title={isPlaying ? 'Tạm dừng' : 'Phát'}>
                  <FontAwesomeIcon icon={isPlaying ? faPause : faPlay} className="text-lg ml-0.5" />
                </button>
                <button type="button"
                  disabled={isTrackChangeLocked}
                  onPointerDown={(e) => !isTrackChangeLocked && handleCommand('next', {}, e)}
                  className={`w-12 h-12 rounded-lg font-bold text-[11px] active:scale-95 ${isTrackChangeLocked ? 'bg-amber-500/25 text-amber-300 border border-amber-500/40 opacity-80' : 'bg-[#2478C8] text-white'}`}
                  title={isTrackChangeLocked ? 'Đang khóa chuyển bài' : 'Bài sau'}>
                  <FontAwesomeIcon icon={isTrackChangeLocked ? faLock : faForwardStep} className="text-base" />
                </button>
                <button type="button"
                  disabled={!hasCurrent}
                  onPointerDown={(e) => hasCurrent && handleCommand('seek', { seconds: currentTime + 10 }, e)}
                  className="w-10 h-10 rounded-lg bg-[#2B2B2B] border border-[#4E4E4E] text-[#C8C8C8] text-[10px] font-bold disabled:opacity-30 active:scale-95"
                  title="+10s">
                  +10
                </button>
                <button type="button"
                  onClick={(e) => handleCommand('stopPlayback', {}, e)}
                  onPointerDown={(e) => { e.preventDefault(); handleCommand('stopPlayback', {}, e) }}
                  className="h-10 px-3 rounded-lg bg-[#EF4444] border border-[#EF4444] text-white font-bold text-[11px] active:scale-95 flex items-center gap-1.5 shadow-lg shadow-red-900/30"
                  title="Dừng phát (tắt bài trên OUTPUT)">
                  <FontAwesomeIcon icon={faPowerOff} className="text-xs" />
                  <span>Dừng</span>
                </button>
                <button type="button"
                  onClick={(e) => handleCommand('fullscreenVideo', {}, e)}
                  onPointerDown={(e) => { e.preventDefault(); handleCommand('fullscreenVideo', {}, e) }}
                  className={`h-10 px-3 rounded-lg border font-bold text-[11px] active:scale-95 flex items-center gap-1.5 transition-all ${
                    isFullscreen
                      ? 'bg-orange-500 border-orange-500 text-white shadow-lg shadow-orange-900/30'
                      : 'bg-[#2478C8] border-[#2478C8] text-white shadow-lg shadow-blue-900/30 hover:bg-[#1f6ab0]'
                  }`}
                  title={isFullscreen ? 'Thu nhỏ player trên màn OUTPUT' : 'Phóng to player full màn OUTPUT (TV)'}>
                  <FontAwesomeIcon icon={isFullscreen ? faCompress : faExpand} className="text-xs" />
                  <span>{isFullscreen ? 'Thu nhỏ màn' : 'Phóng to màn'}</span>
                </button>
              </div>
              <div className="flex items-center gap-2 min-w-[140px] flex-1 max-w-[220px]">
                <FontAwesomeIcon icon={faVolumeDown} className="text-[#A0A0A0] text-xs" />
                <input type="range" min="0" max="100" value={volume}
                  onChange={(e) => setVolume(parseInt(e.target.value, 10))}
                  onPointerUp={(e) => {
                    const val = parseInt(e.target.value, 10)
                    lastVolumeChangeRef.current = Date.now()
                    handleCommand('volume', { value: val }, e)
                  }}
                  className="spotify-slider flex-1 cursor-pointer" />
                <span className="text-[11px] font-mono font-bold text-[#2478C8] w-8 text-right">{volume}</span>
              </div>
            </div>
          </div>

          {/* HÀNG CHỜ — cạnh NOW */}
          <div className="md:col-span-5 flex flex-col min-h-0 bg-[#1C1C1C] max-h-[280px] md:max-h-none">
            <div className="shrink-0 h-10 px-3 flex items-center justify-between bg-[#2B2B2B] border-b border-[#3C3C3C]">
              <span className="text-[11px] font-bold text-white uppercase tracking-wide flex items-center gap-2">
                <FontAwesomeIcon icon={faListCheck} className="text-[#2478C8]" />
                Hàng chờ
                <span className="px-1.5 py-0.5 rounded bg-[#2478C8]/25 text-[#7EC8FF] text-[10px] font-mono">
                  {filteredPlaylist.length}
                </span>
              </span>
              {filteredPlaylist.length > 0 && (
                <button type="button" onClick={() => handleCommand('clearAll', {})}
                  className="text-[10px] font-bold text-[#EF4444] uppercase hover:underline">
                  Xóa hết
                </button>
              )}
            </div>
            <div className="flex-1 overflow-y-auto custom-scrollbar p-2 space-y-1.5 min-h-[120px]">
              {filteredPlaylist.length === 0 ? (
                <div className="h-full min-h-[100px] flex flex-col items-center justify-center text-[#666] gap-2">
                  <FontAwesomeIcon icon={faListCheck} className="text-xl opacity-40" />
                  <p className="text-[10px] font-bold uppercase">Chưa có bài trong hàng chờ</p>
                </div>
              ) : filteredPlaylist.map((video, idx) => (
                <div key={`${video.id}-${idx}`}
                  className="flex items-center gap-2 p-2 rounded-lg bg-[#2B2B2B] border border-[#3C3C3C] hover:border-[#2478C8]/50">
                  <span className="w-5 h-5 rounded bg-[#1C1C1C] text-[10px] font-mono font-bold text-[#A0A0A0] flex items-center justify-center shrink-0">
                    {idx + 1}
                  </span>
                  <img
                    src={video.source === 'youtube' ? `https://i.ytimg.com/vi/${video.id}/default.jpg` : video.thumbnail}
                    alt="" className="w-12 h-8 object-cover rounded bg-black shrink-0"
                  />
                  <div className="flex-1 min-w-0">
                    <p className="text-[11px] font-semibold text-white truncate">{decodeHtmlEntities(video.title)}</p>
                  </div>
                  <button type="button" onClick={(e) => confirmPlayVideo(video, 'selected', e)}
                    className="h-8 px-2 rounded bg-[#EF4444]/20 text-[#EF4444] text-[10px] font-bold border border-[#EF4444]/30"
                    title="Phát ngay">
                    Play
                  </button>
                  <button type="button" onClick={(e) => handleCommand('priority', { videoId: video.id }, e)}
                    className="w-8 h-8 rounded bg-[#2478C8]/20 text-[#7EC8FF]" title="Ưu tiên">
                    <FontAwesomeIcon icon={faArrowUp} className="text-[10px]" />
                  </button>
                  <button type="button" onClick={(e) => handleCommand('removeFromSelected', { videoId: video.id }, e)}
                    className="w-8 h-8 rounded bg-white/5 text-[#A0A0A0]" title="Xóa">
                    <FontAwesomeIcon icon={faTrash} className="text-[10px]" />
                  </button>
                </div>
              ))}
            </div>
          </div>
        </div>

        {/* Tìm kiếm + kết quả */}
        <div className="flex-1 min-h-0 flex flex-col bg-[#222222]">
          <div className="shrink-0 p-2.5 border-b border-[#3C3C3C] space-y-2">
            <div className="flex items-center gap-2">
              <span className="text-[10px] font-bold text-[#C8C8C8] uppercase tracking-wide shrink-0">Tìm bài</span>
              <div className="flex bg-[#1C1C1C] p-0.5 rounded border border-[#3C3C3C]">
                <button type="button" onClick={() => switchSearchSource('youtube')}
                  className={`px-2.5 py-1 rounded text-[9px] font-bold uppercase ${searchSource === 'youtube' ? 'bg-[#EF4444] text-white' : 'text-[#A0A0A0]'}`}>
                  YouTube
                </button>
                <button type="button" onClick={() => switchSearchSource('soundcloud')}
                  className={`px-2.5 py-1 rounded text-[9px] font-bold uppercase ${searchSource === 'soundcloud' ? 'bg-[#FF5500] text-white' : 'text-[#A0A0A0]'}`}>
                  SC
                </button>
              </div>
              <div ref={desktopSearchContainerRef} className="relative flex-1 min-w-0">
                <form onSubmit={handleSearchSubmit} className="flex gap-2 min-w-0">
                  <input
                    ref={desktopInputRef}
                    className="flex-1 h-9 bg-[#1C1C1C] border border-[#4E4E4E] rounded px-3 text-[12px] text-[#F5F5F5] placeholder:text-[#707070] focus:border-[#2478C8] outline-none"
                    placeholder="Tìm bài hát / nghệ sĩ…"
                    value={searchQuery}
                    onChange={(e) => setSearchQuery(e.target.value)}
                    onFocus={() => {
                      setIsSearchFocused(true)
                      setShowSuggestions(true)
                    }}
                    onBlur={() => {
                      setIsSearchFocused(false)
                      setTimeout(() => setShowSuggestions(false), 200)
                    }}
                    onKeyDown={(e) => handleKeyDownSuggestions(e, desktopInputRef)}
                  />
                  <button type="submit"
                    className="h-9 px-4 rounded bg-[#2478C8] text-white text-[11px] font-bold active:scale-95 shrink-0">
                    {isSearching ? '…' : 'Tìm'}
                  </button>
                </form>

                {/* Gợi ý từ khóa / lịch sử — giống remote web desktop */}
                {showSuggestions && (
                  (searchQuery.length >= 2 && searchSource === 'youtube') ||
                  (searchQuery.length < 2 && searchHistory.length > 0)
                ) && (
                  <div className="absolute z-50 left-0 right-0 mt-1.5 bg-[#1C1C1C] border border-[#4E4E4E] rounded-xl shadow-[0_16px_40px_rgba(0,0,0,0.65)] max-h-[320px] overflow-y-auto custom-scrollbar overflow-x-hidden">
                    {searchQuery.length < 2 && searchHistory.length > 0 && (
                      <div className="py-1">
                        <div className="px-3 py-2 text-[10px] font-bold text-[#888] uppercase tracking-wide flex items-center justify-between border-b border-[#3C3C3C]">
                          <span>Lịch sử tìm kiếm</span>
                          <button
                            type="button"
                            onMouseDown={(e) => {
                              e.preventDefault()
                              clearSearchHistory()
                            }}
                            className="text-[#EF4444] hover:text-red-300 uppercase text-[9px] font-bold px-1.5 py-0.5 rounded hover:bg-red-500/10"
                          >
                            Xóa hết
                          </button>
                        </div>
                        {searchHistory.map((item, index) => (
                          <div
                            key={index}
                            onMouseDown={(e) => {
                              e.preventDefault()
                              justSelectedSuggestionRef.current = true
                              setSearchQuery(item)
                              setShowSuggestions(false)
                              handleSearchWithQuery(item)
                            }}
                            className="px-3 py-2.5 cursor-pointer hover:bg-[#2B2B2B] text-[#C8C8C8] hover:text-white transition-colors flex items-center justify-between gap-2 border-b border-[#2B2B2B] last:border-b-0"
                          >
                            <div className="flex items-center gap-2.5 flex-1 min-w-0">
                              <svg className="w-3.5 h-3.5 text-[#707070] shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 8v4l3 3m6-3a9 9 0 11-18 0 9 9 0 0118 0z" />
                              </svg>
                              <span className="text-[12px] font-semibold truncate">{item}</span>
                            </div>
                            <button
                              type="button"
                              onMouseDown={(e) => {
                                e.preventDefault()
                                e.stopPropagation()
                                deleteSearchHistoryItem(item)
                              }}
                              className="w-7 h-7 rounded-full flex items-center justify-center text-[#707070] hover:text-[#EF4444] hover:bg-white/5 shrink-0"
                              title="Xóa lịch sử"
                            >
                              <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2.5} d="M6 18L18 6M6 6l12 12" />
                              </svg>
                            </button>
                          </div>
                        ))}
                      </div>
                    )}

                    {searchQuery.length >= 2 && searchSource === 'youtube' && isLoadingSuggestions && (
                      <div className="px-3 py-3 text-center text-[#888] text-[11px] font-medium">
                        Đang tìm gợi ý…
                      </div>
                    )}

                    {searchQuery.length >= 2 && searchSource === 'youtube' && !isLoadingSuggestions && suggestions.length > 0 && suggestions.map((item, index) => {
                      const text = typeof item === 'string' ? item : item.title
                      return (
                        <div
                          key={index}
                          onMouseDown={(e) => {
                            e.preventDefault()
                            justSelectedSuggestionRef.current = true
                            setSearchQuery(text)
                            setShowSuggestions(false)
                            handleSearchWithQuery(text)
                          }}
                          className={`px-3 py-2.5 cursor-pointer transition-colors border-b border-[#2B2B2B] last:border-b-0 flex items-center gap-2.5 ${
                            index === suggestionIndex
                              ? 'bg-[#2478C8] text-white'
                              : 'hover:bg-[#2B2B2B] text-[#E8E8E8]'
                          }`}
                        >
                          <svg
                            className={`w-3.5 h-3.5 shrink-0 ${index === suggestionIndex ? 'text-white' : 'text-[#2478C8]'}`}
                            fill="none"
                            stroke="currentColor"
                            viewBox="0 0 24 24"
                          >
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2.5} d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
                          </svg>
                          <span className="text-[12px] font-semibold truncate flex-1">{text}</span>
                        </div>
                      )
                    })}
                  </div>
                )}
              </div>
            </div>
          </div>
          <div className="flex-1 overflow-y-auto custom-scrollbar p-2 space-y-1">
            {results.length > 0 ? results.map((video) => (
              <div key={video.id}
                className="flex items-center gap-2 p-2 rounded-lg bg-[#2B2B2B] border border-[#3C3C3C] hover:border-[#2478C8]/50">
                <img
                  src={video.source === 'youtube' ? `https://i.ytimg.com/vi/${video.id}/default.jpg` : video.thumbnail}
                  alt="" className="w-16 h-10 object-cover rounded bg-black shrink-0"
                />
                <div className="flex-1 min-w-0">
                  <p className="text-[12px] font-semibold text-white truncate">{decodeHtmlEntities(video.title)}</p>
                  <p className="text-[10px] text-[#A0A0A0] truncate">{decodeHtmlEntities(video.channelTitle || '')}</p>
                </div>
                <button type="button"
                  onClick={(e) => { e.preventDefault(); handleCommand('addToSelected', { video }, e) }}
                  className="h-9 px-3 rounded-lg bg-[#333] border border-[#4E4E4E] text-[11px] font-bold text-white hover:bg-[#2478C8] shrink-0">
                  + Chờ
                </button>
                <button type="button"
                  onClick={(e) => confirmPlayVideo(video, 'search', e)}
                  className="h-9 px-3 rounded-lg bg-[#2478C8] text-white text-[11px] font-bold shrink-0"
                  title="Phát ngay">
                  Phát
                </button>
              </div>
            )) : (
              <div className="h-full flex items-center justify-center text-[#666] text-[11px] font-semibold uppercase tracking-wide py-12">
                {isSearching ? 'Đang tìm…' : 'Gõ tên bài rồi bấm Tìm'}
              </div>
            )}
          </div>
        </div>
      </div>
    )
  }

  const renderDesktopLayout = () => {
    // ShowCue embed → desk gọn, không layout marketing 3 cột
    if (isOperatorDesk) return renderShowCueDeskLayout()

    const soundEffects = remoteState?.soundEffects || {}
    const effectsFromConfig = Object.entries(soundEffects).map(([id, item], index) => ({
      id,
      ...buildEffectUiConfig(id, typeof item === 'object' ? item : {}, index)
    }))

    return (
      <div className="flex-1 flex flex-col min-h-0 w-full bg-[#050816] relative overflow-hidden">
        {/* Desktop Header */}
        <header className="h-[60px] bg-[#0B1224]/85 backdrop-blur-md border-b border-white/5 flex items-center justify-between px-6 shrink-0 z-30">
          <div
            className="flex items-center gap-3 cursor-pointer"
            onClick={() => navigate('/')}
          >
            <img src={appLogo} alt="Logo" className="w-8 h-8 object-contain" />
            <span className="font-black text-base tracking-wider text-white uppercase bg-gradient-to-r from-white via-slate-200 to-slate-400 bg-clip-text text-transparent">
              KTV APP
            </span>
          </div>
          <div className="text-xs font-black text-slate-300 tracking-[0.15em] uppercase text-center flex-1">
            HỆ THỐNG ĐIỀU KHIỂN TỪ XA CHUYÊN NGHIỆP
          </div>
          <div className="flex items-center gap-3">
            <div className={`w-2.5 h-2.5 rounded-full ${isConnected ? 'bg-green-500 animate-pulse shadow-[0_0_10px_rgba(34,197,94,0.6)]' : 'bg-[#EF4444]'}`} />
            <span className="text-[10px] font-black tracking-widest text-slate-400 uppercase">{isConnected ? 'ONLINE' : 'OFFLINE'}</span>
          </div>
        </header>

        {/* 3-Column Content Container */}
        <div className="flex-1 grid grid-cols-12 min-h-0 divide-x divide-white/5 pb-[80px]">

          {/* COLUMN 1: LEFT PANEL (Control & Settings & FX) - col-span-3 */}
          <div className="col-span-3 flex flex-col min-h-0 overflow-y-auto custom-scrollbar p-5 space-y-6 bg-[#070a18]/40">
            {/* Session widget */}
            <div className="border p-4 rounded-2xl shadow-lg space-y-2 bg-[#0B1224] border-white/5">
              <span className="text-[9px] font-black text-slate-500 uppercase tracking-widest block">
                Mã kết nối (Token)
              </span>
              <div className="flex items-center justify-between gap-3">
                <span className="text-lg font-mono font-black text-[#4F7CFF] bg-[#4F7CFF]/10 px-3 py-1 rounded-xl border border-[#4F7CFF]/20 select-text">
                  {sessionToken}
                </span>
                <span className="text-[8px] font-bold uppercase text-slate-600">
                  Remote Active
                </span>
              </div>
            </div>

            {/* System Actions Bar (Desktop Mode) */}
            <div className="bg-[#0B1224] border border-white/5 p-4 rounded-2xl shadow-lg space-y-3">
              <span className="text-[9px] font-black text-slate-500 uppercase tracking-widest block mb-1">Tác vụ nhanh</span>

              <div className="grid grid-cols-2 gap-2">
                <button
                  onClick={toggleKaraoke}
                  className={`py-2.5 px-3 rounded-xl text-[10px] font-black uppercase tracking-wider flex items-center justify-center gap-2 transition-all border ${isKaraokeMode
                    ? 'bg-[#4F7CFF]/15 border-[#4F7CFF]/30 text-[#4F7CFF] shadow-lg shadow-[#4F7CFF]/5'
                    : 'bg-white/5 border-transparent text-slate-400 hover:text-white'
                    }`}
                >
                  <FontAwesomeIcon icon={faMicrophone} />
                  <span>Karaoke</span>
                </button>

                <button
                  onClick={toggleAutoPlay}
                  className={`py-2.5 px-3 rounded-xl text-[10px] font-black uppercase tracking-wider flex items-center justify-center gap-2 transition-all border ${autoPlayNext
                    ? 'bg-[#8B5CF6]/15 border-[#8B5CF6]/30 text-[#8B5CF6] shadow-lg shadow-[#8B5CF6]/5'
                    : 'bg-white/5 border-transparent text-slate-400 hover:text-white'
                    }`}
                >
                  <FontAwesomeIcon icon={faShuffle} />
                  <span>Tự động</span>
                </button>

                <button
                  onClick={() => handleCommand('fullscreenVideo')}
                  className={`py-2.5 px-3 rounded-xl text-[10px] font-black uppercase tracking-wider flex items-center justify-center gap-2 transition-all border ${isFullscreen
                    ? 'bg-orange-500/15 border-orange-500/30 text-orange-500 shadow-lg shadow-orange-500/5'
                    : 'bg-white/5 border-transparent text-slate-400 hover:text-white'
                    }`}
                >
                  <FontAwesomeIcon icon={isFullscreen ? faCompress : faExpand} />
                  <span>Toàn màn hình</span>
                </button>

                <button
                  onClick={() => handleCommand('stopPlayback')}
                  className="py-2.5 px-3 rounded-xl text-[10px] font-black uppercase tracking-wider flex items-center justify-center gap-2 bg-[#EF4444]/10 hover:bg-[#EF4444]/20 border border-[#EF4444]/20 text-[#EF4444] transition-all"
                >
                  <FontAwesomeIcon icon={faPowerOff} />
                  <span>Tắt bài</span>
                </button>
              </div>
            </div>

            {/* Settings Danger zone */}
            <div className="bg-[#EF4444]/5 border border-[#EF4444]/15 p-4 rounded-2xl space-y-2">
              <span className="text-[8px] font-black text-[#EF4444] uppercase tracking-widest block">Ngắt kết nối</span>
              <button
                onClick={() => {
                  if (window.confirm('Bạn có chắc muốn ngắt kết nối với thiết bị chính?')) {
                    setSearchParams({})
                    navigate('/')
                  }
                }}
                className="w-full py-2 bg-[#EF4444]/10 hover:bg-[#EF4444]/20 border border-[#EF4444]/25 text-[#EF4444] rounded-xl text-[9px] font-black uppercase tracking-widest transition-all active:scale-[0.98]"
              >
                Đăng xuất Remote
              </button>
            </div>
          </div>

          {/* COLUMN 2: CENTER PANEL (Search & Results) - col-span-6 */}
          <div className="col-span-6 flex flex-col min-h-0 p-5 space-y-4">
            <div className="bg-[#0B1224] border border-white/5 p-4 rounded-2xl shadow-lg space-y-3 shrink-0">
              <div className="flex items-center justify-between gap-4">
                <span className="text-[10px] font-black text-slate-300 uppercase tracking-widest">Tìm kiếm bài hát</span>

                {/* Source toggle */}
                <div className="flex bg-black/40 p-0.5 rounded-lg border border-white/5 w-[200px]">
                  <button
                    type="button"
                    onClick={() => switchSearchSource('youtube')}
                    className={`flex-1 py-1 rounded-md text-[8px] font-black flex items-center justify-center gap-1.5 transition-all uppercase tracking-widest ${searchSource === 'youtube' ? 'bg-[#EF4444] text-white shadow-md' : 'text-slate-500 hover:text-slate-300'}`}
                  >
                    <Youtube size={8} /> YouTube
                  </button>
                  <button
                    type="button"
                    onClick={() => switchSearchSource('soundcloud')}
                    className={`flex-1 py-1 rounded-md text-[8px] font-black flex items-center justify-center gap-1.5 transition-all uppercase tracking-widest ${searchSource === 'soundcloud' ? 'bg-[#FF5500] text-white shadow-md' : 'text-slate-500 hover:text-slate-300'}`}
                  >
                    <FontAwesomeIcon icon={faMusic} className="text-[8px]" /> SoundCloud
                  </button>
                </div>
              </div>

              {/* Form Search */}
              <div ref={desktopSearchContainerRef} className="relative">
                <form onSubmit={handleSearchSubmit} className={`relative flex items-center transition-all duration-300`}>
                  <input
                    ref={desktopInputRef}
                    className="w-full bg-black/35 border border-white/5 text-white text-xs rounded-xl py-3 pl-4 transition-all placeholder:text-slate-600 font-bold focus:bg-black/50 focus:border-[#4F7CFF]/50"
                    placeholder={isKaraokeMode ? "Tìm bài hát Karaoke trên YouTube..." : "Tìm bài hát hoặc nghệ sĩ..."}
                    value={searchQuery}
                    onChange={e => setSearchQuery(e.target.value)}
                    onFocus={() => {
                      setIsSearchFocused(true)
                      setShowSuggestions(true)
                    }}
                    onBlur={() => {
                      setIsSearchFocused(false)
                      // Delay closing to allow tap/click on suggestions to be registered
                      setTimeout(() => {
                        setShowSuggestions(false)
                      }, 200)
                    }}
                    onKeyDown={(e) => handleKeyDownSuggestions(e, desktopInputRef)}
                    style={{ paddingRight: isSearchFocused ? '16px' : '48px' }}
                  />
                  <button 
                    type="submit" 
                    className={`absolute right-2 w-8 h-8 bg-[#4F7CFF] rounded-lg flex items-center justify-center text-white shadow hover:bg-[#4F7CFF]/90 transition-all duration-300 active:scale-95 ${isSearchFocused ? 'opacity-0 pointer-events-none' : 'opacity-100'}`}
                  >
                    {isSearching ? <div className="animate-spin w-3.5 h-3.5 border-2 border-white rounded-full border-t-transparent" /> : <Search size={14} />}
                  </button>
                </form>

                {/* Suggestions Dropdown (Desktop) */}
                {showSuggestions && (
                  (searchQuery.length >= 2 && searchSource === 'youtube') ||
                  (searchQuery.length < 2 && searchHistory.length > 0)
                ) && (
                  <div className="absolute z-50 w-full mt-2 bg-black border border-white/10 rounded-2xl shadow-[0_25px_60px_rgba(0,0,0,0.85)] max-h-[350px] overflow-y-auto custom-scrollbar overflow-x-hidden animate-fadeIn">
                    {/* Search History for Desktop */}
                    {searchQuery.length < 2 && searchHistory.length > 0 && (
                      <div className="py-1">
                        <div className="px-5 py-2.5 text-[10px] font-black text-slate-500 uppercase tracking-widest flex items-center justify-between border-b border-white/5 mb-1">
                          <span>Lịch sử tìm kiếm gần đây</span>
                          <button
                            type="button"
                            onMouseDown={(e) => {
                              e.preventDefault()
                              clearSearchHistory()
                            }}
                            className="text-red-400 hover:text-red-300 transition-colors uppercase text-[9px] font-bold px-2 py-1 rounded hover:bg-red-500/10"
                          >
                            Xóa tất cả
                          </button>
                        </div>
                        {searchHistory.map((item, index) => (
                          <div
                            key={index}
                            onMouseDown={(e) => {
                              e.preventDefault()
                              justSelectedSuggestionRef.current = true
                              setSearchQuery(item)
                              setShowSuggestions(false)
                              handleSearchWithQuery(item)
                            }}
                            className="px-5 py-3 cursor-pointer hover:bg-white/5 text-slate-300 hover:text-white transition-all duration-150 flex items-center justify-between gap-3 border-b border-white/5 last:border-b-0"
                          >
                            <div className="flex items-center gap-4.5 flex-1 min-w-0">
                              <svg className="w-4 h-4 text-slate-500 shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 8v4l3 3m6-3a9 9 0 11-18 0 9 9 0 0118 0z" />
                              </svg>
                              <span className="text-[14px] font-semibold truncate tracking-wide">{item}</span>
                            </div>
                            <button
                              type="button"
                              onMouseDown={(e) => {
                                e.preventDefault()
                                e.stopPropagation()
                                deleteSearchHistoryItem(item)
                              }}
                              className="w-8 h-8 rounded-full flex items-center justify-center bg-white/0 hover:bg-white/10 text-slate-500 hover:text-red-400 transition-all shrink-0 active:scale-90"
                              title="Xóa lịch sử"
                            >
                              <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2.5} d="M6 18L18 6M6 6l12 12" />
                              </svg>
                            </button>
                          </div>
                        ))}
                      </div>
                    )}

                    {/* API Suggestions for Desktop */}
                    {searchQuery.length >= 2 && isLoadingSuggestions && (
                      <div className="px-5 py-4 text-center text-slate-400 text-sm font-medium flex items-center justify-center gap-2">
                        <span className="inline-block animate-spin">⏳</span>
                        Đang tìm gợi ý...
                      </div>
                    )}

                    {searchQuery.length >= 2 && !isLoadingSuggestions && suggestions.length > 0 && suggestions.map((item, index) => {
                      const text = typeof item === 'string' ? item : item.title
                      return (
                        <div
                          key={index}
                          onMouseDown={(e) => {
                            e.preventDefault() // Prevent blur so event fires
                            justSelectedSuggestionRef.current = true // Mark as selected
                            setSearchQuery(text)
                            setShowSuggestions(false)
                            handleSearchWithQuery(text)
                          }}
                          className={`px-5 py-3 cursor-pointer transition-all duration-150 border-b border-white/5 last:border-b-0 flex items-center gap-4.5 ${
                            index === suggestionIndex 
                              ? 'bg-[#4F7CFF] text-white font-bold shadow-[0_4px_12px_rgba(79,124,255,0.25)]' 
                              : 'hover:bg-white/5 text-slate-100'
                          }`}
                        >
                          {/* Search Icon */}
                          <svg 
                            className={`w-4 h-4 flex-shrink-0 transition-colors ${
                              index === suggestionIndex ? 'text-white' : 'text-[#4F7CFF]'
                            }`} 
                            fill="none" 
                            stroke="currentColor" 
                            viewBox="0 0 24 24"
                          >
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2.5} d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
                          </svg>
                          
                          {/* Suggestion Text */}
                          <span className="text-[14px] font-semibold truncate flex-1 tracking-wide leading-relaxed">
                            {text}
                          </span>

                          {/* Arrow/Fill Icon */}
                          <svg 
                            className={`w-4 h-4 flex-shrink-0 transition-colors ${
                              index === suggestionIndex ? 'text-white/80' : 'text-slate-500'
                            }`} 
                            fill="none" 
                            stroke="currentColor" 
                            viewBox="0 0 24 24"
                          >
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2.5} d="M10 6H6a2 2 0 00-2 2v10a2 2 0 002 2h10a2 2 0 002-2v-4M14 4h6m0 0v6m0-6L10 14" />
                          </svg>
                        </div>
                      )
                    })}
                  </div>
                )}
              </div>

              {/* Status Indicator */}
              {isKaraokeMode && (
                <div className="inline-flex items-center gap-1.5 text-[8px] text-[#10B981] font-black uppercase tracking-wider bg-[#10B981]/15 px-2 py-0.5 rounded-full border border-[#10B981]/25">
                  <span className="w-1.5 h-1.5 rounded-full bg-[#10B981] animate-pulse" />
                  Đang bật tự động lọc từ khóa Karaoke
                </div>
              )}
            </div>

            {/* Search Results Grid - 3 columns on Desktop */}
            <div className="flex-1 overflow-y-auto custom-scrollbar pr-1 pb-4">
              <div className="grid grid-cols-3 gap-3">
                {getFilteredResults(searchResults).map((video) => (
                  <div
                    key={video.id}
                    onClick={() => setExpandedResultId(prev => prev === video.id ? null : video.id)}
                    className="group flex flex-col bg-[#0B1224] border border-white/5 rounded-xl overflow-hidden hover:border-[#4F7CFF]/30 transition-all relative shadow-md cursor-pointer"
                  >
                    {/* Play Area (Thumbnail) */}
                    <div
                      className="relative w-full aspect-video bg-slate-950 overflow-hidden"
                    >
                      <img
                        src={video.source === 'youtube' ? `https://i.ytimg.com/vi/${video.id}/mqdefault.jpg` : video.thumbnail}
                        className="w-full h-full object-cover group-hover:scale-105 transition-transform duration-500 ease-out"
                        alt=""
                        onLoad={(e) => { e.target.style.opacity = 1 }}
                        style={{ opacity: 0 }}
                        onError={(e) => {
                          if (video.source === 'youtube' && !e.target.src.includes('mqdefault')) {
                            e.target.src = `https://i.ytimg.com/vi/${video.id}/mqdefault.jpg`
                          }
                        }}
                      />
                      {video.duration && (
                        <div className="absolute bottom-1 right-1 px-1 py-0.5 bg-black/75 backdrop-blur-md text-[8px] text-white font-mono rounded border border-white/5">
                          {video.source === 'soundcloud' ? formatSoundCloudDuration(video.duration) : formatYouTubeDuration(video.duration)}
                        </div>
                      )}
                      {/* Play Overlay */}
                      <div className="absolute inset-0 bg-black/45 opacity-0 group-hover:opacity-100 flex items-center justify-center transition-all duration-200">
                        <div className="w-8 h-8 bg-[#EF4444] text-white rounded-full flex items-center justify-center shadow-lg transform scale-90 group-hover:scale-100 transition-transform">
                          <FontAwesomeIcon icon={faPlay} className="text-[10px] ml-0.5" />
                        </div>
                      </div>
                    </div>

                    {/* Info Area & Add Button */}
                    <div className="p-2 flex flex-col flex-1 gap-1">
                      <div className="flex-1 min-w-0">
                        <h3 className="text-[9px] font-black text-white line-clamp-2 leading-tight uppercase tracking-tight mb-0.5 group-hover:text-[#4F7CFF] transition-colors">
                          {decodeHtmlEntities(video.title)}
                        </h3>
                        <p className="text-[8px] font-bold text-slate-500 uppercase tracking-wider truncate">
                          {decodeHtmlEntities(video.channelTitle)}
                        </p>
                      </div>

                      {/* Bottom Row: Triple Actions buttons (shown when expanded) */}
                      {expandedResultId === video.id ? (
                        <div className="grid grid-cols-3 gap-1.5 pt-1.5 border-t border-white/5 mt-1">
                          <button
                            onClick={(e) => { e.stopPropagation(); confirmPlayVideo(video, 'search', e) }}
                            className="py-1.5 bg-[#10B981]/5 hover:bg-[#10B981]/15 text-[#10B981] border border-[#10B981]/25 rounded-lg flex items-center justify-center gap-1 active:scale-95 transition-all text-[8px] font-black uppercase tracking-wider"
                          >
                            <FontAwesomeIcon icon={faPlay} className="text-[7px]" />
                            <span>Play</span>
                          </button>

                          <button
                            onClick={(e) => { e.stopPropagation(); confirmAddToSelected(video, e) }}
                            className="py-1.5 bg-white/5 hover:bg-white/10 text-white border border-white/10 rounded-lg flex items-center justify-center gap-1 active:scale-95 transition-all text-[8px] font-black uppercase tracking-wider"
                          >
                            <FontAwesomeIcon icon={faPlus} className="text-[7px]" />
                            <span>Add</span>
                          </button>

                          <button
                            onClick={(e) => {
                              e.stopPropagation()
                              handleCommand('priority', { videoId: video.id }, e)
                              const toastId = Date.now()
                              setToasts([{ id: toastId, message: `Play next: ${video.title?.slice(0, 18)}...` }])
                              setTimeout(() => setToasts(prev => prev.filter(t => t.id !== toastId)), 3000)
                            }}
                            className="py-1.5 bg-[#F59E0B]/5 hover:bg-[#F59E0B]/15 text-[#F59E0B] border border-[#F59E0B]/25 rounded-lg flex items-center justify-center gap-1 active:scale-95 transition-all text-[8px] font-black uppercase tracking-wider"
                          >
                            <FontAwesomeIcon icon={faArrowUp} className="text-[7px]" />
                            <span>Next</span>
                          </button>
                        </div>
                      ) : (
                        <div className="flex items-center justify-between gap-1 pt-1.5 border-t border-white/5 mt-1">
                          <span className="text-[8px] font-bold text-slate-600 uppercase truncate">
                            {video.viewCount ? `${formatViewCount(video.viewCount)}` : 'SOUNDCLOUD'}
                          </span>
                        </div>
                      )}
                    </div>
                  </div>
                ))}

                {searchResults.length === 0 && !isSearching && (
                  <div className="col-span-full py-24 text-center flex flex-col items-center gap-3 opacity-25">
                    <Search size={60} strokeWidth={1.5} className="text-slate-400 animate-pulse" />
                    <p className="font-black text-xs uppercase tracking-[0.25em] text-slate-400">Tìm kiếm bài hát mong muốn</p>
                  </div>
                )}
              </div>
            </div>
          </div>

          {/* COLUMN 3: RIGHT PANEL (Queue & History) - col-span-3 */}
          <div className="col-span-3 flex flex-col min-h-0 divide-y divide-white/5 bg-[#070a18]/40">

            {/* Upper: Queue list */}
            <div className="flex-1 flex flex-col min-h-0 p-4 overflow-hidden">
              <div className="flex items-center justify-between pb-2 mb-2 shrink-0">
                <span className="text-[9px] font-black text-slate-500 uppercase tracking-widest flex items-center gap-1.5">
                  <FontAwesomeIcon icon={faListCheck} className="text-[#4F7CFF]" />
                  Danh sách chờ ({filteredPlaylist.length})
                </span>
                {filteredPlaylist.length > 0 && (
                  <button
                    onClick={() => handleCommand('clearAll', {})}
                    className="px-2 py-0.5 rounded bg-[#EF4444]/10 hover:bg-[#EF4444]/20 text-[#EF4444] text-[8px] font-black uppercase tracking-widest transition-all"
                  >
                    Xóa sạch
                  </button>
                )}
              </div>

              <div className="flex-1 overflow-y-auto custom-scrollbar space-y-2 pr-1">
                {filteredPlaylist.length === 0 ? (
                  <div className="h-full flex flex-col items-center justify-center opacity-15 py-12">
                    <FontAwesomeIcon icon={faListCheck} className="text-2xl mb-2" />
                    <span className="text-[10px] font-black uppercase tracking-wider">Trống</span>
                  </div>
                ) : (
                  filteredPlaylist.map((video, idx) => (
                    <div key={`${video.id}-${idx}`} className="group flex gap-2 p-1.5 bg-[#0B1224] border border-white/5 rounded-xl hover:border-white/10 transition-all items-center">
                      <div className="w-4 text-center text-[9px] font-black text-slate-600 font-mono">
                        {idx + 1}
                      </div>
                      <div className="relative w-12 aspect-video rounded overflow-hidden bg-slate-950 shrink-0">
                        <img
                          src={video.source === 'youtube' ? `https://i.ytimg.com/vi/${video.id}/default.jpg` : video.thumbnail}
                          className="w-full h-full object-cover"
                          alt=""
                        />
                      </div>
                      <div className="flex-1 min-w-0">
                        <h3 className="text-[10px] font-black text-white truncate uppercase tracking-tight">{decodeHtmlEntities(video.title)}</h3>
                      </div>
                      <div className="flex items-center gap-1 shrink-0">
                        <button
                          onClick={(e) => confirmPlayVideo(video, 'selected', e)}
                          className="w-6 h-6 flex items-center justify-center bg-[#EF4444]/15 hover:bg-[#EF4444]/20 text-[#EF4444] rounded border border-white/5 active:scale-90 transition-all"
                          title="Phát ngay"
                        >
                          <FontAwesomeIcon icon={faBolt} className="text-[8px]" />
                        </button>
                        <button
                          onClick={(e) => handleCommand('priority', { videoId: video.id }, e)}
                          className="w-6 h-6 flex items-center justify-center bg-white/5 hover:bg-[#4F7CFF]/15 text-[#4F7CFF] rounded border border-white/5 active:scale-90 transition-all"
                          title="Ưu tiên"
                        >
                          <FontAwesomeIcon icon={faArrowUp} className="text-[8px]" />
                        </button>
                        <button
                          onClick={(e) => handleCommand('removeFromSelected', { videoId: video.id }, e)}
                          className="w-6 h-6 flex items-center justify-center bg-white/5 hover:bg-red-600/10 text-slate-500 hover:text-[#EF4444] rounded border border-white/5 active:scale-90 transition-all"
                          title="Xóa"
                        >
                          <FontAwesomeIcon icon={faTrash} className="text-[8px]" />
                        </button>
                      </div>
                    </div>
                  ))
                )}
              </div>
            </div>

            {/* Lower: History list */}
            <div className="flex-1 flex flex-col min-h-0 p-4 overflow-hidden">
              <span className="text-[9px] font-black text-slate-500 uppercase tracking-widest flex items-center gap-1.5 pb-2 mb-2 shrink-0">
                <FontAwesomeIcon icon={faClock} className="text-[#8B5CF6]" />
                Lịch sử phát ({watchHistory.length})
              </span>

              <div className="flex-1 overflow-y-auto custom-scrollbar space-y-2 pr-1">
                {watchHistory.length === 0 ? (
                  <div className="h-full flex flex-col items-center justify-center opacity-15 py-12">
                    <FontAwesomeIcon icon={faClock} className="text-2xl mb-2" />
                    <span className="text-[10px] font-black uppercase tracking-wider">Trống</span>
                  </div>
                ) : (
                  watchHistory.slice(0, 15).map((video, idx) => (
                    <div key={`${video.id}-${video.timestamp || idx}`} className="group flex gap-2.5 p-1.5 bg-[#0B1224] border border-white/5 rounded-xl hover:border-white/10 transition-all items-center">
                      <div
                        onClick={(e) => confirmPlayVideo(video, 'history', e)}
                        className="relative w-12 aspect-video rounded overflow-hidden bg-slate-950 shrink-0 cursor-pointer"
                      >
                        <img
                          src={video.source === 'youtube' ? `https://i.ytimg.com/vi/${video.id}/default.jpg` : video.thumbnail}
                          className="w-full h-full object-cover"
                          alt=""
                        />
                        <div className="absolute inset-0 bg-black/40 opacity-0 group-hover:opacity-100 flex items-center justify-center transition-opacity">
                          <FontAwesomeIcon icon={faPlay} className="text-white text-[8px]" />
                        </div>
                      </div>
                      <div className="flex-1 min-w-0">
                        <h3 className="text-[10px] font-black text-white truncate uppercase tracking-tight">{decodeHtmlEntities(video.title)}</h3>
                      </div>
                      <div className="flex items-center gap-1 shrink-0">
                        <button
                          onClick={(e) => confirmAddToSelected(video, e)}
                          className="w-6 h-6 flex items-center justify-center bg-white/5 hover:bg-[#4F7CFF]/15 text-[#4F7CFF] rounded border border-white/5 active:scale-90 transition-all"
                          title="Chọn lại"
                        >
                          <FontAwesomeIcon icon={faPlus} className="text-[8px]" />
                        </button>
                      </div>
                    </div>
                  ))
                )}
              </div>
            </div>

          </div>
        </div>

        {/* BOTTOM WIDE PLAYER (Desktop Player Bar) */}
        {renderDesktopPlayerBar()}
      </div>
    )
  }

  const renderDesktopPlayerBar = () => {
    return (
      <div className="fixed bottom-0 left-0 right-0 h-[80px] bg-[#0B1224]/95 backdrop-blur-xl border-t border-white/5 px-6 flex items-center justify-between gap-6 z-40 shadow-[0_-15px_35px_rgba(0,0,0,0.6)]">
        {/* Left Info: Thumbnail, Title, Artist */}
        <div className="flex items-center gap-3.5 min-w-0 w-[30%]">
          <div className="w-12 h-12 rounded-xl overflow-hidden bg-slate-950 shrink-0 border border-white/10 relative shadow-md">
            {currentVideo ? (
              <img
                src={currentVideo.source === 'youtube' ? `https://i.ytimg.com/vi/${currentVideo.id}/default.jpg` : currentVideo.thumbnail}
                className="w-full h-full object-cover"
                alt=""
              />
            ) : (
              <div className="w-full h-full flex items-center justify-center text-slate-700">
                <FontAwesomeIcon icon={faMusic} className="text-sm opacity-40 animate-pulse" />
              </div>
            )}
          </div>
          <div className="min-w-0">
            <h4 className="text-xs font-black text-white truncate uppercase tracking-tight leading-snug">
              {currentVideo ? decodeHtmlEntities(currentVideo.title) : 'Không có bài đang phát'}
            </h4>
            <p className="text-[9px] font-bold text-slate-500 uppercase tracking-wider truncate mt-0.5">
              {currentVideo ? decodeHtmlEntities(currentVideo.channelTitle) : 'Đang chờ điều khiển...'}
            </p>
          </div>
        </div>

        {/* Center Control & Progress Bar */}
        <div className="flex flex-col items-center justify-center gap-1.5 flex-1 w-[40%]">
          {/* Controls */}
          <div className="flex items-center gap-5">
            {currentVideo && (
              <button
                onClick={(e) => handleCommand('replay', {}, e)}
                className="w-8 h-8 flex items-center justify-center bg-white/5 hover:bg-white/10 text-slate-400 hover:text-white rounded-full transition-all active:scale-90"
                title="Phát lại"
              >
                <FontAwesomeIcon icon={faRedo} className="text-xs" />
              </button>
            )}

            <button
              onClick={(e) => handleCommand('playPause', {}, e)}
              className="w-11 h-11 flex items-center justify-center bg-[#4F7CFF] hover:bg-[#4F7CFF]/90 text-white rounded-full shadow-lg shadow-[#4F7CFF]/15 active:scale-90 transition-all hover:scale-105"
              title="Phát / Tạm dừng"
            >
              <FontAwesomeIcon icon={faPlay} className="text-sm ml-0.5" />
            </button>

            <button
              onClick={(e) => !isTrackChangeLocked && handleCommand('next', {}, e)}
              disabled={isTrackChangeLocked}
              className={`w-8 h-8 flex items-center justify-center rounded-full transition-all active:scale-90 ${isTrackChangeLocked ? 'bg-amber-500/20 text-amber-300 opacity-80' : 'bg-white/5 hover:bg-white/10 text-slate-400 hover:text-white'}`}
              title={isTrackChangeLocked ? 'Đang khóa chuyển bài' : 'Chuyển bài'}
            >
              <FontAwesomeIcon icon={isTrackChangeLocked ? faLock : faForwardStep} className="text-xs" />
            </button>
          </div>

          {/* Progress Seek bar (chỉ click seek được trên YouTube) */}
          <div className="flex items-center gap-3 w-full max-w-[500px]">
            <span className="text-[8px] font-black text-slate-500 font-mono w-8 text-right">{formatTime(currentTime)}</span>
            <input
              type="range"
              min="0"
              max={duration || 100}
              value={currentTime}
              onChange={(e) => {
                isSeekingRef.current = true
                setCurrentTime(parseFloat(e.target.value))
              }}
              onPointerUp={(e) => {
                isSeekingRef.current = false
                const targetTime = parseFloat(e.target.value)
                handleCommand('seek', { seconds: targetTime }, e)
              }}
              disabled={!currentVideo || duration <= 0}
              className="spotify-slider flex-1 cursor-pointer disabled:opacity-40"
            />
            <span className="text-[8px] font-black text-slate-500 font-mono w-8">{formatTime(duration)}</span>
          </div>
        </div>

        {/* Right Volume Bar */}
        <div className="flex items-center justify-end gap-3 w-[30%]">
          <FontAwesomeIcon icon={faVolumeDown} className="text-xs text-slate-500" />
          <div className="w-[120px] flex items-center bg-black/25 px-2 py-1.5 rounded-xl border border-white/5">
            <input
              type="range"
              min="0"
              max="100"
              value={volume}
              onChange={(e) => {
                setVolume(parseInt(e.target.value))
              }}
              onPointerUp={(e) => {
                const val = parseInt(e.target.value)
                lastVolumeChangeRef.current = Date.now()
                handleCommand('volume', { value: val }, e)
              }}
              className="spotify-slider flex-1 cursor-pointer"
            />
          </div>
          <span className="text-[10px] font-black text-[#4F7CFF] font-mono min-w-[36px] text-right">{volume}%</span>
          <FontAwesomeIcon icon={faVolumeUp} className="text-xs text-slate-500" />
        </div>
      </div>
    )
  }



  // forcePhone → only phone; forcePc → only pc; neither → responsive (lg breakpoint)

  return (
    <div className={`w-full h-screen h-dvh overflow-hidden text-white flex flex-col font-sans select-none pb-safe ${
      isOperatorDesk
        ? 'bg-[#1C1C1C]'
        : 'bg-[#050816] bg-gradient-to-br from-[#050816] via-[#0b0f19] to-[#050816] selection:bg-[#4F7CFF]/30'
    }`}>
      {!sessionToken ? (
        <div className="flex-1 flex items-center justify-center p-4">
          {renderTokenEntry()}
        </div>
      ) : (requiresPassword && !isPasswordUnlocked) ? (
        <div className="flex-1 flex items-center justify-center p-4">
          {renderPasswordLockScreen()}
        </div>
      ) : isOperatorDesk ? (
        /* ShowCue embed: always compact desk (Phone/PC chỉ đổi cột) */
        <div className="flex-1 flex flex-col min-h-0">
          {renderShowCueDeskLayout()}
        </div>
      ) : (
        <>
          {/* Mobile / Phone layout */}
          <div className={`flex-1 flex-col min-h-0 ${
            forcePhone ? 'flex' : forcePc ? 'hidden' : 'flex lg:hidden'
          }`}>
            {renderMobileLayout()}
          </div>

          {/* Desktop / PC layout */}
          <div className={`flex-1 flex-col min-h-0 ${
            forcePc ? 'flex' : forcePhone ? 'hidden' : 'hidden lg:flex'
          }`}>
            {renderDesktopLayout()}
          </div>

        </>
      )}

      {/* Toasts Container */}
      <div className="fixed bottom-24 left-1/2 -translate-x-1/2 z-[100] flex flex-col items-center gap-2 pointer-events-none w-[90%] sm:w-[350px]">
        {toasts.map(toast => (
          <div
            key={toast.id}
            className="px-4 py-3 bg-[#4F7CFF]/90 backdrop-blur-xl border border-white/20 rounded-2xl shadow-2xl flex items-center gap-3 animate-slideUp text-white"
          >
            <div className="w-5 h-5 rounded-full bg-white/20 flex items-center justify-center shrink-0">
              <FontAwesomeIcon icon={faCheck} className="text-[9px]" />
            </div>
            <span className="text-[10px] font-black uppercase tracking-widest truncate">{toast.message}</span>
          </div>
        ))}
      </div>

      <style>{`
        .custom-scrollbar::-webkit-scrollbar { width: 4px; }
        .custom-scrollbar::-webkit-scrollbar-track { background: transparent; }
        .custom-scrollbar::-webkit-scrollbar-thumb { background: rgba(255, 255, 255, 0.05); border-radius: 10px; }
        .custom-scrollbar::-webkit-scrollbar-thumb:hover { background: rgba(79, 124, 255, 0.3); }
        .no-scrollbar::-webkit-scrollbar { display: none; }
        .no-scrollbar { -ms-overflow-style: none; scrollbar-width: none; }
        @keyframes slideUp { from { transform: translateY(100%); opacity: 0; } to { transform: translateY(0); opacity: 1; } }
        .animate-slideUp { animation: slideUp 0.25s cubic-bezier(0.16, 1, 0.3, 1) forwards; }

        /* Spotify-style Slider CSS */
        .spotify-slider {
          -webkit-appearance: none;
          appearance: none;
          width: 100%;
          height: 4px;
          background: rgba(255, 255, 255, 0.1);
          border-radius: 9999px;
          outline: none;
          transition: background 0.2s;
        }
        .spotify-slider::-webkit-slider-thumb {
          -webkit-appearance: none;
          appearance: none;
          width: 12px;
          height: 12px;
          border-radius: 50%;
          background: #ffffff;
          cursor: pointer;
          box-shadow: 0 2px 6px rgba(0,0,0,0.4);
          transition: transform 0.1s, background-color 0.1s;
        }
        .spotify-slider::-webkit-slider-thumb:hover {
          transform: scale(1.2);
          background: #4F7CFF;
        }
        .spotify-slider::-moz-range-thumb {
          width: 12px;
          height: 12px;
          border-radius: 50%;
          background: #ffffff;
          cursor: pointer;
          border: none;
          box-shadow: 0 2px 6px rgba(0,0,0,0.4);
          transition: transform 0.1s, background-color 0.1s;
        }
        .spotify-slider::-moz-range-thumb:hover {
          transform: scale(1.2);
          background: #4F7CFF;
        }

        .spotify-slider-purple::-webkit-slider-thumb:hover {
          background: #8B5CF6;
        }
        .spotify-slider-purple::-moz-range-thumb:hover {
          background: #8B5CF6;
        }
      `}</style>
    </div>
  )
}

export default RemoteControl
