using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace ShowCuePlayer.Views;

public partial class KaraokeSecondaryWindow : Window
{
    private string? _pendingUrl;

    /// <summary>WebView player OUTPUT — dùng capture mirror về control.</summary>
    public Microsoft.Web.WebView2.Wpf.WebView2 PlayerWebView => SecondaryWebView;

    /// <summary>Raised when operator presses Esc on this output window (or inside WebView).</summary>
    public event EventHandler? EscapeRequested;

    public KaraokeSecondaryWindow()
    {
        InitializeComponent();
        ShowActivated = false;
        // Tab riêng trên taskbar Windows (dễ Alt+Tab / tìm khi multi-màn)
        ShowInTaskbar = true;
        Title = "ShowCue · Karaoke OUTPUT";
        Topmost = true;
        Focusable = true;
        PreviewKeyDown += OnPreviewKeyDown;
        Loaded += KaraokeSecondaryWindow_Loaded;
        Closed += KaraokeSecondaryWindow_Closed;
        MouseRightButtonUp += KaraokeSecondaryWindow_MouseRightButtonUp;
        SourceInitialized += (_, _) => ApplySeparateTaskbarIdentity(this);
    }

    /// <summary>
    /// Gán AppUserModelID riêng cho cửa sổ karaoke → taskbar Windows có nút/tab tách khỏi Main.
    /// (Chỉ ShowInTaskbar=true vẫn bị Win10/11 gộp chung icon app.)
    /// </summary>
    public static void ApplySeparateTaskbarIdentity(Window window)
    {
        try
        {
            window.ShowInTaskbar = true;
            var hwnd = new WindowInteropHelper(window).EnsureHandle();
            if (hwnd == IntPtr.Zero) return;
            var appId = window is VideoWindow
                ? "ShowCuePlayer.VideoOutput"
                : "ShowCuePlayer.KaraokeOutput";
            SetAppUserModelId(hwnd, appId);
        }
        catch { /* ignore — tab vẫn hiện nếu ShowInTaskbar */ }
    }

    private static void SetAppUserModelId(IntPtr hwnd, string appId)
    {
        var hr = SHGetPropertyStoreForWindow(hwnd, typeof(IPropertyStore).GUID, out var store);
        if (hr != 0 || store is null) return;

        try
        {
            var key = PKEY_AppUserModel_ID;
            var pv = new PropVariant(appId);
            store.SetValue(ref key, ref pv);
            store.Commit();
            pv.Clear();
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }
    }

    private static readonly PropertyKey PKEY_AppUserModel_ID =
        new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetPropertyStoreForWindow(
        IntPtr hwnd,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        out IPropertyStore ppv);

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PropertyKey
    {
        public Guid fmtid;
        public uint pid;
        public PropertyKey(Guid fmtid, uint pid) { this.fmtid = fmtid; this.pid = pid; }
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public IntPtr pointerValue;

        public PropVariant(string value)
        {
            vt = 31; // VT_LPWSTR
            pointerValue = Marshal.StringToCoTaskMemUni(value);
        }

        public void Clear()
        {
            if (pointerValue != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(pointerValue);
                pointerValue = IntPtr.Zero;
            }
            vt = 0;
        }
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        int GetCount(out uint cProps);
        int GetAt(uint iProp, out PropertyKey pkey);
        int GetValue(ref PropertyKey key, out PropVariant pv);
        int SetValue(ref PropertyKey key, ref PropVariant pv);
        int Commit();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            EscapeRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    private void KaraokeSecondaryWindow_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var menu = new System.Windows.Controls.ContextMenu();

        var closeItem = new System.Windows.Controls.MenuItem { Header = "Tắt output (Esc)" };
        closeItem.Click += (_, _) => EscapeRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(closeItem);
        menu.Items.Add(new System.Windows.Controls.Separator());

        var windowedItem = new System.Windows.Controls.MenuItem { Header = "Windowed Mode" };
        windowedItem.Click += (_, _) =>
        {
            Topmost = false;
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            Width = 960;
            Height = 540;
        };
        menu.Items.Add(windowedItem);

        var fullscreenItem = new System.Windows.Controls.MenuItem { Header = "Fullscreen" };
        fullscreenItem.Click += (_, _) =>
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
            Topmost = true;
        };
        menu.Items.Add(fullscreenItem);

        menu.IsOpen = true;
    }

    private async void KaraokeSecondaryWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await SecondaryWebView.EnsureCoreWebView2Async(null);
        if (SecondaryWebView.CoreWebView2 is not null)
        {
            var core = SecondaryWebView.CoreWebView2;
            await core.AddScriptToExecuteOnDocumentCreatedAsync(
                """
                (function () {
                  if (window.__scpEscHook) return;
                  window.__scpEscHook = true;
                  window.addEventListener('keydown', function (e) {
                    if (e.key === 'Escape') {
                      e.preventDefault();
                      e.stopPropagation();
                      try { chrome.webview.postMessage('ESC'); } catch (ex) {}
                    }
                  }, true);
                })();
                """);
            core.WebMessageReceived += (_, args) =>
            {
                string msg;
                try { msg = args.TryGetWebMessageAsString(); }
                catch { msg = args.WebMessageAsJson ?? string.Empty; }
                if (msg.Contains("ESC", StringComparison.OrdinalIgnoreCase))
                    Dispatcher.Invoke(() => EscapeRequested?.Invoke(this, EventArgs.Empty));
            };
        }

        if (!string.IsNullOrEmpty(_pendingUrl))
        {
            SecondaryWebView.CoreWebView2?.Navigate(_pendingUrl);
            _pendingUrl = null;
        }
    }

    private void KaraokeSecondaryWindow_Closed(object? sender, EventArgs e)
    {
        // Dispose WebView an toàn — capture đã stop từ MainWindow trước Close()
        try
        {
            if (SecondaryWebView is not null)
            {
                try { SecondaryWebView.CoreWebView2?.Stop(); } catch { /* ignore */ }
                SecondaryWebView.Dispose();
            }
        }
        catch { /* ignore disposed */ }
    }

    public void Navigate(string url)
    {
        if (SecondaryWebView.CoreWebView2 != null)
            SecondaryWebView.CoreWebView2.Navigate(url);
        else
            _pendingUrl = url;
    }

    /// <summary>Gửi message sang KTV player (pause/blackout/…).</summary>
    public void PostMessage(string jsonOrText)
    {
        try
        {
            var core = SecondaryWebView.CoreWebView2;
            if (core is null) return;
            if (jsonOrText.TrimStart().StartsWith('{'))
                core.PostWebMessageAsJson(jsonOrText);
            else
                core.PostWebMessageAsString(jsonOrText);
        }
        catch { /* ignore */ }
    }
}
