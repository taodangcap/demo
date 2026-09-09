/**
 * Search Store - Manages search state across YouTube/SoundCloud
 * Inspired by vkara's searchStore using Zustand
 */
import { create } from 'zustand'

export const useSearchStore = create((set, get) => ({
  // Search State
  searchQuery: '',
  searchResults: [],
  isSearching: false,
  searchError: null,
  searchSource: 'youtube', // 'youtube' | 'soundcloud'
  
  // Per-source cached state (preserves results when switching sources)
  youtubeState: { query: '', results: [] },
  soundcloudState: { query: '', results: [] },
  
  // Active tab
  activeTab: 'search', // 'search' | 'selected' | 'effects' | 'history' | 'settings'

  // ===== Actions =====
  
  setSearchQuery: (query) => set({ searchQuery: query }),
  
  setSearchResults: (results) => set({ searchResults: results }),
  
  setIsSearching: (searching) => set({ isSearching: searching }),
  
  setSearchError: (error) => set({ searchError: error }),
  
  setActiveTab: (tab) => set({ activeTab: tab }),

  switchSearchSource: (newSource) => {
    const { searchSource, searchQuery, searchResults } = get()
    if (newSource === searchSource) return
    
    // Save current source state
    if (searchSource === 'youtube') {
      set({ youtubeState: { query: searchQuery, results: searchResults } })
    } else {
      set({ soundcloudState: { query: searchQuery, results: searchResults } })
    }
    
    // Load target source state
    const targetState = newSource === 'youtube' ? get().youtubeState : get().soundcloudState
    
    set({
      searchSource: newSource,
      searchQuery: targetState.query,
      searchResults: targetState.results,
      searchError: null
    })
  },

  // Save search results for current source
  saveSourceResults: (results) => {
    const { searchSource, searchQuery } = get()
    if (searchSource === 'youtube') {
      set({ youtubeState: { query: searchQuery, results } })
    } else {
      set({ soundcloudState: { query: searchQuery, results } })
    }
  },

  clearSearch: () => set({
    searchQuery: '',
    searchResults: [],
    searchError: null
  }),
}))
