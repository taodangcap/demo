using System.Windows;
using System.Windows.Forms;
using ShowCuePlayer.Views;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ShowCuePlayer.Models;

namespace ShowCuePlayer.Services;

public interface IVideoPlayerService
{
    /// <summary>True when the fullscreen output window is showing on a monitor.</summary>
    bool IsOutputArmed { get; }

    /// <summary>True when media is currently playing on the output.</summary>
    bool IsPlaying { get; }
    Guid? ActiveOwnerId { get; }
    LedOutputProfile ActiveLedProfile { get; }
    LedTestPattern? ActiveTestPattern { get; }
    long TestPatternVersion { get; }
    long SafeSceneVersion { get; }
    bool IsBlackout { get; }
    bool IsMuted { get; }
    bool IsStandby { get; }
    bool IsFrozen { get; }
    bool IsSafeScene { get; }

    Screen? TargetScreen { get; }

    Task<bool> PrepareAsync(string filePath);
    Task<bool> PlayAsync(Guid ownerId, string filePath, double volume = 1.0, bool loop = false, double imageDurationSeconds = 5.0);
    void Pause();
    void Resume();
    bool Restart(Guid ownerId);
    void Stop(Guid ownerId);
    double GetPosition();
    double GetDuration();
    void Seek(double positionSeconds);

    /// <summary>Stop transport while preserving the current Program frame.</summary>
    void Stop();

    void SetVolume(double volume);
    void SetMasterVolume(double masterVolume);
    void SetMuted(bool enabled);
    void SetLoop(bool enabled);
    void SetTargetScreen(Screen? screen, bool forceFullscreen = false);
    List<Screen> GetAvailableScreens();
    void ApplyLedProfile(LedOutputProfile profile);
    void ShowTestPattern(LedTestPattern pattern);
    void ClearTestPattern();
    void SetBlackout(bool enabled);
    void ShowStandby();
    void SetFreeze(bool enabled);
    bool ShowSafeScene(string? imagePath);
    void ClearSafeScene();
    Task<bool> FadeProgramMaskAsync(bool toBlack, double durationSeconds);
    void ClearProgramTransitionMask();
    void CancelPendingProgramStart();

    /// <summary>
    /// Arm = fullscreen black window on output monitor (safe for live).
    /// Disarm = close output window completely.
    /// </summary>
    void SetOutputEnabled(bool enabled);
    void EnsureOutputArmed();

    bool IsWindowedOutput { get; }
    void SetWindowedOutput(bool windowed);
    void CaptureOutputScreenshot();

    event EventHandler<Guid>? MediaEnded;
    event EventHandler? WindowCreated;
    event EventHandler? OutputStateChanged;

    VideoWindow? OutputWindow { get; }
}

/// <summary>
/// Dual-monitor video output for live events.
/// Control PC = MainWindow; Output monitor = fullscreen black/video (never flash desktop).
public enum ProjectorSource
{
    Program,
    Preview
}

public sealed class VideoPlayerService : IVideoPlayerService
{
    public static readonly System.Collections.Generic.List<ProjectorWindow> ActiveProjectors = new();
    private VideoWindow? _videoWindow;
    private Screen? _targetScreen;
    private double _masterVolume = 1.0;
    private double _currentVolume = 1.0;
    private bool _isOutputArmed;
    private bool _isPlaying;
    private bool _isLooping;
    private Guid? _activeOwnerId;
    private LedOutputProfile _activeLedProfile = LedOutputProfile.CreatePresets()[0].Clone();
    private LedTestPattern? _activeTestPattern;
    private long _testPatternVersion;
    private long _safeSceneVersion;
    private long _playOperationId;
    private bool _isBlackout;
    private bool _isMuted;
    private bool _isStandby;
    private bool _isFrozen;
    private bool _isSafeScene;
    private readonly Dictionary<string, PreparedMediaSignature> _preparedMedia = new(StringComparer.OrdinalIgnoreCase);

    private readonly record struct PreparedMediaSignature(long Length, long LastWriteTimeUtcTicks);

    private bool _isWindowedOutput;
    private bool _forceFullscreen;

