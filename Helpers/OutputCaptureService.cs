using System.IO;
using System.Threading;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ShowCuePlayer.Helpers;

/// <summary>
/// Capture khung hình WebView2 OUTPUT → panel Preview.
/// An toàn khi WebView bị dispose (tắt KARAOKE) — không crash.
/// </summary>
public sealed class OutputCaptureService : IDisposable
{
    private readonly DispatcherTimer _timer;
    private WebView2? _source;
    private Action<BitmapSource?>? _onFrame;
    private int _busy; // 0/1 interlocked-style
    private bool _disposed;
    private int _generation; // tăng mỗi Stop/Start → hủy frame async cũ

    public int IntervalMs
    {
        get => (int)_timer.Interval.TotalMilliseconds;
        set => _timer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(value, 50, 500));
    }

    public bool IsRunning => _timer.IsEnabled;

    public OutputCaptureService()
    {
        // ApplicationIdle: không tranh UI/render với màn OUTPUT (USB MediaElement mượt hơn vì không bị capture)
        _timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
        {
            // ~12fps mặc định — CapturePreviewAsync tốn GPU; 30fps làm giật OUTPUT karaoke
            Interval = TimeSpan.FromMilliseconds(80)
        };
        _timer.Tick += OnTick;
    }

    public void Start(WebView2 source, Action<BitmapSource?> onFrame)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(onFrame);
        _generation++;
        _source = source;
        _onFrame = onFrame;
        if (!_timer.IsEnabled)
            _timer.Start();
    }

    /// <summary>Dừng ngay — gọi TRƯỚC khi Close/Dispose WebView.</summary>
    public void Stop()
    {
        _timer.Stop();
        _generation++;
        _source = null;
        _onFrame = null;
        Interlocked.Exchange(ref _busy, 0);
    }

    private async void OnTick(object? sender, EventArgs e)
    {
        if (_disposed || !_timer.IsEnabled) return;
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return;

        var gen = _generation;
        var web = _source;
        var sink = _onFrame;

        try
        {
            if (web is null || sink is null) return;
            if (gen != _generation) return;

            // WebView đã dispose / đang đóng
            CoreWebView2? core;
            try
            {
                if (!web.IsInitialized) return;
                core = web.CoreWebView2;
            }
            catch (ObjectDisposedException)
            {
                Stop();
                return;
            }
            catch (InvalidOperationException)
            {
                return;
            }

            if (core is null) return;

            await using var ms = new MemoryStream();
            try
            {
                await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Jpeg, ms);
            }
            catch (ObjectDisposedException)
            {
                Stop();
                return;
            }
            catch (InvalidOperationException)
            {
                return;
            }
            catch
            {
                return; // navigate / not ready
            }

            if (_disposed || gen != _generation || !ReferenceEquals(sink, _onFrame)) return;

            ms.Position = 0;
            if (ms.Length < 32) return;

            BitmapImage bmp;
            try
            {
                bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();
            }
            catch
            {
                return;
            }

            if (_disposed || gen != _generation || !ReferenceEquals(sink, _onFrame)) return;
            try { sink(bmp); }
            catch { /* UI đã đóng */ }
        }
        catch (ObjectDisposedException)
        {
            Stop();
        }
        catch (InvalidOperationException)
        {
            // WebView can become unavailable between any two property accesses.
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTick;
        _generation++;
        _source = null;
        _onFrame = null;
    }
}
