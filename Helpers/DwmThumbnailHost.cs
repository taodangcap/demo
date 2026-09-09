using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace ShowCuePlayer.Helpers;

/// <summary>
/// Mirrors an OUTPUT HWND into this element's bounds on the MainWindow HWND.
/// DWM requires the destination to be a top-level window, not a child HWND.
/// </summary>
public sealed class DwmThumbnailHost : FrameworkElement
{
    private IntPtr _thumbnail;
    private IntPtr _sourceHwnd;
    private IntPtr _destinationHwnd;

    public bool HasThumbnail => _thumbnail != IntPtr.Zero;
    public int? LastHResult { get; private set; }
    public bool PreserveAspectRatio { get; set; } = true;

    public DwmThumbnailHost()
    {
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public void SetSource(IntPtr sourceHwnd)
    {
        if (_sourceHwnd != sourceHwnd)
            UnregisterThumbnail();

        _sourceHwnd = sourceHwnd;
        if (_sourceHwnd == IntPtr.Zero)
        {
            LastHResult = null;
            return;
        }

        RegisterThumbnail();
        UpdateThumbnailProperties();
    }

    public void ClearSource()
    {
        UnregisterThumbnail();
        _sourceHwnd = IntPtr.Zero;
        _destinationHwnd = IntPtr.Zero;
        LastHResult = null;
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        UpdateThumbnailProperties();
    }

    public void UpdateThumbnailProperties()
    {
        if (_thumbnail == IntPtr.Zero || _destinationHwnd == IntPtr.Zero || !IsLoaded)
            return;

        var window = Window.GetWindow(this);
        if (window is null || ActualWidth <= 0 || ActualHeight <= 0)
            return;

        try
        {
            var boundsDip = TransformToAncestor(window)
                .TransformBounds(new Rect(0, 0, ActualWidth, ActualHeight));
            var dpi = VisualTreeHelper.GetDpi(window);
            var destination = new RECT
            {
                Left = (int)Math.Floor(boundsDip.Left * dpi.DpiScaleX),
                Top = (int)Math.Floor(boundsDip.Top * dpi.DpiScaleY),
                Right = (int)Math.Ceiling(boundsDip.Right * dpi.DpiScaleX),
                Bottom = (int)Math.Ceiling(boundsDip.Bottom * dpi.DpiScaleY)
            };
            if (destination.Right <= destination.Left || destination.Bottom <= destination.Top)
                return;

            if (PreserveAspectRatio
                && DwmQueryThumbnailSourceSize(_thumbnail, out var sourceSize) == 0
                && sourceSize.X > 0 && sourceSize.Y > 0)
            {
                var availableWidth = destination.Right - destination.Left;
                var availableHeight = destination.Bottom - destination.Top;
                var scale = Math.Min(
                    (double)availableWidth / sourceSize.X,
                    (double)availableHeight / sourceSize.Y);
                var fittedWidth = Math.Max(1, (int)Math.Round(sourceSize.X * scale));
                var fittedHeight = Math.Max(1, (int)Math.Round(sourceSize.Y * scale));
                var left = destination.Left + (availableWidth - fittedWidth) / 2;
                var top = destination.Top + (availableHeight - fittedHeight) / 2;
                destination = new RECT
                {
                    Left = left,
                    Top = top,
                    Right = left + fittedWidth,
                    Bottom = top + fittedHeight
                };
            }

            var properties = new DWM_THUMBNAIL_PROPERTIES
            {
                dwFlags = DWM_TNP_VISIBLE | DWM_TNP_RECTDESTINATION | DWM_TNP_OPACITY
                    | DWM_TNP_SOURCECLIENTAREAONLY,
                fVisible = true,
                opacity = 255,
                fSourceClientAreaOnly = true,
                rcDestination = destination
            };
            LastHResult = DwmUpdateThumbnailProperties(_thumbnail, ref properties);
            if (LastHResult != 0)
                UnregisterThumbnail();
        }
        catch
        {
            // Layout can be between visual trees while the preview panel is toggled.
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RegisterThumbnail();
        UpdateThumbnailProperties();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
        => UnregisterThumbnail();

    private void RegisterThumbnail()
    {
        if (!IsLoaded || _sourceHwnd == IntPtr.Zero)
            return;

        var window = Window.GetWindow(this);
        if (window is null || !IsWindow(_sourceHwnd))
        {
            LastHResult = E_INVALIDARG;
            return;
        }

        var destinationHwnd = new WindowInteropHelper(window).EnsureHandle();
        if (destinationHwnd == IntPtr.Zero)
        {
            LastHResult = E_HANDLE;
            return;
        }

        if (_thumbnail != IntPtr.Zero && _destinationHwnd == destinationHwnd)
            return;

        UnregisterThumbnail();
        _destinationHwnd = destinationHwnd;
        LastHResult = DwmRegisterThumbnail(_destinationHwnd, _sourceHwnd, out _thumbnail);
        if (LastHResult != 0)
            _thumbnail = IntPtr.Zero;
    }

    private void UnregisterThumbnail()
    {
        if (_thumbnail != IntPtr.Zero)
        {
            DwmUnregisterThumbnail(_thumbnail);
            _thumbnail = IntPtr.Zero;
        }
    }

    private const int E_INVALIDARG = unchecked((int)0x80070057);
    private const int E_HANDLE = unchecked((int)0x80070006);

    private const uint DWM_TNP_RECTDESTINATION = 0x00000001;
    private const uint DWM_TNP_OPACITY = 0x00000004;
    private const uint DWM_TNP_VISIBLE = 0x00000008;
    private const uint DWM_TNP_SOURCECLIENTAREAONLY = 0x00000010;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PSIZE
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DWM_THUMBNAIL_PROPERTIES
    {
        public uint dwFlags;
        public RECT rcDestination;
        public RECT rcSource;
        public byte opacity;
        [MarshalAs(UnmanagedType.Bool)] public bool fVisible;
        [MarshalAs(UnmanagedType.Bool)] public bool fSourceClientAreaOnly;
    }

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("dwmapi.dll")]
    private static extern int DwmRegisterThumbnail(IntPtr dest, IntPtr src, out IntPtr thumb);

    [DllImport("dwmapi.dll")]
    private static extern int DwmUnregisterThumbnail(IntPtr thumb);

    [DllImport("dwmapi.dll")]
    private static extern int DwmUpdateThumbnailProperties(
        IntPtr hThumb,
        ref DWM_THUMBNAIL_PROPERTIES props);

    [DllImport("dwmapi.dll")]
    private static extern int DwmQueryThumbnailSourceSize(IntPtr hThumbnail, out PSIZE size);
}
