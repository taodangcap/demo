import { useState, useEffect, useCallback } from 'react'
import { useNavigate } from 'react-router-dom'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import {
    faServer,
    faCheckCircle,
    faTimesCircle,
    faSync,
    faArrowLeft,
    faBolt,
    faGlobe,
    faMicrochip
} from '@fortawesome/free-solid-svg-icons'

const Status = () => {
    const navigate = useNavigate()
    const [statusData, setStatusData] = useState([
        {
            id: 'worker_youtube',
            name: 'YouTube Proxy (Worker)',
            url: import.meta.env.VITE_WORKER_URL,
            status: 'testing',
            latency: null,
            type: 'Cloudflare Worker',
            description: 'Tìm kiếm & Vượt chặn YouTube'
        },
        {
            id: 'remote_server',
            name: 'KTV Backend (Render)',
            url: import.meta.env.VITE_REMOTE_SERVER_URL,
            status: 'testing',
            latency: null,
            type: 'Node.js Server',
            description: 'Điều khiển từ xa & API dự phòng'
        },
        {
            id: 'proxy_soundcloud',
            name: 'SoundCloud Proxy (Worker)',
            url: import.meta.env.VITE_PROXY_SERVER_URL,
            status: 'testing',
            latency: null,
            type: 'Cloudflare Worker',
            description: 'Tìm kiếm SoundCloud'
        }
    ])

    const checkStatus = async (index) => {
        const item = statusData[index]
        if (!item.url) {
            setStatusData(prev => {
                const next = [...prev]
                next[index] = { ...prev[index], status: 'offline', error: 'URL không tồn tại' }
                return next
            })
            return
        }

        const start = Date.now()
        const controller = new AbortController()
        const timeoutId = setTimeout(() => controller.abort(), 5000) // Timeout sau 5 giây

        try {
            const testUrl = item.url.endsWith('/') ? item.url : `${item.url}/`

            // Thử gọi một request đơn giản với mode no-cors để tránh bị chặn khi check ping
            await fetch(testUrl + '?ping=' + Date.now(), {
                method: 'GET',
                mode: 'no-cors', // Rất quan trọng để check server sống/chết mà không bị lỗi CORS
                signal: controller.signal
            })

            clearTimeout(timeoutId)
            const latency = Date.now() - start

            setStatusData(prev => {
                const next = [...prev]
                // Kể cả 404 thì server vẫn "Sống" (Online)
                next[index] = { ...next[index], status: 'online', latency }
                return next
            })
        } catch (error) {
            clearTimeout(timeoutId)
            setStatusData(prev => {
                const next = [...prev]
                next[index] = { ...next[index], status: 'offline', error: error.name === 'AbortError' ? 'Hết thời gian phản hồi' : error.message }
                return next
            })
        }
    }

    const checkAll = useCallback(() => {
        setStatusData(statusData.map(item => ({ ...item, status: 'testing', latency: null })))
        statusData.forEach((_, index) => checkStatus(index))
    }, [statusData])

    useEffect(() => {
        checkAll()
    }, [checkAll])

    return (
        <div className="min-h-screen bg-[#0f172a] text-white p-4 sm:p-8 font-sans selection:bg-blue-500/30">
            {/* Background Decor */}
            <div className="fixed inset-0 overflow-hidden pointer-events-none">
                <div className="absolute top-[-10%] left-[-10%] w-[40%] h-[40%] bg-blue-600/10 blur-[120px] rounded-full"></div>
                <div className="absolute bottom-[-10%] right-[-10%] w-[40%] h-[40%] bg-purple-600/10 blur-[120px] rounded-full"></div>
            </div>

            <div className="max-w-4xl mx-auto relative z-10">
                {/* Header */}
                <div className="flex items-center justify-between mb-8">
                    <button
                        onClick={() => navigate(-1)}
                        className="flex items-center space-x-2 text-gray-400 hover:text-white transition-colors group px-4 py-2 rounded-xl hover:bg-white/5"
                    >
                        <FontAwesomeIcon icon={faArrowLeft} className="group-hover:-translate-x-1 transition-transform" />
                        <span>Quay lại</span>
                    </button>

                    <div className="flex items-center space-x-3 bg-white/5 backdrop-blur-xl px-4 py-2 rounded-2xl border border-white/10">
                        <div className="w-2 h-2 bg-green-500 rounded-full animate-pulse"></div>
                        <span className="text-sm font-medium text-gray-300">Hệ thống đang hoạt động</span>
                    </div>
                </div>

                <div className="mb-12">
                    <h1 className="text-4xl sm:text-5xl font-extrabold tracking-tight mb-4 bg-gradient-to-r from-white via-blue-200 to-blue-400 bg-clip-text text-transparent">
                        Trình kiểm tra hệ thống
                    </h1>
                    <p className="text-gray-400 text-lg max-w-2xl leading-relaxed">
                        Kiểm tra trạng thái kết nối của các máy chủ Backend và Proxy hỗ trợ cho ứng dụng KTV.
                    </p>
                </div>

                {/* Action Bar */}
                <div className="flex justify-end mb-6">
                    <button
                        onClick={checkAll}
                        className="flex items-center space-x-2 bg-blue-600 hover:bg-blue-500 text-white px-6 py-3 rounded-2xl font-bold transition-all shadow-lg shadow-blue-600/20 active:scale-95 group"
                    >
                        <FontAwesomeIcon icon={faSync} className="group-active:rotate-180 transition-transform duration-500" />
                        <span>Kiểm tra lại tất cả</span>
                    </button>
                </div>

                {/* Status Cards Grid */}
                <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
                    {statusData.map((item) => (
                        <div
                            key={item.id}
                            className="bg-white/5 backdrop-blur-2xl border border-white/10 rounded-3xl p-6 transition-all hover:border-white/20 hover:bg-white/[0.07] group flex flex-col h-full"
                        >
                            <div className="flex justify-between items-start mb-6">
                                <div className={`w-12 h-12 rounded-2xl flex items-center justify-center text-xl transition-all ${item.status === 'online' ? 'bg-green-500/20 text-green-400' :
                                    item.status === 'offline' ? 'bg-red-500/20 text-red-400' :
                                        'bg-blue-500/20 text-blue-400 animate-pulse'
                                    }`}>
                                    <FontAwesomeIcon icon={item.status === 'online' ? faCheckCircle : item.status === 'offline' ? faTimesCircle : faServer} />
                                </div>
                                {item.latency && (
                                    <div className="flex items-center space-x-1.5 bg-white/5 px-3 py-1.5 rounded-full border border-white/5">
                                        <FontAwesomeIcon icon={faBolt} className="text-yellow-400 text-[10px]" />
                                        <span className="text-xs font-bold text-gray-300">{item.latency}ms</span>
                                    </div>
                                )}
                            </div>

                            <div className="mb-auto">
                                <h3 className="text-lg font-bold text-white mb-2 group-hover:text-blue-300 transition-colors">
                                    {item.name}
                                </h3>
                                <p className="text-sm text-gray-400 line-clamp-2 leading-relaxed mb-4">
                                    {item.description}
                                </p>
                            </div>

                            <div className="mt-4 pt-4 border-t border-white/5">
                                <div className="flex items-center space-x-2 text-[10px] uppercase tracking-wider font-bold text-gray-500 mb-2">
                                    <FontAwesomeIcon icon={faGlobe} />
                                    <span>Loại server</span>
                                </div>
                                <div className="text-xs text-gray-300 font-medium">
                                    {item.type}
                                </div>
                            </div>

                            <div className="mt-4">
                                <div className="flex items-center space-x-2 text-[10px] uppercase tracking-wider font-bold text-gray-500 mb-2">
                                    <FontAwesomeIcon icon={faMicrochip} />
                                    <span>Trạng thái</span>
                                </div>
                                <div className={`text-sm font-bold flex items-center space-x-2 ${item.status === 'online' ? 'text-green-400' :
                                    item.status === 'offline' ? 'text-red-400' : 'text-blue-400'
                                    }`}>
                                    <div className={`w-2 h-2 rounded-full ${item.status === 'online' ? 'bg-green-400 shadow-[0_0_8px_rgba(74,222,128,0.5)]' :
                                        item.status === 'offline' ? 'bg-red-400' : 'bg-blue-400 animate-ping'
                                        }`}></div>
                                    <span className="capitalize">{item.status === 'testing' ? 'Đang kiểm tra...' : item.status}</span>
                                </div>
                            </div>
                        </div>
                    ))}
                </div>

                {/* Footer Info */}
                <div className="mt-12 text-center">
                    <div className="inline-flex items-center space-x-2 bg-blue-500/10 text-blue-400 px-4 py-2 rounded-xl text-xs font-bold border border-blue-500/20">
                        <FontAwesomeIcon icon={faSync} />
                        <span>Tự động cập nhật mỗi khi tải trang</span>
                    </div>
                    <p className="mt-4 text-gray-500 text-xs">
                        © 2024 KTV APP - Hệ thống Monitoring thời gian thực
                    </p>
                </div>
            </div>
        </div>
    )
}

export default Status
