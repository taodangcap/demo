using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ShowCuePlayer.Helpers;

/// <summary>
/// Captures the Program output HWND when DWM thumbnail registration is unavailable.
/// This fallback is source-neutral and does not create a second media/WebView pipeline.
/// </summary>
public sealed class WindowHwndCaptureService : IDisposable
{
    private readonly object _sync = new();
    private readonly Dispatcher _dispatcher;
    private System.Threading.Timer? _timer;
    private IntPtr _sourceHwnd;
    private Action<BitmapSource?>? _onFrame;
    private int _busy;
    private bool _disposed;
    private bool _isRunning;
    private int _generation;
    private int _intervalMs = 33;
    private int _maxWidth = 960;

    public int IntervalMs
    {
        get { lock (_sync) return _intervalMs; }
        set
        {
            lock (_sync)
            {
                _intervalMs = Math.Clamp(value, 16, 200);
                if (_isRunning)
                    _timer?.Change(_intervalMs, _intervalMs);
            }
        }
    }

    /// <summary>Giới hạn bề ngang preview (nhỏ = mượt hơn). Mặc định 960.</summary>
    public int MaxWidth
    {
        get { lock (_sync) return _maxWidth; }
        set { lock (_sync) _maxWidth = Math.Clamp(value, 320, 1920); }
    }

    public bool IsRunning
    {
        get { lock (_sync) return _isRunning; }
    }

    public WindowHwndCaptureService()
    {
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
    }

