/**
 * Bridge between KTV web and ShowCuePlayer (WebView2).
 * Safe no-op when not running inside chrome.webview.
 */

const isHost = () => !!(typeof window !== 'undefined' && window.chrome?.webview)

export function isShowCueBridgeAvailable() {
  return isHost()
}

/** Web → ShowCue */
export function postToShowCue(payload) {
  if (!isHost()) return false
  try {
    const msg = typeof payload === 'string' ? payload : JSON.stringify(payload)
    window.chrome.webview.postMessage(msg)
    return true
  } catch (e) {
    console.warn('[ShowCueBridge] post failed', e)
    return false
  }
}

/**
 * Listen for ShowCue → Web commands.
 * @param {(data: object) => void} handler
 * @returns {() => void} unsubscribe
 */
export function onShowCueMessage(handler) {
  if (!isHost()) return () => {}

  const listener = (event) => {
    try {
      let data = event.data
      if (typeof data === 'string') {
        try {
          data = JSON.parse(data)
        } catch {
          // plain string e.g. ESC
          data = { type: 'karaoke', action: data }
        }
      }
      if (!data || typeof data !== 'object') return
      handler(data)
    } catch (e) {
      console.warn('[ShowCueBridge] parse message failed', e)
    }
  }

  window.chrome.webview.addEventListener('message', listener)
  return () => {
    try {
      window.chrome.webview.removeEventListener('message', listener)
    } catch { /* ignore */ }
  }
}

export function notifyReady(session) {
  return postToShowCue({ type: 'karaoke', action: 'ready', session })
}

export function notifyPlaying({ session, title, artist, remaining }) {
  return postToShowCue({
    type: 'karaoke',
    action: 'playing',
    session,
    title: title || '',
    artist: artist || '',
    remaining: remaining ?? null
  })
}

export function notifyProgress({ session, position, duration }) {
  return postToShowCue({
    type: 'karaoke',
    action: 'progress',
    session,
    position: Number.isFinite(position) ? position : 0,
    duration: Number.isFinite(duration) ? duration : 0
  })
}

export function notifyPaused(session) {
  return postToShowCue({ type: 'karaoke', action: 'paused', session })
}

export function notifyEnded(session) {
  return postToShowCue({ type: 'karaoke', action: 'ended', session })
}

export function notifyIdle(session) {
  return postToShowCue({ type: 'karaoke', action: 'idle', session })
}
