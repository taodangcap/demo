// Utility functions for localStorage management

// Helper to get API Base URL (copied from remoteControl.js)
const getApiBaseUrl = () => {
  const host = window.location.hostname;
  const isLocal = host === 'localhost' || host === '127.0.0.1';
  const isLan = /^(192\.168\.|10\.|172\.(1[6-9]|2[0-9]|3[0-1])\.)/.test(host);
  const envUrl = import.meta.env.VITE_REMOTE_SERVER_URL;

  // Ưu tiên localhost/LAN nếu đang chạy local để lưu file JSON đúng chỗ
  if (isLocal || isLan) {
    return `http://${host}:3001`;
  }

  if (envUrl) {
    return envUrl.replace(/\/$/, '');
  }

  return '';
}

export const getFavorites = () => {
  const favorites = localStorage.getItem('karaoke_favorites')
  return favorites ? JSON.parse(favorites) : []
}

export const addToFavorites = (songId) => {
  const favorites = getFavorites()
  if (!favorites.includes(songId)) {
    favorites.push(songId)
    localStorage.setItem('karaoke_favorites', JSON.stringify(favorites))
  }
}

export const removeFromFavorites = (songId) => {
  const favorites = getFavorites()
  const updated = favorites.filter(id => id !== songId)
  localStorage.setItem('karaoke_favorites', JSON.stringify(updated))
}

export const isFavorite = (songId) => {
  const favorites = getFavorites()
  return favorites.includes(songId)
}

export const getPlaylists = () => {
  const playlists = localStorage.getItem('karaoke_playlists')
  return playlists ? JSON.parse(playlists) : []
}

export const savePlaylist = (playlist) => {
  const playlists = getPlaylists()
  const existingIndex = playlists.findIndex(p => p.id === playlist.id)

  if (existingIndex >= 0) {
    playlists[existingIndex] = playlist
  } else {
    playlists.push(playlist)
  }

  localStorage.setItem('karaoke_playlists', JSON.stringify(playlists))
}

export const deletePlaylist = (playlistId) => {
  const playlists = getPlaylists()
  const updated = playlists.filter(p => p.id !== playlistId)
  localStorage.setItem('karaoke_playlists', JSON.stringify(updated))
}

export const getSongRatings = () => {
  const ratings = localStorage.getItem('karaoke_ratings')
  return ratings ? JSON.parse(ratings) : {}
}

export const saveRating = (songId, rating, comment = '') => {
  const ratings = getSongRatings()
  ratings[songId] = { rating, comment, timestamp: Date.now() }
  localStorage.setItem('karaoke_ratings', JSON.stringify(ratings))
}

export const getRating = (songId) => {
  const ratings = getSongRatings()
  return ratings[songId] || null
}

export const STORAGE_KEY_AUTO_PLAY_NEXT = 'youtube_auto_play_next'

export const DEFAULT_SOUND_EFFECTS = {}

export const getSoundEffects = () => {
  const stored = localStorage.getItem('karaoke_sound_effects')
  if (!stored) return DEFAULT_SOUND_EFFECTS

  try {
    const parsed = JSON.parse(stored)
    // Convert old string format to object format if necessary
    const migrated = {}
    Object.keys(parsed).forEach(key => {
      if (typeof parsed[key] === 'string') {
        migrated[key] = { url: parsed[key], volume: 80 }
      } else {
        migrated[key] = parsed[key]
      }
    })
    return migrated
  } catch (e) {
    return DEFAULT_SOUND_EFFECTS
  }
}

// New async version to fetch from server
export const fetchSharedSoundEffects = async () => {
  try {
    const url = `${getApiBaseUrl()}/api/effects`
    const res = await fetch(url)
    if (res.ok) {
      const data = await res.json()
      if (Object.keys(data).length > 0) {
        // Save to localStorage as a cache/backup
        localStorage.setItem('karaoke_sound_effects', JSON.stringify(data))
        return data
      }
    }
    return getSoundEffects()
  } catch (e) {
    console.warn('Failed to fetch shared effects, using local:', e)
    return getSoundEffects()
  }
}

export const saveSoundEffects = async (effects) => {
  // 1. Save to local storage for immediate responsiveness
  localStorage.setItem('karaoke_sound_effects', JSON.stringify(effects))

  // 2. Sync to server for shared access
  try {
    const url = `${getApiBaseUrl()}/api/effects`
    const res = await fetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(effects)
    })
    return res.ok;
  } catch (e) {
    console.error('Failed to sync effects to server:', e)
    return false;
  }
}
