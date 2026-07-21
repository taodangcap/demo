using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ShowCuePlayer.Services;

namespace ShowCuePlayer.Views;



public partial class ProjectorWindow : Window
{
    private ProjectorSource _currentSource = ProjectorSource.Program;
    private bool _isFullscreen;
    private double _savedLeft;
    private double _savedTop;
    private double _savedWidth;
    private double _savedHeight;
    private WindowStyle _savedStyle;
    private ResizeMode _savedResizeMode;
    private WindowState _savedState;
    private bool _savedTopmost;

    public ProjectorSource CurrentSource => _currentSource;

    public ProjectorWindow(ProjectorSource initialSource = ProjectorSource.Program)
    {
        InitializeComponent();
        _currentSource = initialSource;
        
        Loaded += ProjectorWindow_Loaded;
        Closed += ProjectorWindow_Closed;
        
        // Register to the global list of active projectors
        VideoPlayerService.ActiveProjectors.Add(this);

        var mainVm = Application.Current.MainWindow?.DataContext as ViewModels.MainViewModel;
        if (mainVm != null)
        {
            mainVm.VideoPlayer.WindowCreated += VideoPlayer_WindowCreated;
            mainVm.VideoPlayer.OutputStateChanged += VideoPlayer_OutputStateChanged;
        }
    }

