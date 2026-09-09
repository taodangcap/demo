import { useState, useEffect, useRef } from 'react'
import { fetchSearchSuggestions } from '../utils/youtube'

const HISTORY_KEY = 'search_history'
const MAX_HISTORY = 10

export default function SearchAutocomplete({ 
  searchQuery, 
  setSearchQuery, 
  onSearch, 
  searchSource,
  isKaraokeMode,
  className = '',
  onSearchComplete = null,
  onFocus = null,
  onBlur = null
}) {
  const [suggestions, setSuggestions] = useState([])
  const [history, setHistory] = useState([])
  const [showSuggestions, setShowSuggestions] = useState(false)
  const [selectedIndex, setSelectedIndex] = useState(-1)
  const [isLoading, setIsLoading] = useState(false)
  const inputRef = useRef(null)
  const suggestionsRef = useRef(null)
  const justSelectedRef = useRef(false) // Track if user just picked a suggestion

  // Load search history on mount
  useEffect(() => {
    try {
      const stored = localStorage.getItem(HISTORY_KEY)
      if (stored) setHistory(JSON.parse(stored))
    } catch (e) {
      console.error('Failed to load search history:', e)
    }
  }, [])

  // Fetch suggestions when user types (debounced)
  useEffect(() => {
    if (searchSource !== 'youtube') {
      setSuggestions([])
      setShowSuggestions(false)
      return
    }

    // Show history when input is empty/short
    if (searchQuery.length < 2) {
      setSuggestions([])
      setIsLoading(false)
      return
    }

    setIsLoading(true)
    const timer = setTimeout(async () => {
      try {
        const results = await fetchSearchSuggestions(searchQuery)
        setSuggestions(results)
        
        // Only show if the input is still focused AND user didn't just pick one
        if (document.activeElement === inputRef.current && results.length > 0 && !justSelectedRef.current) {
          setShowSuggestions(true)
        }
        justSelectedRef.current = false // Reset after fetch
      } catch (error) {
        console.error('Failed to fetch suggestions:', error)
        setSuggestions([])
      } finally {
        setIsLoading(false)
      }
    }, 300) // 300ms debounce

    return () => clearTimeout(timer)
  }, [searchQuery, searchSource])

  // Close suggestions when clicking outside
  useEffect(() => {
    const handleClickOutside = (e) => {
      if (
        suggestionsRef.current && 
        !suggestionsRef.current.contains(e.target) &&
        inputRef.current &&
        !inputRef.current.contains(e.target)
      ) {
        setShowSuggestions(false)
      }
    }

    document.addEventListener('mousedown', handleClickOutside)
    return () => document.removeEventListener('mousedown', handleClickOutside)
  }, [])

  // Keyboard navigation
  const handleKeyDown = (e) => {
    if (!showSuggestions || suggestions.length === 0) {
      if (e.key === 'Enter') {
        e.preventDefault()
        onSearch(e)
      }
      return
    }

    switch (e.key) {
      case 'ArrowDown':
        e.preventDefault()
        setSelectedIndex(prev => 
          prev < suggestions.length - 1 ? prev + 1 : prev
        )
        break
      case 'ArrowUp':
        e.preventDefault()
        setSelectedIndex(prev => prev > 0 ? prev - 1 : -1)
        break
      case 'Enter':
        e.preventDefault()
        if (selectedIndex >= 0) {
          selectSuggestion(suggestions[selectedIndex])
        } else {
          onSearch(e)
        }
        break
      case 'Escape':
        setShowSuggestions(false)
        setSelectedIndex(-1)
        break
      default:
        break
    }
  }

   const selectSuggestion = (suggestion) => {
     const text = typeof suggestion === 'string' ? suggestion : suggestion.title
     justSelectedRef.current = true // Mark as selected to prevent re-showing
     setSearchQuery(text)
     setShowSuggestions(false)
     setSelectedIndex(-1)
     
     // Blur input to close suggestions
     if (inputRef.current) {
       inputRef.current.blur()
     }
     
     // Save to history
     saveToHistory(text)
     
     // Auto-submit search after selection
     setTimeout(() => {
       onSearch({ preventDefault: () => {} })
       // Notify parent that search is complete
       if (onSearchComplete) {
         onSearchComplete()
       }
     }, 100)
   }

  const saveToHistory = (query) => {
    if (!query || query.trim().length < 2) return
    try {
      const newHistory = [query, ...history.filter(h => h !== query)].slice(0, MAX_HISTORY)
      setHistory(newHistory)
      localStorage.setItem(HISTORY_KEY, JSON.stringify(newHistory))
    } catch (e) {
      console.error('Failed to save history:', e)
    }
  }

  const clearHistory = () => {
    setHistory([])
    localStorage.removeItem(HISTORY_KEY)
  }

  const deleteHistoryItem = (itemToRemove) => {
    try {
      const newHistory = history.filter(h => h !== itemToRemove)
      setHistory(newHistory)
      localStorage.setItem(HISTORY_KEY, JSON.stringify(newHistory))
    } catch (e) {
      console.error('Failed to delete history item:', e)
    }
  }

  const handleFormSubmit = (e) => {
    e.preventDefault()
    if (searchQuery.trim()) {
      saveToHistory(searchQuery)
      onSearch(e)
      setShowSuggestions(false)
    }
  }

  const placeholder = isKaraokeMode 
    ? "Tìm karaoke..." 
    : searchSource === 'youtube' 
      ? "Tìm video YouTube..." 
      : "Tìm nhạc SoundCloud..."

  return (
    <div className="relative flex-1 transition-all duration-200">
      <input
        ref={inputRef}
        type="text"
        value={searchQuery}
        onChange={(e) => setSearchQuery(e.target.value)}
        onKeyDown={handleKeyDown}
        onFocus={(e) => {
          setShowSuggestions(true)
          if (onFocus) onFocus(e)
        }}
        onBlur={(e) => {
          // Delay closing to allow click/tap events to fire properly on suggestions
          setTimeout(() => {
            setShowSuggestions(false)
          }, 200)
          if (onBlur) onBlur(e)
        }}
        placeholder={placeholder}
        className={`${className} transition-all duration-200`}
      />

      {showSuggestions && (
        (searchQuery.length >= 2 && (isLoading || suggestions.length > 0)) ||
        (searchQuery.length < 2 && history.length > 0)
      ) && (
        <div 
          ref={suggestionsRef}
          className="absolute z-50 w-full mt-2 bg-black border border-white/10 rounded-2xl shadow-[0_20px_50px_rgba(0,0,0,0.85)] max-h-[350px] overflow-y-auto custom-scrollbar overflow-x-hidden animate-fadeIn"
        >
          {/* History list when query is short/empty */}
          {searchQuery.length < 2 && history.length > 0 && (
            <div className="py-1">
              <div className="px-4 py-2.5 text-[10px] font-black text-slate-500 uppercase tracking-widest flex items-center justify-between border-b border-white/5 mb-1">
                <span>Lịch sử tìm kiếm gần đây</span>
                <button
                  type="button"
                  onMouseDown={(e) => {
                    e.preventDefault()
                    clearHistory()
                  }}
                  onTouchStart={(e) => {
                    e.preventDefault()
                    clearHistory()
                  }}
                  className="text-red-400 hover:text-red-300 transition-colors uppercase text-[9px] font-bold px-2 py-1 rounded hover:bg-red-500/10"
                >
                  Xóa tất cả
                </button>
              </div>
              {history.map((item, index) => (
                <div
                  key={index}
                  onMouseDown={(e) => {
                    e.preventDefault()
                    selectSuggestion(item)
                  }}
                  onTouchStart={(e) => {
                    e.preventDefault()
                    selectSuggestion(item)
                  }}
                  className="px-4 py-3 cursor-pointer hover:bg-white/5 text-slate-300 hover:text-white transition-all duration-150 flex items-center justify-between gap-3 border-b border-white/5 last:border-b-0"
                >
                  <div className="flex items-center gap-3 flex-1 min-w-0">
                    <svg className="w-4 h-4 text-slate-500 shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 8v4l3 3m6-3a9 9 0 11-18 0 9 9 0 0118 0z" />
                    </svg>
                    <span className="text-sm font-semibold truncate tracking-wide">{item}</span>
                  </div>
                  <button
                    type="button"
                    onMouseDown={(e) => {
                      e.preventDefault()
                      e.stopPropagation()
                      deleteHistoryItem(item)
                    }}
                    onTouchStart={(e) => {
                      e.preventDefault()
                      e.stopPropagation()
                      deleteHistoryItem(item)
                    }}
                    className="w-8 h-8 rounded-full flex items-center justify-center bg-white/0 hover:bg-white/10 text-slate-500 hover:text-red-400 transition-all shrink-0 active:scale-90"
                    title="Xóa lịch sử này"
                  >
                    <svg className="w-4.5 h-4.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2.5} d="M6 18L18 6M6 6l12 12" />
                    </svg>
                  </button>
                </div>
              ))}
            </div>
          )}

          {/* Loading state for API Suggestions */}
          {searchQuery.length >= 2 && isLoading && (
            <div className="px-4 py-4 text-center text-slate-400 text-sm font-medium flex items-center justify-center gap-2">
              <span className="inline-block animate-spin">⏳</span>
              Đang tìm gợi ý...
            </div>
          )}

          {/* Suggestions list from API */}
          {searchQuery.length >= 2 && !isLoading && suggestions.length > 0 && suggestions.map((item, index) => {
            const text = typeof item === 'string' ? item : item.title
            return (
              <div
                key={index}
                onMouseDown={(e) => {
                  e.preventDefault() // Ngăn blur input để chạy event thành công
                  selectSuggestion(item)
                }}
                onTouchStart={(e) => {
                  e.preventDefault() // Ngăn blur input cho mobile
                  selectSuggestion(item)
                }}
                className={`px-4 py-3 cursor-pointer transition-all duration-150 border-b border-white/5 last:border-b-0 flex items-center gap-3 ${
                  index === selectedIndex 
                    ? 'bg-[#4F7CFF] text-white font-bold shadow-[0_4px_12px_rgba(79,124,255,0.25)]' 
                    : 'hover:bg-white/5 text-slate-100'
                }`}
              >
                {/* Search icon */}
                <svg 
                  className={`w-4 h-4 flex-shrink-0 transition-colors ${
                    index === selectedIndex ? 'text-white' : 'text-[#4F7CFF]'
                  }`} 
                  fill="none" 
                  stroke="currentColor" 
                  viewBox="0 0 24 24"
                >
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2.5} d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
                </svg>
                
                {/* Suggestion text */}
                <span className="text-sm font-semibold truncate flex-1 tracking-wide">
                  {text}
                </span>

                {/* Arrow icon (fill text action indicator) */}
                <svg 
                  className={`w-4 h-4 flex-shrink-0 transition-colors ${
                    index === selectedIndex ? 'text-white/80' : 'text-slate-500'
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

          {/* No results */}
          {searchQuery.length >= 2 && !isLoading && suggestions.length === 0 && (
            <div className="px-4 py-6 text-center text-slate-400 text-sm">
              Không tìm thấy gợi ý nào
            </div>
          )}
        </div>
      )}
    </div>
  )
}