    public bool IsOutputArmed => _isOutputArmed;
    public bool IsWindowedOutput => _isWindowedOutput;
    public bool IsPlaying => _isPlaying;
    public Guid? ActiveOwnerId => _activeOwnerId;
    public LedOutputProfile ActiveLedProfile => _activeLedProfile.Clone();
    public LedTestPattern? ActiveTestPattern => _activeTestPattern;
    public long TestPatternVersion => _testPatternVersion;
    public long SafeSceneVersion => _safeSceneVersion;
    public bool IsBlackout => _isBlackout;
    public bool IsMuted => _isMuted;
    public bool IsStandby => _isStandby;
    public bool IsFrozen => _isFrozen;
    public bool IsSafeScene => _isSafeScene;
    public Screen? TargetScreen => _targetScreen;
    public VideoWindow? OutputWindow => _videoWindow;

    public event EventHandler<Guid>? MediaEnded;
    public event EventHandler? WindowCreated;
    public event EventHandler? OutputStateChanged;

    public List<Screen> GetAvailableScreens() => Screen.AllScreens.ToList();

    public async Task<bool> PrepareAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !TryGetMediaSignature(filePath, out var signature))
            return false;
        lock (_preparedMedia)
        {
            if (_preparedMedia.TryGetValue(filePath, out var cached) && cached == signature)
                return true;
            _preparedMedia.Remove(filePath);
        }

        bool ready;
        if (MetadataService.IsImageFormat(filePath))
        {
            ready = await Task.Run(() =>
            {
                try
                {
                    var frame = BitmapFrame.Create(
                        new Uri(filePath, UriKind.Absolute),
                        BitmapCreateOptions.PreservePixelFormat,
                        BitmapCacheOption.OnLoad);
                    return frame.PixelWidth > 0 && frame.PixelHeight > 0;
                }
                catch { return false; }
            });
        }
        else
        {
            ready = await RunOnUiAsync(() => ProbeVideoAsync(filePath));
        }

        if (ready && TryGetMediaSignature(filePath, out var verifiedSignature) && verifiedSignature == signature)
        {
            lock (_preparedMedia)
                _preparedMedia[filePath] = signature;
        }
        return ready;
    }

    private static bool TryGetMediaSignature(string filePath, out PreparedMediaSignature signature)
    {
        try
        {
            var info = new System.IO.FileInfo(filePath);
            if (!info.Exists)
            {
                signature = default;
                return false;
            }

            signature = new PreparedMediaSignature(info.Length, info.LastWriteTimeUtc.Ticks);
            return true;
        }
        catch
        {
            signature = default;
            return false;
        }
    }

    public void SetTargetScreen(Screen? screen, bool forceFullscreen = false)
    {
        _targetScreen = screen;
        _isWindowedOutput = false;
        _forceFullscreen = forceFullscreen;
        if (_videoWindow != null && _videoWindow.IsLoaded && _isOutputArmed)
        {
            RunOnUi(() =>
            {
                var target = _targetScreen ?? Screen.PrimaryScreen;
                PlaceOnScreen(_videoWindow, target, showInTaskbar: false, forceFullscreen: _forceFullscreen);
            });
        }
    }

    public void SetWindowedOutput(bool windowed)
    {
        _isWindowedOutput = windowed;
        _forceFullscreen = false;
        if (windowed)
        {
            _targetScreen = null;
        }
        if (_videoWindow != null && _videoWindow.IsLoaded && _isOutputArmed)
        {
            if (windowed)
                RunOnUi(() => PlaceWindowedOnScreen(_videoWindow, Screen.PrimaryScreen));
            else
            {
                var target = _targetScreen ?? Screen.PrimaryScreen;
                RunOnUi(() => PlaceOnScreen(_videoWindow, target, showInTaskbar: false, forceFullscreen: _forceFullscreen));
            }
        }
        RaiseState();
    }

    public void CaptureOutputScreenshot()
    {
        RunOnUi(() =>
        {
            if (_videoWindow == null || !_isOutputArmed)
            {
                System.Windows.MessageBox.Show("Output chua duoc bat (Armed).", "Loi", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            try
            {
                double left = _videoWindow.Left;
                double top = _videoWindow.Top;
                double width = _videoWindow.ActualWidth;
                double height = _videoWindow.ActualHeight;

                if (!_isWindowedOutput && _targetScreen != null)
                {
                    var bounds = _targetScreen.Bounds;
                    left = bounds.Left;
                    top = bounds.Top;
                    width = bounds.Width;
                    height = bounds.Height;
                }

                using (var bmp = new System.Drawing.Bitmap((int)width, (int)height))
                {
                    using (var g = System.Drawing.Graphics.FromImage(bmp))
                    {
                        g.CopyFromScreen((int)left, (int)top, 0, 0, new System.Drawing.Size((int)width, (int)height));
                    }

                    string picturesPath = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                    if (!System.IO.Directory.Exists(picturesPath))
                        System.IO.Directory.CreateDirectory(picturesPath);

                    string filePath = System.IO.Path.Combine(picturesPath, $"Program_Screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                    bmp.Save(filePath, System.Drawing.Imaging.ImageFormat.Png);

                    System.Windows.MessageBox.Show($"Da chup man hinh chuong trinh va luu vao:\n{filePath}", "Chup man hinh", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Loi chup man hinh: {ex.Message}", "Loi", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        });
    }

    public void ApplyLedProfile(LedOutputProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        _activeLedProfile = profile.Clone();
        RunOnUi(() => _videoWindow?.ApplyLedProfile(_activeLedProfile));
        RaiseState();
    }

    public void ShowTestPattern(LedTestPattern pattern)
    {
        RunOnUi(() =>
        {
            if (!_isOutputArmed) ArmOutput();
            _playOperationId++;
            _isFrozen = false;
            _testPatternVersion++;
            _activeTestPattern = pattern;
            _isPlaying = false;
            _activeOwnerId = null;
            _isStandby = false;
            _safeSceneVersion++;
            _isSafeScene = false;
            _videoWindow?.ShowTestPattern(pattern);
            RaiseState();
        });
    }

    public void ClearTestPattern()
    {
        _testPatternVersion++;
        _activeTestPattern = null;
        RunOnUi(() =>
        {
            _videoWindow?.ClearTestPattern();
            RaiseState();
        });
    }

    public void SetBlackout(bool enabled)
    {
        RunOnUi(() =>
        {
            if (enabled && !_isOutputArmed) ArmOutput();
            _isBlackout = enabled;
            _videoWindow?.SetBlackout(enabled);
            RaiseState();
        });
    }

    public void ShowStandby()
    {
        RunOnUi(() =>
        {
            if (!_isOutputArmed) ArmOutput();
            _playOperationId++;
            _testPatternVersion++;
            _activeTestPattern = null;
            _isBlackout = false;
            _isFrozen = false;
            _safeSceneVersion++;
            _isSafeScene = false;
            _videoWindow?.ShowStandby();
            _isPlaying = false;
            _activeOwnerId = null;
            _isStandby = true;
            RaiseState();
        });
    }

    public void SetFreeze(bool enabled)
    {
        RunOnUi(() =>
        {
            if (enabled && !_isOutputArmed) ArmOutput();
            if (enabled) _playOperationId++;
            _isFrozen = enabled;
            _videoWindow?.SetFreeze(enabled);
            RaiseState();
        });
    }

    public bool ShowSafeScene(string? imagePath)
    {
        bool shown = false;
        RunOnUi(() =>
        {
            if (!_isOutputArmed) ArmOutput();
            _playOperationId++;
            _testPatternVersion++;
            _activeTestPattern = null;
            _isFrozen = false;
            _isPlaying = false;
            _activeOwnerId = null;
            _isStandby = false;
            shown = _videoWindow?.ShowSafeScene(imagePath) == true;
            _safeSceneVersion++;
            _isSafeScene = shown;
            RaiseState();
        });
        return shown;
    }

    public void ClearSafeScene()
    {
        if (!_isSafeScene) return;
        RunOnUi(() =>
        {
            _videoWindow?.ClearSafeScene();
            _safeSceneVersion++;
            _isSafeScene = false;
            RaiseState();
        });
    }

    public async Task<bool> FadeProgramMaskAsync(bool toBlack, double durationSeconds)
    {
        if (_isFrozen)
            return false;
        try
        {
            return await RunOnUiAsync(async () =>
            {
                if (_videoWindow is null) return false;
                var fade = _videoWindow.FadeTransitionMaskAsync(toBlack ? 1 : 0, durationSeconds);
                var timeout = Task.Delay(TimeSpan.FromSeconds(Math.Max(1, durationSeconds + 0.75)));
                var completed = await Task.WhenAny(fade, timeout);
                return ReferenceEquals(completed, fade);
            });
        }
        catch
        {
            RunOnUi(() => _videoWindow?.ClearTransitionMask());
            return false;
        }
    }

    public void ClearProgramTransitionMask()
        => RunOnUi(() => _videoWindow?.ClearTransitionMask());

    public void CancelPendingProgramStart()
    {
        _playOperationId++;
        RunOnUi(() => _videoWindow?.CancelPendingProgramStart());
    }

    public void EnsureOutputArmed()
    {
        if (!_isOutputArmed)
        {
            RunOnUi(() => ArmOutput());
        }
    }

    public void SetOutputEnabled(bool enabled)
    {
        RunOnUi(() =>
        {
            if (enabled)
            {
                _isOutputArmed = true;
                ArmOutput();
            }
            else
            {
                var mainVm = System.Windows.Application.Current.MainWindow?.DataContext as ViewModels.MainViewModel;
                bool keepArmedOffscreen = (ActiveProjectors.Count > 0) || (mainVm?.IsKaraokeOutputOn == true);
                if (keepArmedOffscreen)
                {
                    _isOutputArmed = true;
                    if (_videoWindow != null)
                    {
                        PlaceOffScreen(_videoWindow);
                    }
                    _isPlaying = false;
                    RaiseState();
                }
                else
                {
                    DisarmOutput();
                }
            }
        });
    }

    public async Task<bool> PlayAsync(Guid ownerId, string filePath, double volume = 1.0, bool loop = false, double imageDurationSeconds = 5.0)
    {
        return await RunOnUiAsync(async () =>
        {
            if (_isFrozen) return false;

            // Auto-arm so first video cue always hits the output monitor
            if (!_isOutputArmed)
                ArmOutput();

            EnsureWindowCreated();
            _currentVolume = volume;
            _isLooping = loop;
            var playOperationId = ++_playOperationId;
            var testPatternVersion = _testPatternVersion;
            var safeSceneVersion = _safeSceneVersion;
            var started = await _videoWindow!.PlayAsync(
                filePath, GetEffectiveVolume(), loop, imageDurationSeconds);
            if (!started || _isFrozen || playOperationId != _playOperationId)
            {
                if (playOperationId == _playOperationId)
                {
                    _isPlaying = false;
                    _activeOwnerId = null;
                    RaiseState();
                }
                return false;
            }
            if (_activeTestPattern is not null && _testPatternVersion == testPatternVersion)
            {
                _videoWindow.ClearTestPattern();
                _testPatternVersion++;
                _activeTestPattern = null;
            }
            if (_isSafeScene && _safeSceneVersion == safeSceneVersion)
            {
                _videoWindow.ClearSafeScene();
                _safeSceneVersion++;
                _isSafeScene = false;
            }
            _activeOwnerId = ownerId;
            _isPlaying = true;
            _isStandby = false;
            _isSafeScene = false;
            RaiseState();
            return true;
        });
    }

    public void Stop(Guid ownerId)
    {
        if (_activeOwnerId != ownerId) return;
        RunOnUi(() => _videoWindow?.Pause());
        _isPlaying = false;
        _activeOwnerId = null;
        RaiseState();
    }

    public double GetPosition() => _videoWindow?.GetPosition() ?? 0;
    public double GetDuration() => _videoWindow?.GetDuration() ?? 0;
    public void Seek(double positionSeconds) => RunOnUi(() => _videoWindow?.Seek(positionSeconds));

    public void Pause()
    {
        RunOnUi(() =>
        {
            _videoWindow?.Pause();
            if (_isPlaying)
            {
                _isPlaying = false;
                RaiseState();
            }
        });
    }

    public void Resume()
    {
        RunOnUi(() =>
        {
            if (_videoWindow == null) return;
            _videoWindow.Resume();
            _isPlaying = true;
            RaiseState();
        });
    }

    public bool Restart(Guid ownerId)
    {
        if (_activeOwnerId != ownerId || _videoWindow is null) return false;
        bool restarted = false;
        RunOnUi(() => restarted = _videoWindow?.RestartProgram() == true);
        if (restarted)
        {
            _isPlaying = true;
            RaiseState();
        }
        return restarted;
    }

    public void Stop()
    {
        RunOnUi(() => _videoWindow?.Pause());
        _isPlaying = false;
        _activeOwnerId = null;
        RaiseState();
    }

    public void SetVolume(double volume)
    {
        _currentVolume = volume;
        RunOnUi(() =>
        {
            if (_videoWindow != null)
                _videoWindow.Volume = GetEffectiveVolume();
        });
    }

    public void SetMasterVolume(double masterVolume)
    {
        _masterVolume = masterVolume;
        RunOnUi(() =>
        {
            if (_videoWindow != null)
                _videoWindow.Volume = GetEffectiveVolume();
        });
    }

    public void SetMuted(bool enabled)
    {
        _isMuted = enabled;
        RunOnUi(() =>
        {
            if (_videoWindow != null)
                _videoWindow.Volume = GetEffectiveVolume();
        });
        RaiseState();
    }

    public void SetLoop(bool enabled)
    {
        _isLooping = enabled;
        RunOnUi(() => _videoWindow?.SetLoop(enabled));
    }

    private double GetEffectiveVolume()
        => _isMuted ? 0 : Math.Clamp(_currentVolume * _masterVolume, 0, 1);

    // ─── Internals ────────────────────────────────────────────────

    private void ArmOutput()
    {
        EnsureWindowCreated();
        
        var mainVm = System.Windows.Application.Current.MainWindow?.DataContext as ViewModels.MainViewModel;
        if (mainVm != null && !mainVm.IsVideoOutputEnabled)
        {
            PlaceOffScreen(_videoWindow!);
        }
        else
        {
            if (_isWindowedOutput)
                PlaceWindowedOnScreen(_videoWindow!, Screen.PrimaryScreen);
            else
            {
                var target = _targetScreen ?? Screen.PrimaryScreen;
                PlaceOnScreen(_videoWindow!, target, showInTaskbar: false, forceFullscreen: _forceFullscreen);
            }
        }

        _videoWindow!.ShowStandby();
        _videoWindow.Show();
        // Keep keyboard focus on control window
        System.Windows.Application.Current.MainWindow?.Activate();

        _isOutputArmed = true;
        _isPlaying = false;
        _isStandby = true;
        RaiseState();
    }

    private void DisarmOutput()
    {
        if (_videoWindow != null)
        {
            _videoWindow.Stop();
            _videoWindow.Close();
            _videoWindow = null;
        }
        ResetClosedOutputState();
        RaiseState();
    }

    private void EnsureWindowCreated()
    {
        if (_videoWindow != null && _videoWindow.IsLoaded) return;

        _videoWindow = new VideoWindow();
        _videoWindow.ApplyLedProfile(_activeLedProfile);
        if (_activeTestPattern is LedTestPattern pattern)
            _videoWindow.ShowTestPattern(pattern);
        _videoWindow.SetBlackout(_isBlackout);
        _videoWindow.SetFreeze(_isFrozen);
        _videoWindow.EscapeRequested += (_, _) => DisarmOutput();
        _videoWindow.Closed += (_, _) =>
        {
            _videoWindow = null;
            ResetClosedOutputState();
            RaiseState();
        };
        _videoWindow.Player.MediaEnded += (_, _) =>
        {
            // Loop is restarted inside VideoWindow — do not treat as end
            if (_isLooping) return;
            _isPlaying = false;
            RaiseState();
            if (_activeOwnerId is Guid owner)
                MediaEnded?.Invoke(this, owner);
        };
        _videoWindow.ImageEnded += (_, _) =>
        {
            if (_isLooping) return;
            _isPlaying = false;
            RaiseState();
            if (_activeOwnerId is Guid owner)
                MediaEnded?.Invoke(this, owner);
        };

        if (_isWindowedOutput)
            PlaceWindowedOnScreen(_videoWindow, Screen.PrimaryScreen);
        else if (_targetScreen != null)
            PlaceOnScreen(_videoWindow, _targetScreen);

        WindowCreated?.Invoke(this, EventArgs.Empty);
    }

    private void ResetClosedOutputState()
    {
        _isOutputArmed = false;
        _isWindowedOutput = false;
        _isPlaying = false;
        _activeOwnerId = null;
        _playOperationId++;
        _testPatternVersion++;
        _safeSceneVersion++;
        _activeTestPattern = null;
        _isBlackout = false;
        _isStandby = false;
        _isFrozen = false;
        _isSafeScene = false;
    }

    /// <summary>
    /// Place window on a specific monitor using explicit bounds.
    /// Avoid WindowState.Maximized — it can jump to primary on multi-DPI setups.
    /// </summary>
    /// <param name="showInTaskbar">
    /// Video output thường ẩn taskbar; Karaoke OUTPUT nên true để có tab riêng.
    /// </param>
    public static void PlaceOnScreen(Window window, Screen screen, bool showInTaskbar = true, bool forceFullscreen = false)
    {
        var bounds = screen.Bounds;
        if (!forceFullscreen && screen.Primary && window is VideoWindow)
        {
            window.Topmost = false;
            window.WindowStyle = WindowStyle.SingleBorderWindow;
            window.ResizeMode = ResizeMode.CanResize;
            window.ShowInTaskbar = true;
            window.ShowActivated = true;
            window.WindowState = WindowState.Normal;
            window.Width = Math.Min(960, bounds.Width * 0.8);
            window.Height = Math.Min(540, bounds.Height * 0.8);
            window.Left = bounds.Left + (bounds.Width - window.Width) / 2;
            window.Top = bounds.Top + (bounds.Height - window.Height) / 2;
            return;
        }

        window.WindowStyle = WindowStyle.None;
        window.ResizeMode = ResizeMode.NoResize;
        window.ShowInTaskbar = showInTaskbar;
        window.Topmost = true;
        // VideoWindow (USB + karaoke OUTPUT): activate 1 lần để WebView/MediaElement không bị background-throttle
        window.ShowActivated = window is VideoWindow;
        window.WindowState = WindowState.Normal;
        // Giảm jank compose: làm tròn pixel, không blur
        System.Windows.Media.RenderOptions.SetBitmapScalingMode(window, System.Windows.Media.BitmapScalingMode.NearestNeighbor);
        window.UseLayoutRounding = true;
        window.SnapsToDevicePixels = true;

        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        // TOPMOST + full monitor bounds (tránh Maximized nhảy DPI)
        SetWindowPos(hwnd, HWND_TOPMOST, bounds.Left, bounds.Top, bounds.Width, bounds.Height,
            SWP_NOACTIVATE | SWP_SHOWWINDOW);
        // Force redraw after move — WebView đôi khi kẹt 1 frame đen
        try { window.InvalidateVisual(); } catch { /* ignore */ }
    }

    public static void PlaceWindowedOnScreen(Window window, Screen screen, int preferredWidth = 960, int preferredHeight = 540)
    {
        var area = screen.WorkingArea;
        int width = Math.Min(preferredWidth, (int)(area.Width * 0.8));
        int height = Math.Min(preferredHeight, (int)(area.Height * 0.8));
        int left = area.Left + (area.Width - width) / 2;
        int top = area.Top + (area.Height - height) / 2;

        window.Topmost = false;
        window.WindowState = WindowState.Normal;
        window.WindowStyle = WindowStyle.SingleBorderWindow;
        window.ResizeMode = ResizeMode.CanResize;
        window.ShowInTaskbar = true;
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        SetWindowPos(hwnd, IntPtr.Zero, left, top, width, height, SWP_NOACTIVATE);
    }

    public static void PlaceOffScreen(Window window)
    {
        window.WindowStyle = WindowStyle.None;
        window.ResizeMode = ResizeMode.NoResize;
        window.ShowInTaskbar = false;
        window.Topmost = false;
        window.Left = -20000;
        window.Top = -20000;

        // Use a standard screen size to prevent WebView2 / layout distortion
        var targetScreen = Screen.AllScreens.Length > 1 ? Screen.AllScreens[1] : Screen.PrimaryScreen;
        var bounds = targetScreen.Bounds;
        window.Width = bounds.Width > 0 ? bounds.Width : 1920;
        window.Height = bounds.Height > 0 ? bounds.Height : 1080;

        window.WindowState = WindowState.Normal;
    }

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    private void RaiseState() => OutputStateChanged?.Invoke(this, EventArgs.Empty);

    private static async Task<bool> ProbeVideoAsync(string filePath)
    {
        var player = new MediaPlayer { Volume = 0 };
        var opened = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler onOpened = (_, _) => opened.TrySetResult(true);
        EventHandler<ExceptionEventArgs> onFailed = (_, _) => opened.TrySetResult(false);
        player.MediaOpened += onOpened;
        player.MediaFailed += onFailed;

        try
        {
            player.Open(new Uri(filePath, UriKind.Absolute));
            var completed = await Task.WhenAny(opened.Task, Task.Delay(TimeSpan.FromSeconds(3)));
            return ReferenceEquals(completed, opened.Task) && await opened.Task;
        }
        catch
        {
            return false;
        }
        finally
        {
            player.MediaOpened -= onOpened;
            player.MediaFailed -= onFailed;
            player.Close();
        }
    }

    private static async Task<T> RunOnUiAsync<T>(Func<Task<T>> action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            return await action();
        return await (await dispatcher.InvokeAsync(action));
    }

    private static void RunOnUi(Action action)
    {
        var app = System.Windows.Application.Current;
        if (app?.Dispatcher == null || app.Dispatcher.CheckAccess())
            action();
        else
            app.Dispatcher.Invoke(action);
    }
}
