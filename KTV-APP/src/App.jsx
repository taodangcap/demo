import { useState, useEffect, lazy, Suspense } from 'react'
import { BrowserRouter as Router, Routes, Route, useLocation } from 'react-router-dom'
import { AnimatePresence, motion } from 'framer-motion'
import Loading from './components/Loading'

// Lazy load pages for better performance (code splitting)
const Home = lazy(() => import('./pages/Home'))
const VideoPlayer = lazy(() => import('./pages/VideoPlayer'))
const RemoteControl = lazy(() => import('./pages/RemoteControl'))
const Status = lazy(() => import('./pages/Status'))
const GioiThieu = lazy(() => import('./pages/GioiThieu'))
const Admin = lazy(() => import('./pages/Admin'))
const NotFound = lazy(() => import('./pages/NotFound'))

// Page transition variants
const pageTransition = {
  initial: { opacity: 0, y: 6 },
  animate: { 
    opacity: 1, 
    y: 0,
    transition: { duration: 0.25, ease: [0.25, 0.46, 0.45, 0.94] }
  },
  exit: { 
    opacity: 0, 
    y: -6,
    transition: { duration: 0.15, ease: [0.55, 0.085, 0.68, 0.53] }
  },
}

// Minimal loading fallback for lazy-loaded pages
function PageLoader() {
  return (
    <div className="flex items-center justify-center min-h-screen bg-gradient-to-br from-slate-900 to-slate-800">
      <motion.div
        initial={{ opacity: 0, scale: 0.9 }}
        animate={{ opacity: 1, scale: 1 }}
        className="flex flex-col items-center gap-3"
      >
        <div className="w-8 h-8 border-2 border-primary-500 border-t-transparent rounded-full animate-spin" />
        <span className="text-xs font-bold text-slate-400 uppercase tracking-widest">Loading...</span>
      </motion.div>
    </div>
  )
}

// Animated routes wrapper
function AnimatedRoutes() {
  const location = useLocation()

  return (
    <AnimatePresence mode="wait">
      <motion.div
        key={location.pathname}
        variants={pageTransition}
        initial="initial"
        animate="animate"
        exit="exit"
        className="min-h-screen"
      >
        <Suspense fallback={<PageLoader />}>
          <Routes location={location}>
            <Route path="/" element={<GioiThieu />} />
            <Route path="/gioithieu" element={<GioiThieu />} />
            <Route path="/karaoke" element={<Home />} />
            <Route path="/player" element={<VideoPlayer />} />
            <Route path="/remote" element={<RemoteControl />} />
            <Route path="/status" element={<Status />} />
            <Route path="/admin" element={<Admin />} />
            <Route path="*" element={<NotFound />} />
          </Routes>
        </Suspense>
      </motion.div>
    </AnimatePresence>
  )
}

function App() {
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    // Faster initial load - reduced from 2s to 800ms
    const timer = setTimeout(() => {
      setLoading(false)
    }, 800)

    return () => clearTimeout(timer)
  }, [])

  if (loading) {
    return <Loading />
  }

  return (
    <Router future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>
      <AnimatedRoutes />
    </Router>
  )
}

export default App
