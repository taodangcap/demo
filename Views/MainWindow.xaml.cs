using ShowCuePlayer.Helpers;
using ShowCuePlayer.ViewModels;
using ShowCuePlayer.Services;
using ShowCuePlayer.Views;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Collections.ObjectModel;

namespace ShowCuePlayer;

/// <summary>
/// Main application window. All logic is in MainViewModel.
/// Code-behind only handles window chrome and drag-drop passthrough.
/// </summary>
public partial class MainWindow : Window
{
    public MainViewModel? ViewModel => DataContext as MainViewModel;
    public ObservableCollection<Models.AudioOutputEndpoint> KaraokeAudioOutputDevices { get; } = new();
    private Views.VideoWindow? _karaokeSecondaryWindow;
    private Views.VideoWindow? _previewSourceWindow;
    /// <summary>Mirror panel Preview (view) từ HWND OUTPUT — mượt hơn WebView CapturePreview.</summary>
    private readonly WindowHwndCaptureService _windowMirror = new();
    private CancellationTokenSource? _karaokeCaptureCts;
    private CancellationTokenSource? _mixerProgramCaptureCts;
    private readonly SemaphoreSlim _karaokeOutputGate = new(1, 1);
    private readonly Dictionary<string, int> _soundEffectHandles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Models.KaraokeSoundEffect> _soundEffectPlaybackModels = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _loadingSoundEffects = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<Guid> _registeredSoundEffectHotkeys = new();
    private readonly System.Windows.Threading.DispatcherTimer _soundEffectTimer;
    private readonly IGitHubUpdateService _updateService;
    private CancellationTokenSource? _updateCts;
    private CancellationTokenSource? _soundEffectVolumeSaveCts;
    private AppUpdateInfo? _availableUpdate;
    private bool _updateBusy;
    private Models.KaraokeSoundEffect? _selectedSoundEffect;
    private bool _soundEffectAudioInitialized;
    private bool _soundEffectSeeking;
    private bool _updatingSoundEffectVolumeUi;
    private bool _karaokeProgressSeeking;
    private bool _karaokeMuted;
    private bool _loadingKaraokeAudioOutputs;
    private long _karaokeOutputVersion;

    /// <summary>
    /// WindowStyle=None + WindowState.Maximized sẽ đè cả taskbar (kể cả taskbar dọc).
    /// Dùng maximize thủ công vào WorkingArea.
    /// </summary>
    private bool _isWorkAreaMaximized;
    private Rect _restoreBounds;

