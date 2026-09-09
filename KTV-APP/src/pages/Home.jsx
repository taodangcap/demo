import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import SEO from '../components/SEO'
import '../styles/landing.css'

const PERSON_SCHEMA = {
  "@context": "https://schema.org",
  "@type": "Person",
  "name": "Trịnh Quang Huy",
  "alternateName": "taodangcap",
  "url": window.location.origin,
  "description": "Trịnh Quang Huy (taodangcap) là Fullstack Developer và Music Producer tại Việt Nam, chuyên xây dựng web app hiệu suất cao.",
  "jobTitle": "Artist / Developer",
  "image": "https://www.trinhquanghuy.net/avatar.jpg",
  "birthDate": "2004-12-03",
  "nationality": {
    "@type": "Country",
    "name": "Vietnam"
  },
  "sameAs": [
    "https://github.com/taodangcap",
    "https://facebook.com/taodangcap",
    "https://youtube.com/@trinhquanghuyy",
    "https://www.instagram.com/taodangcap/",
    "https://soundcloud.com/taodangcap",
    "https://open.spotify.com/artist/25KvwOjJHNy7JvXj0F4B2F",
    "https://music.apple.com/us/artist/tr%E1%BB%8Bnh-quang-huy/1809758870",
    "https://www.linkedin.com/in/trinhquanghuyvn/"
  ]
};

const NAV_LINKS = [
  { id: 'hero', label: 'Trịnh Quang Huy ', icon: 'fas fa-music' },
  { id: 'about', label: 'Giới thiệu', icon: 'fas fa-user' },
  { id: 'features', label: 'Tính năng', icon: 'fas fa-star' },
  { id: 'contact', label: 'Liên hệ', icon: 'fas fa-envelope' },
]

const FEATURES = [
  {
    icon: 'fas fa-music',
    title: 'Kho Nhạc Khổng Lồ',
    description: 'Hàng nghìn bài hát đa dạng từ nhạc Việt Nam đến quốc tế, luôn được cập nhật mới nhất',
  },
  {
    icon: 'fas fa-microphone',
    title: 'Chất Lượng Âm Thanh',
    description: 'Âm thanh sống động, rõ ràng với công nghệ xử lý âm thanh hiện đại',
  },
  {
    icon: 'fas fa-headphones',
    title: 'Trải Nghiệm Tuyệt Vời',
    description: 'Giao diện đẹp mắt, dễ sử dụng, phù hợp cho mọi lứa tuổi',
  },
  {
    icon: 'fas fa-sparkles',
    title: 'Cập Nhật Thường Xuyên',
    description: 'Kho nhạc được cập nhật liên tục với những bài hát mới nhất, hot nhất',
  },
]

const STATS = [
  { value: '1000+', label: 'Bài hát' },
  { value: '50+', label: 'Thể loại' },
  { value: '24/7', label: 'Hỗ trợ' },
]

