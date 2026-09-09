import { useState, useEffect } from 'react'
import { useNavigate } from 'react-router-dom'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { 
  faLock, faSave, faRotateLeft, faVolumeUp, faArrowLeft, faPlus,
  faTrash, faClock
} from '@fortawesome/free-solid-svg-icons'
import { getSoundEffects, saveSoundEffects, DEFAULT_SOUND_EFFECTS, fetchSharedSoundEffects } from '../utils/storage'
import appLogo from '../img/logo.png'

// Lấy mật khẩu từ .env, mặc định là 'admin' nếu không có
const ADMIN_PASSWORD = import.meta.env.VITE_ADMIN_PASSWORD || 'admin'

function Admin() {
  const navigate = useNavigate()
  const [password, setPassword] = useState('')
  const [isLoggedIn, setIsLoggedIn] = useState(false)
  const [loginError, setLoginError] = useState('')
  const [effects, setEffects] = useState(getSoundEffects())
  const [durations, setDurations] = useState({})
  const [saveStatus, setSaveStatus] = useState('')
  const [isSyncing, setIsSyncing] = useState(false)

  // Load from server on mount
  useEffect(() => {
    const loadSharedEffects = async () => {
      setIsSyncing(true)
      const sharedEffects = await fetchSharedSoundEffects()
      setEffects(sharedEffects)
      setIsSyncing(false)
    }
    loadSharedEffects()
  }, [])

  // Load durations
  useEffect(() => {
    Object.keys(effects).forEach(id => {
      const url = effects[id]?.url
      if (url && !durations[id]) {
        const audio = new Audio(url)
        audio.addEventListener('loadedmetadata', () => {
          setDurations(prev => ({ ...prev, [id]: audio.duration }))
        })
        audio.addEventListener('error', () => {
          setDurations(prev => ({ ...prev, [id]: -1 })) // Error indicator
        })
      }
    })
  }, [effects])

  const handleLogin = (e) => {
    e.preventDefault()
    if (password === ADMIN_PASSWORD) {
      setIsLoggedIn(true)
      setLoginError('')
    } else {
      setLoginError('Mật khẩu không chính xác!')
    }
  }

  const handleSave = async (e) => {
    if (e) e.preventDefault()
    setIsSyncing(true)
    const success = await saveSoundEffects(effects)
    setIsSyncing(false)
    if (success) {
      setSaveStatus('Đã lưu & đồng bộ thành công!')
    } else {
      setSaveStatus('⚠️ LỖI: Chỉ lưu được trên máy này, không lưu được vào file JSON!')
    }
    setTimeout(() => setSaveStatus(''), 3000)
  }

  const handleReset = async () => {
    if (window.confirm('Bạn có chắc muốn khôi phục về mặc định?')) {
      setEffects(DEFAULT_SOUND_EFFECTS)
      await saveSoundEffects(DEFAULT_SOUND_EFFECTS)
      setSaveStatus('Đã khôi phục & đồng bộ mặc định!')
      setTimeout(() => setSaveStatus(''), 3000)
    }
  }

  const handleAddEffect = async () => {
    const id = prompt('Nhập tên mã hiệu ứng (viết liền, không dấu, ví dụ: cheering, funny...):')
    if (id && id.trim()) {
      const trimmedId = id.trim().toLowerCase().replace(/\s+/g, '_')
      if (effects[trimmedId]) {
        alert('Mã hiệu ứng này đã tồn tại!')
        return
      }
      const newEffects = { ...effects, [trimmedId]: { label: trimmedId.charAt(0).toUpperCase() + trimmedId.slice(1), url: '', volume: 80 } }
      setEffects(newEffects)
      
      // Đồng bộ ngay lập tức vào file JSON
      setIsSyncing(true)
      await saveSoundEffects(newEffects)
      setIsSyncing(false)
      setSaveStatus(`Đã thêm hiệu ứng "${trimmedId}" thành công!`)
      setTimeout(() => setSaveStatus(''), 2000)
    }
  }

  const handleDeleteEffect = async (id) => {
    if (window.confirm(`Bạn có chắc muốn xóa hiệu ứng "${id}"?`)) {
      const newEffects = { ...effects }
      delete newEffects[id]
      setEffects(newEffects)
      
      // Đồng bộ ngay lập tức vào file JSON để "thật sự xóa" trong DB
      setIsSyncing(true)
      await saveSoundEffects(newEffects)
      setIsSyncing(false)
      setSaveStatus(`Đã xóa hiệu ứng "${id}" khỏi hệ thống!`)
      setTimeout(() => setSaveStatus(''), 2000)
    }
  }

  const formatDuration = (seconds) => {
    if (seconds < 0) return 'Lỗi link'
    if (!seconds) return '--:--'
    const mins = Math.floor(seconds / 60)
    const secs = Math.floor(seconds % 60)
    return `${mins}:${secs.toString().padStart(2, '0')}`
  }

  if (!isLoggedIn) {
     // ... Keep existing login UI ...
  }

  // To save time, just update the main render
  return (
    <div className="min-h-screen bg-[#020617] text-white p-6 lg:p-12 overflow-y-auto font-sans selection:bg-blue-500/30">
      {!isLoggedIn ? (
        <div className="min-h-screen bg-[#020617] flex items-center justify-center p-6 font-sans">
        <div className="w-full max-w-md bg-slate-900/50 backdrop-blur-3xl border border-white/10 rounded-[2.5rem] p-10 shadow-2xl relative overflow-hidden group">
          <div className="absolute -top-24 -left-24 w-48 h-48 bg-blue-600/10 rounded-full blur-3xl group-hover:bg-blue-600/20 transition-all duration-700" />
          <div className="absolute -bottom-24 -right-24 w-48 h-48 bg-purple-600/10 rounded-full blur-3xl group-hover:bg-purple-600/20 transition-all duration-700" />
          
          <div className="relative z-10">
            <div className="flex flex-col items-center mb-10">
              <div className="w-20 h-20 bg-gradient-to-br from-blue-600 to-indigo-700 rounded-3xl p-5 mb-6 shadow-2xl shadow-blue-500/20 rotate-3 group-hover:rotate-0 transition-transform duration-500">
                <img src={appLogo} alt="Logo" className="w-full h-full object-contain brightness-0 invert" />
              </div>
              <h1 className="text-3xl font-black text-white uppercase tracking-tighter">
                Admin <span className="text-blue-500">Panel</span>
              </h1>
              <p className="text-slate-500 text-xs font-bold uppercase tracking-[0.3em] mt-2">Truy cập hệ thống</p>
            </div>
            
            <form onSubmit={handleLogin} className="space-y-6">
              <div className="space-y-2">
                <label className="text-[10px] font-black text-slate-500 uppercase tracking-widest ml-1">Mật khẩu</label>
                <div className="relative">
                  <div className="absolute inset-y-0 left-0 pl-4 flex items-center pointer-events-none text-slate-600">
                    <FontAwesomeIcon icon={faLock} />
                  </div>
                  <input 
                    type="password"
                    className="w-full bg-black/40 border border-white/10 rounded-2xl py-4 pl-12 pr-4 text-white placeholder:text-slate-700 focus:outline-none focus:ring-2 focus:ring-blue-500/50 transition-all font-bold tracking-widest"
                    placeholder="••••••••"
                    value={password}
                    onChange={(e) => setPassword(e.target.value)}
                    autoFocus
                  />
                </div>
              </div>
              
              {loginError && (
                <div className="bg-red-500/10 border border-red-500/20 py-3 rounded-xl">
                  <p className="text-red-400 text-[10px] font-black text-center uppercase tracking-widest">{loginError}</p>
                </div>
              )}
              
              <button 
                type="submit"
                className="w-full bg-gradient-to-r from-blue-600 to-indigo-600 hover:from-blue-500 hover:to-indigo-500 text-white font-black py-4 rounded-2xl shadow-xl shadow-blue-500/30 active:scale-95 transition-all uppercase tracking-widest text-sm"
              >
                Đăng nhập hệ thống
              </button>
            </form>
          </div>
        </div>
      </div>
      ) : (
      <div className="max-w-6xl mx-auto">
        <header className="flex flex-col md:flex-row md:items-center justify-between gap-6 mb-16 px-2">
          <div className="flex items-center gap-6">
            <button 
              onClick={() => navigate('/')} 
              className="w-12 h-12 rounded-2xl bg-white/5 border border-white/10 flex items-center justify-center text-slate-400 hover:text-white hover:bg-white/10 transition-all active:scale-90"
            >
              <FontAwesomeIcon icon={faArrowLeft} />
            </button>
            <div>
              <div className="flex items-center gap-3">
                <span className="px-2 py-0.5 bg-blue-600 text-[8px] font-black rounded uppercase tracking-widest">Thiết lập</span>
                <h1 className="text-3xl font-black uppercase tracking-tight">Cài đặt âm thanh</h1>
              </div>
              <p className="text-[10px] font-bold text-slate-500 uppercase tracking-[0.2em] mt-1">
                Quản lý hiệu ứng, âm lượng và độ dài MP3 | {isSyncing ? 'Đang đồng bộ...' : 'Đã đồng bộ máy chủ'}
              </p>
            </div>
          </div>
          
          <div className="flex items-center gap-3">
            <button 
              onClick={handleAddEffect}
              className="px-6 py-3 rounded-2xl bg-blue-600 hover:bg-blue-500 text-white text-[10px] font-black uppercase tracking-widest transition-all flex items-center gap-3 shadow-lg shadow-blue-500/20"
            >
              <FontAwesomeIcon icon={faPlus} />
              Thêm hiệu ứng
            </button>
             <button 
              onClick={handleReset}
              className="px-6 py-3 rounded-2xl bg-white/5 border border-white/10 text-slate-400 hover:text-red-400 hover:border-red-500/30 text-[10px] font-black uppercase tracking-widest transition-all flex items-center gap-3"
            >
              <FontAwesomeIcon icon={faRotateLeft} />
              Mặc định
            </button>
          </div>
        </header>

        <form onSubmit={handleSave} className="space-y-8">
          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6 pb-40 px-2">
            {Object.keys(effects).map((id) => (
              <div key={id} className="bg-slate-900/40 backdrop-blur-3xl border border-white/5 p-7 rounded-[2rem] hover:border-blue-500/20 transition-all group relative overflow-hidden">
                <button 
                  type="button"
                  onClick={() => handleDeleteEffect(id)}
                  className="absolute top-6 right-6 w-8 h-8 rounded-full bg-red-500/10 text-red-500/40 hover:text-red-500 hover:bg-red-500/20 flex items-center justify-center transition-all opacity-0 group-hover:opacity-100 z-20"
                >
                   <FontAwesomeIcon icon={faTrash} className="text-xs" />
                </button>

                <div className="relative z-10">
                  <div className="flex items-center justify-between mb-6">
                    <div className="flex items-center gap-4">
                      <div className="w-12 h-12 rounded-2xl bg-blue-600/10 flex items-center justify-center text-blue-500 group-hover:scale-110 transition-transform shadow-lg shadow-blue-500/5">
                        <FontAwesomeIcon icon={faVolumeUp} className="text-lg" />
                      </div>
                      <div>
                        <label className="text-[10px] font-black uppercase tracking-[0.2em] text-slate-400 group-hover:text-blue-400 transition-colors">
                          {id.replace(/_/g, ' ').charAt(0).toUpperCase() + id.replace(/_/g, ' ').slice(1)}
                        </label>
                        <p className="text-[9px] font-bold text-slate-600 uppercase tracking-widest mt-0.5">ID: {id}</p>
                      </div>
                    </div>
                    
                    {/* Duration Display */}
                    <div className="flex items-center gap-2 bg-black/30 px-3 py-1.5 rounded-xl border border-white/5">
                      <FontAwesomeIcon icon={faClock} className="text-[10px] text-blue-400" />
                      <span className="text-[10px] font-black font-mono text-blue-300">
                         {formatDuration(durations[id])}
                      </span>
                    </div>
                  </div>
                  
                    <div className="space-y-4">
                      <div className="space-y-2">
                         <span className="text-[9px] font-bold text-slate-500 uppercase tracking-widest ml-1">Tên hiển thị</span>
                         <input 
                           type="text"
                           className="w-full bg-black/40 border border-white/5 rounded-2xl py-4 px-5 text-[11px] text-white placeholder:text-slate-800 focus:outline-none focus:ring-2 focus:ring-blue-500/30 transition-all font-bold shadow-inner"
                           value={effects[id]?.label || ''}
                           onChange={(e) => {
                             const newLabel = e.target.value
                             setEffects({...effects, [id]: { ...(typeof effects[id] === 'object' ? effects[id] : { volume: 80, url: effects[id] }), label: newLabel }})
                           }}
                           placeholder="Ví dụ: Vỗ tay nhiệt liệt"
                         />
                      </div>

                      <div className="space-y-2">
                         <span className="text-[9px] font-bold text-slate-500 uppercase tracking-widest ml-1">Đường dẫn MP3</span>
                         <input 
                           type="text"
                           className="w-full bg-black/40 border border-white/5 rounded-2xl py-4 px-5 text-[11px] text-white placeholder:text-slate-800 focus:outline-none focus:ring-2 focus:ring-blue-500/30 transition-all font-mono shadow-inner"
                           value={effects[id]?.url || (typeof effects[id] === 'string' ? effects[id] : '')}
                           onChange={(e) => {
                             const newUrl = e.target.value
                             setEffects({...effects, [id]: { ...(typeof effects[id] === 'object' ? effects[id] : { volume: 80, label: id }), url: newUrl }})
                             // Clear duration to re-fetch
                             const newDurations = { ...durations }
                             delete newDurations[id]
                             setDurations(newDurations)
                           }}
                           placeholder="https://example.com/sound.mp3"
                         />
                      </div>
                    </div>
                </div>
              </div>
            ))}
            
            <button 
              type="button"
              onClick={handleAddEffect}
              className="bg-slate-900/20 border-2 border-dashed border-white/5 p-8 rounded-[2rem] hover:border-blue-500/20 hover:bg-blue-600/5 transition-all group flex flex-col items-center justify-center gap-4 text-slate-600 hover:text-blue-500"
            >
              <div className="w-14 h-14 rounded-full bg-slate-800 flex items-center justify-center group-hover:bg-blue-600/20 transition-all">
                <FontAwesomeIcon icon={faPlus} className="text-lg" />
              </div>
              <span className="text-xs font-black uppercase tracking-widest">Thêm mới hiệu ứng</span>
            </button>
          </div>

          <div className="fixed bottom-10 left-1/2 -translate-x-1/2 w-full max-w-lg px-6 flex flex-col items-center gap-4 z-50">
             <div className={`px-6 py-2.5 rounded-full bg-green-500/20 border border-green-500/30 text-green-400 text-[10px] font-black uppercase tracking-[0.2em] transition-all backdrop-blur-md shadow-lg ${saveStatus ? 'opacity-100 translate-y-0' : 'opacity-0 translate-y-4 pointer-events-none'}`}>
                {saveStatus}
              </div>
            
            <button 
              type="submit"
              className="w-full bg-gradient-to-r from-blue-600 to-indigo-700 hover:from-blue-500 hover:to-indigo-600 text-white font-black py-5 rounded-[2rem] shadow-[0_20px_50px_rgba(37,99,235,0.3)] active:scale-95 transition-all uppercase tracking-[0.25em] flex items-center justify-center gap-4 text-sm border-t border-white/20"
            >
              <FontAwesomeIcon icon={faSave} />
              Lưu cấu hình hệ thống
            </button>
          </div>
        </form>
        
        <div className="h-40" />
      </div>
      )}
    </div>
  )
}

export default Admin
