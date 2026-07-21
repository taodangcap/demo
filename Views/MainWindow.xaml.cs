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

namespace ShowCuePlayer;

/// <summary>
/// Main application window. All logic is in MainViewModel.
/// Code-behind only handles window chrome and drag-drop passthrough.
/// </summary>
public partial class MainWindow : Window
{
    public MainViewModel? ViewModel => DataContext as MainViewModel;
    private Views.VideoWindow? _karaokeSecondaryWindow;
    private Views.VideoWindow? _previewSourceWindow;
    /// <summary>Mirror panel Preview (view) từ HWND OUTPUT — mượt hơn WebView CapturePreview.</summary>
    private readonly WindowHwndCaptureService _windowMirror = new();
    private CancellationTokenSource? _karaokeCaptureCts;
    private CancellationTokenSource? _mixerProgramCaptureCts;

    /// <summary>
    /// WindowStyle=None + WindowState.Maximized sẽ đè cả taskbar (kể cả taskbar dọc).
    /// Dùng maximize thủ công vào WorkingArea.
    /// </summary>
    private bool _isWorkAreaMaximized;
    private Rect _restoreBounds;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        AllowDrop = true;

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

        Drop += OnDrop;
        DragOver += OnDragOver;
        PreviewKeyDown += OnPreviewKeyDown;
        SizeChanged += MainWindow_SizeChanged;
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
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            ViewModel.FilteredCues.CollectionChanged += (_, _) => Dispatcher.BeginInvoke(UpdateCueCardSizing);
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
            or nameof(MainViewModel.IsVideoOutputEnabled))
            Dispatcher.BeginInvoke(RefreshActiveOutputPreview);

        if (e.PropertyName == nameof(MainViewModel.MixerPreviewInput))
            Dispatcher.BeginInvoke(UpdateMixerPreview);

        if (e.PropertyName == nameof(MainViewModel.ActiveWorkspace))
        {
            Dispatcher.BeginInvoke(ApplyResponsiveLayout);
            Dispatcher.BeginInvoke(UpdateMixerPreview);
            Dispatcher.BeginInvoke(RefreshActiveOutputPreview);
        }


        if (e.PropertyName is nameof(MainViewModel.KaraokeBusVolume)
            or nameof(MainViewModel.MasterVolume))
        {
            var volume = (ViewModel?.KaraokeBusVolume ?? 1) * (ViewModel?.MasterVolume ?? 1);
            ViewModel?.VideoPlayer.OutputWindow?.SetKaraokeVolume(volume);
        }
    }

    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyResponsiveLayout();
        if (MixerProgramDwmHost.Visibility == Visibility.Visible)
            MixerProgramDwmHost.UpdateThumbnailProperties();
        else if (KaraokeDwmHost.Visibility == Visibility.Visible)
            KaraokeDwmHost.UpdateThumbnailProperties();
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

            await KaraokeControlWebView.EnsureCoreWebView2Async(null);
            await HookWebViewEscBridgeAsync(KaraokeControlWebView);
            await ApplyKaraokeUrlsToWebViewsAsync(navigateRemote: true, navigatePlayer: false);

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
        try { KaraokeControlWebView.CoreWebView2?.PostWebMessageAsJson(message); } catch { /* ignore */ }
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
                if (KaraokeControlWebView.CoreWebView2 != null)
                    KaraokeControlWebView.CoreWebView2.Navigate(remote.ToString());
                else
                    KaraokeControlWebView.Source = remote;
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
        MixerPreviewVideo.Stop();
        MixerPreviewVideo.Source = null;
        StopMixerProgramPreview();
        StopKaraokeOutputCapture();
        _windowMirror.Dispose();
        _karaokeSecondaryWindow?.Close();
        KaraokeControlWebView.Dispose();
    }

    private void OnVideoWindowCreated(object? sender, System.EventArgs e)
    {
        if (ViewModel?.VideoPlayer.OutputWindow is { } window)
        {
            window.KaraokeProcessFailed -= OnKaraokeProcessFailed;
            window.KaraokeProcessFailed += OnKaraokeProcessFailed;
        }
        Dispatcher.Invoke(RefreshActiveOutputPreview);
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

            if (ViewModel?.IsMixerWorkspace != true || ViewModel.MixerPreviewInput is not { } input)
                return;
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
        }
    }

    private void RefreshActiveOutputPreview()
    {
        if (ViewModel?.IsMixerWorkspace == true)
        {
            StopKaraokeOutputCapture();
            RefreshMixerProgramPreview();
            return;
        }

        StopMixerProgramPreview();
        RefreshLocalOutputPreview();
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

    private void RefreshLocalOutputPreview()
    {
        var outputWindow = ViewModel?.VideoPlayer.OutputWindow;
        if (ViewModel?.IsShowWorkspace != true
            || ViewModel.IsLivePreviewVisible != true
            || ViewModel.VideoPlayer.IsOutputArmed != true
            || outputWindow?.IsLoaded != true)
        {
            StopKaraokeOutputCapture();
            PreviewPlaceholder.Visibility = Visibility.Visible;
            return;
        }

        StartOutputWindowCapture(outputWindow);
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

        // 1–9 = play cue #n (cố định, không cấu hình)
        if (mods == ModifierKeys.None && key is >= Key.D1 and <= Key.D9)
        {
            int n = key - Key.D1 + 1;
            ViewModel.PlayCueByNumberCommand.Execute(n);
            e.Handled = true;
            return;
        }
        if (mods == ModifierKeys.None && key is >= Key.NumPad1 and <= Key.NumPad9)
        {
            int n = key - Key.NumPad1 + 1;
            ViewModel.PlayCueByNumberCommand.Execute(n);
            e.Handled = true;
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
        if (Hit(Models.HotkeyActions.Go))
        {
            ViewModel.GoCommand.Execute(null);
            return true;
        }
        if (Hit(Models.HotkeyActions.GoNext))
        {
            ViewModel.GoNextCommand.Execute(null);
            return true;
        }
        if (Hit(Models.HotkeyActions.GoPrevious))
        {
            ViewModel.GoPreviousCommand.Execute(null);
            return true;
        }
        if (Hit(Models.HotkeyActions.ToggleLiveMode))
        {
            ViewModel.ToggleLiveModeCommand.Execute(null);
            return true;
        }
        if (Hit(Models.HotkeyActions.ToggleLiveLock))
        {
            ViewModel.ToggleLiveLockCommand.Execute(null);
            return true;
        }
        if (Hit(Models.HotkeyActions.ToggleVideoOutput))
        {
            ViewModel.ToggleVideoOutputCommand.Execute(null);
            return true;
        }
        if (Hit(Models.HotkeyActions.ToggleLedBlackout))
        {
            ViewModel.ToggleLedBlackoutCommand.Execute(null);
            return true;
        }
        if (Hit(Models.HotkeyActions.ShowEmergencySafeScene))
        {
            ViewModel.ShowEmergencySafeSceneCommand.Execute(null);
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
        CloseKaraokeSecondaryIfOpen();
        e.Handled = true;
    }

    private void CloseKaraokeSecondaryIfOpen()
    {
        bool refreshShowPreview = ViewModel?.IsShowWorkspace == true;
        if (refreshShowPreview)
            StopKaraokeOutputCapture();

        // 2) Pause (an toàn nếu webview còn)
        try
        {
            PostToKaraokePlayer("{\"type\":\"karaoke\",\"action\":\"pause\"}", masterOnly: true);
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
    }

    /// <summary>
    /// Mirror the Program output HWND for every source: standby, local media, and Karaoke.
    /// DWM is preferred; PrintWindow capture is the compatibility fallback.
    /// </summary>
    private void StartKaraokeOutputCapture()
    {
        if (ViewModel?.IsMixerWorkspace == true)
            RefreshActiveOutputPreview();
        else
            StartOutputWindowCapture(_karaokeSecondaryWindow);
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
    }

    private static bool IsTypingInTextBox()
    {
        return Keyboard.FocusedElement is TextBox or System.Windows.Controls.PasswordBox
            or System.Windows.Controls.RichTextBox;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && ViewModel is not null)
            await ViewModel.HandleDropAsync(paths);
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



    private async Task NavigateKaraokePlayerFromVmAsync()
    {
        if (ViewModel is null) return;
        if (ViewModel.VideoPlayer.IsFrozen)
        {
            ViewModel.StatusMessage = "OUTPUT FROZEN · hãy UNFREEZE trước khi điều hướng Karaoke";
            return;
        }

        // Chỉ navigate OUTPUT master — preview = capture (không WebView 2)
        if (Uri.TryCreate(ViewModel.KaraokePlayerUrl, UriKind.Absolute, out var master))
        {
            ViewModel.CancelMixerTakeForExternalProgramChange();
            _karaokeSecondaryWindow?.SetKaraokeVolume(ViewModel.MasterVolume * ViewModel.KaraokeBusVolume);
            _karaokeSecondaryWindow?.SetKaraokeAutoNext(ViewModel.KaraokeAutoNextEnabled);
            var patternToClear = ViewModel.VideoPlayer.ActiveTestPattern;
            var patternVersionToClear = ViewModel.VideoPlayer.TestPatternVersion;
            var safeSceneVersionToClear = ViewModel.VideoPlayer.SafeSceneVersion;
            if (_karaokeSecondaryWindow is not null &&
                await _karaokeSecondaryWindow.NavigateAsync(master.ToString()))
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
            else
                ViewModel.StatusMessage = "Karaoke navigation failed hoặc bị chặn · test pattern vẫn được giữ";
        }
    }

    private void BtnKaraokeToggleSecondary_Click(object sender, RoutedEventArgs e)
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
        if (ViewModel?.VideoPlayer.IsFrozen == true)
        {
            ViewModel.StatusMessage = "OUTPUT FROZEN · hãy UNFREEZE trước khi bật Karaoke";
            return;
        }
        if (ViewModel?.IsLiveLocked == true && string.IsNullOrWhiteSpace(ViewModel.KaraokeSessionId))
        {
            ViewModel.StatusMessage = "SHOW LOCK · cần tạo Karaoke session trước khi khóa";
            return;
        }

        // Session + URL, KHÔNG navigate remote trước player
        ViewModel?.PrepareKaraokeSessionSilent();

        if (ViewModel is null) return;
        // Karaoke is exclusive only with local video; audio and images keep their state.
        ViewModel.StopLocalVideosForKaraoke();
        ViewModel.VideoPlayer.EnsureOutputArmed();

        // Dọn cửa sổ “zombie” (Closed nhưng ref còn)
        _karaokeSecondaryWindow = ViewModel.VideoPlayer.OutputWindow;
        if (_karaokeSecondaryWindow is null) return;

        if (!_karaokeSecondaryWindow.IsVisible)
            _karaokeSecondaryWindow.Show();
        Activate();

        // 1) Master OUTPUT (1 WebView)
        _ = NavigateKaraokePlayerFromVmAsync();

        // 2) Preview = DWM/capture OUTPUT
        StartKaraokeOutputCapture();

        // 3) Remote
        if (ViewModel is not null &&
            Uri.TryCreate(ViewModel.KaraokeRemoteUrl, UriKind.Absolute, out var remote))
        {
            _karaokeRemoteNavCts?.Cancel();
            var cts = new CancellationTokenSource();
            _karaokeRemoteNavCts = cts;
            _ = NavigateRemoteAfterDelayAsync(remote.ToString(), 800, cts.Token);
        }

        ViewModel?.SetKaraokeOutputOn(true);
        if (ViewModel is not null)
        {
            ViewModel.StatusMessage = $"Karaoke LIVE · session {ViewModel.KaraokeSessionId}";
        }
    }

    /// <summary>
    /// User đóng cửa sổ OUTPUT (X / taskbar) — sync state OFF, dừng capture.
    /// CloseKaraokeSecondaryIfOpen() cũng gọi Close() → handler idempotent.
    /// </summary>
    private void KaraokeSecondaryWindow_ClosedByUser(object? sender, EventArgs e)
    {
        // Marshal về UI thread (Closed đôi khi từ teardown WebView)
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => KaraokeSecondaryWindow_ClosedByUser(sender, e));
            return;
        }

        // Đã dọn bởi CloseKaraokeSecondaryIfOpen (ref = null) — chỉ bảo đảm OFF
        if (_karaokeSecondaryWindow is null)
        {
            if (ViewModel?.IsKaraokeOutputOn == true)
                ViewModel.SetKaraokeOutputOn(false);
            return;
        }

        // Cửa sổ đang đóng là instance hiện tại (hoặc ref zombie)
        if (ReferenceEquals(_karaokeSecondaryWindow, sender) || !_karaokeSecondaryWindow.IsLoaded)
            CloseKaraokeSecondaryIfOpen();
    }

    private void OpenWindowedProjector(ProjectorSource source)
    {
        var projector = new ProjectorWindow(source);
        projector.Show();
        projector.Activate();
    }

    private void OpenFullscreenProjector(ProjectorSource source, System.Windows.Forms.Screen screen)
    {
        var projector = new ProjectorWindow(source);
        projector.Show();
        projector.GoFullscreen(screen);
    }

    private void ProgramContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || sender is not ContextMenu menu) return;

        menu.Items.Clear();

        // 1) Mở khung chiếu chính (Master Output)
        var masterOutputMenu = new MenuItem
        {
            Header = "Mở khung chiếu chính (Master Output)",
            Icon = new FontAwesome.Sharp.IconBlock { Icon = FontAwesome.Sharp.IconChar.Desktop, FontSize = 13 }
        };
        foreach (var screen in ViewModel.VideoScreens)
        {
            var item = new MenuItem
            {
                Header = screen.DisplayName,
                Command = ViewModel.SelectScreenAndStartOutputCommand,
                CommandParameter = screen,
                Icon = new FontAwesome.Sharp.IconBlock { Icon = FontAwesome.Sharp.IconChar.Display, FontSize = 13 }
            };
            if (screen.IsPrimary)
            {
                item.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xAA, 0x44));
            }
            masterOutputMenu.Items.Add(item);
        }
        masterOutputMenu.Items.Add(new Separator());
        var windowedItem = new MenuItem
        {
            Header = "Cửa sổ mới (Windowed)",
            Command = ViewModel.StartWindowedOutputCommand,
            Icon = new FontAwesome.Sharp.IconBlock { Icon = FontAwesome.Sharp.IconChar.WindowMaximize, FontSize = 13 }
        };
        masterOutputMenu.Items.Add(windowedItem);
        menu.Items.Add(masterOutputMenu);

        menu.Items.Add(new Separator());

        // 2) OBS-style Projectors
        // 2a) Khung chiếu cửa sổ (Chương trình)
        var projWindowedProgram = new MenuItem
        {
            Header = "Khung chiếu cửa sổ (Chương trình)",
            Icon = new FontAwesome.Sharp.IconBlock { Icon = FontAwesome.Sharp.IconChar.WindowRestore, FontSize = 13 }
        };
        projWindowedProgram.Click += (_, _) => OpenWindowedProjector(ProjectorSource.Program);
        menu.Items.Add(projWindowedProgram);

        // 2b) Khung chiếu cửa sổ (Trước xem)
        var projWindowedPreview = new MenuItem
        {
            Header = "Khung chiếu cửa sổ (Trước xem)",
            Icon = new FontAwesome.Sharp.IconBlock { Icon = FontAwesome.Sharp.IconChar.WindowRestore, FontSize = 13 }
        };
        projWindowedPreview.Click += (_, _) => OpenWindowedProjector(ProjectorSource.Preview);
        menu.Items.Add(projWindowedPreview);

        // 2c) Khung chiếu toàn màn hình (Chương trình)
        var projFullscreenProgram = new MenuItem
        {
            Header = "Khung chiếu toàn màn hình (Chương trình)",
            Icon = new FontAwesome.Sharp.IconBlock { Icon = FontAwesome.Sharp.IconChar.Expand, FontSize = 13 }
        };
        int scrIndex = 1;
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            var capture = screen;
            var item = new MenuItem
            {
                Header = $"Display {scrIndex}{(screen.Primary ? " (Chính)" : "")} ({screen.Bounds.Width}×{screen.Bounds.Height})"
            };
            item.Click += (_, _) => OpenFullscreenProjector(ProjectorSource.Program, capture);
            projFullscreenProgram.Items.Add(item);
            scrIndex++;
        }
        menu.Items.Add(projFullscreenProgram);

        // 2d) Khung chiếu toàn màn hình (Trước xem)
        var projFullscreenPreview = new MenuItem
        {
            Header = "Khung chiếu toàn màn hình (Trước xem)",
            Icon = new FontAwesome.Sharp.IconBlock { Icon = FontAwesome.Sharp.IconChar.Expand, FontSize = 13 }
        };
        scrIndex = 1;
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            var capture = screen;
            var item = new MenuItem
            {
                Header = $"Display {scrIndex}{(screen.Primary ? " (Chính)" : "")} ({screen.Bounds.Width}×{screen.Bounds.Height})"
            };
            item.Click += (_, _) => OpenFullscreenProjector(ProjectorSource.Preview, capture);
            projFullscreenPreview.Items.Add(item);
            scrIndex++;
        }
        menu.Items.Add(projFullscreenPreview);

        menu.Items.Add(new Separator());

        // 3) Chụp màn hình
        var captureItem = new MenuItem
        {
            Header = "Chụp màn hình (Chương trình)",
            Command = ViewModel.CaptureOutputScreenshotCommand,
            Icon = new FontAwesome.Sharp.IconBlock { Icon = FontAwesome.Sharp.IconChar.Camera, FontSize = 13 }
        };
        menu.Items.Add(captureItem);

        menu.Items.Add(new Separator());

        // 4) Tắt Output
        var disableOutputItem = new MenuItem
        {
            Header = "[X]  Tắt Output",
            Command = ViewModel.DisableOutputCommand,
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x52, 0x52)),
            Icon = new FontAwesome.Sharp.IconBlock { Icon = FontAwesome.Sharp.IconChar.Times, FontSize = 13 }
        };
        menu.Items.Add(disableOutputItem);
    }

    /// <summary>
    /// Test 1 màn: OUTPUT karaoke = cửa sổ windowed trên cùng màn (không fullscreen che app).
    /// </summary>
    private static void PlaceKaraokeSingleScreenTestWindow(Views.KaraokeSecondaryWindow w)
    {
        w.ShowActivated = false;
        w.ShowInTaskbar = true;
        w.Topmost = true;
        w.WindowStyle = WindowStyle.SingleBorderWindow;
        w.ResizeMode = ResizeMode.CanResize;
        w.WindowState = WindowState.Normal;
        w.Width = 960;
        w.Height = 540;
        w.Title = "ShowCue · Karaoke OUTPUT (TEST 1 màn)";

        var work = SystemParameters.WorkArea;
        // Góc dưới-phải — app control vẫn nhìn được
        w.Left = Math.Max(work.Left, work.Right - w.Width - 16);
        w.Top = Math.Max(work.Top, work.Bottom - w.Height - 16);
    }
}