function Home() {
  const sectionIds = useMemo(() => NAV_LINKS.map((link) => link.id), [])
  const [isDark, setIsDark] = useState(false)
  const [mobileOpen, setMobileOpen] = useState(false)
  const [activeSection, setActiveSection] = useState('hero')
  const [typedSubtitle, setTypedSubtitle] = useState('')
  const [navCompact, setNavCompact] = useState(false)

  useEffect(() => {
    const storedTheme = localStorage.getItem('theme') || 'light'
    if (storedTheme === 'dark') {
      setIsDark(true)
      document.body.setAttribute('data-theme', 'dark')
    }
  }, [])

  useEffect(() => {
    if (isDark) {
      document.body.setAttribute('data-theme', 'dark')
    } else {
      document.body.removeAttribute('data-theme')
    }
    localStorage.setItem('theme', isDark ? 'dark' : 'light')
  }, [isDark])

  const [showCursor, setShowCursor] = useState(true)

  useEffect(() => {
    const subtitle = 'Ứng Dụng Karaoke Chuyên Nghiệp'
    setTypedSubtitle('')
    setShowCursor(true)
    let index = 0
    const timer = setInterval(() => {
      if (index < subtitle.length) {
        setTypedSubtitle(subtitle.substring(0, index + 1))
        index += 1
      } else {
        clearInterval(timer)
        setTimeout(() => setShowCursor(false), 500)
      }
    }, 120)
    return () => clearInterval(timer)
  }, [])

  useEffect(() => {
    const handleScroll = () => {
      const scrollPosition = window.scrollY + 160
      sectionIds.forEach((id) => {
        const element = document.getElementById(id)
        if (!element) return
        const { offsetTop, offsetHeight } = element
        if (scrollPosition >= offsetTop && scrollPosition < offsetTop + offsetHeight) {
          setActiveSection(id)
        }
      })
      setNavCompact(window.scrollY > 40)
    }
    window.addEventListener('scroll', handleScroll)
    handleScroll()
    return () => window.removeEventListener('scroll', handleScroll)
  }, [sectionIds])

  useEffect(() => {
    const observer = new IntersectionObserver(
      (entries) => {
        entries.forEach((entry) => {
          if (entry.isIntersecting) entry.target.classList.add('animated')
        })
      },
      { threshold: 0.1, rootMargin: '0px 0px -60px 0px' },
    )
    const elements = document.querySelectorAll('.animate-on-scroll')
    elements.forEach((el) => observer.observe(el))
    return () => observer.disconnect()
  }, [])

  const handleNavClick = (sectionId) => (event) => {
    event.preventDefault()
    const element = document.getElementById(sectionId)
    if (element) {
      const offsetTop = element.offsetTop - 80
      window.scrollTo({ top: offsetTop, behavior: 'smooth' })
      setMobileOpen(false)
    }
  }

  const handleContactSubmit = (event) => {
    event.preventDefault()
    event.target.reset()
    alert('Cảm ơn bạn đã liên hệ! Chúng tôi sẽ phản hồi sớm nhất có thể.')
  }

  return (
    <div className="landing-page">
      <SEO
        title="Karaoke trực tuyến - Trịnh Quang Huy (taodangcap)"
        description="Hát Karaoke online chuyên nghiệp với ứng dụng của Trịnh Quang Huy (taodangcap). Kho nhạc SoundCloud chất lượng cao, điều khiển từ xa thông minh."
        keywords="Trịnh Quang Huy, taodangcap, karaoke online, hát karaoke trực tuyến, TQH Karaoke"
        schema={PERSON_SCHEMA}
      />
      <nav id="navbar" className={`navbar ${navCompact ? 'navbar-compact' : ''}`}>
        <div className="nav-container">
          <div className="nav-content">
            <div className="nav-desktop">
              {NAV_LINKS.map((link) => {
                if (link.id === 'about') {
                  return (
                    <Link
                      key={link.id}
                      to="/"
                      className={`nav-item ${activeSection === link.id ? 'active' : ''}`}
                    >
                      <i className={link.icon} />
                      <span>{link.label}</span>
                    </Link>
                  )
                }
                if (link.id === 'hero') {
                  return (
                    <Link
                      key={link.id}
                      to="/"
                      className={`nav-item ${activeSection === link.id ? 'active' : ''} nav-logo`}
                      style={{ textDecoration: 'none' }}
                      onClick={handleNavClick(link.id)}
                    >
                      <span>{link.label}</span>
                    </Link>
                  )
                }
                return (
                  <a
                    key={link.id}
                    href={`#${link.id}`}
                    className={`nav-item ${activeSection === link.id ? 'active' : ''}`}
                    data-section={link.id}
                    onClick={handleNavClick(link.id)}
                  >
                    <i className={link.icon} />
                    <span>{link.label}</span>
                  </a>
                )
              })}
            </div>
            <div className="nav-mobile">
              <span className="nav-brand">TQH</span>
              <button className="mobile-menu-btn" onClick={() => setMobileOpen((prev) => !prev)}>
                <i className={`fas ${mobileOpen ? 'fa-times' : 'fa-bars'}`} />
              </button>
            </div>
            <button className="dark-mode-toggle" onClick={() => setIsDark((prev) => !prev)} aria-label="Toggle dark mode">
              <i className={`fas ${isDark ? 'fa-sun' : 'fa-moon'}`} />
            </button>
          </div>
          <div className={`mobile-menu ${mobileOpen ? 'active' : ''}`}>
            {NAV_LINKS.map((link) => {
              if (link.id === 'about') {
                return (
                  <Link
                    key={`mobile-${link.id}`}
                    to="/"
                    className={`mobile-nav-item ${activeSection === link.id ? 'active' : ''}`}
                    onClick={() => setMobileOpen(false)}
                  >
                    <i className={link.icon} />
                    <span>{link.label}</span>
                  </Link>
                )
              }
              return (
                <a
                  key={`mobile-${link.id}`}
                  href={`#${link.id}`}
                  className={`mobile-nav-item ${activeSection === link.id ? 'active' : ''} ${link.id === 'hero' ? 'nav-logo' : ''}`}
                  data-section={link.id}
                  onClick={handleNavClick(link.id)}
                >
                  {link.id === 'hero' ? (
                    <span>{link.label}</span>
                  ) : (
                    <>
                      <i className={link.icon} />
                      <span>{link.label}</span>
                    </>
                  )}
                </a>
              )
            })}
          </div>
        </div>
      </nav>

      <section id="hero" className="hero-section">
        <div className="hero-background">
          <div className="floating-element floating-1" />
          <div className="floating-element floating-2" />
          <div className="floating-element floating-3" />
        </div>
        <div className="hero-content">
          <h1 className="hero-title">Trịnh Quang Huy</h1>
          <p className="hero-subtitle">
            <span className="subtitle-text">{typedSubtitle}</span>
            {showCursor && <span className="typing-cursor">|</span>}
          </p>
          <div className="hero-buttons">
            <Link to="/player" className="hero-btn hero-btn-primary">
              Bắt Đầu Hát Ngay
            </Link>
            <button className="hero-btn hero-btn-secondary" onClick={handleNavClick('about')}>
              Khám Phá Thêm
            </button>
          </div>
        </div>
      </section>

      <section id="about" className="section">
        <div className="container">
          <h2 className="section-title animate-on-scroll">Giới thiệu</h2>
          <div className="about-content">
            <div className="about-text glass-card animate-on-scroll">
              <h3 className="about-name">Trịnh Quang Huy - Karaoke</h3>
              <p className="about-description">
                Chào mừng đến với ứng dụng karaoke chuyên nghiệp của Trịnh Quang Huy. Đây là nền tảng hát karaoke hiện đại với hàng nghìn bài hát đa dạng, từ nhạc Việt Nam đến quốc tế. Với giao diện thân thiện, dễ sử dụng và chất lượng âm thanh vượt trội, chúng tôi mang đến cho bạn trải nghiệm hát karaoke tuyệt vời nhất ngay tại nhà.
              </p>
              <p className="about-description">
                Ứng dụng được phát triển với công nghệ hiện đại, hỗ trợ tìm kiếm bài hát nhanh chóng, tạo playlist yêu thích, và nhiều tính năng thú vị khác. Hãy cùng bạn bè và gia đình tận hưởng những khoảnh khắc âm nhạc đáng nhớ!
              </p>
            </div>
            <div className="about-visual glass-card animate-on-scroll">
              <div className="stats-grid">
                {STATS.map((stat) => (
                  <div key={stat.label} className="stat-item">
                    <div className="stat-number">{stat.value}</div>
                    <div className="stat-label">{stat.label}</div>
                  </div>
                ))}
              </div>
            </div>
          </div>
        </div>
      </section>

      <section id="features" className="section section-alt">
        <div className="container">
          <h2 className="section-title animate-on-scroll">Tính Năng Nổi Bật</h2>
          <div className="features-grid">
            {FEATURES.map((feature) => (
              <div key={feature.title} className="feature-card glass-card animate-on-scroll">
                <div className="feature-icon">
                  <i className={feature.icon} />
                </div>
                <h3 className="feature-title">{feature.title}</h3>
                <p className="feature-description">{feature.description}</p>
              </div>
            ))}
          </div>
        </div>
      </section>

      <section id="contact" className="section">
        <div className="container">
          <h2 className="section-title animate-on-scroll">Liên hệ</h2>
          <div className="contact-content">
            <div className="contact-info">
              <h3 className="contact-subtitle">Thông tin liên hệ</h3>
              <div className="contact-item">
                <div className="contact-icon">
                  <i className="fas fa-envelope" />
                </div>
                <div className="contact-details">
                  <h4>Email</h4>
                  <p>contact@tqh-karaoke.com</p>
                </div>
              </div>
              <div className="contact-item">
                <div className="contact-icon">
                  <i className="fas fa-phone" />
                </div>
                <div className="contact-details">
                  <h4>Điện thoại</h4>
                  <p>+84 123 456 789</p>
                </div>
              </div>
              <div className="contact-item">
                <div className="contact-icon">
                  <i className="fas fa-map-marker-alt" />
                </div>
                <div className="contact-details">
                  <h4>Địa chỉ</h4>
                  <p>TP. Hồ Chí Minh, Việt Nam</p>
                </div>
              </div>
            </div>
            <div className="contact-form">
              <h3 className="contact-subtitle">Gửi tin nhắn</h3>
              <form onSubmit={handleContactSubmit}>
                <div className="form-group">
                  <input type="text" name="name" placeholder="Họ và tên" required />
                </div>
                <div className="form-group">
                  <input type="email" name="email" placeholder="Email" required />
                </div>
                <div className="form-group">
                  <textarea name="message" rows="4" placeholder="Tin nhắn" required></textarea>
                </div>
                <button type="submit" className="contact-submit-btn">Gửi tin nhắn</button>
              </form>
            </div>
          </div>
        </div>
      </section>

      <footer className="footer">
        <div className="container">
          <p>© 2026 Trịnh Quang Huy (taodangcap). Tất cả quyền được bảo lưu.</p>
        </div>
      </footer>
    </div>
  )
}

export default Home
