using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Diagnostics;
using ShowCuePlayer.Helpers;
using ShowCuePlayer.Models;
using ShowCuePlayer.Services;

namespace ShowCuePlayer.Views;

/// <summary>
/// Fullscreen output window for the secondary (audience) monitor.
/// Idle = black + logo. Playing = video. Never flashes desktop.
/// </summary>
public partial class VideoWindow : Window
{
    private bool _isLooping;
    private readonly DispatcherTimer _imageTimer;
    private System.Windows.Controls.Image? _visibleImage;
    private readonly Stopwatch _imageStopwatch = new();
    private double _imageElapsedSeconds;
    private double _imageDurationSeconds;
    private bool _isImageActive;
    private double _karaokeVolume = 1.0;
    private bool _karaokeAutoNext = true;
    private bool _karaokeHooksInstalled;
    private bool _processFailureHooked;
    private LedOutputProfile _ledProfile = LedOutputProfile.CreatePresets()[0].Clone();
    private bool _isFrozen;
    private bool _isLocalMediaPlaying;
    private bool _isKaraokePlaying;
    private bool _isKaraokeNavigating;
    private bool _resumeMediaAfterFreeze;
    private bool _resumeImageAfterFreeze;
    private bool _resumeKaraokeAfterFreeze;
    private readonly SemaphoreSlim _karaokeNavigationStartGate = new(1, 1);
    private readonly SemaphoreSlim _localMediaStartGate = new(1, 1);
    private ulong? _activeKaraokeNavigationId;
    private long _localMediaOperationId;
    private TaskCompletionSource<bool>? _pendingLocalMediaStart;

    public event EventHandler? EscapeRequested;
    public event EventHandler? ImageEnded;
    public event EventHandler<string>? KaraokeProcessFailed;