    public void Start(IntPtr sourceHwnd, Action<BitmapSource?> onFrame)
    {
        if (sourceHwnd == IntPtr.Zero) throw new ArgumentException("Invalid HWND", nameof(sourceHwnd));
        ArgumentNullException.ThrowIfNull(onFrame);
        lock (_sync)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(WindowHwndCaptureService));
            _generation++;
            _sourceHwnd = sourceHwnd;
            _onFrame = onFrame;
            _isRunning = true;
            if (_timer is null)
                _timer = new System.Threading.Timer(OnTick, null, 0, _intervalMs);
            else
                _timer.Change(0, _intervalMs);
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            _isRunning = false;
            _generation++;
            _sourceHwnd = IntPtr.Zero;
            _onFrame = null;
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }

    private void OnTick(object? state)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return;

        try
        {
            int gen;
            int maxWidth;
            IntPtr hwnd;
            Action<BitmapSource?>? sink;
            lock (_sync)
            {
                if (_disposed || !_isRunning) return;
                gen = _generation;
                maxWidth = _maxWidth;
                hwnd = _sourceHwnd;
                sink = _onFrame;
            }

            if (hwnd == IntPtr.Zero || sink is null) return;
            if (!IsWindow(hwnd))
            {
                Stop();
                return;
            }

            if (!GetClientRect(hwnd, out var rc) || rc.Right <= 0 || rc.Bottom <= 0)
                return;

            int srcW = rc.Right - rc.Left;
            int srcH = rc.Bottom - rc.Top;
            if (srcW < 2 || srcH < 2) return;

            // Scale xuống theo MaxWidth — view nhỏ, mượt, CPU thấp
            int dstW = srcW;
            int dstH = srcH;
            if (srcW > maxWidth)
            {
                double scale = (double)maxWidth / srcW;
                dstW = maxWidth;
                dstH = Math.Max(1, (int)Math.Round(srcH * scale));
            }

            // Chẵn cho BitBlt
            if ((dstW & 1) == 1) dstW--;
            if ((dstH & 1) == 1) dstH--;
            if (dstW < 2 || dstH < 2) return;

            IntPtr hdcWindow = GetDC(hwnd);
            if (hdcWindow == IntPtr.Zero) return;

            IntPtr hdcMem = CreateCompatibleDC(hdcWindow);
            IntPtr hdcScaled = CreateCompatibleDC(hdcWindow);
            IntPtr hBmpFull = IntPtr.Zero;
            IntPtr hBmpScaled = IntPtr.Zero;
            IntPtr oldFull = IntPtr.Zero;
            IntPtr oldScaled = IntPtr.Zero;

            try
            {
                hBmpFull = CreateCompatibleBitmap(hdcWindow, srcW, srcH);
                if (hBmpFull == IntPtr.Zero) return;
                oldFull = SelectObject(hdcMem, hBmpFull);

                // PW_RENDERFULLCONTENT: bắt cả surface WebView/DWM (Win8.1+)
                if (!PrintWindow(hwnd, hdcMem, PW_RENDERFULLCONTENT | PW_CLIENTONLY))
                {
                    // Fallback BitBlt client
                    BitBlt(hdcMem, 0, 0, srcW, srcH, hdcWindow, 0, 0, SRCCOPY);
                }

                hBmpScaled = CreateCompatibleBitmap(hdcWindow, dstW, dstH);
                if (hBmpScaled == IntPtr.Zero) return;
                oldScaled = SelectObject(hdcScaled, hBmpScaled);

                SetStretchBltMode(hdcScaled, HALFTONE);
                StretchBlt(hdcScaled, 0, 0, dstW, dstH, hdcMem, 0, 0, srcW, srcH, SRCCOPY);

                // GetDIBits requires the bitmap to be deselected from its memory DC.
                SelectObject(hdcScaled, oldScaled);
                oldScaled = IntPtr.Zero;
                var pixels = CopyBitmapBits(hBmpScaled, dstW, dstH);
                if (pixels is null) return;

                var bmpSource = BitmapSource.Create(
                    dstW, dstH, 96, 96,
                    PixelFormats.Bgr32, null,
                    pixels,
                    dstW * 4);
                bmpSource.Freeze();

                lock (_sync)
                {
                    if (_disposed || !_isRunning || gen != _generation || !ReferenceEquals(sink, _onFrame))
                        return;
                }

                if (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished) return;
                _dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
                {
                    lock (_sync)
                    {
                        if (_disposed || !_isRunning || gen != _generation || !ReferenceEquals(sink, _onFrame))
                            return;
                    }
                    try { sink(bmpSource); } catch { /* ignore stale UI sinks */ }
                }));
            }
            finally
            {
                if (oldScaled != IntPtr.Zero) SelectObject(hdcScaled, oldScaled);
                if (oldFull != IntPtr.Zero) SelectObject(hdcMem, oldFull);
                if (hBmpScaled != IntPtr.Zero) DeleteObject(hBmpScaled);
                if (hBmpFull != IntPtr.Zero) DeleteObject(hBmpFull);
                if (hdcScaled != IntPtr.Zero) DeleteDC(hdcScaled);
                if (hdcMem != IntPtr.Zero) DeleteDC(hdcMem);
                if (hdcWindow != IntPtr.Zero) ReleaseDC(hwnd, hdcWindow);
            }
        }
        catch
        {
            // ignore frame errors
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private static byte[]? CopyBitmapBits(IntPtr hBitmap, int width, int height)
    {
        var bmi = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = -height, // top-down
                biPlanes = 1,
                biBitCount = 32,
                biCompression = BI_RGB
            }
        };

        var buffer = new byte[width * height * 4];
        IntPtr screen = GetDC(IntPtr.Zero);
        try
        {
            int scanLines = GetDIBits(screen, hBitmap, 0, (uint)height, buffer, ref bmi, DIB_RGB_COLORS);
            if (scanLines != height)
                return null;
        }
        finally
        {
            if (screen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screen);
        }
        return buffer;
    }

    public void Dispose()
    {
        System.Threading.Timer? timer;
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _isRunning = false;
            _generation++;
            _sourceHwnd = IntPtr.Zero;
            _onFrame = null;
            timer = _timer;
            _timer = null;
        }
        timer?.Dispose();
    }

    #region GDI

    private const int SRCCOPY = 0x00CC0020;
    private const int HALFTONE = 4;
    private const int BI_RGB = 0;
    private const int DIB_RGB_COLORS = 0;
    private const int PW_CLIENTONLY = 0x1;
    private const int PW_RENDERFULLCONTENT = 0x2;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public int bmiColors;
    }

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, int nFlags);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hdcDest, int x, int y, int cx, int cy,
        IntPtr hdcSrc, int x1, int y1, int rop);

    [DllImport("gdi32.dll")]
    private static extern bool StretchBlt(IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest,
        IntPtr hdcSrc, int xSrc, int ySrc, int wSrc, int hSrc, int rop);

    [DllImport("gdi32.dll")]
    private static extern int SetStretchBltMode(IntPtr hdc, int mode);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr hdc, IntPtr hbmp, uint uStartScan, uint cScanLines,
        [Out] byte[] lpvBits, ref BITMAPINFO lpbi, uint uUsage);

    #endregion
}
