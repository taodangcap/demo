import { useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import SEO from '../components/SEO';
import '../styles/portfolio.css';

/* ─── Data ─────────────────────────────────── */
const NAV = [
  { id: 'about', label: 'Giới thiệu', icon: 'fas fa-user' },
  { id: 'skills', label: 'Kỹ năng', icon: 'fas fa-code' },
  { id: 'experience', label: 'Kinh nghiệm', icon: 'fas fa-briefcase' },
  { id: 'projects', label: 'Dự án', icon: 'fas fa-folder' },
  { id: 'contact', label: 'Liên hệ', icon: 'fas fa-envelope' },
];

const SKILLS = [
  {
    cat: 'Frontend', icon: 'fa-code', cls: 'fe',
    tags: ['HTML5', 'CSS3', 'Tailwind CSS', 'JavaScript', 'jQuery', 'Responsive Design', 'UI/UX Design'],
  },
  {
    cat: 'Backend', icon: 'fa-server', cls: 'be',
    tags: ['PHP', 'REST API', 'Authentication', 'Payment Integration'],
  },
  {
    cat: 'Database', icon: 'fa-database', cls: 'db',
    tags: ['MySQL', 'Google Sheets API', 'Database Design', 'Query Optimization'],
  },
  {
    cat: 'Công cụ', icon: 'fa-wrench', cls: 'tl',
    tags: ['Git', 'GitHub', 'VS Code', 'Postman', 'Stripe', 'PayPal', 'Discord Bot', 'C#', '.NET', 'Canon SDK'],
  },
];

const EXPERIENCES = [
  {
    role: 'IT Help Desk',
    company: 'IT Support',
    icon: 'fa-headset', cls: 'it',
    points: [
      'Hỗ trợ xử lý sự cố phần cứng và phần mềm cho người dùng',
      'Cài đặt, cấu hình Windows và phần mềm văn phòng',
      'Hỗ trợ kết nối mạng nội bộ, máy in và thiết bị ngoại vi',
      'Quản lý tài khoản và hỗ trợ kỹ thuật từ xa',
    ],
  },
  {
    role: 'Manufacturing Engineer (ME)',
    company: 'Sản xuất — Pin MacBook Air M2',
    icon: 'fa-microchip', cls: 'me',
    points: [
      'Tham gia dây chuyền sản xuất và đóng gói pin MacBook Air M2',
      'Cải tiến quy trình sản xuất nhằm tăng hiệu suất và chất lượng',
      'Phân tích lỗi, phối hợp các bộ phận xử lý sự cố kỹ thuật',
      'Theo dõi dữ liệu sản xuất và đề xuất tối ưu hoá',
    ],
  },
];

const PROJECTS = [
  {
    em: 'fas fa-shopping-cart', name: 'Hệ thống bán tài khoản tự động',
    desc: 'Website thương mại điện tử cho phép bán & giao tài khoản tự động ngay sau khi thanh toán.',
    tech: ['PHP', 'MySQL', 'jQuery', 'Tailwind'],
    feats: ['Đăng ký / đăng nhập', 'Thanh toán Stripe & PayPal', 'Giao tài khoản tự động', 'Dashboard quản lý đơn hàng'],
  },
  {
    em: 'fas fa-users', name: 'Hệ thống quản lý nhân sự',
    desc: 'Phần mềm quản lý nhân viên toàn diện: báo cáo, lọc, xuất Excel nhiều sheet.',
    tech: ['PHP', 'MySQL', 'OpenXML', 'Bootstrap'],
    feats: ['Hồ sơ nhân viên', 'Báo cáo phòng ban', 'Xuất Excel nhiều sheet', 'Thống kê nhân sự'],
  },
  {
    em: 'fas fa-store', name: 'Quản lý cửa hàng tạp hoá',
    desc: 'Phần mềm quản lý bán hàng, kho hàng và in hóa đơn cho cửa hàng nhỏ.',
    tech: ['PHP', 'MySQL', 'Material UI'],
    feats: ['Quản lý sản phẩm & kho', 'In hóa đơn', 'Báo cáo doanh thu', 'Phân quyền nhân viên'],
  },
  {
    em: 'fas fa-camera', name: 'Photobooth Canon SDK',
    desc: 'Ứng dụng điều khiển máy ảnh Canon phục vụ chụp ảnh sự kiện đa chi nhánh.',
    tech: ['C#', 'Canon SDK', 'WinAPI'],
    feats: ['Chụp trực tiếp từ máy tính', 'Điều khiển qua Canon SDK', 'Hỗ trợ nhiều chi nhánh', 'Đồng bộ dữ liệu'],
  },
  {
    em: 'fas fa-tools', name: 'TH All In One',
    desc: 'Bộ công cụ hỗ trợ kỹ thuật & quản trị hệ thống toàn diện, tự động hoá quy trình.',
    tech: ['C#', '.NET', 'WinAPI'],
    feats: ['Cứu hộ máy tính', 'Tạo USB Boot', 'Kiểm tra hệ thống', 'Tự động hoá IT'],
  },
  {
    em: 'fas fa-microphone', name: 'TQH Karaoke App',
    desc: 'Hệ thống hát Karaoke trực tuyến với điều khiển từ xa và kho nhạc SoundCloud.',
    tech: ['React', 'Vite', 'Node.js'],
    feats: ['Kho nhạc SoundCloud', 'Điều khiển từ xa', 'Chất lượng âm thanh cao', 'Đa thiết bị'],
    internalLink: '/karaoke',
  },
];

/* ─── Component ──────────────────────────── */
/* ─── Kawaii Click Effect ────────────────────── */
const KAWAII_EMOJIS = ['🌸','✨','⭐','💖','🎀','🌟','💕','🦄','🍬','🎵','💫','🌺','🍡','🌈','🎊','🎉'];

function spawnConfetti(x, y, count = 8) {
  for (let i = 0; i < count; i++) {
    const el = document.createElement('span');
    el.textContent = KAWAII_EMOJIS[Math.floor(Math.random() * KAWAII_EMOJIS.length)];

    // Base styles — fixed pos at click point
    Object.assign(el.style, {
      position: 'fixed',
      left: x + 'px',
      top: y + 'px',
      fontSize: (0.9 + Math.random() * 0.9) + 'rem',
      pointerEvents: 'none',
      userSelect: 'none',
      zIndex: '99999',
      willChange: 'transform, opacity',
      lineHeight: '1',
    });
    document.body.appendChild(el);

    // Random burst direction
    const angle  = (Math.random() * 360) * (Math.PI / 180);
    const dist   = 55 + Math.random() * 90;
    const tx     = Math.cos(angle) * dist;
    const ty     = Math.sin(angle) * dist - 40; // slight upward bias
    const rot    = (Math.random() - 0.5) * 540;
    const dur    = 600 + Math.random() * 500;
    const delay  = Math.random() * 80;

    // Web Animations API — smooth keyframe animation
    el.animate([
      {
        transform: `translate(-50%, -50%) scale(0.2) rotate(0deg)`,
        opacity: 1,
        offset: 0,
      },
      {
        transform: `translate(calc(-50% + ${tx * 0.4}px), calc(-50% + ${ty * 0.4}px)) scale(1.3) rotate(${rot * 0.3}deg)`,
        opacity: 1,
        offset: 0.25,
      },
      {
        transform: `translate(calc(-50% + ${tx}px), calc(-50% + ${ty}px)) scale(0.6) rotate(${rot}deg)`,
        opacity: 0.8,
        offset: 0.7,
      },
      {
        transform: `translate(calc(-50% + ${tx * 1.15}px), calc(-50% + ${ty * 1.3 + 30}px)) scale(0.1) rotate(${rot * 1.5}deg)`,
        opacity: 0,
        offset: 1,
      },
    ], {
      duration: dur,
      delay: delay,
      easing: 'cubic-bezier(0.25, 0.46, 0.45, 0.94)',
      fill: 'forwards',
    }).onfinish = () => el.remove();
  }
}

export default function GioiThieu() {
  const [scrolled, setScrolled] = useState(false);
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [activeId, setActiveId] = useState('');
  const [form, setForm] = useState({ name: '', email: '', msg: '' });
  const [showScrollTop, setShowScrollTop] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const fadeRefs = useRef([]);

  /* Force dark bg on page load – override Tailwind */
  useEffect(() => {
    const prevBg = document.body.style.backgroundColor;
    document.body.style.backgroundColor = '#0a0a0f';
    return () => { document.body.style.backgroundColor = prevBg; };
  }, []);

  /* Global kawaii click effect – anywhere on page */
  useEffect(() => {
    const handleClick = (e) => spawnConfetti(e.clientX, e.clientY, 8);
    window.addEventListener('click', handleClick);
    return () => window.removeEventListener('click', handleClick);
  }, []);

  /* Scroll: navbar & active section */
  useEffect(() => {
    window.scrollTo(0, 0);
    const onScroll = () => {
      setScrolled(window.scrollY > 20);
      setShowScrollTop(window.scrollY > 300);
      const y = window.scrollY + 90;
      for (const { id } of NAV) {
        const el = document.getElementById(id);
        if (el && y >= el.offsetTop && y < el.offsetTop + el.offsetHeight) {
          setActiveId(id); break;
        }
      }
    };
    window.addEventListener('scroll', onScroll, { passive: true });
    onScroll();
    return () => window.removeEventListener('scroll', onScroll);
  }, []);

  /* IntersectionObserver – fade-up */
  useEffect(() => {
    const io = new IntersectionObserver(
      (entries) => entries.forEach((e) => { if (e.isIntersecting) e.target.classList.add('in'); }),
      { threshold: 0.1 },
    );
    document.querySelectorAll('.fade-up').forEach((el) => io.observe(el));
    return () => io.disconnect();
  }, []);

  /* smooth scroll */
  const goTo = (id) => (e) => {
    e?.preventDefault();
    document.getElementById(id)?.scrollIntoView({ behavior: 'smooth', block: 'start' });
    setDrawerOpen(false);
  };

  /* form submit */
  const submit = async (e) => {
    e.preventDefault();
    if (submitting) return;

    setSubmitting(true);
    try {
      const response = await fetch('/api/contact', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify(form),
      });

      const data = await response.json();
      if (response.ok) {
        setForm({ name: '', email: '', msg: '' });
        alert('✅ Cảm ơn! Lời nhắn của bạn.');
      } else {
        alert(`❌ Lỗi: ${data.error || 'Không thể gửi tin nhắn'}`);
      }
    } catch (error) {
      console.error('Contact submit error:', error);
      alert('❌ Lỗi kết nối mạng, vui lòng thử lại sau.');
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="pf-page">
      <SEO
        title="Trịnh Quang Huy (Táo) | Software Developer"
        description="Portfolio của Trịnh Quang Huy (Táo) – Software Developer chuyên PHP, C#, MySQL và hệ thống quản lý doanh nghiệp."
        keywords="Trịnh Quang Huy, Táo, taodangcap, Software Developer, PHP, C#, MySQL, IT Support"
        canonical={typeof window !== 'undefined' ? `${window.location.origin}/` : '/'}
      />

      {/* Floating Stickers */}
      <div className="float-sticker" style={{ top: '8%', left: '8%' }}>🌸</div>
      <div className="float-sticker" style={{ top: '15%', right: '5%' }}>✨</div>
      <div className="float-sticker" style={{ top: '35%', left: '3%' }}>🎀</div>
      <div className="float-sticker" style={{ top: '50%', right: '6%' }}>⭐</div>
      <div className="float-sticker" style={{ top: '68%', left: '4%' }}>🦄</div>
      <div className="float-sticker" style={{ top: '85%', right: '8%' }}>🍪</div>

      {/* ────────── NAVBAR ────────── */}
      <nav className={`pf-nav ${scrolled ? 'scrolled' : ''}`}>
        <div className="pf-wrap">
          <div className="pf-nav-inner">
            <a href="/" className="pf-logo" onClick={(e) => {
              if (window.location.pathname === '/' || window.location.pathname === '/gioithieu') {
                e.preventDefault();
                goTo('hero')(e);
              }
            }}>
              <img src="/logo.png" alt="TH Logo" className="pf-logo-img" />
              <span className="pf-logo-text">trịnh quang huy<span className="dot">.</span></span>
            </a>

            {/* Desktop links */}
            <ul className="pf-links">
              {NAV.map(({ id, label, icon }) => (
                <li key={id}>
                  <a href={`#${id}`} className={activeId === id ? 'on' : ''} onClick={goTo(id)}>
                    {icon && <i className={icon} style={{ marginRight: '5px' }} />}
                    {label}
                  </a>
                </li>
              ))}
              <li>
                <Link to="/karaoke" className="cta">
                  <i className="fas fa-microphone" style={{ marginRight: '6px' }} />
                  Karaoke App
                </Link>
              </li>
            </ul>

            {/* Hamburger – mobile only */}
            <button className="pf-burger" onClick={() => setDrawerOpen((p) => !p)} aria-label="Menu">
              <span /><span /><span />
            </button>
          </div>
        </div>
      </nav>

      {/* Mobile drawer – separate from desktop links */}
      <ul className={`pf-drawer ${drawerOpen ? 'open' : ''}`}>
        {NAV.map(({ id, label, icon }) => (
          <li key={id}>
            <a href={`#${id}`} className={activeId === id ? 'on' : ''} onClick={goTo(id)}>
              {icon && <i className={icon} style={{ marginRight: '8px' }} />}
              {label}
            </a>
          </li>
        ))}
        <li>
          <Link to="/karaoke" className="cta" onClick={() => setDrawerOpen(false)}>
            <i className="fas fa-microphone" style={{ marginRight: '8px' }} />
            Karaoke App
          </Link>
        </li>
      </ul>

      {/* ────────── HERO ────────── */}
      <section className="pf-hero" id="hero">
        {/* Animated gradient blobs */}
        <div className="pf-hero-blob blob-1" />
        <div className="pf-hero-blob blob-2" />
        <div className="pf-hero-blob blob-3" />

        <div className="pf-wrap" style={{ width: '100%' }}>
          <div className="pf-hero-grid">
            {/* Left text */}
            <div>
              <div className="pf-badge">
                <i className="fas fa-circle" />
                Đang tìm việc · Open to work
              </div>

              <h1 className="pf-name">
                Trịnh Quang Huy
              </h1>

              <div className="pf-role">
                <span>Software Developer</span>
                <span className="pf-role-dot" />
                <span>IT Support</span>
                <span className="pf-role-dot" />
                <span className="mono" style={{ fontSize: '.85rem', color: 'var(--pink)' }}>@Táo</span>
              </div>

              <div className="pf-chips">
                <span className="chip"><i className="fas fa-calendar" />03/12/2004</span>
                <span className="chip"><i className="fas fa-map-marker-alt" />TP. Bắc Ninh</span>
                <span className="chip"><i className="fas fa-flag" />Việt Nam</span>
              </div>

              <p className="pf-desc">
                Lập trình viên yêu thích công nghệ và tự động hóa quy trình làm việc.
                Chuyên xây dựng hệ thống quản lý doanh nghiệp, ứng dụng web và công cụ
                hỗ trợ kỹ thuật. Có kinh nghiệm IT Support và môi trường sản xuất công nghiệp.
              </p>

              <div className="pf-actions">
                <a href="#projects" className="btn btn-primary" onClick={goTo('projects')}>
                  <i className="fas fa-folder-open" /> Xem dự án
                </a>
                <a href="#contact" className="btn btn-ghost" onClick={goTo('contact')}>
                  <i className="fas fa-paper-plane" /> Liên hệ
                </a>
                <a
                  href="https://github.com/taodangcap"
                  target="_blank" rel="noopener noreferrer"
                  className="btn btn-ghost"
                >
                  <i className="fab fa-github" /> GitHub
                </a>
              </div>
            </div>

            {/* Right avatar */}
            <div className="pf-avatar-card">
              <img
                src="/avt.png"
                alt="Trịnh Quang Huy"
                onError={(e) => {
                  e.currentTarget.style.display = 'none';
                  e.currentTarget.nextSibling.style.display = 'flex';
                }}
              />
              <div className="pf-avatar-fallback">🧑‍💻</div>
            </div>
          </div>
        </div>
      </section>

      {/* ────────── ABOUT ────────── */}
      <section className="pf-section pf-section-mint" id="about">
        <div className="pf-wrap">
          <p className="sec-label sec-label-pink">Giới thiệu</p>
          <h2 className="sec-title">Về bản thân</h2>
          <p className="sec-sub">Đôi nét về hành trình và con người tôi.</p>

          <div className="about-grid">
            {/* Info card */}
            <div>
              <div className="info-card fade-up">
                <div className="info-card-header">
                  <i className="fas fa-heart animate-pulse" style={{ color: 'var(--white)' }} />
                  <span>Thông tin cá nhân</span>
                </div>
                <div className="info-row">
                  <span className="info-key">Họ tên</span>
                  <span className="info-val">Trịnh Quang Huy</span>
                </div>
                <div className="info-row">
                  <span className="info-key">Biệt danh</span>
                  <span className="info-val" style={{ color: 'var(--pink-d)' }}>Táo</span>
                </div>
                <div className="info-row">
                  <span className="info-key">Ngày sinh</span>
                  <span className="info-val">03 / 12 / 2004</span>
                </div>
                <div className="info-row">
                  <span className="info-key">Quốc tịch</span>
                  <span className="info-val">Việt Nam</span>
                </div>
                <div className="info-row">
                  <span className="info-key">Quê hương</span>
                  <span className="info-val">Tiền Phong, Tp. Bắc Ninh</span>
                </div>
                <div className="info-row">
                  <span className="info-key">Trạng thái</span>
                  <span className="info-val">
                    <span className="status-dot">Open to work</span>
                  </span>
                </div>
                <div className="info-row">
                  <span className="info-key">GitHub</span>
                  <span className="info-val">
                    <a href="https://github.com/taodangcap" target="_blank" rel="noopener noreferrer">
                      @taodangcap
                    </a>
                  </span>
                </div>
                <div className="info-row">
                  <span className="info-key">LinkedIn</span>
                  <span className="info-val">
                    <a href="https://www.linkedin.com/in/trinhquanghuyvn/" target="_blank" rel="noopener noreferrer">
                      trinhquanghuyvn
                    </a>
                  </span>
                </div>
              </div>

              <div className="socials fade-up" style={{ marginTop: '1rem' }}>
                {[
                  { href: 'https://github.com/taodangcap', icon: 'fab fa-github', l: 'GitHub', cls: 'github' },
                  { href: 'https://www.linkedin.com/in/trinhquanghuyvn/', icon: 'fab fa-linkedin', l: 'LinkedIn', cls: 'linkedin' },
                  { href: 'https://facebook.com/taodangcap', icon: 'fab fa-facebook', l: 'Facebook', cls: 'facebook' },
                  { href: 'https://www.instagram.com/taodangcap/', icon: 'fab fa-instagram', l: 'Instagram', cls: 'instagram' },
                  { href: 'https://soundcloud.com/taodangcap', icon: 'fab fa-soundcloud', l: 'SoundCloud', cls: 'soundcloud' },
                ].map(({ href, icon, l, cls }) => (
                  <a key={l} href={href} target="_blank" rel="noopener noreferrer" className={`soc-btn ${cls}`}>
                    <i className={icon} />{l}
                  </a>
                ))}
              </div>
            </div>

            {/* About text */}
            <div className="about-body fade-up">
              <p>
                Xin chào! Tôi là <strong>Trịnh Quang Huy (Táo)</strong> — lập trình viên sinh năm 2004 với
                đam mê xây dựng các sản phẩm công nghệ có giá trị thực tế. Tôi có nền tảng
                về <strong>Công nghệ Thông tin</strong> và kinh nghiệm thực tế trong lĩnh vực IT Support.
              </p>
              <p>
                Tôi tập trung vào việc tạo ra các <strong>hệ thống quản lý doanh nghiệp</strong> và
                ứng dụng web với giao diện thân thiện, hiệu năng cao. Ngoài lập trình, tôi còn
                có kinh nghiệm làm việc trong môi trường sản xuất công nghệ cao — trực tiếp
                tham gia dây chuyền sản xuất <strong>pin MacBook Air M2</strong>.
              </p>
              <p>
                Mục tiêu của tôi là phát triển sự nghiệp trong lĩnh vực <strong>Software Development</strong>{' '}
                và <strong>Automation</strong> — xây dựng các sản phẩm mang lại giá trị thiết thực
                và trải nghiệm người dùng xuất sắc.
              </p>

              <div className="stats-strip">
                {[
                  { n: '5+', l: 'Dự án hoàn thành' },
                  { n: '2+', l: 'Năm kinh nghiệm' },
                  { n: '10+', l: 'Công nghệ' },
                ].map(({ n, l }) => (
                  <div key={l} className="stat">
                    <div className="stat-n">{n}</div>
                    <div className="stat-l">{l}</div>
                  </div>
                ))}
              </div>
            </div>
          </div>
        </div>
      </section>

      {/* ────────── SKILLS ────────── */}
      <section className="pf-section" id="skills">
        <div className="pf-wrap">
          <p className="sec-label sec-label-teal">Kỹ năng</p>
          <h2 className="sec-title">Chuyên môn</h2>
          <p className="sec-sub">Bộ kỹ năng để xây dựng sản phẩm từ A đến Z.</p>

          <div className="skills-grid">
            {SKILLS.map(({ cat, icon, cls, tags }) => (
              <div key={cat} className={`skill-card ${cls} fade-up`}>
                <div className="sk-head">
                  <div className={`sk-icon ${cls}`}><i className={`fas ${icon}`} /></div>
                  <h3 className="sk-title">{cat}</h3>
                </div>
                <div className="sk-tags">
                  {tags.map((t) => <span key={t} className="sk-tag">{t}</span>)}
                </div>
              </div>
            ))}
          </div>
        </div>
      </section>

      {/* ────────── EXPERIENCE ────────── */}
      <section className="pf-section pf-section-lavender" id="experience">
        <div className="pf-wrap">
          <p className="sec-label sec-label-purple">Kinh nghiệm</p>
          <h2 className="sec-title">Kinh nghiệm làm việc</h2>
          <p className="sec-sub">Kết hợp kỹ thuật phần mềm và môi trường sản xuất thực tế.</p>

          <div className="exp-list">
            {EXPERIENCES.map(({ role, company, icon, cls, points }) => (
              <div key={role} className="exp-card fade-up">
                <div className={`exp-icon-wrap ${cls}`}>
                  <i className={`fas ${icon}`} />
                </div>
                <div>
                  <h3 className="exp-role">{role}</h3>
                  <p className="exp-co">{company}</p>
                  <ul className="exp-pts">
                    {points.map((pt) => <li key={pt}>{pt}</li>)}
                  </ul>
                </div>
              </div>
            ))}
          </div>
        </div>
      </section>

      {/* ────────── PROJECTS ────────── */}
      <section className="pf-section" id="projects">
        <div className="pf-wrap">
          <p className="sec-label sec-label-pink">Dự án</p>
          <h2 className="sec-title">Dự án nổi bật</h2>
          <p className="sec-sub">Các sản phẩm tôi đã xây dựng — từ ý tưởng đến triển khai.</p>

          <div className="proj-grid">
            {PROJECTS.map(({ em, name, desc, tech, feats, internalLink }) => (
              <div key={name} className="proj-card fade-up">
                <div className="proj-stripe" />
                <div className="proj-top">
                  <div className="proj-em"><i className={em} /></div>
                  <h3 className="proj-name">{name}</h3>
                  <p className="proj-desc">{desc}</p>
                </div>
                <div className="proj-pills">
                  {tech.map((t) => <span key={t} className="pill">{t}</span>)}
                </div>
                <ul className="proj-feats">
                  {feats.map((f) => (
                    <li key={f}><i className="fas fa-check-circle" />{f}</li>
                  ))}
                </ul>
                <div className="proj-foot">
                  {internalLink ? (
                    <Link to={internalLink} className="btn btn-primary" style={{ width: '100%', justifyContent: 'center' }}>
                      <i className="fas fa-arrow-right" /> Xem dự án
                    </Link>
                  ) : (
                    <span className="btn btn-ghost" style={{ width: '100%', justifyContent: 'center', opacity: .5, cursor: 'default' }}>
                      <i className="fas fa-lock" /> Dự án riêng tư
                    </span>
                  )}
                </div>
              </div>
            ))}
          </div>
        </div>
      </section>

      {/* ────────── CONTACT ────────── */}
      <section className="pf-section pf-section-peach" id="contact">
        <div className="pf-wrap">
          <p className="sec-label sec-label-orange">Liên hệ</p>
          <h2 className="sec-title">Kết nối với tôi</h2>
          <p className="sec-sub">Có dự án hoặc cơ hội việc làm? Hãy liên hệ!</p>

          <div className="contact-grid">
            {/* Methods */}
            <div className="contact-methods fade-up">
              {[
                { icon: 'fas fa-envelope', k: 'Email', v: 'contact@trinhquanghuy.net', href: 'mailto:contact@trinhquanghuy.net' },
                { icon: 'fab fa-github', k: 'GitHub', v: 'github.com/taodangcap', href: 'https://github.com/taodangcap' },
                { icon: 'fab fa-facebook', k: 'Facebook', v: 'fb.com/taodangcap', href: 'https://facebook.com/taodangcap' },
                { icon: 'fab fa-linkedin', k: 'LinkedIn', v: 'trinhquanghuyvn', href: 'https://www.linkedin.com/in/trinhquanghuyvn/' },
              ].map(({ icon, k, v, href }) => (
                <a key={k} href={href} target="_blank" rel="noopener noreferrer" className="c-method">
                  <div className="c-icon"><i className={icon} /></div>
                  <div>
                    <p className="c-key">{k}</p>
                    <p className="c-val">{v}</p>
                  </div>
                  <i className="fas fa-arrow-right" style={{ marginLeft: 'auto', color: 'var(--ink-soft)', fontSize: '.75rem' }} />
                </a>
              ))}

              <div className="location-pill">
                <i className="fas fa-map-marker-alt" />
                <span>TP. Bắc Ninh, Việt Nam</span>
              </div>
            </div>

            {/* Form */}
            <div className="c-form fade-up">
              <div className="c-form-header">
                <i className="fas fa-paper-plane" style={{ color: 'var(--white)' }} />
                <h3>Gửi tin nhắn</h3>
              </div>
              <div className="c-form-body">
                <form onSubmit={submit}>
                  <div className="f-group">
                    <label className="f-label" htmlFor="f-name">Họ và tên</label>
                    <input id="f-name" className="f-input" type="text" placeholder="Trịnh Quang Huy"
                      value={form.name} onChange={(e) => setForm((p) => ({ ...p, name: e.target.value }))} required disabled={submitting} />
                  </div>
                  <div className="f-group">
                    <label className="f-label" htmlFor="f-email">Email</label>
                    <input id="f-email" className="f-input" type="email" placeholder="email@example.com"
                      value={form.email} onChange={(e) => setForm((p) => ({ ...p, email: e.target.value }))} required disabled={submitting} />
                  </div>
                  <div className="f-group">
                    <label className="f-label" htmlFor="f-msg">Tin nhắn</label>
                    <textarea id="f-msg" className="f-textarea" placeholder="Mô tả về cơ hội hoặc dự án..."
                      value={form.msg} onChange={(e) => setForm((p) => ({ ...p, msg: e.target.value }))} required disabled={submitting} />
                  </div>
                  <button type="submit" className="btn btn-primary" style={{ width: '100%', justifyContent: 'center' }} disabled={submitting}>
                    <i className={submitting ? "fas fa-spinner fa-spin" : "fas fa-paper-plane"} /> {submitting ? 'Đang gửi...' : 'Gửi tin nhắn'}
                  </button>
                </form>
              </div>
            </div>
          </div>
        </div>
      </section>

      {/* ────────── FOOTER ────────── */}
      <footer className="pf-footer">
        <div className="pf-wrap">
          <div className="pf-footer-inner">
            <p className="pf-footer-copy">© 2026 Trịnh Quang Huy · Made with ❤️</p>
            <div className="pf-footer-links">
              <a href="https://github.com/taodangcap" target="_blank" rel="noopener noreferrer">GitHub</a>
              <Link to="/karaoke">Karaoke App</Link>
              <a href="#hero" onClick={goTo('hero')}>↑ Lên đầu</a>
            </div>
          </div>
        </div>
      </footer>

      {/* Back to Top Button */}
      <button
        className={`pf-back-to-top ${showScrollTop ? 'visible' : ''}`}
        onClick={goTo('hero')}
        aria-label="Cuộn lên đầu"
      >
        <i className="fas fa-arrow-up" />
      </button>
    </div>
  );
}