    public VideoWindow()
    {
        InitializeComponent();
        _imageTimer = new DispatcherTimer();
        _imageTimer.Tick += OnImageTimerTick;
        KaraokeWebView.NavigationStarting += (_, e) =>
        {
            _activeKaraokeNavigationId = e.NavigationId;
            _isKaraokeNavigating = true;
            _isKaraokePlaying = false;
        };
        KaraokeWebView.NavigationCompleted += async (_, e) =>
        {
            if (_activeKaraokeNavigationId != e.NavigationId) return;
            _isKaraokeNavigating = false;
            _isKaraokePlaying = e.IsSuccess;
            await ApplyKaraokeVolumeAsync();
            if (_isFrozen && e.IsSuccess)
            {
                _resumeKaraokeAfterFreeze = true;
                PauseKaraokeMedia();
            }
        };
        Player.MediaEnded += Player_MediaEnded;
        SizeChanged += (_, _) => ApplyProgramLayout();
        MouseRightButtonUp += OnRightClick;
        SourceInitialized += (_, _) => KaraokeSecondaryWindow.ApplySeparateTaskbarIdentity(this);
        Activated += (_, _) => Keyboard.Focus(this);
        PreviewMouseDown += (_, _) =>
        {
            Activate();
            Keyboard.Focus(this);
        };
        PreviewKeyDown += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Escape)
            {
                EscapeRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.System || e.Key == Key.F4)
                e.Handled = true;
        };
        ShowIdle();
    }

    public double Volume
    {
        get => Player.Volume;
        set => Player.Volume = value;
    }

    public Microsoft.Web.WebView2.Wpf.WebView2 PlayerWebView => KaraokeWebView;

    public void SetLoop(bool enabled) => _isLooping = enabled;

    public bool RestartProgram()
    {
        if (_isFrozen) return false;

        if (_isImageActive && _visibleImage is not null)
        {
            _imageElapsedSeconds = 0;
            StartImageClock();
            return true;
        }

        if (Player.Visibility == Visibility.Visible && Player.Source is not null)
        {
            Player.Position = TimeSpan.Zero;
            Player.Play();
            _isLocalMediaPlaying = true;
            return true;
        }

        return false;
    }

    public void ApplyLedProfile(LedOutputProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        _ledProfile = profile.Clone();
        _ledProfile.CanvasWidth = Math.Clamp(_ledProfile.CanvasWidth, 64, 16384);
        _ledProfile.CanvasHeight = Math.Clamp(_ledProfile.CanvasHeight, 64, 16384);
        ApplyProgramLayout();
    }

    public void ShowTestPattern(LedTestPattern pattern)
    {
        ClearSafeScene();
        CancelPendingLocalMediaStart();
        CancelFreezeWithoutResume();
        PauseProgramForOverlay();
        TestPatternOverlay.Children.Clear();
        TestPatternOverlay.Background = pattern switch
        {
            LedTestPattern.Black => Brushes.Black,
            LedTestPattern.White => Brushes.White,
            LedTestPattern.Red => Brushes.Red,
            LedTestPattern.Green => Brushes.Lime,
            LedTestPattern.Blue => Brushes.Blue,
            LedTestPattern.Grid => CreateGridPatternBrush(),
            LedTestPattern.ColorBars => Brushes.Black,
            _ => Brushes.Black
        };

        if (pattern == LedTestPattern.ColorBars)
            AddColorBars();

        TestPatternOverlay.Visibility = Visibility.Visible;
    }

    public void ClearTestPattern()
    {
        TestPatternOverlay.Visibility = Visibility.Collapsed;
        TestPatternOverlay.Children.Clear();
        TestPatternOverlay.Background = null;
    }

    public void SetBlackout(bool enabled)
        => BlackoutOverlay.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;

    public bool ShowSafeScene(string? imagePath)
    {
        bool keepBlackout = BlackoutOverlay.Visibility == Visibility.Visible;
        ShowStandby();
        if (keepBlackout) SetBlackout(true);
        IdlePanel.Visibility = Visibility.Collapsed;
        try
        {
            SafeSceneImage.Source = !string.IsNullOrWhiteSpace(imagePath) && File.Exists(imagePath)
                ? LoadDisplayBitmap(imagePath)
                : new BitmapImage(new Uri("pack://application:,,,/Assets/logo.png", UriKind.Absolute));
            SafeSceneOverlay.Visibility = Visibility.Visible;
            return SafeSceneImage.Source is not null;
        }
        catch
        {
            try
            {
                SafeSceneImage.Source = new BitmapImage(
                    new Uri("pack://application:,,,/Assets/logo.png", UriKind.Absolute));
                SafeSceneOverlay.Visibility = Visibility.Visible;
                return true;
            }
            catch
            {
                SafeSceneImage.Source = null;
                SafeSceneOverlay.Visibility = Visibility.Collapsed;
                ShowIdle();
                return false;
            }
        }
    }

    public void ClearSafeScene()
    {
        SafeSceneOverlay.Visibility = Visibility.Collapsed;
        SafeSceneImage.Source = null;
    }

    public Task FadeTransitionMaskAsync(double targetOpacity, double durationSeconds)
    {
        targetOpacity = Math.Clamp(targetOpacity, 0, 1);
        durationSeconds = Math.Clamp(durationSeconds, 0, 5);
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        TransitionMaskOverlay.Visibility = Visibility.Visible;
        var animation = new DoubleAnimation(
            TransitionMaskOverlay.Opacity,
            targetOpacity,
            TimeSpan.FromSeconds(durationSeconds));
        animation.Completed += (_, _) =>
        {
            TransitionMaskOverlay.BeginAnimation(OpacityProperty, null);
            TransitionMaskOverlay.Opacity = targetOpacity;
            if (targetOpacity <= 0)
                TransitionMaskOverlay.Visibility = Visibility.Collapsed;
            completion.TrySetResult(true);
        };
        TransitionMaskOverlay.BeginAnimation(OpacityProperty, animation);
        return completion.Task;
    }

    public void ClearTransitionMask()
    {
        TransitionMaskOverlay.BeginAnimation(OpacityProperty, null);
        TransitionMaskOverlay.Opacity = 0;
        TransitionMaskOverlay.Visibility = Visibility.Collapsed;
    }

    private void PauseProgramForOverlay()
    {
        if (_isImageActive && _imageTimer.IsEnabled)
            PauseImageClock();
        if (_isLocalMediaPlaying && Player.Visibility == Visibility.Visible)
        {
            Player.Pause();
            _isLocalMediaPlaying = false;
        }
        if (KaraokeWebView.Visibility == Visibility.Visible)
            PauseKaraokeMedia();
    }

    private void CancelFreezeWithoutResume()
    {
        _isFrozen = false;
        _resumeMediaAfterFreeze = false;
        _resumeImageAfterFreeze = false;
        _resumeKaraokeAfterFreeze = false;
    }

    public void SetFreeze(bool enabled)
    {
        if (_isFrozen == enabled) return;
        _isFrozen = enabled;

        if (enabled)
        {
            CancelPendingLocalMediaStart();
            _resumeMediaAfterFreeze = _isLocalMediaPlaying && Player.Visibility == Visibility.Visible;
            _resumeImageAfterFreeze = _isImageActive && _imageTimer.IsEnabled;
            _resumeKaraokeAfterFreeze = (_isKaraokePlaying || _isKaraokeNavigating) &&
                                         KaraokeWebView.Visibility == Visibility.Visible;

            if (_resumeMediaAfterFreeze)
            {
                Player.Pause();
                _isLocalMediaPlaying = false;
            }
            if (_resumeImageAfterFreeze)
                PauseImageClock();
            if (_resumeKaraokeAfterFreeze)
            {
                if (_isKaraokeNavigating)
                {
                    try { KaraokeWebView.CoreWebView2?.Stop(); } catch { /* navigation teardown */ }
                    _isKaraokeNavigating = false;
                }
                PauseKaraokeMedia();
            }
            return;
        }

        if (_resumeMediaAfterFreeze && Player.Visibility == Visibility.Visible)
        {
            Player.Play();
            _isLocalMediaPlaying = true;
        }
        if (_resumeImageAfterFreeze && _isImageActive)
            StartImageClock();
        if (_resumeKaraokeAfterFreeze && KaraokeWebView.Visibility == Visibility.Visible)
            ResumeKaraokeMedia();

        _resumeMediaAfterFreeze = false;
        _resumeImageAfterFreeze = false;
        _resumeKaraokeAfterFreeze = false;
    }

    private void ApplyProgramLayout()
    {
        double availableWidth = Math.Max(1, OutputRoot.ActualWidth);
        double availableHeight = Math.Max(1, OutputRoot.ActualHeight);
        double canvasWidth = Math.Max(1, _ledProfile.CanvasWidth);
        double canvasHeight = Math.Max(1, _ledProfile.CanvasHeight);

        switch (_ledProfile.ScalingMode)
        {
            case LedScalingMode.Stretch:
                ProgramCanvas.Width = availableWidth;
                ProgramCanvas.Height = availableHeight;
                break;
            case LedScalingMode.PixelPerfect:
                var dpi = VisualTreeHelper.GetDpi(this);
                ProgramCanvas.Width = canvasWidth / dpi.DpiScaleX;
                ProgramCanvas.Height = canvasHeight / dpi.DpiScaleY;
                break;
            default:
                double scaleX = availableWidth / canvasWidth;
                double scaleY = availableHeight / canvasHeight;
                double scale = _ledProfile.ScalingMode == LedScalingMode.Fill
                    ? Math.Max(scaleX, scaleY)
                    : Math.Min(scaleX, scaleY);
                ProgramCanvas.Width = canvasWidth * scale;
                ProgramCanvas.Height = canvasHeight * scale;
                break;
        }
    }

    private static DrawingBrush CreateGridPatternBrush()
    {
        var drawing = new GeometryDrawing(
            Brushes.Black,
            new System.Windows.Media.Pen(new SolidColorBrush(Color.FromRgb(96, 96, 96)), 1),
            new RectangleGeometry(new Rect(0, 0, 64, 64)));
        var brush = new DrawingBrush(drawing)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 64, 64),
            ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, 64, 64),
            ViewboxUnits = BrushMappingMode.Absolute
        };
        brush.Freeze();
        return brush;
    }

    private void AddColorBars()
    {
        var colors = new[]
        {
            Colors.White, Colors.Yellow, Colors.Cyan, Colors.Lime,
            Colors.Magenta, Colors.Red, Colors.Blue
        };
        var grid = new Grid();
        foreach (var color in colors)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var bar = new Border { Background = new SolidColorBrush(color) };
            Grid.SetColumn(bar, grid.ColumnDefinitions.Count - 1);
            grid.Children.Add(bar);
        }
        TestPatternOverlay.Children.Add(grid);
    }

    public async Task<bool> NavigateAsync(string url)
    {
        if (_isFrozen) return false;
        try
        {
            // Giải phóng pipeline MediaElement (USB/local) trước khi bật WebView
            CancelPendingLocalMediaStart();
            try
            {
                Player.Stop();
                Player.Close();
                Player.Source = null;
            }
            catch { /* ignore */ }
            Player.Visibility = Visibility.Collapsed;
            _isLocalMediaPlaying = false;
            StopImageTimer();
            _isImageActive = false;
            HideImages();
            IdlePanel.Visibility = Visibility.Collapsed;

            // Env GPU + chống throttle khi màn OUTPUT không focus (mượt hơn, gần USB)
            await WebView2Performance.EnsureOptimizedAsync(KaraokeWebView);
            if (_isFrozen) return false;
            HookKaraokeProcessFailure();
            await EnsureKaraokeHooksAsync();
            if (_isFrozen) return false;

            var core = KaraokeWebView.CoreWebView2;
            if (core is null) return false;

            var started = new TaskCompletionSource<ulong>(TaskCreationOptions.RunContinuationsAsynchronously);
            var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ulong? navigationId = null;
            EventHandler<Microsoft.Web.WebView2.Core.CoreWebView2NavigationStartingEventArgs>? startingHandler = null;
            EventHandler<Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs>? completedHandler = null;
            startingHandler = (_, args) =>
            {
                if (navigationId is not null) return;
                navigationId = args.NavigationId;
                started.TrySetResult(args.NavigationId);
            };
            completedHandler = (_, args) =>
            {
                if (navigationId == args.NavigationId)
                    completed.TrySetResult(args.IsSuccess);
            };
            KaraokeWebView.Visibility = Visibility.Visible;
            try
            {
                await _karaokeNavigationStartGate.WaitAsync();
                try
                {
                    if (_isFrozen) return false;
                    KaraokeWebView.NavigationStarting += startingHandler;
                    KaraokeWebView.NavigationCompleted += completedHandler;
                    core.Navigate(url);
                    var startResult = await Task.WhenAny(started.Task, Task.Delay(TimeSpan.FromSeconds(2)));
                    if (!ReferenceEquals(startResult, started.Task))
                    {
                        try { core.Stop(); } catch { /* navigation start timeout */ }
                        return false;
                    }
                }
                finally
                {
                    _karaokeNavigationStartGate.Release();
                }

                var result = await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(15)));
                if (!ReferenceEquals(result, completed.Task))
                {
                    if (_activeKaraokeNavigationId == navigationId)
                    {
                        try { core.Stop(); } catch { /* navigation timeout */ }
                        _isKaraokeNavigating = false;
                        _isKaraokePlaying = false;
                    }
                    return false;
                }

                return await completed.Task && !_isFrozen;
            }
            finally
            {
                KaraokeWebView.NavigationStarting -= startingHandler;
                KaraokeWebView.NavigationCompleted -= completedHandler;
            }
        }
        catch (ObjectDisposedException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    private void HookKaraokeProcessFailure()
    {
        if (_processFailureHooked) return;
        var core = KaraokeWebView.CoreWebView2;
        if (core is null) return;
        core.ProcessFailed += OnKaraokeProcessFailed;
        _processFailureHooked = true;
    }

    private void OnKaraokeProcessFailed(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2ProcessFailedEventArgs e)
    {
        KaraokeProcessFailed?.Invoke(this, e.ProcessFailedKind.ToString());
    }

    public void SetKaraokeAutoNext(bool enabled)
    {
        _karaokeAutoNext = enabled;
        _ = ApplyKaraokeAutoNextAsync();
    }

    private async Task EnsureKaraokeHooksAsync()
    {
        try
        {
            var core = KaraokeWebView.CoreWebView2;
            if (core is null || _karaokeHooksInstalled) return;
            await core.AddScriptToExecuteOnDocumentCreatedAsync(
                "try{window.__scpAutoNext=new URL(location.href).searchParams.get('autonext')!=='0';}" +
                "catch(_){window.__scpAutoNext=true;}" +
                "document.addEventListener('ended',function(e){" +
                "if(window.__scpAutoNext===false){" +
                "e.stopImmediatePropagation();e.preventDefault();" +
                "try{e.target.pause();}catch(_){}}},true);");
            _karaokeHooksInstalled = true;
            await ApplyKaraokeAutoNextAsync();
        }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    private async Task ApplyKaraokeAutoNextAsync()
    {
        try
        {
            var core = KaraokeWebView.CoreWebView2;
            if (core is null) return;
            await core.ExecuteScriptAsync($"window.__scpAutoNext={(_karaokeAutoNext ? "true" : "false")};");
        }
        catch { /* page is navigating */ }
    }

    public void PostMessage(string message)
    {
        try
        {
            var core = KaraokeWebView.CoreWebView2;
            if (core is null) return;
            if (message.TrimStart().StartsWith('{')) core.PostWebMessageAsJson(message);
            else core.PostWebMessageAsString(message);
            if (message.Contains("\"action\":\"pause\"", StringComparison.OrdinalIgnoreCase))
            {
                _isKaraokePlaying = false;
                _resumeKaraokeAfterFreeze = false;
            }
            else if (message.Contains("\"action\":\"play\"", StringComparison.OrdinalIgnoreCase))
            {
                if (_isFrozen)
                {
                    _resumeKaraokeAfterFreeze = true;
                    PauseKaraokeMedia();
                }
                else
                {
                    _isKaraokePlaying = true;
                }
            }
        }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    public void SetKaraokeVolume(double volume)
    {
        _karaokeVolume = Math.Clamp(volume, 0, 1);
        _ = ApplyKaraokeVolumeAsync();
    }

    private async Task ApplyKaraokeVolumeAsync()
    {
        try
        {
            var core = KaraokeWebView.CoreWebView2;
            if (core is null) return;
            var value = _karaokeVolume.ToString(System.Globalization.CultureInfo.InvariantCulture);
            await core.ExecuteScriptAsync($"document.querySelectorAll('video,audio').forEach(e=>{{e.muted=false;e.volume={value};}});");
        }
        catch { /* navigation in progress */ }
    }

    public void HideKaraoke()
    {
        PauseKaraokeMedia();
        try { KaraokeWebView.CoreWebView2?.Stop(); } catch { /* ignore */ }
        _isKaraokeNavigating = false;
        KaraokeWebView.Visibility = Visibility.Collapsed;
        _isKaraokePlaying = false;
        ShowIdle();
    }

    public async Task<bool> PlayAsync(string filePath, double volume, bool loop, double imageDurationSeconds = 5.0)
    {
        var operationId = ++_localMediaOperationId;
        _pendingLocalMediaStart?.TrySetResult(false);
        await _localMediaStartGate.WaitAsync();
        try
        {
            if (operationId != _localMediaOperationId || _isFrozen)
                return false;
            var started = await PlayOwnedAsync(filePath, volume, loop, imageDurationSeconds);
            if (operationId == _localMediaOperationId && !_isFrozen)
                return started;

            if (started)
                ResetLocalMediaPipeline();
            return false;
        }
        finally
        {
            _localMediaStartGate.Release();
        }
    }

    private async Task<bool> PlayOwnedAsync(string filePath, double volume, bool loop, double imageDurationSeconds)
    {
        _isLooping = loop;

        if (MetadataService.IsImageFormat(filePath))
        {
            try
            {
                PlayImage(filePath, imageDurationSeconds);
                return !_isFrozen;
            }
            catch
            {
                return false;
            }
        }

        PauseKaraokeMedia();
        KaraokeWebView.Visibility = Visibility.Collapsed;
        _isKaraokePlaying = false;
        IdlePanel.Visibility = Visibility.Collapsed;

        StopImageTimer();
        _isImageActive = false;
        HideImages();
        Player.Visibility = Visibility.Visible;

        try
        {
            Player.Stop();
            Player.Close();
            Player.Source = null;
        }
        catch { /* previous media teardown */ }
        var opened = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingLocalMediaStart = opened;
        RoutedEventHandler onOpened = (_, _) => opened.TrySetResult(true);
        EventHandler<ExceptionRoutedEventArgs> onFailed = (_, _) => opened.TrySetResult(false);
        Player.MediaOpened += onOpened;
        Player.MediaFailed += onFailed;
        try
        {
            Player.Source = new Uri(filePath, UriKind.Absolute);
            Player.Volume = volume;
            Player.Play();

            var result = await Task.WhenAny(opened.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            var started = ReferenceEquals(result, opened.Task) && await opened.Task;
            if (!started || _isFrozen)
            {
                try
                {
                    Player.Stop();
                    Player.Close();
                    Player.Source = null;
                }
                catch { /* failed media cleanup */ }
                Player.Visibility = Visibility.Collapsed;
                _isLocalMediaPlaying = false;
                return false;
            }

            _isLocalMediaPlaying = true;
            return true;
        }
        catch
        {
            try
            {
                Player.Stop();
                Player.Close();
                Player.Source = null;
            }
            catch { /* failed media cleanup */ }
            Player.Visibility = Visibility.Collapsed;
            _isLocalMediaPlaying = false;
            return false;
        }
        finally
        {
            Player.MediaOpened -= onOpened;
            Player.MediaFailed -= onFailed;
            if (ReferenceEquals(_pendingLocalMediaStart, opened))
                _pendingLocalMediaStart = null;
        }
    }

    private void CancelPendingLocalMediaStart()
    {
        _localMediaOperationId++;
        _pendingLocalMediaStart?.TrySetResult(false);
    }

    public void CancelPendingProgramStart()
        => CancelPendingLocalMediaStart();

    private void ResetLocalMediaPipeline()
    {
        try
        {
            Player.Stop();
            Player.Close();
            Player.Source = null;
        }
        catch { /* local media teardown */ }
        Player.Visibility = Visibility.Collapsed;
        _isLocalMediaPlaying = false;
    }

    private void PauseKaraokeMedia()
    {
        _isKaraokePlaying = false;
        try
        {
            var core = KaraokeWebView.CoreWebView2;
            if (core is null) return;
            _ = core.ExecuteScriptAsync(
                "document.querySelectorAll('video,audio').forEach(media=>{media.pause();media.muted=true;});");
        }
        catch { /* WebView may be navigating while the Program source changes. */ }
    }

    private void ResumeKaraokeMedia()
    {
        try
        {
            var core = KaraokeWebView.CoreWebView2;
            if (core is null) return;
            _ = core.ExecuteScriptAsync(
                "document.querySelectorAll('video,audio').forEach(media=>{media.muted=false;media.play().catch(()=>{});});");
            _isKaraokePlaying = true;
        }
        catch { /* WebView may be navigating while freeze is released. */ }
    }

    public void Pause()
    {
        if (_isImageActive)
        {
            _resumeImageAfterFreeze = false;
            _imageElapsedSeconds = GetPosition();
            _imageStopwatch.Reset();
            _imageTimer.Stop();
            return;
        }
        if (Player.CanPause)
        {
            _resumeMediaAfterFreeze = false;
            Player.Pause();
            _isLocalMediaPlaying = false;
        }
    }

    public void Resume()
    {
        if (_isFrozen)
        {
            if (_isImageActive)
                _resumeImageAfterFreeze = true;
            else if (Player.Visibility == Visibility.Visible && Player.Source is not null)
                _resumeMediaAfterFreeze = true;
            return;
        }

        if (_isImageActive)
        {
            StartImageClock();
            return;
        }
        Player.Play();
        _isLocalMediaPlaying = true;
    }

    public double GetPosition() => _isImageActive
        ? Math.Min(_imageDurationSeconds, _imageElapsedSeconds + _imageStopwatch.Elapsed.TotalSeconds)
        : Player.Position.TotalSeconds;

    public double GetDuration() => _isImageActive
        ? _imageDurationSeconds
        : (Player.NaturalDuration.HasTimeSpan ? Player.NaturalDuration.TimeSpan.TotalSeconds : 0);

    public void Seek(double positionSeconds)
    {
        if (_isImageActive)
        {
            _imageElapsedSeconds = Math.Clamp(positionSeconds, 0, _imageDurationSeconds);
            StartImageClock();
        }
        else
        {
            Player.Position = TimeSpan.FromSeconds(Math.Max(0, positionSeconds));
        }
    }

    /// <summary>Clear media, show black + logo (safe between songs).</summary>
    public void ShowStandby()
    {
        ClearSafeScene();
        CancelPendingLocalMediaStart();
        ClearTestPattern();
        SetBlackout(false);
        CancelFreezeWithoutResume();
        StopImageTimer();
        _isImageActive = false;
        PauseKaraokeMedia();
        try { KaraokeWebView.CoreWebView2?.Stop(); } catch { /* ignore */ }
        _isKaraokeNavigating = false;
        KaraokeWebView.Visibility = Visibility.Collapsed;
        _isKaraokePlaying = false;
        try
        {
            Player.Stop();
            Player.Source = null;
        }
        catch { /* ignore */ }

        Player.Visibility = Visibility.Collapsed;
        _isLocalMediaPlaying = false;
        HideImages();
        _resumeMediaAfterFreeze = false;
        _resumeImageAfterFreeze = false;
        _resumeKaraokeAfterFreeze = false;
        ShowIdle();
    }

    private void PlayImage(string filePath, double durationSeconds)
    {
        PauseKaraokeMedia();
        KaraokeWebView.Visibility = Visibility.Collapsed;
        _isKaraokePlaying = false;
        IdlePanel.Visibility = Visibility.Collapsed;
        Player.Stop();
        Player.Source = null;
        Player.Visibility = Visibility.Collapsed;
        _isLocalMediaPlaying = false;

        var next = ReferenceEquals(_visibleImage, ImageFront) ? ImageBack : ImageFront;
        var previous = _visibleImage;
        next.Source = LoadDisplayBitmap(filePath);
        next.Visibility = Visibility.Visible;
        next.Opacity = 0;

        var fade = TimeSpan.FromMilliseconds(400);
        next.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, fade));
        if (previous is not null)
        {
            var fadeOut = new DoubleAnimation(previous.Opacity, 0, fade);
            fadeOut.Completed += (_, _) =>
            {
                if (!ReferenceEquals(previous, _visibleImage))
                {
                    previous.Visibility = Visibility.Collapsed;
                    previous.Source = null;
                }
            };
            previous.BeginAnimation(OpacityProperty, fadeOut);
        }

        _visibleImage = next;
        _imageDurationSeconds = Math.Max(0.5, durationSeconds);
        _imageElapsedSeconds = 0;
        _isImageActive = true;
        StartImageClock();
        if (_isFrozen)
        {
            _resumeImageAfterFreeze = true;
            PauseImageClock();
        }
    }

    private void StartImageClock()
    {
        if (!_isImageActive) return;
        _imageTimer.Stop();
        _imageTimer.Interval = TimeSpan.FromSeconds(Math.Max(0.01, _imageDurationSeconds - _imageElapsedSeconds));
        _imageStopwatch.Restart();
        _imageTimer.Start();
    }

    private void PauseImageClock()
    {
        if (!_isImageActive || !_imageTimer.IsEnabled) return;
        _imageElapsedSeconds = GetPosition();
        _imageStopwatch.Reset();
        _imageTimer.Stop();
    }

    private static BitmapImage LoadDisplayBitmap(string filePath)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
        bitmap.DecodePixelWidth = 3840;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private void OnImageTimerTick(object? sender, EventArgs e)
    {
        _imageTimer.Stop();
        _imageElapsedSeconds = _imageDurationSeconds;
        _imageStopwatch.Reset();
        if (_isLooping)
        {
            _imageElapsedSeconds = 0;
            _imageStopwatch.Restart();
            _imageTimer.Start();
            return;
        }
        ImageEnded?.Invoke(this, EventArgs.Empty);
    }

    private void StopImageTimer()
    {
        _imageTimer.Stop();
        _imageStopwatch.Reset();
        _imageElapsedSeconds = 0;
        _imageDurationSeconds = 0;
    }

    private void HideImages()
    {
        foreach (var image in new[] { ImageFront, ImageBack })
        {
            image.BeginAnimation(OpacityProperty, null);
            image.Opacity = 0;
            image.Visibility = Visibility.Collapsed;
            image.Source = null;
        }
        _visibleImage = null;
    }

    public void Stop() => Pause();

    private void ShowIdle()
    {
        IdlePanel.Visibility = Visibility.Visible;
    }

    private void Player_MediaEnded(object sender, RoutedEventArgs e)
    {
        if (_isLooping)
        {
            Player.Position = TimeSpan.Zero;
            Player.Play();
            e.Handled = true;
            return;
        }

        // Natural end keeps the final Program frame until another source replaces it.
        Player.Pause();
        _isLocalMediaPlaying = false;
    }

    private void OnRightClick(object sender, MouseButtonEventArgs e)
    {
        var menu = new System.Windows.Controls.ContextMenu();

        // 1) Toàn màn hình (Submenu)
        var fullscreenMenu = new System.Windows.Controls.MenuItem { Header = "Toàn màn hình" };
        int index = 1;
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            var capture = screen;
            var n = index;
            var item = new System.Windows.Controls.MenuItem
            {
                Header = $"Display {n}{(screen.Primary ? " (Chính)" : "")} ({screen.Bounds.Width}×{screen.Bounds.Height})"
            };
            item.Click += (_, _) =>
            {
                VideoPlayerService.PlaceOnScreen(this, capture);
            };
            fullscreenMenu.Items.Add(item);
            index++;
        }
        menu.Items.Add(fullscreenMenu);

        // 2) Chỉnh cửa sổ khít với nội dung
        var fitItem = new System.Windows.Controls.MenuItem { Header = "Chỉnh cửa sổ khít với nội dung" };
        fitItem.Click += (_, _) =>
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            Width = 960;
            Height = 540;
            WindowState = WindowState.Normal;
            // Center on primary screen
            var area = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
            Left = area.Left + (area.Width - Width) / 2;
            Top = area.Top + (area.Height - Height) / 2;
        };
        menu.Items.Add(fitItem);

        // 3) Luôn hiện trên cùng (Checkable toggle)
        var topmostItem = new System.Windows.Controls.MenuItem
        {
            Header = "Luôn hiện trên cùng",
            IsCheckable = true,
            IsChecked = this.Topmost
        };
        topmostItem.Click += (_, _) =>
        {
            this.Topmost = !this.Topmost;
        };
        menu.Items.Add(topmostItem);

        menu.Items.Add(new System.Windows.Controls.Separator());

        // 4) Đóng
        var closeItem = new System.Windows.Controls.MenuItem { Header = "Đóng" };
        closeItem.Click += (_, _) =>
        {
            this.Close();
        };
        menu.Items.Add(closeItem);

        menu.IsOpen = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        _imageTimer.Stop();
        _imageTimer.Tick -= OnImageTimerTick;
        Player.MediaEnded -= Player_MediaEnded;
        try
        {
            if (_processFailureHooked && KaraokeWebView.CoreWebView2 is { } core)
                core.ProcessFailed -= OnKaraokeProcessFailed;
        }
        catch { }
        try { KaraokeWebView.Dispose(); } catch { }
        base.OnClosed(e);
    }
}