    public MainWindow(MainViewModel viewModel, IGitHubUpdateService updateService)
    {
        InitializeComponent();
        DataContext = viewModel;
        _updateService = updateService;

        // Responsive defaults theo vùng làm việc (trừ taskbar)
        MinWidth = 720;
        MinHeight = 480;
        var work = SystemParameters.WorkArea;
        Width = Math.Clamp(work.Width * 0.88, MinWidth, work.Width);
        Height = Math.Clamp(work.Height * 0.88, MinHeight, work.Height);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        // Không dùng Maximized hệ thống — sẽ chui dưới taskbar
        if (WindowState == WindowState.Maximized)
            WindowState = WindowState.Normal;

        PreviewKeyDown += OnPreviewKeyDown;
        SizeChanged += MainWindow_SizeChanged;
        _soundEffectTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _soundEffectTimer.Tick += (_, _) => UpdateSoundEffectTransport();
        _soundEffectTimer.Start();
        MixerPreviewVideo.MediaEnded += (_, _) =>
        {
            MixerPreviewVideo.Position = TimeSpan.Zero;
            MixerPreviewVideo.Play();
        };

        if (ViewModel != null)
        {
            ViewModel.VideoPlayer.WindowCreated += OnVideoWindowCreated;
            ViewModel.KaraokeOutputStopRequested += OnKaraokeOutputStopRequested;
            ViewModel.KaraokeUrlsChanged += OnKaraokeUrlsChanged;
            ViewModel.KaraokeAutoNextChanged += OnKaraokeAutoNextChanged;
            ViewModel.VideoScreenChanged += OnVideoScreenChanged;
            ViewModel.KaraokeToggleOutputRequested += OnKaraokeToggleOutputRequested;
            ViewModel.MixerKaraokeTakeRequested += OnMixerKaraokeTakeRequested;
            ViewModel.SoundEffectHotkeyPressed += OnSoundEffectHotkeyPressed;
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            ViewModel.FilteredCues.CollectionChanged += (_, _) => Dispatcher.BeginInvoke(UpdateCueCardSizing);
            ViewModel.Settings.SettingsChanged += Settings_SettingsChanged;
            ViewModel.AudioEngine.ChannelEnded += SoundEffectAudio_ChannelEnded;
        }

        this.Loaded += MainWindow_Loaded;
        this.Closed += MainWindow_Closed;
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.IsLivePreviewVisible)
            or nameof(MainViewModel.SelectedPlaylist))
        {
            ApplyResponsiveLayout();
            // Tắt preview → dừng capture (trả GPU cho màn OUTPUT karaoke)
            if (e.PropertyName == nameof(MainViewModel.IsLivePreviewVisible))
                Dispatcher.BeginInvoke(RefreshActiveOutputPreview);
        }

        if (e.PropertyName is nameof(MainViewModel.IsOutputPlaying)
            or nameof(MainViewModel.IsVideoOutputEnabled)
            or nameof(MainViewModel.IsKaraokeOutputOn))
            Dispatcher.BeginInvoke(RefreshActiveOutputPreview);

        if (e.PropertyName is nameof(MainViewModel.MixerPreviewInput)
            or nameof(MainViewModel.IsMixerKaraokePreview))
            Dispatcher.BeginInvoke(UpdateMixerPreview);

        if (e.PropertyName == nameof(MainViewModel.ActiveWorkspace))
        {
            Dispatcher.BeginInvoke(ApplyResponsiveLayout);
            Dispatcher.BeginInvoke(UpdateMixerPreview);
            Dispatcher.BeginInvoke(RefreshActiveOutputPreview);
        }


        if (e.PropertyName == nameof(MainViewModel.MasterVolume))
        {
            var volume = ViewModel?.MasterVolume ?? 1;
            ViewModel?.VideoPlayer.OutputWindow?.SetKaraokeVolume(volume);
            ApplyAllSoundEffectVolumes();
        }
    }

    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyResponsiveLayout();
        UpdateKaraokeProgramAspectRatio();
        if (KaraokeOnlyProgramDwmHost.Visibility == Visibility.Visible)
            KaraokeOnlyProgramDwmHost.UpdateThumbnailProperties();
        if (MixerProgramDwmHost.Visibility == Visibility.Visible)
            MixerProgramDwmHost.UpdateThumbnailProperties();
        else if (KaraokeDwmHost.Visibility == Visibility.Visible)
            KaraokeDwmHost.UpdateThumbnailProperties();
    }

    private void KaraokeOnlyProgramFrame_SizeChanged(object sender, SizeChangedEventArgs e)
        => UpdateKaraokeProgramAspectRatio();

    private void UpdateKaraokeProgramAspectRatio()
    {
        if (KaraokeOnlyProgramFrame.ActualWidth <= 0)
            return;

        var targetHeight = KaraokeOnlyProgramFrame.ActualWidth * 9.0 / 16.0;
        if (double.IsNaN(KaraokeOnlyProgramFrame.Height)
            || Math.Abs(KaraokeOnlyProgramFrame.Height - targetHeight) > 0.5)
            KaraokeOnlyProgramFrame.Height = targetHeight;
    }

    private void SearchSuggestion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string suggestion } || ViewModel is null) return;
        ViewModel.SearchText = suggestion;
        ViewModel.ShowSearchSuggestions = false;
        SearchBox.Focus();
        SearchBox.CaretIndex = SearchBox.Text.Length;
    }

    private void MixerProgramSeekSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        => ViewModel?.MixerBeginProgramSeekCommand.Execute(null);

    private async void MixerProgramSeekSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Slider slider || ViewModel is null) return;
        if (!await ViewModel.SeekMixerProgramAsync(slider.Value))
            slider.Value = ViewModel.MixerProgramInput?.Position ?? 0;
    }

    private void MixerProgramSeekSlider_LostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
        => ViewModel?.CancelMixerProgramSeek();

    private void MixerProgramVolumeSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        => ViewModel?.BeginMixerProgramVolumeEdit();

    private async void MixerProgramVolumeSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Slider slider || ViewModel is null) return;
        if (!await ViewModel.SetMixerProgramVolumeAsync(slider.Value))
            slider.Value = ViewModel.MixerProgramInput?.Volume ?? 0;
    }

    private void MixerProgramVolumeSlider_LostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
        => ViewModel?.CancelMixerProgramVolumeEdit();

    private void ContentGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        => ApplyResponsiveLayout();

    /// <summary>
    /// Co giãn layout khi kéo dãn cửa sổ: preview, search, toolbar karaoke.
    /// </summary>
    private void ApplyResponsiveLayout()
    {
        if (!IsLoaded) return;

        double w = Math.Max(0, ActualWidth);
        double h = Math.Max(0, ActualHeight);
        bool showPreview = ViewModel?.IsLivePreviewVisible == true;
        bool isKaraoke = ViewModel?.SelectedPlaylist?.IsKaraoke == true;
        UpdateCueCardSizing();

        // ── Cột preview ──────────────────────────────────────────────
        if (showPreview)
        {
            // Hẹp: preview nhỏ hơn; rộng: tối đa 420
            double previewW = w switch
            {
                < 720 => Math.Clamp(w * 0.34, 140, 180),
                < 900 => Math.Clamp(w * 0.30, 160, 220),
                < 1100 => Math.Clamp(w * 0.26, 180, 280),
                < 1400 => Math.Clamp(w * 0.24, 200, 360),
                _ => Math.Clamp(w * 0.22, 240, 420)
            };

            ColSplitter.Width = new GridLength(6);
            ColPreview.MinWidth = w < 800 ? 140 : 160;
            ColPreview.MaxWidth = w < 900 ? 260 : 520;
            // Chỉ set Width nếu user chưa kéo splitter lệch quá xa (tránh giật khi resize)
            if (ColPreview.Width.IsAbsolute && Math.Abs(ColPreview.Width.Value - previewW) > 40
                || ColPreview.Width.IsStar || ColPreview.Width.IsAuto
                || ColPreview.Width.Value < 50)
            {
                ColPreview.Width = new GridLength(previewW);
            }
        }
        else
        {
            ColSplitter.Width = new GridLength(0);
            ColPreview.MinWidth = 0;
            ColPreview.MaxWidth = 0;
            ColPreview.Width = new GridLength(0);
        }

        // ── Ô search header ──────────────────────────────────────────
        if (SearchBoxHost != null)
        {
            SearchBoxHost.Width = w switch
            {
                < 800 => 100,
                < 1000 => 120,
                < 1300 => 150,
                _ => 180
            };
        }

        // ── Toolbar karaoke: WrapPanel tự xuống dòng; chỉnh size theo bề ngang ─
        if (isKaraoke)
        {
            double karaokeW = showPreview
                ? Math.Max(200, w - (ColPreview.ActualWidth > 0 ? ColPreview.ActualWidth : 200) - 24)
                : Math.Max(200, w - 16);

            bool tiny = karaokeW < 640;
            bool compact = karaokeW < 820;

            if (TxtKaraokeStatus != null)
            {
                TxtKaraokeStatus.MaxWidth = Math.Max(120, karaokeW - 36);
                TxtKaraokeStatus.FontSize = tiny ? 10 : 11;
            }

            if (TxtKaraokeSession != null)
                TxtKaraokeSession.Width = tiny ? 64 : (compact ? 76 : 88);

            // Không ghi đè Content (đang Binding VM) — chỉ co size / ẩn phụ
            if (BtnKaraokeToggleSecondary != null)
                BtnKaraokeToggleSecondary.MinWidth = tiny ? 72 : 100;


            if (BtnKaraokeAutoNext != null)
                BtnKaraokeAutoNext.Visibility = tiny ? Visibility.Collapsed : Visibility.Visible;
            if (BtnKaraokeNew != null)
                BtnKaraokeNew.Visibility = compact && tiny ? Visibility.Collapsed : Visibility.Visible;

            if (LblOperator != null)
                LblOperator.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            if (LblOutput != null)
                LblOutput.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        }

        // ── Preview frame 16:9 ───────────────────────────────────────
        if (PreviewFrameBorder != null && PreviewSidePanel != null && showPreview)
        {
            double panelInner = Math.Max(100, PreviewSidePanel.ActualWidth - 20);
            if (panelInner > 40)
            {
                double h16x9 = panelInner * 9.0 / 16.0;
                double panelH = PreviewSidePanel.ActualHeight > 0
                    ? PreviewSidePanel.ActualHeight
                    : h * 0.5;
                // Preview không quá 48% chiều cao panel; tối thiểu 90px
                double maxH = Math.Max(90, panelH * 0.48);
                double minH = w < 800 ? 90 : 100;
                PreviewFrameBorder.Height = Math.Clamp(h16x9, minH, maxH);
            }

            // DWM host cập nhật khi co giãn
            if (KaraokeDwmHost?.Visibility == Visibility.Visible)
            {
                try { KaraokeDwmHost.UpdateThumbnailProperties(); }
                catch { /* ignore */ }
            }
        }

        if (ViewModel?.IsMixerWorkspace == true)
            UpdateMixerLayout(w, h);
    }

    private void UpdateMixerLayout(double windowWidth, double windowHeight)
    {
        // The Mixer grid may still report its previous size while switching from Collapsed.
        var gridWidth = Math.Max(320, windowWidth - 20);
        var gridHeight = Math.Max(260, windowHeight - 92);

        var transitionWidth = gridWidth switch
        {
            < 800 => 96,
            < 1150 => 112,
            _ => 128
        };
        MixerTransitionColumn.Width = new GridLength(transitionWidth);

        var monitorWidth = Math.Max(180, (gridWidth - transitionWidth - 24) / 2);
        var idealMonitorHeight = monitorWidth * 9.0 / 16.0 + 52;
        var heightCap = Math.Clamp(gridHeight * 0.48, 190, 400);
        var inputReserveCap = Math.Max(170, gridHeight - 150);
        var monitorHeight = Math.Min(idealMonitorHeight, Math.Min(heightCap, inputReserveCap));
        monitorHeight = Math.Max(170, monitorHeight);

        var layoutChanged = !MixerMonitorRow.Height.IsAbsolute
            || Math.Abs(MixerMonitorRow.Height.Value - monitorHeight) > 0.5;
        MixerMonitorRow.Height = new GridLength(monitorHeight);

        if (ViewModel is not null)
        {
            (ViewModel.MixerInputCardWidth, ViewModel.MixerInputCardHeight) = gridWidth switch
            {
                < 800 => (126, 112),
                < 1150 => (150, 126),
                < 1500 => (168, 138),
                _ => (184, 148)
            };
        }

        var compact = gridWidth < 900;
        MixerCutButton.Height = compact ? 28 : 30;
        MixerFadeButton.Height = compact ? 32 : 34;
        MixerOutputToggle.Content = gridWidth < 820 ? "OUT" : "OUTPUT";
        MixerProgramScreenText.Visibility = gridWidth < 1050
            ? Visibility.Collapsed
            : Visibility.Visible;

        _windowMirror.MaxWidth = (int)Math.Clamp(monitorWidth, 320, 1280);
        if (layoutChanged)
        {
            Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Render,
                new Action(() =>
                {
                    if (MixerProgramDwmHost.Visibility == Visibility.Visible)
                        MixerProgramDwmHost.UpdateThumbnailProperties();
                }));
        }
    }

    private void UpdateCueCardSizing()
    {
        if (ViewModel is null || !IsLoaded) return;
        var count = ViewModel.FilteredCues.Count;
        var available = Math.Max(280, ColMain.ActualWidth - 28);

        double itemWidth = 112;
        if (count is > 0 and <= 4)
        {
            var perCue = available / count;
            itemWidth = Math.Clamp(perCue, 112, 132);
        }

        var scale = itemWidth / 112.0;
        ViewModel.CueItemWidth = itemWidth;
        ViewModel.CueItemHeight = Math.Clamp(382 * scale, 382, 450);
    }



    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            ClampToWorkingArea();
            ApplyResponsiveLayout();
            LoadKaraokeAudioOutputs(applySavedSelection: true);

            await KaraokeOnlyControlWebView.EnsureCoreWebView2Async(null);
            // The operator/queue WebView is control-only. All audible Karaoke audio
            // must come exclusively from the Program WebView on the selected output.
            if (KaraokeOnlyControlWebView.CoreWebView2 is { } operatorCore)
                operatorCore.IsMuted = true;
            await HookWebViewEscBridgeAsync(KaraokeOnlyControlWebView);
            await ApplyKaraokeUrlsToWebViewsAsync(navigateRemote: true, navigatePlayer: false);
            SyncSoundEffectHotkeys();
            RefreshSoundEffectsBoard();
            _ = CheckForUpdatesAsync(interactive: false);

            _ = Dispatcher.BeginInvoke(new Action(ApplyResponsiveLayout),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    private void OnKaraokeUrlsChanged(object? sender, EventArgs e)
        => _ = ApplyKaraokeUrlsToWebViewsAsync(navigateRemote: true, navigatePlayer: true, delayRemoteMs: 600);

    private void OnKaraokeAutoNextChanged(object? sender, EventArgs e)
    {
        if (ViewModel is null) return;
        ViewModel.VideoPlayer.OutputWindow?.SetKaraokeAutoNext(ViewModel.KaraokeAutoNextEnabled);
        var enabled = ViewModel.KaraokeAutoNextEnabled ? "true" : "false";
        var message = $"{{\"type\":\"karaoke\",\"action\":\"autoNext\",\"enabled\":{enabled}}}";
        PostToKaraokePlayer(message);
        try { KaraokeOnlyControlWebView.CoreWebView2?.PostWebMessageAsJson(message); } catch { /* ignore */ }
    }

    private void OnVideoScreenChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnVideoScreenChanged(sender, e));
            return;
        }

        Dispatcher.BeginInvoke(RefreshActiveOutputPreview);
    }



    private void OnKaraokeToggleOutputRequested(object? sender, EventArgs e)
        => BtnKaraokeToggleSecondary_Click(this, new RoutedEventArgs());

    private CancellationTokenSource? _karaokeRemoteNavCts;

    private async Task ApplyKaraokeUrlsToWebViewsAsync(bool navigateRemote, bool navigatePlayer, int delayRemoteMs = 0)
    {
        if (ViewModel is null) return;
        if (navigatePlayer && ViewModel.VideoPlayer.IsFrozen)
        {
            _karaokeRemoteNavCts?.Cancel();
            ViewModel.StatusMessage = "OUTPUT FROZEN · hãy UNFREEZE trước khi điều hướng Karaoke";
            return;
        }

        try
        {
            // 1) OUTPUT master only (preview = capture, không WebView 2)
            if (navigatePlayer &&
                Uri.TryCreate(ViewModel.KaraokePlayerUrl, UriKind.Absolute, out var player))
            {
                var patternToClear = ViewModel.VideoPlayer.ActiveTestPattern;
                var patternVersionToClear = ViewModel.VideoPlayer.TestPatternVersion;
                var safeSceneVersionToClear = ViewModel.VideoPlayer.SafeSceneVersion;
                if (_karaokeSecondaryWindow is not null)
                {
                    ViewModel.CancelMixerTakeForExternalProgramChange();
                    var navigated = await _karaokeSecondaryWindow.NavigateAsync(player.ToString());
                    if (navigated)
                    {
                        if (ViewModel.VideoPlayer.IsSafeScene &&
                            ViewModel.VideoPlayer.SafeSceneVersion == safeSceneVersionToClear)
                        {
                            ViewModel.VideoPlayer.ClearSafeScene();
                        }
                        if (patternToClear is not null &&
                            ViewModel.VideoPlayer.ActiveTestPattern == patternToClear &&
                            ViewModel.VideoPlayer.TestPatternVersion == patternVersionToClear)
                        {
                            ViewModel.VideoPlayer.ClearTestPattern();
                        }
                    }
                    else if (!navigated)
                    {
                        ViewModel.StatusMessage = "Karaoke navigation failed · test pattern vẫn được giữ";
                    }
                }
            }

            // 2) Remote sau (đợi master register) — cùng ?session=
            if (navigateRemote &&
                Uri.TryCreate(ViewModel.KaraokeRemoteUrl, UriKind.Absolute, out var remote))
            {
                if (delayRemoteMs > 0)
                {
                    _karaokeRemoteNavCts?.Cancel();
                    var cts = new CancellationTokenSource();
                    _karaokeRemoteNavCts = cts;
                    var remoteStr = remote.ToString();
                    _ = NavigateRemoteAfterDelayAsync(remoteStr, delayRemoteMs, cts.Token);
                }
                else
                {
                    NavigateRemoteNow(remote.ToString());
                }
            }
        }
        catch { /* preview only */ }
    }

    private async Task NavigateRemoteAfterDelayAsync(string remoteUrl, int delayMs, CancellationToken ct)
    {
        try
        {
            await Task.Delay(delayMs, ct);
            if (ct.IsCancellationRequested) return;
            Dispatcher.Invoke(() => NavigateRemoteNow(remoteUrl));
        }
        catch (TaskCanceledException) { /* superseded */ }
        catch { /* ignore */ }
    }

    private void NavigateRemoteNow(string remoteUrl)
    {
        try
        {
            if (Uri.TryCreate(remoteUrl, UriKind.Absolute, out var remote))
            {
                if (KaraokeOnlyControlWebView.CoreWebView2 != null)
                {
                    KaraokeOnlyControlWebView.CoreWebView2.IsMuted = true;
                    KaraokeOnlyControlWebView.CoreWebView2.Navigate(remote.ToString());
                }
                else
                    KaraokeOnlyControlWebView.Source = remote;
            }
        }
        catch { /* ignore */ }
    }

    /// <summary>Gửi lệnh sang OUTPUT master (và preview nếu cần pause).</summary>
    private void PostToKaraokePlayer(string jsonOrText, bool masterOnly = false)
    {
        try
        {
            // Master output first
            _karaokeSecondaryWindow?.PostMessage(jsonOrText);

            if (masterOnly) return;

        }
        catch { /* ignore */ }
    }

    private void PreviewFrameBorder_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyResponsiveLayout();
        if (KaraokeDwmHost.Visibility == Visibility.Visible)
            KaraokeDwmHost.UpdateThumbnailProperties();
    }

    /// <summary>
    /// WebView2 nuốt phím WPF — inject JS bắt Esc rồi postMessage về app.
    /// </summary>
    private async Task HookWebViewEscBridgeAsync(Microsoft.Web.WebView2.Wpf.WebView2 webView)
    {
        if (webView.CoreWebView2 is null) return;
        var core = webView.CoreWebView2;
        await core.AddScriptToExecuteOnDocumentCreatedAsync(
            """
            (async function () {
              try {
                if (sessionStorage.getItem('__scpCacheResetV3')) return;
                sessionStorage.setItem('__scpCacheResetV3', '1');
                let changed = false;
                if ('serviceWorker' in navigator) {
                  const registrations = await navigator.serviceWorker.getRegistrations();
                  for (const registration of registrations) {
                    changed = (await registration.unregister()) || changed;
                  }
                }
                if ('caches' in window) {
                  const keys = await caches.keys();
                  for (const key of keys) changed = (await caches.delete(key)) || changed;
                }
                if (changed) {
                  const url = new URL(location.href);
                  url.searchParams.set('_scp', Date.now().toString());
                  location.replace(url.toString());
                }
              } catch (_) {}
            })();
            """);
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
        core.WebMessageReceived -= OnKaraokeWebViewMessage;
        core.WebMessageReceived += OnKaraokeWebViewMessage;
    }

    private void OnKaraokeWebViewMessage(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
    {
        string msg;
        try { msg = e.TryGetWebMessageAsString(); }
        catch { msg = e.WebMessageAsJson ?? string.Empty; }

        if (string.IsNullOrWhiteSpace(msg)) return;

        // Esc thuần từ hook JS, hoặc JSON action esc/escape
        var isEsc =
            msg.Equals("ESC", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("\"action\":\"ESC\"", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("\"action\":\"esc\"", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("\"action\":\"escape\"", StringComparison.OrdinalIgnoreCase);

        if (isEsc)
        {
            Dispatcher.Invoke(() =>
                HandleEscapeKey(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)));
            return;
        }

        // NOW title / ready từ KTV player → panel status cố định
        if (ViewModel is null) return;
        var action = ExtractJsonStringField(msg, "action");
        if (string.IsNullOrEmpty(action)) return;

        var title = ExtractJsonStringField(msg, "title");
        Dispatcher.Invoke(() => ViewModel.ApplyKaraokeBridgeState(action, title));
    }

    private static string? ExtractJsonStringField(string json, string field)
    {
        // Lightweight extract "field":"value" without full JSON dependency
        try
        {
            var key = $"\"{field}\":";
            var idx = json.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;
            var i = idx + key.Length;
            while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
            if (i >= json.Length || json[i] != '"') return null;
            i++;
            var start = i;
            while (i < json.Length && json[i] != '"')
            {
                if (json[i] == '\\' && i + 1 < json.Length) i += 2;
                else i++;
            }
            return json[start..i];
        }
        catch { return null; }
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _updateCts?.Cancel();
        _updateCts?.Dispose();
        _updateCts = null;
        _soundEffectVolumeSaveCts?.Cancel();
        _soundEffectVolumeSaveCts?.Dispose();
        _soundEffectVolumeSaveCts = null;
        MixerPreviewVideo.Stop();
        MixerPreviewVideo.Source = null;
        StopMixerProgramPreview();
        StopKaraokeOutputCapture();
        StopAllSoundEffects(refresh: false);
        _soundEffectTimer.Stop();
        _windowMirror.Dispose();
        _karaokeSecondaryWindow?.Close();
        KaraokeOnlyControlWebView.Dispose();
        if (ViewModel is not null)
        {
            foreach (var id in _registeredSoundEffectHotkeys)
                ViewModel.HotkeyService.Unregister(id);
            _registeredSoundEffectHotkeys.Clear();
            ViewModel.Settings.SettingsChanged -= Settings_SettingsChanged;
            ViewModel.AudioEngine.ChannelEnded -= SoundEffectAudio_ChannelEnded;
            ViewModel.SoundEffectHotkeyPressed -= OnSoundEffectHotkeyPressed;
        }
    }

    private void OnVideoWindowCreated(object? sender, System.EventArgs e)
    {
        if (ViewModel?.VideoPlayer.OutputWindow is { } window)
        {
            window.KaraokeProcessFailed -= OnKaraokeProcessFailed;
            window.KaraokeProcessFailed += OnKaraokeProcessFailed;
            window.KaraokeStateChanged -= OnKaraokeStateChanged;
            window.KaraokeStateChanged += OnKaraokeStateChanged;
            window.KaraokeProgressChanged -= OnKaraokeProgressChanged;
            window.KaraokeProgressChanged += OnKaraokeProgressChanged;
            window.SetKaraokeMuted(_karaokeMuted);
        }
        Dispatcher.Invoke(RefreshActiveOutputPreview);
    }

    private void OnKaraokeStateChanged(string? action, string? title)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnKaraokeStateChanged(action, title));
            return;
        }
        ViewModel?.ApplyKaraokeBridgeState(action, title);
        if (action is not null &&
            (action.Equals("ended", StringComparison.OrdinalIgnoreCase) ||
             action.Equals("idle", StringComparison.OrdinalIgnoreCase) ||
             action.Equals("ready", StringComparison.OrdinalIgnoreCase) ||
             action.Equals("stopped", StringComparison.OrdinalIgnoreCase) ||
             action.Equals("stop", StringComparison.OrdinalIgnoreCase) ||
             action.Equals("empty", StringComparison.OrdinalIgnoreCase)))
            ResetKaraokeProgress();
    }

    private void OnKaraokeProgressChanged(double positionSeconds, double durationSeconds)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnKaraokeProgressChanged(positionSeconds, durationSeconds));
            return;
        }
        if (_karaokeProgressSeeking || durationSeconds <= 0) return;
        KaraokeProgressSlider.Maximum = durationSeconds;
        KaraokeProgressSlider.Value = Math.Clamp(positionSeconds, 0, durationSeconds);
        KaraokeElapsedText.Text = FormatKaraokeTime(positionSeconds);
        KaraokeDurationText.Text = FormatKaraokeTime(durationSeconds);
    }

    private static string FormatKaraokeTime(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{(int)time.TotalMinutes}:{time.Seconds:00}";
    }

    private void UpdateMixerPreview()
    {
        try
        {
            MixerPreviewVideo.Stop();
            MixerPreviewVideo.Source = null;
            MixerPreviewVideo.Visibility = Visibility.Collapsed;
            MixerPreviewImage.Source = null;
            MixerPreviewImage.Visibility = Visibility.Collapsed;
            MixerKaraokePreviewPlaceholder.Visibility = Visibility.Collapsed;

            if (ViewModel?.IsMixerWorkspace != true)
                return;
            if (ViewModel.IsMixerKaraokePreview)
            {
                MixerKaraokePreviewPlaceholder.Visibility = Visibility.Visible;
                return;
            }
            if (ViewModel.MixerPreviewInput is not { } input) return;
            var path = input.Model.FilePath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

            if (Services.MetadataService.IsImageFormat(path))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
                MixerPreviewImage.Source = bitmap;
                MixerPreviewImage.Visibility = Visibility.Visible;
            }
            else
            {
                MixerPreviewVideo.Source = new Uri(path, UriKind.Absolute);
                MixerPreviewVideo.Volume = 0;
                MixerPreviewVideo.IsMuted = true;
                MixerPreviewVideo.Visibility = Visibility.Visible;
                MixerPreviewVideo.Play();
            }
        }
        catch
        {
            MixerPreviewVideo.Visibility = Visibility.Collapsed;
            MixerPreviewImage.Visibility = Visibility.Collapsed;
            MixerKaraokePreviewPlaceholder.Visibility = Visibility.Collapsed;
        }
    }

    private void RefreshActiveOutputPreview()
    {
        if (ViewModel?.IsMixerWorkspace == true)
        {
            RefreshMixerProgramPreview();
            return;
        }

        StopMixerProgramPreview();
        RefreshKaraokeOnlyProgramPreview();
    }

    private void RefreshKaraokeOnlyProgramPreview()
    {
        var output = ViewModel?.VideoPlayer.OutputWindow;
        if (ViewModel?.IsKaraokeOutputOn != true || output?.IsLoaded != true)
        {
            try { KaraokeOnlyProgramDwmHost.ClearSource(); } catch { /* ignore */ }
            KaraokeOnlyProgramDwmHost.Visibility = Visibility.Collapsed;
            KaraokeOnlyProgramPlaceholder.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            var hwnd = new WindowInteropHelper(output).EnsureHandle();
            if (hwnd == IntPtr.Zero) return;
            KaraokeOnlyProgramDwmHost.Visibility = Visibility.Visible;
            KaraokeOnlyProgramDwmHost.UpdateLayout();
            KaraokeOnlyProgramDwmHost.SetSource(hwnd);
            KaraokeOnlyProgramDwmHost.UpdateThumbnailProperties();
            KaraokeOnlyProgramPlaceholder.Visibility = KaraokeOnlyProgramDwmHost.HasThumbnail
                ? Visibility.Collapsed
                : Visibility.Visible;
        }
        catch
        {
            KaraokeOnlyProgramPlaceholder.Visibility = Visibility.Visible;
        }
    }

    private void RefreshMixerProgramPreview()
    {
        StopMixerProgramPreview();
        if (ViewModel?.IsMixerWorkspace != true
            || ViewModel.VideoPlayer.IsOutputArmed != true
            || ViewModel.VideoPlayer.OutputWindow is not { IsLoaded: true } output)
            return;

        var captureSession = new CancellationTokenSource();
        _mixerProgramCaptureCts = captureSession;
        MixerProgramDwmHost.Visibility = Visibility.Visible;
        MixerProgramDwmHost.UpdateLayout();
        _ = TryStartMixerProgramDwmThenFallbackAsync(output, captureSession);
    }

    private async Task TryStartMixerProgramDwmThenFallbackAsync(
        Views.VideoWindow output,
        CancellationTokenSource captureSession)
    {
        var cancellationToken = captureSession.Token;
        try
        {
            for (var attempt = 0; attempt < 10; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Dispatcher.InvokeAsync(
                    () => { },
                    System.Windows.Threading.DispatcherPriority.Loaded,
                    cancellationToken);
                await Task.Delay(40 + attempt * 35, cancellationToken);

                if (!IsMixerProgramCaptureCurrent(output, captureSession))
                    return;

                var hwnd = new WindowInteropHelper(output).EnsureHandle();
                if (hwnd == IntPtr.Zero) continue;

                MixerProgramDwmHost.Visibility = Visibility.Visible;
                MixerProgramDwmHost.UpdateLayout();
                MixerProgramDwmHost.SetSource(hwnd);
                MixerProgramDwmHost.UpdateThumbnailProperties();

                if (MixerProgramDwmHost.HasThumbnail)
                {
                    MixerProgramCaptureImage.Source = null;
                    MixerProgramCaptureImage.Visibility = Visibility.Collapsed;
                    _windowMirror.Stop();
                    return;
                }

                if (MixerProgramDwmHost.LastHResult is not null)
                    break;
            }

            cancellationToken.ThrowIfCancellationRequested();
            await Dispatcher.InvokeAsync(
                () => StartMixerProgramCaptureFallback(output, captureSession),
                System.Windows.Threading.DispatcherPriority.Normal,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected when Output or workspace changes while the mirror is starting.
        }
        catch
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (IsMixerProgramCaptureCurrent(output, captureSession))
                        StartMixerProgramCaptureFallback(output, captureSession);
                }));
            }
        }
    }

    private bool IsMixerProgramCaptureCurrent(
        Views.VideoWindow output,
        CancellationTokenSource captureSession)
        => ReferenceEquals(_mixerProgramCaptureCts, captureSession)
           && !captureSession.IsCancellationRequested
           && ViewModel?.IsMixerWorkspace == true
           && ViewModel.VideoPlayer.IsOutputArmed
           && ReferenceEquals(ViewModel.VideoPlayer.OutputWindow, output)
           && output.IsLoaded;

    private void StartMixerProgramCaptureFallback(
        Views.VideoWindow output,
        CancellationTokenSource captureSession)
    {
        if (!IsMixerProgramCaptureCurrent(output, captureSession))
            return;

        try { MixerProgramDwmHost.ClearSource(); } catch { /* ignore */ }
        MixerProgramDwmHost.Visibility = Visibility.Collapsed;
        MixerProgramCaptureImage.Source = null;
        MixerProgramCaptureImage.Visibility = Visibility.Visible;

        var hwnd = new WindowInteropHelper(output).Handle;
        if (hwnd == IntPtr.Zero)
            hwnd = new WindowInteropHelper(output).EnsureHandle();
        if (hwnd == IntPtr.Zero) return;

        var panelWidth = MixerProgramCaptureImage.ActualWidth;
        _windowMirror.MaxWidth = (int)Math.Clamp(panelWidth > 40 ? panelWidth : 960, 480, 1280);
        _windowMirror.IntervalMs = 33;
        _windowMirror.Start(hwnd, frame =>
        {
            if (frame is not null && IsMixerProgramCaptureCurrent(output, captureSession))
                MixerProgramCaptureImage.Source = frame;
        });
    }

    private void StopMixerProgramPreview()
    {
        var captureSession = _mixerProgramCaptureCts;
        _mixerProgramCaptureCts = null;
        captureSession?.Cancel();
        captureSession?.Dispose();
        _windowMirror.Stop();
        MixerProgramCaptureImage.Source = null;
        MixerProgramCaptureImage.Visibility = Visibility.Collapsed;
        try { MixerProgramDwmHost.ClearSource(); } catch { /* ignore */ }
        MixerProgramDwmHost.Visibility = Visibility.Collapsed;
    }

    private void OnKaraokeProcessFailed(object? sender, string failureKind)
    {
        Dispatcher.BeginInvoke(() =>
        {
            CloseKaraokeSecondaryIfOpen();
            if (ViewModel is not null)
                ViewModel.StatusMessage = $"⚠ Karaoke WebView lỗi ({failureKind}) · Output đã tắt an toàn";
        });
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;
        bool typing = IsTypingInTextBox();

        // Esc luôn giữ vai trò dừng an toàn; đồng thời đóng trạng thái nhập/tìm kiếm.
        if (key == Key.Escape && typing)
        {
            if (ViewModel is not null)
                ViewModel.ShowSearchSuggestions = false;
            Keyboard.ClearFocus();
            Focus();
            typing = false;
        }

        if (typing) return;
        if (ViewModel is null) return;

        // Hotkeys từ Settings (kiểu OBS)
        if (TryHandleConfiguredHotkey(key, mods))
        {
            e.Handled = true;
            return;
        }

        // Soundboard hotkeys are handled by the global hook; swallow the matching local event
        // so one physical key press cannot be handled twice while this window has focus.
        if (ViewModel.Settings.Current.KaraokeSoundEffects.Any(effect =>
                Helpers.HotkeyUtil.Matches(effect.HotkeyText, key, mods)))
        {
            e.Handled = true;
            return;
        }

    }

    /// <summary>Map phím đã lưu trong Settings → lệnh MainViewModel.</summary>
    private bool TryHandleConfiguredHotkey(Key key, ModifierKeys mods)
    {
        if (ViewModel is null) return false;
        var map = ViewModel.Settings.Current.Hotkeys;
        if (map is null || map.Count == 0)
        {
            ViewModel.Settings.Current.EnsureHotkeys();
            map = ViewModel.Settings.Current.Hotkeys;
        }

        bool Hit(string actionId) =>
            map.TryGetValue(actionId, out var binding)
            && !string.IsNullOrWhiteSpace(binding)
            && Helpers.HotkeyUtil.Matches(binding, key, mods);

        if (Hit(Models.HotkeyActions.StopAll))
        {
            ViewModel.StopAllCommand.Execute(null);
            StopAllSoundEffects();
            CloseKaraokeSecondaryIfOpen();
            return true;
        }
        if (Hit(Models.HotkeyActions.StopTab))
        {
            // Checkbox "Esc tắt tab": nếu hotkey là Esc và checkbox tắt → bỏ qua
            var stopTabBinding = map.GetValueOrDefault(Models.HotkeyActions.StopTab) ?? "";
            bool isEsc = Helpers.HotkeyUtil.Matches(stopTabBinding, Key.Escape, ModifierKeys.None);
            if (isEsc && !ViewModel.Settings.Current.StopAllOnEscape)
                return true;

            ViewModel.StopSelectedTabCommand.Execute(null);
            if (ViewModel.SelectedPlaylist?.IsKaraoke == true)
                CloseKaraokeSecondaryIfOpen();
            return true;
        }
        if (Hit(Models.HotkeyActions.SaveProject))
        {
            ViewModel.SaveProjectCommand.Execute(null);
            return true;
        }
        if (Hit(Models.HotkeyActions.OpenProject))
        {
            ViewModel.OpenProjectCommand.Execute(null);
            return true;
        }
        if (Hit(Models.HotkeyActions.ApplyKaraoke))
        {
            ViewModel.ApplyKaraokeSessionCommand.Execute(null);
            return true;
        }

        if (Hit(Models.HotkeyActions.ToggleKaraokeOnOff))
        {
            BtnKaraokeToggleSecondary_Click(this, new RoutedEventArgs());
            return true;
        }

        return false;
    }

    /// <summary>
    /// Esc = tắt tab đang chọn; karaoke → đóng màn OUTPUT karaoke.
    /// Shift+Esc = stop all.
    /// </summary>
    private void HandleEscapeKey(bool shiftStopAll)
    {
        if (ViewModel is null) return;

        if (shiftStopAll)
        {
            ViewModel.StopAllCommand.Execute(null);
            StopAllSoundEffects();
            CloseKaraokeSecondaryIfOpen();
            return;
        }

        if (ViewModel.Settings?.Current?.StopAllOnEscape == false)
            return;

        // 1) Logic VM (stop cues / disarm video)
        ViewModel.StopSelectedTabCommand.Execute(null);

        // 2) Karaoke: luôn đóng secondary output (không phụ thuộc event)
        if (ViewModel.SelectedPlaylist?.IsKaraoke == true)
            CloseKaraokeSecondaryIfOpen();
    }

    private void OnKaraokeOutputStopRequested(object? sender, EventArgs e)
        => CloseKaraokeSecondaryIfOpen();

    // ─── Thao tác nhanh (Click fallback — luôn chạy được) ─────────

    private void BtnQuickArm_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null) return;
        if (!ViewModel.CanMutateShowConfiguration("thay đổi trạng thái output")) return;
        // Toggle qua property → OnIsVideoOutputEnabledChanged → arm/disarm thật
        ViewModel.IsVideoOutputEnabled = !ViewModel.IsVideoOutputEnabled;
        ApplyResponsiveLayout();
        e.Handled = true;
    }

    private void BtnQuickStopAll_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null) return;
        if (ViewModel.StopAllCommand.CanExecute(null))
            ViewModel.StopAllCommand.Execute(null);
        StopAllSoundEffects();
        CloseKaraokeSecondaryIfOpen();
        e.Handled = true;
    }

    private void CloseKaraokeSecondaryIfOpen()
    {
        Interlocked.Increment(ref _karaokeOutputVersion);
        bool refreshShowPreview = ViewModel?.IsShowWorkspace == true;
        if (refreshShowPreview)
            StopKaraokeOutputCapture();

        // 2) Pause (an toàn nếu webview còn)
        try
        {
            PostToKaraokePlayer("{\"type\":\"karaoke\",\"action\":\"stop\"}", masterOnly: true);
        }
        catch { /* ignore */ }

        var win = _karaokeSecondaryWindow;
        _karaokeSecondaryWindow = null; // cắt ref ngay — Closed handler không double-close

        if (win != null)
        {
            try { win.HideKaraoke(); } catch { /* ignore */ }
        }

        if (refreshShowPreview)
            RefreshActiveOutputPreview();

        // Luôn OFF — kể cả khi window đã chết sẵn (X/taskbar) mà UI còn ON
        ViewModel?.SetKaraokeOutputOn(false);
        if (ViewModel is not null)
            ViewModel.ProgramOutputModeText = "ĐÃ TẮT";
        ResetKaraokeProgress();
    }

    /// <summary>
    /// Mirror the Program output HWND for every source: standby, local media, and Karaoke.
    /// DWM is preferred; PrintWindow capture is the compatibility fallback.
    /// </summary>
    private void StartKaraokeOutputCapture()
    {
        RefreshKaraokeOnlyProgramPreview();
    }

    private void StartOutputWindowCapture(Views.VideoWindow? outputWindow)
    {
        if (outputWindow is null
            || ViewModel?.IsShowWorkspace != true
            || ViewModel.IsLivePreviewVisible != true
            || ViewModel.VideoPlayer.IsOutputArmed != true)
        {
            StopKaraokeOutputCapture();
            PreviewPlaceholder.Visibility = Visibility.Visible;
            return;
        }

        // Preview ẩn → không mirror
        _previewSourceWindow = outputWindow;
        _karaokeCaptureCts?.Cancel();
        _karaokeCaptureCts?.Dispose();
        _karaokeCaptureCts = new CancellationTokenSource();

        PreviewPlaceholder.Visibility = Visibility.Collapsed;
        KaraokeCaptureImage.Visibility = Visibility.Collapsed;
        KaraokeCaptureImage.Source = null;

        // DWM trước (gần VisualBrush USB). Fail → HWND mirror.
        KaraokeDwmHost.Visibility = Visibility.Visible;
        KaraokeDwmHost.UpdateLayout();

        _ = TryStartDwmThenFallbackAsync(_karaokeCaptureCts.Token);
    }

    private async Task TryStartDwmThenFallbackAsync(CancellationToken cancellationToken)
    {
        try
        {
            await TryStartDwmThenFallbackCoreAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Preview lifecycle cancellation is expected when the source changes or turns off.
        }
    }

    private async Task TryStartDwmThenFallbackCoreAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Loaded, cancellationToken);
            await Task.Delay(40 + attempt * 35, cancellationToken);

            var outputWindow = _previewSourceWindow;
            if (outputWindow is null || !outputWindow.IsLoaded)
                continue;

            try
            {
                var src = new WindowInteropHelper(outputWindow).EnsureHandle();
                if (src == IntPtr.Zero) continue;

                KaraokeDwmHost.Visibility = Visibility.Visible;
                KaraokeDwmHost.UpdateLayout();
                KaraokeDwmHost.SetSource(src);
                KaraokeDwmHost.UpdateThumbnailProperties();

                if (KaraokeDwmHost.HasThumbnail)
                {
                    KaraokeCaptureImage.Visibility = Visibility.Collapsed;
                    _windowMirror.Stop();
                    if (ViewModel is not null)
                        ViewModel.StatusMessage = "Preview VIEW (DWM) ⇄ OUTPUT · mượt như USB";
                    return;
                }

                if (KaraokeDwmHost.LastHResult is not null)
                    break;
            }
            catch { /* retry / fallback */ }
        }

        cancellationToken.ThrowIfCancellationRequested();
        await Dispatcher.InvokeAsync(StartCaptureFallback, System.Windows.Threading.DispatcherPriority.Normal, cancellationToken);
    }

    private void StartCaptureFallback()
    {
        var outputWindow = _previewSourceWindow;
        if (outputWindow is null) return;
        if (ViewModel?.IsShowWorkspace != true || ViewModel.IsLivePreviewVisible != true)
        {
            StopKaraokeOutputCapture();
            return;
        }

        try { KaraokeDwmHost.ClearSource(); } catch { /* ignore */ }
        KaraokeDwmHost.Visibility = Visibility.Collapsed;
        KaraokeCaptureImage.Visibility = Visibility.Visible;
        PreviewPlaceholder.Visibility = Visibility.Collapsed;

        var hwnd = new WindowInteropHelper(outputWindow).Handle;
        if (hwnd == IntPtr.Zero)
            hwnd = new WindowInteropHelper(outputWindow).EnsureHandle();
        if (hwnd == IntPtr.Zero) return;

        // Scale theo bề ngang panel preview (~mượt 30fps)
        double panelW = PreviewFrameBorder?.ActualWidth ?? 480;
        _windowMirror.MaxWidth = (int)Math.Clamp(panelW > 40 ? panelW : 640, 480, 1280);
        _windowMirror.IntervalMs = 33; // ~30 FPS — view control
        _windowMirror.Start(hwnd, frame =>
        {
            if (frame is null) return;
            KaraokeCaptureImage.Source = frame;
        });

        if (ViewModel is not null)
            ViewModel.StatusMessage = "Preview VIEW (HWND mirror) ⇄ OUTPUT · ~30 FPS";
    }

    private void StopKaraokeOutputCapture()
    {
        _karaokeCaptureCts?.Cancel();
        _karaokeCaptureCts?.Dispose();
        _karaokeCaptureCts = null;
        _previewSourceWindow = null;
        _windowMirror.Stop();
        KaraokeCaptureImage.Source = null;
        KaraokeCaptureImage.Visibility = Visibility.Collapsed;
        try
        {
            KaraokeDwmHost.ClearSource();
            KaraokeDwmHost.Visibility = Visibility.Collapsed;
        }
        catch { /* ignore */ }
        try
        {
            KaraokeOnlyProgramDwmHost.ClearSource();
            KaraokeOnlyProgramDwmHost.Visibility = Visibility.Collapsed;
            KaraokeOnlyProgramPlaceholder.Visibility = Visibility.Visible;
        }
        catch { /* ignore */ }
    }

    private static bool IsTypingInTextBox()
    {
        return Keyboard.FocusedElement is TextBox or System.Windows.Controls.PasswordBox
            or System.Windows.Controls.RichTextBox;
    }

    // Title bar drag move + double-click maximize
    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (e.ClickCount == 2)
        {
            BtnMaximize_Click(sender, e);
            return;
        }
        // Kéo khi đang “maximize work area” → thu nhỏ lại rồi drag
        if (_isWorkAreaMaximized)
        {
            _isWorkAreaMaximized = false;
            var mouse = e.GetPosition(this);
            var ratioX = Width > 0 ? mouse.X / Width : 0.5;
            if (_restoreBounds.Width > 100 && _restoreBounds.Height > 100)
            {
                Width = _restoreBounds.Width;
                Height = _restoreBounds.Height;
            }
            else
            {
                Width = Math.Min(Width, SystemParameters.WorkArea.Width * 0.88);
                Height = Math.Min(Height, SystemParameters.WorkArea.Height * 0.88);
            }
            var screenPos = PointToScreen(mouse);
            Left = screenPos.X - (Width * ratioX);
            Top = Math.Max(SystemParameters.WorkArea.Top, screenPos.Y - 20);
            ClampToWorkingArea();
        }
        try { DragMove(); }
        catch { /* ignore */ }
    }

    private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private async void BtnCheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (_updateBusy) return;
        if (_availableUpdate is not null)
            await DownloadAndInstallUpdateAsync(_availableUpdate);
        else
            await CheckForUpdatesAsync(interactive: true);
    }

    private async Task CheckForUpdatesAsync(bool interactive)
    {
        if (_updateBusy) return;
        _updateBusy = true;
        _updateCts?.Cancel();
        _updateCts?.Dispose();
        _updateCts = new CancellationTokenSource();
        BtnCheckUpdates.Content = "…";
        BtnCheckUpdates.ToolTip = "Đang kiểm tra GitHub Releases";

        try
        {
            var result = await _updateService.CheckForUpdateAsync(_updateCts.Token);
            if (result.Update is { } update)
            {
                _availableUpdate = update;
                BtnCheckUpdates.Content = "↓";
                BtnCheckUpdates.Foreground = new SolidColorBrush(Color.FromRgb(0x67, 0xE8, 0xA4));
                BtnCheckUpdates.ToolTip = $"Có bản {update.Version} · bấm để cập nhật";
                if (ViewModel is not null)
                    ViewModel.StatusMessage = $"Có bản cập nhật 7zyx Media {update.Version} trên GitHub";
                if (interactive)
                    await DownloadAndInstallUpdateAsync(update);
                return;
            }

            BtnCheckUpdates.Content = "↻";
            BtnCheckUpdates.ClearValue(Button.ForegroundProperty);
            BtnCheckUpdates.ToolTip = "Kiểm tra cập nhật GitHub";
            if (interactive)
            {
                var message = result.Error ?? $"Bạn đang dùng bản mới nhất ({_updateService.CurrentVersion}).";
                MessageBox.Show(this, message, "Cập nhật 7zyx Media",
                    MessageBoxButton.OK,
                    result.Error is null ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            _updateBusy = false;
            if (_availableUpdate is null && Equals(BtnCheckUpdates.Content, "…"))
                BtnCheckUpdates.Content = "↻";
        }
    }

    private async Task DownloadAndInstallUpdateAsync(AppUpdateInfo update)
    {
        if (_updateBusy)
        {
            // Interactive check calls this while owning the update operation.
        }
        else
        {
            _updateBusy = true;
            _updateCts?.Cancel();
            _updateCts?.Dispose();
            _updateCts = new CancellationTokenSource();
        }

        try
        {
            if (ViewModel?.IsKaraokeOutputOn == true
                || ViewModel?.VideoPlayer.IsOutputArmed == true
                || _soundEffectHandles.Count > 0)
            {
                MessageBox.Show(this,
                    "PROGRAM hoặc âm thanh đang chạy. Hãy tắt khung chiếu và dừng hiệu ứng trước khi cập nhật.",
                    "Chưa thể cập nhật", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var notes = string.IsNullOrWhiteSpace(update.Notes)
                ? "Không có ghi chú phát hành."
                : update.Notes.Trim();
            if (notes.Length > 800) notes = notes[..800] + "…";
            var sizeText = update.Size > 0 ? $"{update.Size / 1024d / 1024d:F1} MB" : "không rõ";
            var answer = MessageBox.Show(this,
                $"Có bản 7zyx Media {update.Version}\n" +
                $"Bản hiện tại: {_updateService.CurrentVersion}\n" +
                $"Dung lượng: {sizeText}\n\n{notes}\n\n" +
                "Tải và cài đặt ngay? App sẽ đóng sau khi tải xong.",
                "Cập nhật từ GitHub", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (answer != MessageBoxResult.Yes) return;

            _updateCts ??= new CancellationTokenSource();
            var progress = new Progress<double>(value =>
            {
                var percent = (int)Math.Round(value * 100);
                BtnCheckUpdates.Content = $"{percent}%";
                if (ViewModel is not null)
                    ViewModel.StatusMessage = $"Đang tải cập nhật {update.Version} · {percent}%";
            });
            var installer = await _updateService.DownloadInstallerAsync(
                update, progress, _updateCts.Token);

            if (ViewModel is not null)
            {
                await ViewModel.Settings.SaveAsync();
                ViewModel.StopAllCommand.Execute(null);
            }
            StopAllSoundEffects(refresh: false);
            _updateService.LaunchInstaller(installer);
            Application.Current.Shutdown();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            BtnCheckUpdates.Content = "↓";
            MessageBox.Show(this, $"Không cập nhật được:\n{ex.Message}",
                "Cập nhật 7zyx Media", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _updateBusy = false;
            if (_availableUpdate is not null && Equals(BtnCheckUpdates.Content, "…"))
                BtnCheckUpdates.Content = "↓";
        }
    }

    private void BtnMaximize_Click(object sender, RoutedEventArgs e)
    {
        // WindowStyle=None: Maximized hệ thống che taskbar → maximize vào WorkingArea
        if (_isWorkAreaMaximized || WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
            if (_restoreBounds.Width > 100 && _restoreBounds.Height > 100)
            {
                Left = _restoreBounds.Left;
                Top = _restoreBounds.Top;
                Width = _restoreBounds.Width;
                Height = _restoreBounds.Height;
            }
            _isWorkAreaMaximized = false;
            ClampToWorkingArea();
        }
        else
        {
            _restoreBounds = new Rect(Left, Top, Width, Height);
            WindowState = WindowState.Normal;
            FitToWorkingArea();
            _isWorkAreaMaximized = true;
        }
        ApplyResponsiveLayout();
    }

    /// <summary>Phóng vừa vùng làm việc (trừ taskbar mọi cạnh).</summary>
    private void FitToWorkingArea()
    {
        var wa = GetWorkingAreaDip();
        Left = wa.Left;
        Top = wa.Top;
        Width = Math.Max(MinWidth, wa.Width);
        Height = Math.Max(MinHeight, wa.Height);
    }

    /// <summary>Giữ cửa sổ nằm trong working area (không chui dưới taskbar).</summary>
    private void ClampToWorkingArea()
    {
        if (WindowState == WindowState.Minimized) return;
        // Nếu đang Maximized hệ thống → chuyển sang work-area fit
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
            FitToWorkingArea();
            _isWorkAreaMaximized = true;
            return;
        }

        var wa = GetWorkingAreaDip();
        var w = Math.Min(Width, wa.Width);
        var h = Math.Min(Height, wa.Height);
        if (w < MinWidth) w = Math.Min(MinWidth, wa.Width);
        if (h < MinHeight) h = Math.Min(MinHeight, wa.Height);
        Width = w;
        Height = h;

        if (Left < wa.Left) Left = wa.Left;
        if (Top < wa.Top) Top = wa.Top;
        if (Left + Width > wa.Right) Left = Math.Max(wa.Left, wa.Right - Width);
        if (Top + Height > wa.Bottom) Top = Math.Max(wa.Top, wa.Bottom - Height);
    }

    /// <summary>Working area màn hình chứa cửa sổ, đơn vị DIP (WPF).</summary>
    private Rect GetWorkingAreaDip()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                var screen = System.Windows.Forms.Screen.FromHandle(hwnd);
                var wa = screen.WorkingArea; // device pixels
                var source = PresentationSource.FromVisual(this);
                if (source?.CompositionTarget != null)
                {
                    var fromDevice = source.CompositionTarget.TransformFromDevice;
                    var topLeft = fromDevice.Transform(new Point(wa.Left, wa.Top));
                    var bottomRight = fromDevice.Transform(new Point(wa.Right, wa.Bottom));
                    return new Rect(topLeft, bottomRight);
                }
                // Fallback: assume 96 DPI
                return new Rect(wa.Left, wa.Top, wa.Width, wa.Height);
            }
        }
        catch { /* fall through */ }

        return SystemParameters.WorkArea;
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
        => Close();

    /// <summary>Opens a button's ContextMenu as a drop-down (used by Add / More).</summary>
    private void OpenButtonMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.ContextMenu is null) return;
        btn.ContextMenu.PlacementTarget = btn;
        btn.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        btn.ContextMenu.DataContext = btn.DataContext ?? DataContext;
        btn.ContextMenu.IsOpen = true;
        e.Handled = true;
    }

    // ─── Karaoke Logic (operator desk) ───────────────────────────────────

    private void BtnKaraokeApply_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.ApplyKaraokeSessionCommand.Execute(null);
    }

    private void BtnKaraokeNewSession_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.NewKaraokeSessionCommand.Execute(null);
    }

    private void BtnKaraokeAutoNext_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.ToggleKaraokeAutoNextCommand.Execute(null);
    }

    private void BtnKaraokePlay_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.IsKaraokeOutputOn != true) return;
        if (ViewModel.VideoPlayer.IsFrozen)
        {
            ViewModel.StatusMessage = "OUTPUT FROZEN · hãy UNFREEZE trước khi phát Karaoke";
            return;
        }
        PostToKaraokePlayer("{\"type\":\"karaoke\",\"action\":\"play\"}", masterOnly: true);
        ViewModel.StatusMessage = "Karaoke Control → PLAY";
    }

    private void BtnKaraokePause_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.IsKaraokeOutputOn != true) return;
        PostToKaraokePlayer("{\"type\":\"karaoke\",\"action\":\"pause\"}", masterOnly: true);
        ViewModel.StatusMessage = "Karaoke Control → PAUSE";
    }



    private async Task<bool> NavigateKaraokePlayerFromVmAsync(
        Views.VideoWindow outputWindow,
        long outputVersion)
    {
        if (ViewModel is null) return false;
        if (ViewModel.VideoPlayer.IsFrozen)
        {
            ViewModel.StatusMessage = "OUTPUT FROZEN · hãy UNFREEZE trước khi điều hướng Karaoke";
            return false;
        }

        // Chỉ navigate OUTPUT master — preview = capture (không WebView 2)
        if (Uri.TryCreate(ViewModel.KaraokePlayerUrl, UriKind.Absolute, out var master))
        {
            ViewModel.CancelMixerTakeForExternalProgramChange();
            outputWindow.SetKaraokeVolume(ViewModel.MasterVolume);
            outputWindow.SetKaraokeMuted(_karaokeMuted);
            outputWindow.SetKaraokeAutoNext(ViewModel.KaraokeAutoNextEnabled);
            var patternToClear = ViewModel.VideoPlayer.ActiveTestPattern;
            var patternVersionToClear = ViewModel.VideoPlayer.TestPatternVersion;
            var safeSceneVersionToClear = ViewModel.VideoPlayer.SafeSceneVersion;
            if (await outputWindow.NavigateAsync(master.ToString()) &&
                outputVersion == Volatile.Read(ref _karaokeOutputVersion) &&
                ReferenceEquals(_karaokeSecondaryWindow, outputWindow))
            {
                if (ViewModel.VideoPlayer.IsSafeScene &&
                    ViewModel.VideoPlayer.SafeSceneVersion == safeSceneVersionToClear)
                {
                    ViewModel.VideoPlayer.ClearSafeScene();
                }
                if (patternToClear is not null &&
                    ViewModel.VideoPlayer.ActiveTestPattern == patternToClear &&
                    ViewModel.VideoPlayer.TestPatternVersion == patternVersionToClear)
                {
                    ViewModel.VideoPlayer.ClearTestPattern();
                }
                return true;
            }
            else
                ViewModel.StatusMessage = "Karaoke navigation failed hoặc bị chặn · test pattern vẫn được giữ";
        }
        return false;
    }

    private async void OnMixerKaraokeTakeRequested(string transition)
    {
        if (ViewModel is null || ViewModel.VideoPlayer.IsFrozen) return;
        try
        {
            var useFade = string.Equals(transition, "FADE", StringComparison.OrdinalIgnoreCase);
            if (useFade)
            {
                var seconds = Math.Clamp(ViewModel.MixerTransitionDurationMs, 0, 5000) / 1000d;
                await ViewModel.VideoPlayer.FadeProgramMaskAsync(true, seconds / 2d);
            }

            if (!ViewModel.IsKaraokeOutputOn && !await OpenKaraokeOutputAsync())
            {
                ViewModel.VideoPlayer.ClearProgramTransitionMask();
                ViewModel.StatusMessage = "MIXER Karaoke TAKE thất bại · Output chưa sẵn sàng";
                return;
            }

            ViewModel.SetMixerKaraokeProgramActive(true);
            RefreshActiveOutputPreview();
            if (useFade)
                await ViewModel.VideoPlayer.FadeProgramMaskAsync(false,
                    Math.Clamp(ViewModel.MixerTransitionDurationMs, 0, 5000) / 2000d);
            ViewModel.StatusMessage = $"MIXER {transition} → Karaoke LIVE";
        }
        catch (Exception ex)
        {
            ViewModel.VideoPlayer.ClearProgramTransitionMask();
            ViewModel.StatusMessage = $"MIXER Karaoke TAKE lỗi: {ex.Message}";
        }
    }

    private async void BtnKaraokeToggleSecondary_Click(object sender, RoutedEventArgs e)
    {
        // Nguồn sự thật = state UI: nếu đang ON (hoặc còn cửa sổ sống) → luôn TẮT.
        // Tránh bug: đóng tab OUTPUT bằng X/taskbar → window chết nhưng UI vẫn ON,
        // bấm "KARAOKE OFF" lại mở cửa sổ mới (vẫn ON).
        ViewModel?.CancelMixerTakeForExternalProgramChange();
        bool wantOff = ViewModel?.IsKaraokeOutputOn == true;
        if (wantOff)
        {
            CloseKaraokeSecondaryIfOpen();
            return;
        }
        // KARAOKE ON chỉ khởi động nguồn Program nội bộ. Cửa sổ M1/M2 là thao tác riêng.
        ViewModel!.VideoPlayer.SetPreviewOnlyOutput();
        if (await OpenKaraokeOutputAsync())
        {
            ViewModel.ProgramOutputModeText = "PROGRAM · LIVE";
            ViewModel.StatusMessage = "Karaoke ON · đang hiển thị trong PROGRAM";
        }
    }

    private async Task<bool> OpenKaraokeOutputAsync()
    {
        if (ViewModel is null) return false;
        await _karaokeOutputGate.WaitAsync();

        var outputVersion = Interlocked.Increment(ref _karaokeOutputVersion);
        try
        {
            if (ViewModel.IsKaraokeOutputOn && _karaokeSecondaryWindow is { IsLoaded: true })
                return true;
            if (ViewModel.VideoPlayer.IsFrozen)
            {
                ViewModel.StatusMessage = "OUTPUT FROZEN · hãy UNFREEZE trước khi bật Karaoke";
                return false;
            }
            if (ViewModel.IsLiveLocked && string.IsNullOrWhiteSpace(ViewModel.KaraokeSessionId))
            {
                ViewModel.StatusMessage = "SHOW LOCK · cần tạo Karaoke session trước khi khóa";
                return false;
            }

            // Session + URL, KHÔNG navigate remote trước player
            ViewModel.PrepareKaraokeSessionSilent();

            // Karaoke is exclusive only with local video; audio and images keep their state.
            ViewModel.StopLocalVideosForKaraoke();
            ViewModel.VideoPlayer.EnsureOutputArmed();

            // Dọn cửa sổ “zombie” (Closed nhưng ref còn)
            _karaokeSecondaryWindow = ViewModel.VideoPlayer.OutputWindow;
            if (_karaokeSecondaryWindow is null) return false;
            var outputWindow = _karaokeSecondaryWindow;

            if (!outputWindow.IsVisible)
                outputWindow.Show();
            Activate();

            // Master phải tải thành công trước khi trạng thái được chuyển sang ON/LIVE.
            if (!await NavigateKaraokePlayerFromVmAsync(outputWindow, outputVersion))
            {
                try { outputWindow.HideKaraoke(); } catch { /* best effort */ }
                if (outputVersion == Volatile.Read(ref _karaokeOutputVersion))
                    ViewModel.SetKaraokeOutputOn(false);
                return false;
            }

            // Preview = DWM/capture OUTPUT
            StartKaraokeOutputCapture();

            // Remote được mở sau khi master đã đăng ký session.
            if (Uri.TryCreate(ViewModel.KaraokeRemoteUrl, UriKind.Absolute, out var remote))
            {
                _karaokeRemoteNavCts?.Cancel();
                var cts = new CancellationTokenSource();
                _karaokeRemoteNavCts = cts;
                _ = NavigateRemoteAfterDelayAsync(remote.ToString(), 800, cts.Token);
            }

            if (outputVersion != Volatile.Read(ref _karaokeOutputVersion)) return false;
            ViewModel.SetKaraokeOutputOn(true);
            ViewModel.ProgramOutputModeText = $"{ViewModel.SelectedVideoScreen?.ShortName ?? "OUTPUT"} · LIVE";
            ViewModel.StatusMessage = $"Karaoke sẵn sàng · session {ViewModel.KaraokeSessionId}";
            return true;
        }
        finally
        {
            _karaokeOutputGate.Release();
        }
    }

    private void ProgramContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || sender is not ContextMenu menu) return;

        menu.Items.Clear();
        var internalItem = new MenuItem
        {
            Header = "Chỉ hiển thị trong PROGRAM",
            IsCheckable = true,
            IsChecked = ViewModel.IsKaraokeOutputOn && ViewModel.VideoPlayer.IsPreviewOnlyOutput
        };
        internalItem.Click += async (_, _) => await OpenProgramInternalAsync();
        menu.Items.Add(internalItem);
        menu.Items.Add(new Separator());

        var projectorMenu = new MenuItem { Header = "Chuyển khung chiếu sang" };
        foreach (var screenInfo in ViewModel.VideoScreens.OrderBy(screen => screen.Index))
        {
            if (screenInfo.Screen is null) continue;

            var target = screenInfo;
            var screenMenu = new MenuItem
            {
                Header = $"M{target.Index} · {(target.IsPrimary ? "Điều khiển" : "Output")} · {target.Width}×{target.Height}"
            };

            var isCurrentScreen = ViewModel.IsKaraokeOutputOn
                && string.Equals(ViewModel.VideoPlayer.TargetScreen?.DeviceName, target.DeviceName,
                    StringComparison.OrdinalIgnoreCase);
            var windowedItem = new MenuItem
            {
                Header = "Mở dạng cửa sổ",
                IsCheckable = true,
                IsChecked = isCurrentScreen && ViewModel.VideoPlayer.IsWindowedOutput
            };
            windowedItem.Click += async (_, _) => await OpenProgramWindowedAsync(target);
            screenMenu.Items.Add(windowedItem);

            var fullscreenItem = new MenuItem
            {
                Header = "Mở toàn màn hình",
                IsCheckable = true,
                IsChecked = isCurrentScreen
                    && !ViewModel.VideoPlayer.IsWindowedOutput
                    && !ViewModel.VideoPlayer.IsPreviewOnlyOutput
            };
            fullscreenItem.Click += async (_, _) =>
                await OpenProgramFullscreenAsync(target.Screen, target, target.DisplayName);
            screenMenu.Items.Add(fullscreenItem);
            projectorMenu.Items.Add(screenMenu);
        }
        menu.Items.Add(projectorMenu);
        menu.Items.Add(new Separator());

        var captureItem = new MenuItem
        {
            Header = "Chụp màn hình (Chương trình)",
            Command = ViewModel.CaptureOutputScreenshotCommand
        };
        menu.Items.Add(captureItem);
        menu.Items.Add(new Separator());
        var offItem = new MenuItem
        {
            Header = "Tắt Output ngoài (giữ Program)",
            Foreground = System.Windows.Media.Brushes.IndianRed,
            IsEnabled = ViewModel.IsKaraokeOutputOn && !ViewModel.VideoPlayer.IsPreviewOnlyOutput
        };
        offItem.Click += BtnProgramOutputOff_Click;
        menu.Items.Add(offItem);
    }

    private async Task OpenProgramInternalAsync()
    {
        if (ViewModel is null) return;
        ViewModel.VideoPlayer.SetPreviewOnlyOutput();
        if (!ViewModel.IsKaraokeOutputOn)
            await OpenKaraokeOutputAsync();
        else
            ViewModel.VideoPlayer.EnsureOutputArmed();
        if (ViewModel.IsVideoOutputEnabled)
            ViewModel.IsVideoOutputEnabled = false;
        ViewModel.VideoPlayer.SetPreviewOnlyOutput();
        RefreshActiveOutputPreview();
        ViewModel.ProgramOutputModeText = "OUTPUT OFF · LIVE";
        ViewModel.StatusMessage = "Output ngoài đã tắt · PROGRAM và phiên Karaoke vẫn đang chạy";
    }

    private async Task OpenProgramFullscreenAsync(
        System.Windows.Forms.Screen? screen,
        Models.VideoScreenInfo? screenInfo,
        string displayLabel)
    {
        if (ViewModel is null) return;
        if (screen is null)
        {
            ViewModel.StatusMessage = "Không tìm thấy màn hình đã chọn";
            return;
        }
        try
        {
            // Apply placement before opening so the window cannot flash on the wrong monitor.
            ViewModel.VideoPlayer.SetTargetScreen(screen, forceFullscreen: true);
            if (screenInfo is not null)
                ViewModel.SelectedVideoScreen = screenInfo;

            if (!ViewModel.IsKaraokeOutputOn)
                await OpenKaraokeOutputAsync();
            else
                ViewModel.VideoPlayer.EnsureOutputArmed();

            // Bảo đảm cửa sổ đã tạo cũng nhận đúng màn hình sau khi WebView sẵn sàng.
            ViewModel.VideoPlayer.SetTargetScreen(screen, forceFullscreen: true);
            ViewModel.ProgramOutputModeText = $"{screenInfo?.ShortName ?? displayLabel} · FULLSCREEN";
            ViewModel.StatusMessage = $"Khung chiếu PROGRAM → {displayLabel} · FULLSCREEN";
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"Không mở được khung chiếu: {ex.Message}";
        }
    }

    private async Task OpenProgramWindowedAsync(Models.VideoScreenInfo? requestedScreen = null)
    {
        if (ViewModel is null) return;
        var target = requestedScreen
            ?? ViewModel.VideoScreens.FirstOrDefault(screen => screen.IsPrimary)
            ?? ViewModel.VideoScreens.OrderBy(screen => screen.Index).FirstOrDefault();
        if (target?.Screen is null)
        {
            ViewModel.StatusMessage = "Không tìm thấy màn hình để mở cửa sổ PROGRAM";
            return;
        }

        // Apply placement first to avoid a one-frame fullscreen jump during a live switch.
        ViewModel.VideoPlayer.SetWindowedOutput(true, target.Screen);
        ViewModel.SelectedVideoScreen = target;
        if (!ViewModel.IsKaraokeOutputOn)
            await OpenKaraokeOutputAsync();
        else
            ViewModel.VideoPlayer.EnsureOutputArmed();
        ViewModel.VideoPlayer.SetWindowedOutput(true, target.Screen);
        ViewModel.ProgramOutputModeText = $"{target.ShortName} · WINDOW";
        ViewModel.StatusMessage = $"Khung chiếu PROGRAM → {target.DisplayName} · CỬA SỔ";
    }

    private void BtnProgramOutputOff_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null) return;

        // Only release the external screen. The offscreen master WebView remains alive,
        // so Program preview, playback position, session and queue are preserved.
        if (!ViewModel.IsKaraokeOutputOn)
        {
            ViewModel.ProgramOutputModeText = "ĐÃ TẮT";
            ViewModel.StatusMessage = "Output đang tắt · chưa có phiên Karaoke đang chạy";
            return;
        }

        if (ViewModel.IsVideoOutputEnabled)
            ViewModel.IsVideoOutputEnabled = false;
        ViewModel.VideoPlayer.SetPreviewOnlyOutput();
        RefreshActiveOutputPreview();
        ViewModel.ProgramOutputModeText = "OUTPUT OFF · LIVE";
        ViewModel.StatusMessage = "Đã tắt Output ngoài · PROGRAM và phiên Karaoke vẫn được giữ nguyên";
    }

    private void BtnKaraokeMute_Click(object sender, RoutedEventArgs e)
    {
        _karaokeMuted = !_karaokeMuted;
        KaraokeMuteIcon.Text = _karaokeMuted ? "🔇" : "🔊";
        KaraokeMuteButton.ToolTip = _karaokeMuted ? "Bật lại âm thanh" : "Tắt tiếng";
        ViewModel?.VideoPlayer.OutputWindow?.SetKaraokeMuted(_karaokeMuted);
        if (ViewModel is { } vm)
        {
            ApplyAllSoundEffectVolumes();
            vm.StatusMessage = _karaokeMuted ? "PROGRAM đã tắt tiếng" : "PROGRAM đã bật lại âm thanh";
        }
    }

    private void LoadKaraokeAudioOutputs(bool applySavedSelection)
    {
        if (ViewModel is null) return;
        try
        {
            _loadingKaraokeAudioOutputs = true;
            var endpoints = WindowsAudioOutputService.GetActiveOutputs();
            KaraokeAudioOutputDevices.Clear();
            foreach (var endpoint in endpoints)
                KaraokeAudioOutputDevices.Add(endpoint);

            var savedId = ViewModel.Settings.Current.KaraokeAudioEndpointId;
            var selected = endpoints.FirstOrDefault(endpoint =>
                               string.Equals(endpoint.Id, savedId, StringComparison.OrdinalIgnoreCase))
                           ?? endpoints.FirstOrDefault(endpoint => endpoint.IsDefault)
                           ?? endpoints.FirstOrDefault();
            KaraokeAudioOutputCombo.SelectedValue = selected?.Id;

            if (applySavedSelection && selected is not null && !string.IsNullOrWhiteSpace(savedId))
                ApplyKaraokeAudioOutput(selected, save: false);
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"Không đọc được thiết bị âm thanh: {ex.Message}";
        }
        finally
        {
            _loadingKaraokeAudioOutputs = false;
        }
    }

    private async void KaraokeAudioOutputCombo_SelectionChanged(
        object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_loadingKaraokeAudioOutputs ||
            KaraokeAudioOutputCombo.SelectedItem is not Models.AudioOutputEndpoint endpoint)
            return;

        if (ApplyKaraokeAudioOutput(endpoint, save: true) && ViewModel is not null)
            await ViewModel.Settings.SaveAsync();
    }

    private void RefreshKaraokeAudioOutputs_Click(object sender, RoutedEventArgs e)
        => LoadKaraokeAudioOutputs(applySavedSelection: false);

    private bool ApplyKaraokeAudioOutput(Models.AudioOutputEndpoint endpoint, bool save)
    {
        if (ViewModel is null) return false;
        try
        {
            WindowsAudioOutputService.SetDefaultOutput(endpoint.Id);

            // BASS phát soundboard bằng engine riêng; chuyển nó sang thiết bị cùng tên nếu tìm thấy.
            var bassDevices = ViewModel.AudioEngine.GetOutputDevices();
            var endpointKey = NormalizeAudioDeviceName(endpoint.Name);
            var bassDevice = bassDevices
                .Where(device => device.IsEnabled && device.Index != 0)
                .OrderByDescending(device => AudioDeviceMatchScore(
                    endpointKey, NormalizeAudioDeviceName(device.Name)))
                .FirstOrDefault(device => AudioDeviceMatchScore(
                    endpointKey, NormalizeAudioDeviceName(device.Name)) > 0);
            if (bassDevice is not null && ViewModel.AudioEngine.CurrentDeviceIndex != bassDevice.Index)
            {
                StopAllSoundEffects();
                if (ViewModel.AudioEngine.SwitchDevice(bassDevice.Index))
                {
                    ViewModel.Settings.Current.AudioDeviceIndex = bassDevice.Index;
                    ViewModel.Settings.Current.AudioDeviceName = bassDevice.Name;
                }
            }

            if (save)
            {
                ViewModel.Settings.Current.KaraokeAudioEndpointId = endpoint.Id;
                ViewModel.Settings.Current.KaraokeAudioEndpointName = endpoint.Name;
            }
            ViewModel.StatusMessage = bassDevice is null
                ? $"AUDIO OUT → {endpoint.Name} · Karaoke đã chuyển, chưa ghép được Soundboard"
                : $"AUDIO OUT → {endpoint.Name} · Karaoke + Soundboard";
            return true;
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"Không chuyển được AUDIO OUT: {ex.Message}";
            return false;
        }
    }

    private static string NormalizeAudioDeviceName(string? value)
        => new((value ?? string.Empty).Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant).ToArray());

    private static int AudioDeviceMatchScore(string endpoint, string bass)
    {
        if (endpoint.Length == 0 || bass.Length == 0) return 0;
        if (endpoint == bass) return 100;
        if (endpoint.Contains(bass, StringComparison.Ordinal) ||
            bass.Contains(endpoint, StringComparison.Ordinal)) return 80;

        var commonPrefix = endpoint.Zip(bass).TakeWhile(pair => pair.First == pair.Second).Count();
        return commonPrefix >= 6 ? commonPrefix : 0;
    }

    private void KaraokeProgressSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => _karaokeProgressSeeking = true;

    private async void KaraokeProgressSlider_LostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_karaokeProgressSeeking) return;
        _karaokeProgressSeeking = false;
        var position = KaraokeProgressSlider.Value;
        KaraokeElapsedText.Text = FormatKaraokeTime(position);
        if (ViewModel?.VideoPlayer.OutputWindow is { } outputWindow)
            await outputWindow.SeekKaraokeAsync(position);
    }

    private void ResetKaraokeProgress()
    {
        _karaokeProgressSeeking = false;
        KaraokeProgressSlider.Maximum = 1;
        KaraokeProgressSlider.Value = 0;
        KaraokeElapsedText.Text = "0:00";
        KaraokeDurationText.Text = "0:00";
    }

    private async void BtnProgramScreen1Window_Click(object sender, RoutedEventArgs e)
        => await OpenProgramWindowedAsync();

    private async void BtnProgramScreen2Fullscreen_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null) return;
        var screen2 = ViewModel.VideoScreens
            .OrderBy(screen => screen.Index)
            .FirstOrDefault(screen => !screen.IsPrimary && screen.Screen is not null);
        if (screen2 is null)
        {
            ViewModel.StatusMessage = "Chưa kết nối Màn 2 · hãy cắm màn hình rồi chọn Detect trong Windows";
            return;
        }

        await OpenProgramFullscreenAsync(screen2.Screen, screen2, screen2.DisplayName);
    }

    private void Settings_SettingsChanged(object? sender, EventArgs e)
    {
        void RefreshAndReconcile()
        {
            var effects = ViewModel?.Settings.Current.KaraokeSoundEffects;
            foreach (var activeId in _soundEffectHandles.Keys.ToArray())
            {
                var current = effects?.FirstOrDefault(effect =>
                    string.Equals(effect.Id, activeId, StringComparison.OrdinalIgnoreCase));
                if (!_soundEffectPlaybackModels.TryGetValue(activeId, out var playingModel)
                    || !ReferenceEquals(current, playingModel))
                    StopSoundEffect(activeId, refresh: false);
            }
            if (_selectedSoundEffect is not null
                && effects?.Any(effect => ReferenceEquals(effect, _selectedSoundEffect)) != true)
                _selectedSoundEffect = null;
            SyncSoundEffectHotkeys();
            RefreshSoundEffectsBoard();
        }

        if (Dispatcher.CheckAccess())
            RefreshAndReconcile();
        else
            Dispatcher.BeginInvoke(RefreshAndReconcile);
    }

    private void SyncSoundEffectHotkeys()
    {
        if (ViewModel is null) return;

        foreach (var id in _registeredSoundEffectHotkeys)
            ViewModel.HotkeyService.Unregister(id);
        _registeredSoundEffectHotkeys.Clear();

        var conflictCount = 0;
        foreach (var effect in ViewModel.Settings.Current.KaraokeSoundEffects)
        {
            if (string.IsNullOrWhiteSpace(effect.HotkeyText)
                || !Guid.TryParse(effect.Id, out var id)
                || !Helpers.HotkeyUtil.TryParse(effect.HotkeyText, out var key, out var modifiers)
                || HasConfiguredActionConflict(effect.HotkeyText)
                || !ViewModel.HotkeyService.Register(id, effect.HotkeyText, key, modifiers))
            {
                if (!string.IsNullOrWhiteSpace(effect.HotkeyText))
                    conflictCount++;
                continue;
            }

            _registeredSoundEffectHotkeys.Add(id);
        }

        if (conflictCount > 0)
            ViewModel.StatusMessage = $"⚠ {conflictCount} hotkey hiệu ứng đang bị trùng nên chưa được kích hoạt";
    }

    private bool HasConfiguredActionConflict(string hotkeyText)
        => ViewModel?.Settings.Current.Hotkeys.Values.Any(binding =>
            !string.IsNullOrWhiteSpace(binding)
            && string.Equals(binding, hotkeyText, StringComparison.OrdinalIgnoreCase)) == true;

    private async void OnSoundEffectHotkeyPressed(string effectId)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(new Action(() => OnSoundEffectHotkeyPressed(effectId)));
            return;
        }

        var effect = ViewModel?.Settings.Current.KaraokeSoundEffects.FirstOrDefault(item =>
            string.Equals(item.Id, effectId, StringComparison.OrdinalIgnoreCase));
        if (effect is not null)
            await ToggleSoundEffectAsync(effect);
    }

    private void RefreshSoundEffectsBoard()
    {
        if (!IsInitialized || ViewModel is null) return;
        var effects = ViewModel.Settings.Current.KaraokeSoundEffects;
        SoundEffectsItemsControl.ItemsSource = null;
        SoundEffectsItemsControl.ItemsSource = effects.ToList();
        SoundEffectsEmptyText.Visibility = effects.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdateSoundEffectTransport();
    }

    private async void BtnAddSoundEffects_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null) return;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Thêm âm thanh vào Soundboard",
            Filter = "File âm thanh|*.mp3;*.wav;*.m4a;*.aac;*.wma;*.flac;*.ogg;*.opus|Tất cả file|*.*",
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != true) return;

        var effects = ViewModel.Settings.Current.KaraokeSoundEffects;
        foreach (var filePath in dialog.FileNames)
        {
            if (effects.Count >= 200) break;
            var defaultName = System.IO.Path.GetFileNameWithoutExtension(filePath);
            var name = defaultName;
            if (dialog.FileNames.Length == 1)
            {
                var nameDialog = new Controls.Dialogs.InputDialog(
                    "Tên hiệu ứng", "Nhập tên hiển thị trên nút:", defaultName) { Owner = this };
                if (nameDialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(nameDialog.Result))
                    name = nameDialog.Result.Trim();
            }

            effects.Add(new Models.KaraokeSoundEffect
            {
                Name = name,
                FilePath = filePath
            });
        }

        await SaveSoundEffectsAsync();
        ViewModel.StatusMessage = $"Đã thêm {dialog.FileNames.Length} hiệu ứng âm thanh";
    }

    private async void SoundEffectButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: Models.KaraokeSoundEffect effect } || ViewModel is null)
            return;

        await ToggleSoundEffectAsync(effect);
    }

    private async Task ToggleSoundEffectAsync(Models.KaraokeSoundEffect effect)
    {
        if (ViewModel is null) return;

        _selectedSoundEffect = effect;
        UpdateSoundEffectTransport();

        if (_soundEffectHandles.TryGetValue(effect.Id, out var existingHandle))
        {
            if (ViewModel.AudioEngine.IsPlaying(existingHandle))
            {
                ViewModel.AudioEngine.Pause(existingHandle);
                effect.IsPlaying = false;
                effect.IsPaused = true;
                ViewModel.StatusMessage = $"Tạm dừng hiệu ứng: {effect.Name}";
            }
            else
            {
                ViewModel.AudioEngine.Resume(existingHandle);
                effect.IsPlaying = true;
                effect.IsPaused = false;
                ViewModel.StatusMessage = $"Tiếp tục hiệu ứng: {effect.Name}";
            }
            RefreshSoundEffectsBoard();
            return;
        }

        await StartSoundEffectAsync(effect);
    }

    private async Task StartSoundEffectAsync(Models.KaraokeSoundEffect effect)
    {
        if (ViewModel is null || _loadingSoundEffects.Contains(effect.Id)) return;

        if (!System.IO.File.Exists(effect.FilePath))
        {
            ViewModel.StatusMessage = $"Không tìm thấy file hiệu ứng: {effect.Name}";
            MessageBox.Show(this, "File âm thanh không còn tồn tại. Hãy chuột phải và chọn Đổi file âm thanh.",
                "Thiếu file hiệu ứng", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!EnsureSoundEffectAudio()) return;
        _loadingSoundEffects.Add(effect.Id);
        _selectedSoundEffect = effect;
        ViewModel.StatusMessage = $"Đang nạp hiệu ứng: {effect.Name}";
        try
        {
            var handle = await ViewModel.AudioEngine.LoadAsync(effect.FilePath);
            // BASS stream handles are unsigned DWORDs represented by ManagedBass as int.
            // A valid handle can therefore be negative (for example 0x80000001).
            if (handle is 0 or -1)
            {
                ViewModel.StatusMessage = $"Không phát được hiệu ứng {effect.Name} · kiểm tra định dạng file hoặc thiết bị âm thanh";
                return;
            }

            if (!ViewModel.Settings.Current.KaraokeSoundEffects.Any(item => ReferenceEquals(item, effect)))
            {
                ViewModel.AudioEngine.Stop(handle);
                return;
            }

            _soundEffectHandles[effect.Id] = handle;
            _soundEffectPlaybackModels[effect.Id] = effect;
            effect.DurationSeconds = Math.Max(0, ViewModel.AudioEngine.GetDuration(handle));
            effect.IsPlaying = true;
            effect.IsPaused = false;
            ViewModel.AudioEngine.SetVolume(handle, GetEffectiveSoundEffectVolume(effect));
            ViewModel.AudioEngine.SetLoop(handle, effect.Loop);
            ViewModel.AudioEngine.Play(handle);
            RefreshSoundEffectsBoard();
            ViewModel.StatusMessage = $"Hiệu ứng → {effect.Name}";
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"Không phát được hiệu ứng {effect.Name}: {ex.Message}";
        }
        finally
        {
            _loadingSoundEffects.Remove(effect.Id);
            UpdateSoundEffectTransport();
        }
    }

    private bool EnsureSoundEffectAudio()
    {
        if (_soundEffectAudioInitialized) return true;
        if (ViewModel is null) return false;
        _soundEffectAudioInitialized = ViewModel.AudioEngine.Initialize(
            ViewModel.Settings.Current.AudioDeviceIndex)
            && ViewModel.AudioEngine.CurrentDeviceIndex != 0;
        if (!_soundEffectAudioInitialized)
        {
            ViewModel.StatusMessage = "Không khởi tạo được thiết bị âm thanh cho Soundboard";
            MessageBox.Show(this,
                "Không mở được thiết bị âm thanh. Hãy kiểm tra loa/mixer đang kết nối rồi thử lại.",
                "Soundboard", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        return _soundEffectAudioInitialized;
    }

    private void SoundEffectAudio_ChannelEnded(object? sender, int handle)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var pair = _soundEffectHandles.FirstOrDefault(item => item.Value == handle);
            if (string.IsNullOrEmpty(pair.Key)) return;
            if (_soundEffectPlaybackModels.TryGetValue(pair.Key, out var effect) && effect.Loop)
                return;
            StopSoundEffect(pair.Key);
        });
    }

    private void StopSoundEffect(string effectId, bool refresh = true)
    {
        if (_soundEffectHandles.Remove(effectId, out var handle) && ViewModel is not null)
        {
            try { ViewModel.AudioEngine.Stop(handle); } catch { /* best effort */ }
        }

        if (_soundEffectPlaybackModels.Remove(effectId, out var playbackModel))
        {
            playbackModel.IsPlaying = false;
            playbackModel.IsPaused = false;
        }

        var effect = ViewModel?.Settings.Current.KaraokeSoundEffects
            .FirstOrDefault(item => string.Equals(item.Id, effectId, StringComparison.OrdinalIgnoreCase));
        if (effect is not null)
        {
            effect.IsPlaying = false;
            effect.IsPaused = false;
        }
        if (refresh)
        {
            RefreshSoundEffectsBoard();
            UpdateSoundEffectTransport();
        }
    }

    private void StopAllSoundEffects(bool refresh = true)
    {
        foreach (var effectId in _soundEffectHandles.Keys.ToArray())
            StopSoundEffect(effectId, refresh: false);
        if (refresh)
        {
            RefreshSoundEffectsBoard();
            UpdateSoundEffectTransport();
        }
    }

    private void BtnStopAllSoundEffects_Click(object sender, RoutedEventArgs e)
    {
        StopAllSoundEffects();
        if (ViewModel is not null)
            ViewModel.StatusMessage = "Đã dừng toàn bộ hiệu ứng âm thanh";
    }

    private void UpdateSoundEffectTransport()
    {
        var effect = _selectedSoundEffect;
        if (effect is null)
        {
            SoundEffectTransportPanel.Visibility = Visibility.Collapsed;
            return;
        }

        SoundEffectTransportPanel.Visibility = Visibility.Visible;
        SoundEffectTransportTitle.Text = effect.Name;
        var duration = effect.DurationSeconds;
        var position = 0d;
        if (ViewModel is not null && _soundEffectHandles.TryGetValue(effect.Id, out var handle))
        {
            duration = Math.Max(duration, ViewModel.AudioEngine.GetDuration(handle));
            position = Math.Max(0, ViewModel.AudioEngine.GetPosition(handle));
            effect.DurationSeconds = duration;
        }

        if (!_soundEffectSeeking)
        {
            SoundEffectSeekSlider.Maximum = Math.Max(1, duration);
            SoundEffectSeekSlider.Value = Math.Clamp(position, 0, SoundEffectSeekSlider.Maximum);
        }
        SoundEffectTransportTime.Text = $"{FormatSoundEffectTime(position)} / {FormatSoundEffectTime(duration)}";
        _updatingSoundEffectVolumeUi = true;
        try
        {
            var effectVolume = double.IsFinite(effect.Volume) ? Math.Clamp(effect.Volume, 0, 1) : 1;
            SoundEffectVolumeSlider.Value = effectVolume;
            SoundEffectVolumeText.Text = $"{effectVolume:P0}";
        }
        finally
        {
            _updatingSoundEffectVolumeUi = false;
        }
        SoundEffectPlayPauseButton.Content = effect.IsPlaying ? "PAUSE" : "PLAY";
        SoundEffectLoopButton.Content = effect.Loop ? "LOOP: ON" : "LOOP: OFF";
        if (effect.Loop)
            SoundEffectLoopButton.Background = new SolidColorBrush(Color.FromRgb(11, 104, 69));
        else
            SoundEffectLoopButton.ClearValue(Button.BackgroundProperty);
    }

    private static string FormatSoundEffectTime(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) seconds = 0;
        var time = TimeSpan.FromSeconds(seconds);
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{(int)time.TotalMinutes}:{time.Seconds:00}";
    }

    private double GetEffectiveSoundEffectVolume(Models.KaraokeSoundEffect effect)
    {
        if (_karaokeMuted || ViewModel is null) return 0;
        var effectVolume = double.IsFinite(effect.Volume) ? Math.Clamp(effect.Volume, 0, 1) : 1;
        return Math.Clamp(ViewModel.MasterVolume, 0, 1) * effectVolume;
    }

    private void ApplyAllSoundEffectVolumes()
    {
        if (ViewModel is null) return;
        foreach (var (effectId, handle) in _soundEffectHandles)
        {
            if (_soundEffectPlaybackModels.TryGetValue(effectId, out var effect))
                ViewModel.AudioEngine.SetVolume(handle, GetEffectiveSoundEffectVolume(effect));
        }
    }

    private async void SoundEffectVolumeSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingSoundEffectVolumeUi || ViewModel is null || _selectedSoundEffect is not { } effect)
            return;

        effect.Volume = Math.Clamp(e.NewValue, 0, 1);
        SoundEffectVolumeText.Text = $"{effect.Volume:P0}";
        if (_soundEffectHandles.TryGetValue(effect.Id, out var handle))
            ViewModel.AudioEngine.SetVolume(handle, GetEffectiveSoundEffectVolume(effect));

        _soundEffectVolumeSaveCts?.Cancel();
        _soundEffectVolumeSaveCts?.Dispose();
        var saveCts = new CancellationTokenSource();
        _soundEffectVolumeSaveCts = saveCts;
        try
        {
            await Task.Delay(400, saveCts.Token);
            await ViewModel.Settings.SaveAsync();
        }
        catch (OperationCanceledException)
        {
            // A newer slider value will be saved instead.
        }
    }

    private void SoundEffectSeekSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        => _soundEffectSeeking = true;

    private void SoundEffectSeekSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is not null && _selectedSoundEffect is { } effect
            && _soundEffectHandles.TryGetValue(effect.Id, out var handle))
            ViewModel.AudioEngine.Seek(handle, SoundEffectSeekSlider.Value);
        _soundEffectSeeking = false;
        UpdateSoundEffectTransport();
    }

    private void SeekSelectedSoundEffect(double deltaSeconds)
    {
        if (ViewModel is null || _selectedSoundEffect is not { } effect
            || !_soundEffectHandles.TryGetValue(effect.Id, out var handle)) return;
        var duration = Math.Max(0, ViewModel.AudioEngine.GetDuration(handle));
        var position = ViewModel.AudioEngine.GetPosition(handle);
        ViewModel.AudioEngine.Seek(handle, Math.Clamp(position + deltaSeconds, 0, duration));
        UpdateSoundEffectTransport();
    }

    private void BtnSoundEffectBack10_Click(object sender, RoutedEventArgs e)
        => SeekSelectedSoundEffect(-10);

    private void BtnSoundEffectForward10_Click(object sender, RoutedEventArgs e)
        => SeekSelectedSoundEffect(10);

    private async void BtnSoundEffectPlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || _selectedSoundEffect is not { } effect) return;
        if (!_soundEffectHandles.TryGetValue(effect.Id, out var handle))
        {
            await StartSoundEffectAsync(effect);
            return;
        }
        if (ViewModel.AudioEngine.IsPlaying(handle))
        {
            ViewModel.AudioEngine.Pause(handle);
            effect.IsPlaying = false;
            effect.IsPaused = true;
        }
        else
        {
            ViewModel.AudioEngine.Resume(handle);
            effect.IsPlaying = true;
            effect.IsPaused = false;
        }
        RefreshSoundEffectsBoard();
    }

    private void BtnSoundEffectStop_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedSoundEffect is { } effect)
            StopSoundEffect(effect.Id);
    }

    private async void BtnSoundEffectLoop_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || _selectedSoundEffect is not { } effect) return;
        effect.Loop = !effect.Loop;
        if (_soundEffectHandles.TryGetValue(effect.Id, out var handle))
            ViewModel.AudioEngine.SetLoop(handle, effect.Loop);
        UpdateSoundEffectTransport();
        await SaveSoundEffectsAsync();
    }

    private async void SoundEffectRename_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || (sender as FrameworkElement)?.DataContext is not Models.KaraokeSoundEffect effect)
            return;
        var dialog = new Controls.Dialogs.InputDialog(
            "Đổi tên hiệu ứng", "Tên mới của nút hiệu ứng:", effect.Name) { Owner = this };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.Result)) return;
        effect.Name = dialog.Result.Trim();
        await SaveSoundEffectsAsync();
    }

    private async void SoundEffectSetHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null
            || (sender as FrameworkElement)?.DataContext is not Models.KaraokeSoundEffect effect)
            return;

        var dialog = new Controls.Dialogs.HotkeyDialog(effect.HotkeyText, effect.Name) { Owner = this };
        bool? accepted;
        ViewModel.HotkeyService.IsSuspended = true;
        try
        {
            accepted = dialog.ShowDialog();
        }
        finally
        {
            ViewModel.HotkeyService.IsSuspended = false;
        }

        if (accepted != true) return;
        var requested = dialog.Result.Trim();
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var conflict = GetSoundEffectHotkeyConflict(effect, requested);
            if (conflict is not null)
            {
                MessageBox.Show(this, conflict, "Trùng hotkey",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        effect.HotkeyText = requested;
        await SaveSoundEffectsAsync();
        ViewModel.StatusMessage = string.IsNullOrWhiteSpace(requested)
            ? $"Đã xóa hotkey của hiệu ứng: {effect.Name}"
            : $"Hotkey {requested} → {effect.Name}";
    }

    private async void SoundEffectClearHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null
            || (sender as FrameworkElement)?.DataContext is not Models.KaraokeSoundEffect effect)
            return;
        effect.HotkeyText = string.Empty;
        await SaveSoundEffectsAsync();
        ViewModel.StatusMessage = $"Đã xóa hotkey của hiệu ứng: {effect.Name}";
    }

    private string? GetSoundEffectHotkeyConflict(Models.KaraokeSoundEffect effect, string requested)
    {
        if (ViewModel is null
            || !Helpers.HotkeyUtil.TryParse(requested, out var key, out var modifiers))
            return "Tổ hợp phím không hợp lệ.";

        var configured = ViewModel.Settings.Current.Hotkeys.FirstOrDefault(pair =>
            !string.IsNullOrWhiteSpace(pair.Value)
            && string.Equals(pair.Value, requested, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(configured.Key))
        {
            var actionName = Models.HotkeyActions.Catalog
                .FirstOrDefault(item => string.Equals(item.Id, configured.Key, StringComparison.OrdinalIgnoreCase)).Name;
            return $"Phím '{requested}' đã được dùng cho: {actionName ?? configured.Key}.";
        }

        Guid.TryParse(effect.Id, out var effectId);
        var registration = ViewModel.HotkeyService.GetAll().FirstOrDefault(item =>
            item.CueId != effectId
            && item.Key == key
            && item.Modifiers == modifiers);
        if (registration is not null)
        {
            var otherEffect = ViewModel.Settings.Current.KaraokeSoundEffects.FirstOrDefault(item =>
                Guid.TryParse(item.Id, out var id) && id == registration.CueId);
            return otherEffect is not null
                ? $"Phím '{requested}' đã gán cho hiệu ứng: {otherEffect.Name}."
                : $"Phím '{requested}' đã gán cho một cue khác.";
        }

        return null;
    }

    private async void SoundEffectChangeFile_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || (sender as FrameworkElement)?.DataContext is not Models.KaraokeSoundEffect effect)
            return;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = $"Đổi file cho hiệu ứng: {effect.Name}",
            Filter = "File âm thanh|*.mp3;*.wav;*.m4a;*.aac;*.wma;*.flac;*.ogg;*.opus|Tất cả file|*.*",
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;
        StopSoundEffect(effect.Id, refresh: false);
        effect.FilePath = dialog.FileName;
        effect.DurationSeconds = 0;
        await SaveSoundEffectsAsync();
    }

    private async void SoundEffectDelete_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || (sender as FrameworkElement)?.DataContext is not Models.KaraokeSoundEffect effect)
            return;
        var confirm = MessageBox.Show(this, $"Xóa hiệu ứng ‘{effect.Name}’?", "Xóa hiệu ứng",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;
        StopSoundEffect(effect.Id, refresh: false);
        ViewModel.Settings.Current.KaraokeSoundEffects.Remove(effect);
        await SaveSoundEffectsAsync();
    }

    private async Task SaveSoundEffectsAsync()
    {
        if (ViewModel is null) return;
        try
        {
            await ViewModel.Settings.SaveAsync();
            RefreshSoundEffectsBoard();
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"Không lưu được Soundboard: {ex.Message}";
        }
    }

}
