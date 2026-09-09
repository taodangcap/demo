import { useEffect, useState } from 'react'
import logo from '../img/logo.png'
import '../styles/loading.css'

function Loading() {
  const [progress, setProgress] = useState(0)
  const [dots, setDots] = useState('')

  useEffect(() => {
    // Progress animation
    const progressInterval = setInterval(() => {
      setProgress((prev) => {
        if (prev >= 100) {
          clearInterval(progressInterval)
          return 100
        }
        return prev + Math.random() * 15
      })
    }, 200)

    // Typing dots animation
    const dotsInterval = setInterval(() => {
      setDots((prev) => {
        if (prev === '...') return ''
        return prev + '.'
      })
    }, 500)

    return () => {
      clearInterval(progressInterval)
      clearInterval(dotsInterval)
    }
  }, [])

  return (
    <div className="loading-screen">
      <div className="loading-background">
        <div className="floating-orb orb-1" />
        <div className="floating-orb orb-2" />
        <div className="floating-orb orb-3" />
        <div className="floating-orb orb-4" />
      </div>

      <div className="loading-content">
        <div className="logo-container">
          <div className="logo-glow" />
          <img src={logo} alt="TQH Logo" className="loading-logo" />
          <div className="logo-ring" />
        </div>

        <div className="loading-text">
          <h2 className="loading-title">Trịnh Quang Huy</h2>
          <p className="loading-subtitle">
            Đang tải ứng dụng{dots}
          </p>
        </div>

        <div className="progress-container">
          <div className="progress-bar">
            <div 
              className="progress-fill" 
              style={{ width: `${Math.min(progress, 100)}%` }}
            />
          </div>
          <span className="progress-text">{Math.min(Math.round(progress), 100)}%</span>
        </div>

        <div className="loading-particles">
          {[...Array(12)].map((_, i) => (
            <div 
              key={i} 
              className="particle" 
              style={{
                '--delay': `${i * 0.1}s`,
                '--angle': `${(i * 30)}deg`
              }}
            />
          ))}
        </div>
      </div>
    </div>
  )
}

export default Loading
