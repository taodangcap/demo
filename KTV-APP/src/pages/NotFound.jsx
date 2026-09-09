import { Link } from 'react-router-dom'
import { useEffect } from 'react'
import SEO from '../components/SEO'
import '../styles/landing.css'

const NotFound = () => {
    useEffect(() => {
        const storedTheme = localStorage.getItem('theme') || 'light'
        if (storedTheme === 'dark') {
            document.body.setAttribute('data-theme', 'dark')
        }
    }, [])

    return (
        <div className="landing-page" style={{ minHeight: '100vh', display: 'flex', flexDirection: 'column', justifyContent: 'center', alignItems: 'center' }}>
            <SEO
                title="404 - Không tìm thấy trang | Trịnh Quang Huy"
                description="Trang bạn đang tìm kiếm không tồn tại hoặc đã bị di dời."
            />

            <div className="hero-background">
                <div className="floating-element floating-1" />
                <div className="floating-element floating-3" />
            </div>

            <div className="container" style={{ textAlign: 'center', position: 'relative', zIndex: 1 }}>
                <div className="glass-card animate-on-scroll animated" style={{ padding: '3rem', maxWidth: '600px', margin: '0 auto' }}>
                    <h1 style={{ fontSize: '6rem', fontWeight: '800', margin: '0', color: 'var(--primary-color)', letterSpacing: '10px' }}>404</h1>
                    <h2 style={{ fontSize: '1.5rem', marginBottom: '1.5rem' }}>Opps! Trang không tồn tại</h2>
                    <p style={{ marginBottom: '2rem', opacity: 0.8 }}>
                        Có vẻ như đường dẫn bạn truy cập không đúng hoặc trang này đã được thay đổi địa chỉ.
                        Đừng lo, bạn có thể quay lại trang chủ để tiếp tục khám phá.
                    </p>
                    <div style={{ display: 'flex', gap: '1rem', justifyContent: 'center' }}>
                        <Link to="/" className="hero-btn hero-btn-primary">Quay về Trang chủ</Link>
                        <Link to="/karaoke" className="hero-btn hero-btn-secondary">Ghé thăm Karaoke</Link>
                    </div>
                </div>
            </div>

            <footer className="footer" style={{ position: 'absolute', bottom: 0, width: '100%' }}>
                <div className="container">
                    <p>© 2025 Trịnh Quang Huy. All Rights Reserved.</p>
                </div>
            </footer>
        </div>
    )
}

export default NotFound
