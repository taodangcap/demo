/**
 * useDebouncedSearch - Optimized search with debounce
 * Uses use-debounce to avoid firing API calls on every keystroke
 */
import { useCallback } from 'react'
import { useDebouncedCallback } from 'use-debounce'
import { useSearchStore } from '../store/searchStore'
import { usePlayerStore } from '../store/playerStore'
import { searchVideos, getVideoDetails, isPlayableVideo } from '../utils/youtube'
import { searchTracks } from '../utils/soundcloud'

export function useDebouncedSearch() {
  const {
    searchQuery,
    searchSource,
    setSearchResults,
    setIsSearching,
    setSearchError,
    saveSourceResults,
    setActiveTab,
  } = useSearchStore()

  const isKaraokeMode = usePlayerStore((s) => s.isKaraokeMode)

  const executeSearch = useCallback(async (query, source, karaokeMode) => {
    if (!query.trim()) return

    setIsSearching(true)
    setSearchError(null)

    const finalQuery = karaokeMode ? `${query} karaoke` : query
    let results = []

    try {
      if (source === 'youtube') {
        results = await searchVideos(finalQuery, 15)
        if (results.length > 0) {
          const alreadyHasDetails = results.every((r) => r.duration)
          if (!alreadyHasDetails) {
            const videoIds = results.map((r) => r.id)
            const details = await getVideoDetails(videoIds)
            results = results
              .map((result) => {
                const detail = details.find((d) => d.id === result.id)
                return detail
                  ? { ...result, ...detail, source: 'youtube' }
                  : { ...result, source: 'youtube' }
              })
              .filter(isPlayableVideo)
          } else {
            results = results.map((r) => ({ ...r, source: 'youtube' })).filter(isPlayableVideo)
          }
        }
      } else if (source === 'soundcloud') {
        results = await searchTracks(finalQuery, 15)
        results = results.map((r) => ({ ...r, source: 'soundcloud' }))
      }
    } catch (error) {
      console.error('Search error:', error)
      if (source === 'soundcloud') {
        if (error.message?.includes('Proxy server')) {
          setSearchError('Proxy server chưa chạy. Vui lòng mở terminal và chạy: npm run server')
        } else {
          setSearchError('Lỗi khi tìm kiếm SoundCloud.')
        }
      } else {
        setSearchError('Lỗi khi tìm kiếm.')
      }
    }

    setSearchResults(results)
    saveSourceResults(results)
    setIsSearching(false)
    setActiveTab('search')
  }, [setSearchResults, setIsSearching, setSearchError, saveSourceResults, setActiveTab])

  // Debounced version for auto-search on typing (300ms)
  const debouncedSearch = useDebouncedCallback(
    (query, source, karaokeMode) => {
      executeSearch(query, source, karaokeMode)
    },
    300
  )

  // Immediate search (for form submit / button click)
  const searchNow = useCallback(() => {
    executeSearch(searchQuery, searchSource, isKaraokeMode)
  }, [executeSearch, searchQuery, searchSource, isKaraokeMode])

  // Trigger debounced auto-search
  const triggerAutoSearch = useCallback((query) => {
    if (query.trim().length >= 2) {
      debouncedSearch(query, searchSource, isKaraokeMode)
    }
  }, [debouncedSearch, searchSource, isKaraokeMode])

  return {
    searchNow,
    triggerAutoSearch,
    executeSearch,
  }
}

export default useDebouncedSearch