    private void ProjectorWindow_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateSource();
        UpdateTitle();
    }

    private void VideoPlayer_WindowCreated(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(UpdateSource));
    }

    private void VideoPlayer_OutputStateChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(UpdateSource));
    }

    private void ProjectorWindow_Closed(object? sender, EventArgs e)
    {
        // Unsubscribe
        var mainVm = Application.Current.MainWindow?.DataContext as ViewModels.MainViewModel;
        if (mainVm != null)
        {
            mainVm.VideoPlayer.WindowCreated -= VideoPlayer_WindowCreated;
            mainVm.VideoPlayer.OutputStateChanged -= VideoPlayer_OutputStateChanged;
        }

        // Clean up
        try { MirrorHost.ClearSource(); } catch { }
        VideoPlayerService.ActiveProjectors.Remove(this);
        
        // If there are no more active projectors and the main output is disabled, disarm VideoWindow
        if (mainVm != null && !mainVm.IsVideoOutputEnabled && VideoPlayerService.ActiveProjectors.Count == 0)
        {
            mainVm.VideoPlayer.SetOutputEnabled(false);
        }
    }

    public void SetSource(ProjectorSource source)
    {
        if (_currentSource == source) return;
        _currentSource = source;
        UpdateSource();
        UpdateTitle();
    }

    private void UpdateTitle()
    {
        Title = _currentSource == ProjectorSource.Program 
            ? "Khung chiếu - Chương trình" 
            : "Khung chiếu - Trước xem";
    }

    public void UpdateSource()
    {
        try { MirrorHost.ClearSource(); } catch { }
        PreviewMirrorRect.Visibility = Visibility.Collapsed;
        MirrorHost.Visibility = Visibility.Collapsed;
        PlaceholderText.Visibility = Visibility.Collapsed;

        var mainWin = Application.Current.MainWindow as MainWindow;
        var mainVm = mainWin?.DataContext as ViewModels.MainViewModel;

        if (_currentSource == ProjectorSource.Program)
        {
            // PROGRAM SOURCE
            if (mainVm != null)
            {
                // Ensure output window is created (even if off-screen)
                if (mainVm.VideoPlayer.OutputWindow == null)
                {
                    // Trigger creation offscreen
                    mainVm.VideoPlayer.SetOutputEnabled(true);
                    if (!mainVm.IsVideoOutputEnabled && mainVm.VideoPlayer.OutputWindow is { } win)
                    {
                        VideoPlayerService.PlaceOffScreen(win);
                    }
                }

                var outputWindow = mainVm.VideoPlayer.OutputWindow;
                if (outputWindow != null && outputWindow.IsLoaded)
                {
                    var hwnd = new WindowInteropHelper(outputWindow).EnsureHandle();
                    if (hwnd != IntPtr.Zero)
                    {
                        MirrorHost.Visibility = Visibility.Visible;
                        MirrorHost.SetSource(hwnd);
                        MirrorHost.UpdateThumbnailProperties();
                    }
                    else
                    {
                        PlaceholderText.Text = "STAND BY";
                        PlaceholderText.Visibility = Visibility.Visible;
                    }
                }
                else
                {
                    PlaceholderText.Text = "STAND BY";
                    PlaceholderText.Visibility = Visibility.Visible;
                }
            }
            else
            {
                PlaceholderText.Text = "STAND BY";
                PlaceholderText.Visibility = Visibility.Visible;
            }
        }
        else
        {
            // PREVIEW SOURCE (Mixer Mode Preview Grid)
            if (mainWin != null && mainWin.MixerPreviewGrid != null)
            {
                PreviewMirrorRect.Visibility = Visibility.Visible;
                PreviewMirrorRect.Fill = new VisualBrush(mainWin.MixerPreviewGrid)
                {
                    Stretch = Stretch.Uniform,
                    TileMode = TileMode.None,
                    AlignmentX = AlignmentX.Center,
                    AlignmentY = AlignmentY.Center
                };
            }
            else
            {
                PlaceholderText.Text = "NO PREVIEW CONTENT";
                PlaceholderText.Visibility = Visibility.Visible;
            }
        }
    }

    private void OnRightClick(object sender, MouseButtonEventArgs e)
    {
        var menu = new ContextMenu();

        // 1) Toàn màn hình (Submenu)
        var fullscreenMenu = new MenuItem { Header = "Toàn màn hình" };
        int index = 1;
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            var capture = screen;
            var n = index;
            var item = new MenuItem
            {
                Header = $"Display {n}{(screen.Primary ? " (Chính)" : "")} ({screen.Bounds.Width}×{screen.Bounds.Height})"
            };
            item.Click += (_, _) => GoFullscreen(capture);
            fullscreenMenu.Items.Add(item);
            index++;
        }
        menu.Items.Add(fullscreenMenu);

        // 2) Chọn nguồn (Submenu)
        var sourceMenu = new MenuItem { Header = "Chọn nguồn" };
        
        var programItem = new MenuItem { Header = "Chương trình (Program)", IsCheckable = true, IsChecked = _currentSource == ProjectorSource.Program };
        programItem.Click += (_, _) => SetSource(ProjectorSource.Program);
        sourceMenu.Items.Add(programItem);

        var previewItem = new MenuItem { Header = "Trước xem (Preview)", IsCheckable = true, IsChecked = _currentSource == ProjectorSource.Preview };
        previewItem.Click += (_, _) => SetSource(ProjectorSource.Preview);
        sourceMenu.Items.Add(previewItem);

        menu.Items.Add(sourceMenu);

        menu.Items.Add(new Separator());

        // 3) Chỉnh cửa sổ khít với nội dung
        var fitItem = new MenuItem { Header = "Chỉnh cửa sổ khít với nội dung" };
        fitItem.Click += (_, _) => FitToContent();
        menu.Items.Add(fitItem);

        // 4) Luôn hiện trên cùng
        var topmostItem = new MenuItem
        {
            Header = "Luôn hiện trên cùng",
            IsCheckable = true,
            IsChecked = Topmost
        };
        topmostItem.Click += (_, _) =>
        {
            Topmost = !Topmost;
        };
        menu.Items.Add(topmostItem);

        menu.Items.Add(new Separator());

        // 5) Đóng
        var closeItem = new MenuItem { Header = "Đóng" };
        closeItem.Click += (_, _) => Close();
        menu.Items.Add(closeItem);

        menu.IsOpen = true;
    }

    internal void GoFullscreen(System.Windows.Forms.Screen screen)
    {
        if (!_isFullscreen)
        {
            // Save state for restoring later
            _savedLeft = Left;
            _savedTop = Top;
            _savedWidth = Width;
            _savedHeight = Height;
            _savedStyle = WindowStyle;
            _savedResizeMode = ResizeMode;
            _savedState = WindowState;
            _savedTopmost = Topmost;
        }

        _isFullscreen = true;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        WindowState = WindowState.Normal;

        // Position on target screen bounds
        var bounds = screen.Bounds;
        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;
        
        // Maximize to cover taskbar properly
        WindowState = WindowState.Maximized;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _isFullscreen)
        {
            ExitFullscreen();
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    private void ExitFullscreen()
    {
        if (!_isFullscreen) return;
        _isFullscreen = false;

        WindowStyle = _savedStyle;
        ResizeMode = _savedResizeMode;
        Topmost = _savedTopmost;
        WindowState = _savedState;
        
        Left = _savedLeft;
        Top = _savedTop;
        Width = _savedWidth;
        Height = _savedHeight;
    }

    private void FitToContent()
    {
        if (_isFullscreen) ExitFullscreen();

        // 16:9 standard aspect ratio
        Width = 960;
        Height = 540;
        WindowState = WindowState.Normal;

        // Center on primary screen working area
        var area = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + (area.Height - Height) / 2;
    }
}
