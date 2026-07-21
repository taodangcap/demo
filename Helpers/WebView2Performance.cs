using System.IO;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ShowCuePlayer.Helpers;

/// <summary>
/// Cấu hình WebView2 cho màn OUTPUT karaoke mượt hơn (gần path USB/MediaElement).
/// Chromium mặc định throttle khi cửa sổ không focus / bị occlusion → giật trên màn phụ.
/// </summary>
public static class WebView2Performance
{
    private static CoreWebView2Environment? _outputEnv;
    private static readonly SemaphoreSlim EnvLock = new(1, 1);

    /// <summary>
    /// Flags chống throttle + ưu tiên GPU decode/raster — quan trọng cho màn OUTPUT không focus.
    /// </summary>
    public const string BrowserArgs =
        "--autoplay-policy=no-user-gesture-required " +
        "--disable-background-timer-throttling " +
        "--disable-renderer-backgrounding " +
        "--disable-backgrounding-occluded-windows " +
        "--disable-features=CalculateNativeWinOcclusion,IntensiveWakeUpThrottling " +
        "--enable-gpu-rasterization " +
        "--enable-zero-copy " +
        "--ignore-gpu-blocklist " +
        "--disable-gpu-driver-bug-workarounds " +
        "--enable-features=VaapiVideoDecoder,CanvasOopRasterization,PlatformHEVCDecoderSupport";

    public static async Task<CoreWebView2Environment> GetOutputEnvironmentAsync()
    {
        if (_outputEnv is not null) return _outputEnv;

        await EnvLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_outputEnv is not null) return _outputEnv;

            var userData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ShowCuePlayer", "WebView2-Output");
            Directory.CreateDirectory(userData);

            var options = new CoreWebView2EnvironmentOptions(BrowserArgs);
            _outputEnv = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: userData,
                options: options).ConfigureAwait(true);
            return _outputEnv;
        }
        finally
        {
            EnvLock.Release();
        }
    }

    /// <summary>Apply once after CoreWebView2 is ready — UI chrome off, hardware path on.</summary>
    public static void ApplyOutputSettings(CoreWebView2 core)
    {
        if (core is null) return;
        try
        {
            var s = core.Settings;
            s.IsStatusBarEnabled = false;
            s.AreDefaultContextMenusEnabled = false;
            s.IsZoomControlEnabled = false;
            s.IsBuiltInErrorPageEnabled = false;
            s.IsScriptEnabled = true;
            s.IsWebMessageEnabled = true;
            try { s.IsGeneralAutofillEnabled = false; } catch { /* runtime */ }
            try { s.IsPasswordAutosaveEnabled = false; } catch { /* runtime */ }
            try { s.IsSwipeNavigationEnabled = false; } catch { /* runtime */ }
            try { s.AreBrowserAcceleratorKeysEnabled = false; } catch { /* runtime */ }
            try { s.IsPinchZoomEnabled = false; } catch { /* runtime */ }
        }
        catch { /* older runtime */ }

        try { core.IsMuted = false; }
        catch { /* ignore */ }
    }

    public static async Task EnsureOptimizedAsync(WebView2 webView)
    {
        ArgumentNullException.ThrowIfNull(webView);
        var env = await GetOutputEnvironmentAsync().ConfigureAwait(true);
        await webView.EnsureCoreWebView2Async(env).ConfigureAwait(true);
        if (webView.CoreWebView2 is { } core)
            ApplyOutputSettings(core);

        try
        {
            webView.DefaultBackgroundColor = System.Drawing.Color.Black;
            webView.ZoomFactor = 1.0;
        }
        catch { /* ignore */ }
    }
}
