using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ShowCuePlayer.AudioEngine;
using ShowCuePlayer.Database.Repositories;
using ShowCuePlayer.Models;
using ShowCuePlayer.Helpers;
using ShowCuePlayer.Services;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Windows;
using Microsoft.Win32;

namespace ShowCuePlayer.ViewModels;

/// <summary>
/// Root ViewModel for the main window.
/// Manages the cue card grid, toolbar commands, search, drag-drop,
/// master volume, device selection, and auto-save.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IServiceProvider _services;
    private readonly IAudioEngine _audio;
    private readonly IImportService _import;
    private readonly IProjectService _project;
    private readonly ISearchService _search;
    private readonly IHotkeyService _hotkeys;
    private readonly IVideoPlayerService _videoPlayer;
    private readonly IPreloadManager _preload;
    private readonly ICrossfadeEngine _crossfade;
    private readonly System.Windows.Threading.DispatcherTimer _autoSaveTimer;
    private readonly System.Windows.Threading.DispatcherTimer _nowNextTimer;
    private readonly SemaphoreSlim _importGate = new(1, 1);
    private readonly SemaphoreSlim _mixerTakeGate = new(1, 1);
    private long _mixerTakeVersion;
    private Guid? _mixerTakeTargetId;
    private Guid? _mixerStartingCueId;
    private CueCardViewModel? _mixerSeekingProgram;
    private CueCardViewModel? _mixerVolumeProgram;
    private CancellationTokenSource? _importCts;
    private bool _transitionBusy;
    private CueCardViewModel? _lastPlayedCue;
    private bool _loadingLedSettings;
    private bool _reloadingVideoScreens;

    public ISettingsService Settings { get; }
    public IVideoPlayerService VideoPlayer => _videoPlayer;
    public IAudioEngine AudioEngine => _audio;
    public IHotkeyService HotkeyService => _hotkeys;

    // ─── Playlists ────────────────────────────────────────────────
    public ObservableCollection<PlaylistViewModel> Playlists { get; } = new ObservableCollection<PlaylistViewModel>();
    public ObservableCollection<CueCardViewModel> FilteredCues { get; } = new ObservableCollection<CueCardViewModel>();
    public ObservableCollection<string> SearchSuggestions { get; } = new();

    // ─── Devices ──────────────────────────────────────────────────
    public ObservableCollection<AudioDeviceInfo> OutputDevices { get; } = new ObservableCollection<AudioDeviceInfo>();
    public ObservableCollection<VideoScreenInfo> VideoScreens { get; } = new ObservableCollection<VideoScreenInfo>();
    public ObservableCollection<LedOutputProfile> LedProfiles { get; } = new();
    public ObservableCollection<LedTestPattern> LedTestPatterns { get; } = new(Enum.GetValues<LedTestPattern>());
    public ObservableCollection<CueCardViewModel> MixerInputs { get; } = new();
    public IReadOnlyList<int> MixerTransitionDurations { get; } = new[] { 100, 250, 500, 1000 };
    public IReadOnlyList<LedScalingMode> LedScalingModes { get; } = Enum.GetValues<LedScalingMode>();

    // ─── Observable State ─────────────────────────────────────────
    [ObservableProperty] private PlaylistViewModel? _selectedPlaylist;
    [ObservableProperty] private CueCardViewModel? _selectedCue;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _showSearchSuggestions;
    [ObservableProperty] private double _masterVolume = 1.0;
    [ObservableProperty] private double _audioBusVolume = 1.0;
    [ObservableProperty] private double _videoBusVolume = 1.0;
    [ObservableProperty] private double _karaokeBusVolume = 1.0;
    [ObservableProperty] private AudioDeviceInfo? _selectedDevice;
    [ObservableProperty] private VideoScreenInfo? _selectedVideoScreen;
    [ObservableProperty] private bool _hasSecondaryDisplay;
    [ObservableProperty] private bool _isEditMode;
    [ObservableProperty] private bool _isLivePreviewVisible = true;
    [ObservableProperty] private bool _isVideoOutputEnabled = false;
    [ObservableProperty] private bool _isImporting;
    [ObservableProperty] private double _cueItemWidth = 112;
    [ObservableProperty] private double _cueItemHeight = 382;
    [ObservableProperty] private double _mixerInputCardWidth = 168;
    [ObservableProperty] private double _mixerInputCardHeight = 138;
    [ObservableProperty] private int _importProgress;
    [ObservableProperty] private int _importTotal;
    [ObservableProperty] private string _importCurrentFile = string.Empty;
    [ObservableProperty] private string _projectName = "Untitled Project";
    [ObservableProperty] private string _statusMessage = "Ready";
    [ObservableProperty] private string _outputStatusText = "Output OFF";
    [ObservableProperty] private bool _isOutputPlaying;
    [ObservableProperty] private LedOutputProfile? _selectedLedProfile;
    [ObservableProperty] private LedTestPattern _selectedLedTestPattern = LedTestPattern.Grid;
    [ObservableProperty] private int _ledCustomCanvasWidth = 1920;
    [ObservableProperty] private int _ledCustomCanvasHeight = 1080;
    [ObservableProperty] private LedScalingMode _ledCustomScalingMode = LedScalingMode.Fit;
    [ObservableProperty] private bool _isLedBlackout;
    [ObservableProperty] private bool _isLedFrozen;
    [ObservableProperty] private bool _isSafeScene;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShowWorkspace))]
    [NotifyPropertyChangedFor(nameof(IsMixerWorkspace))]
    private WorkspaceMode _activeWorkspace = WorkspaceMode.Show;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MixerPreviewTitle))]
    private CueCardViewModel? _mixerPreviewInput;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MixerPreviewTitle))]
    private bool _isMixerKaraokePreview;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MixerProgramTitle))]
    private bool _isMixerKaraokeProgram;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MixerProgramTitle))]
    private CueCardViewModel? _mixerProgramInput;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MixerProgramTitle))]
    private bool _isMixerFadeToBlack;
    [ObservableProperty] private int _mixerTransitionDurationMs = 250;
    [ObservableProperty] private bool _isMixerProgramMuted;
    public bool IsShowWorkspace => ActiveWorkspace == WorkspaceMode.Show;
    public bool IsMixerWorkspace => ActiveWorkspace == WorkspaceMode.Mixer;
    public string MixerPreviewTitle => IsMixerKaraokePreview
        ? $"Karaoke · {(string.IsNullOrWhiteSpace(KaraokeNowTitle) ? "Ready" : KaraokeNowTitle)}"
        : MixerPreviewInput?.Title ?? "No input";
    public string MixerProgramTitle
    {
        get
        {
            if (!_videoPlayer.IsOutputArmed) return "OUTPUT OFF";
            if (IsMixerFadeToBlack) return "FADE TO BLACK";
            if (_videoPlayer.IsBlackout) return "BLACKOUT";
            if (_videoPlayer.IsSafeScene) return "SAFE SCENE";
            if (_videoPlayer.ActiveTestPattern is LedTestPattern pattern) return $"TEST · {pattern}";
            if (_videoPlayer.IsFrozen) return $"FREEZE · {(IsMixerKaraokeProgram ? "Karaoke" : MixerProgramInput?.Title ?? "Program")}";
            if (_videoPlayer.IsStandby) return "STANDBY";
            if (IsMixerKaraokeProgram) return $"KARAOKE · {KaraokeNowTitle}";
            return MixerProgramInput?.Title ?? "PROGRAM READY";
        }
    }
    [ObservableProperty] private string _ledPreflightStatus = "Not checked";

    // Live desk (gói F)
    [ObservableProperty] private bool _isLiveMode;
    [ObservableProperty] private bool _isLiveLocked;
    [ObservableProperty] private bool _isShowConfigurationEnabled = true;
    public bool IsKaraokeConfigurationEnabled => IsShowConfigurationEnabled && !IsLedFrozen;
    [ObservableProperty] private string _nowTitle = "—";
    [ObservableProperty] private string _nowRemaining = "0:00";
    [ObservableProperty] private string _nextTitle = "—";
    [ObservableProperty] private bool _nowIsUrgent; // < 15s remaining
    [ObservableProperty] private bool _nowIsPlaying;
    [ObservableProperty] private string _nowNotes = string.Empty;
    [ObservableProperty] private int _missingMediaCount;

    // Karaoke session (1 ô → remote + player) — operator-only desk
    [ObservableProperty] private string _karaokeSessionId = string.Empty;
    [ObservableProperty] private string _karaokeRemoteUrl = "https://huy.sale/remote?session=";
    /// <summary>OUTPUT master player URL (registers remote session).</summary>
    [ObservableProperty] private string _karaokePlayerUrl = "https://huy.sale/player";

    /// <summary>Màn chiếu karaoke (secondary) đang bật.</summary>
    [ObservableProperty] private bool _isKaraokeOutputOn;
    /// <summary>Player master đã báo ready qua bridge.</summary>
    [ObservableProperty] private bool _karaokeMasterReady;
    /// <summary>Bài đang phát (từ bridge).</summary>
    [ObservableProperty] private string _karaokeNowTitle = "—";
    /// <summary>Dòng status cố định trên panel karaoke.</summary>
    [ObservableProperty] private string _karaokeStatusText = "Karaoke OFF · chỉ operator";
    /// <summary>Nhãn nút ON/OFF chính.</summary>
    [ObservableProperty] private string _karaokeOnOffLabel = "KARAOKE ON";
    /// <summary>Màu gợi ý: true = đang ON (nút đỏ OFF), false = OFF (nút xanh ON).</summary>
    [ObservableProperty] private bool _karaokeOnOffIsDanger;
    /// <summary>Vị trí và chế độ của cửa sổ Program đang dùng.</summary>
    [ObservableProperty] private string _programOutputModeText = "ĐÃ TẮT";
    [ObservableProperty] private bool _karaokeAutoNextEnabled;
    [ObservableProperty] private string _karaokeAutoNextLabel = "AUTO: OFF";
    /// <summary>Test 1 màn: karaoke OUTPUT = cửa sổ windowed trên cùng màn.</summary>
    [ObservableProperty] private bool _isKaraokeSingleScreenTest = true;
    /// <summary>Nhãn nút test 1 màn.</summary>
    [ObservableProperty] private string _karaokeSingleScreenTestLabel = "Test 1 màn: ON";

    /// <summary>Raised when karaoke URLs should be applied (open remote / refresh player).</summary>
    public event EventHandler? KaraokeUrlsChanged;
    public event EventHandler? KaraokeAutoNextChanged;
    public event EventHandler? VideoScreenChanged;
    /// <summary>Raised to toggle karaoke secondary output from VM (Space/GO on karaoke tab).</summary>
    public event EventHandler? KaraokeToggleOutputRequested;
    public event Action<string>? MixerKaraokeTakeRequested;
    public event Action<string>? SoundEffectHotkeyPressed;

    public MainViewModel(
        IServiceProvider services,
        IAudioEngine audio,
        IImportService import,
        IProjectService project,
        ISearchService search,
        ISettingsService settings,
        IHotkeyService hotkeys,
        IVideoPlayerService videoPlayer,
        IPreloadManager preload,
        ICrossfadeEngine crossfade)
    {
        _services = services;
        _audio = audio;
        _import = import;
        _project = project;
        _search = search;
        Settings = settings;
        _hotkeys = hotkeys;
        _videoPlayer = videoPlayer;
        _preload = preload;
        _crossfade = crossfade;

        _hotkeys.HotkeyPressed += OnHotkeyPressed;

        _autoSaveTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        _autoSaveTimer.Tick += async (_, _) => await AutoSaveAsync();

        _nowNextTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _nowNextTimer.Tick += (_, _) => RefreshNowNextBar();
        _nowNextTimer.Start();

        _videoPlayer.OutputStateChanged += OnVideoOutputStateChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        LoadVideoScreens();
        _ = InitializeAsync();
    }

    private void LoadVideoScreens()
    {
        var all = System.Windows.Forms.Screen.AllScreens
            .OrderByDescending(screen => screen.Primary)
            .ThenBy(screen => screen.Bounds.Left)
            .ThenBy(screen => screen.Bounds.Top)
            .ToArray();

        _reloadingVideoScreens = true;
        try
        {
            VideoScreens.Clear();
            for (int i = 0; i < all.Length; i++)
                VideoScreens.Add(new VideoScreenInfo(all[i], i + 1, all.Length));

            HasSecondaryDisplay = VideoScreens.Any(screen => !screen.IsPrimary);

            // Prefer saved device, else non-primary (projector), else primary.
            VideoScreenInfo? pick = null;
            var saved = Settings.Current.VideoOutputDeviceName;
            if (!string.IsNullOrEmpty(saved))
                pick = VideoScreens.FirstOrDefault(s => s.DeviceName == saved);

            if (pick is null && Settings.Current.PreferSecondaryOutput)
                pick = VideoScreens.FirstOrDefault(s => !s.IsPrimary);

            SelectedVideoScreen = pick
                ?? VideoScreens.FirstOrDefault(screen => screen.IsPrimary)
                ?? VideoScreens.FirstOrDefault();
        }
        finally
        {
            _reloadingVideoScreens = false;
        }

        // Initial/settings load: remember the target without opening or moving a live window.
        if (!_videoPlayer.IsOutputArmed)
            _videoPlayer.SetTargetScreen(SelectedVideoScreen?.Screen);
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            var previousDevice = _videoPlayer.TargetScreen?.DeviceName
                ?? SelectedVideoScreen?.DeviceName;
            var wasArmed = _videoPlayer.IsOutputArmed;
            var wasPreviewOnly = _videoPlayer.IsPreviewOnlyOutput;
            var wasWindowed = _videoPlayer.IsWindowedOutput;
            LoadVideoScreens();

            if (!wasArmed)
                return;

            if (wasPreviewOnly)
            {
                _videoPlayer.SetPreviewOnlyOutput();
                return;
            }

            var restored = VideoScreens.FirstOrDefault(screen =>
                string.Equals(screen.DeviceName, previousDevice, StringComparison.OrdinalIgnoreCase));
            if (restored is not null)
            {
                SelectedVideoScreen = restored;
                if (wasWindowed)
                    _videoPlayer.SetWindowedOutput(true, restored.Screen);
                else
                    _videoPlayer.SetTargetScreen(restored.Screen, forceFullscreen: true);
                return;
            }

            // Never fall back to fullscreen on the operator monitor when a projector is unplugged.
            var primary = VideoScreens.FirstOrDefault(screen => screen.IsPrimary)
                ?? VideoScreens.FirstOrDefault();
            if (primary is null)
                return;

            _videoPlayer.SetWindowedOutput(true, primary.Screen);
            SelectedVideoScreen = primary;
            ProgramOutputModeText = $"{primary.ShortName} · WINDOW";
            StatusMessage = "⚠ Mất màn đang chiếu · PROGRAM đã thu về Màn 1 dạng cửa sổ";
        });
    }

    partial void OnSelectedVideoScreenChanged(VideoScreenInfo? value)
    {
        if (!_reloadingVideoScreens && value is not null)
        {
            if (_videoPlayer.IsPreviewOnlyOutput)
            {
                // PROGRAM nội bộ stays hidden; only remember the screen for the next external output.
            }
            else if (_videoPlayer.IsWindowedOutput)
            {
                _videoPlayer.SetWindowedOutput(true, value.Screen);
            }
            else
            {
                _videoPlayer.SetTargetScreen(value.Screen, forceFullscreen: _videoPlayer.IsOutputArmed);
            }
        }
        if (value is not null)
        {
            Settings.Current.VideoOutputDeviceName = value.DeviceName;
            _ = Settings.SaveAsync();
            RefreshOutputStatus();
            if (IsMixerWorkspace)
            {
                StatusMessage = _videoPlayer.IsOutputArmed
                    ? $"PROGRAM chuyển sang {value.DisplayName}"
                    : $"Đã chọn màn Program: {value.DisplayName} · OUTPUT đang tắt";
            }
        }
        VideoScreenChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnIsVideoOutputEnabledChanged(bool value)
    {
        if (!value)
        {
            InvalidateMixerTake();
            IsMixerFadeToBlack = false;
            _videoPlayer.ClearProgramTransitionMask();
        }
        _videoPlayer.SetOutputEnabled(value);
        RefreshOutputStatus();
        StatusMessage = value
            ? $"Program READY → {SelectedVideoScreen?.ShortName ?? "monitor"}"
            : "Output OFF — projector released";
    }

    private void OnVideoOutputStateChanged(object? sender, EventArgs e)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            if (!_videoPlayer.IsOutputArmed)
            {
                IsVideoOutputEnabled = false;
            }
            IsOutputPlaying = _videoPlayer.IsPlaying;
            IsLedBlackout = _videoPlayer.IsBlackout;
            IsMixerProgramMuted = _videoPlayer.IsMuted;
            if (IsMixerFadeToBlack && !_videoPlayer.IsBlackout)
                IsMixerFadeToBlack = false;
            IsLedFrozen = _videoPlayer.IsFrozen;
            IsSafeScene = _videoPlayer.IsSafeScene;
            OnPropertyChanged(nameof(MixerProgramTitle));
            RefreshOutputStatus();
        });
    }

    partial void OnIsLedFrozenChanged(bool value)
        => OnPropertyChanged(nameof(IsKaraokeConfigurationEnabled));

    partial void OnIsShowConfigurationEnabledChanged(bool value)
        => OnPropertyChanged(nameof(IsKaraokeConfigurationEnabled));

    private void RefreshOutputStatus()
    {
        if (!_videoPlayer.IsOutputArmed)
            OutputStatusText = "Tắt";
        else if (_videoPlayer.IsBlackout)
            OutputStatusText = "BLACKOUT";
        else if (_videoPlayer.IsSafeScene)
            OutputStatusText = "SAFE SCENE";
        else if (_videoPlayer.ActiveTestPattern is LedTestPattern pattern)
            OutputStatusText = $"TEST · {pattern}";
        else if (_videoPlayer.IsFrozen)
            OutputStatusText = "FREEZE / HOLD";
        else if (_videoPlayer.IsStandby)
            OutputStatusText = $"STANDBY · {SelectedVideoScreen?.ShortName}";
        else if (_videoPlayer.IsPlaying)
            OutputStatusText = $"Đang phát · {SelectedVideoScreen?.ShortName}";
        else
            OutputStatusText = $"Sẵn sàng · {SelectedVideoScreen?.ShortName}";
    }

    private async Task InitializeAsync()
    {
        await Settings.LoadAsync();
        MasterVolume = Settings.Current.MasterVolume;
        AudioBusVolume = Settings.Current.AudioBusVolume;
        VideoBusVolume = Settings.Current.VideoBusVolume;
        KaraokeBusVolume = Settings.Current.KaraokeBusVolume;
        ApplyBusVolumes();

        // Re-pick screen after settings loaded (LoadVideoScreens ran early with defaults)
        LoadVideoScreens();
        LoadLedProfiles();

        if (Settings.Current.AutoSaveEnabled)
            _autoSaveTimer.Start();

        // Karaoke session đã lưu
        KaraokeSessionId = Settings.Current.KaraokeSessionId ?? string.Empty;
        if (!Settings.Current.KaraokeSafeTakeDefaultsApplied)
        {
            Settings.Current.KaraokeAutoNextEnabled = false;
            Settings.Current.KaraokeSafeTakeDefaultsApplied = true;
            await Settings.SaveAsync();
        }
        KaraokeAutoNextEnabled = Settings.Current.KaraokeAutoNextEnabled;
        RebuildKaraokeUrls();

        // Test 1 màn: auto ON nếu chỉ 1 monitor hoặc đã lưu setting
        IsKaraokeSingleScreenTest = Settings.Current.KaraokeSingleScreenTest || VideoScreens.Count <= 1;
        RefreshKaraokeSingleScreenTestLabel();
        RefreshKaraokeStatusUi();

        // Karaoke-only shell: keep legacy playlists in storage, but always operate from Karaoke.
        EnsureDefaultPlaylists();
        ValidateAllMedia();

        var screens = VideoScreens.Count;
        StatusMessage = screens >= 2
            ? $"Sẵn sàng · chọn cue → Space để GO · Output → {SelectedVideoScreen?.ShortName ?? "?"} · Esc = dừng tab"
            : "Sẵn sàng · chọn cue → Space để GO · Karaoke đang ở chế độ test 1 màn · Esc = dừng tab";
        if (MissingMediaCount > 0)
            StatusMessage += $" · ⚠ {MissingMediaCount} file thiếu";
        RefreshOutputStatus();
    }

    private void LoadLedProfiles()
    {
        _loadingLedSettings = true;
        try
        {
            LedProfiles.Clear();
            foreach (var preset in LedOutputProfile.CreatePresets())
                LedProfiles.Add(preset);

            LedCustomCanvasWidth = Math.Clamp(Settings.Current.LedCustomCanvasWidth, 64, 16384);
            LedCustomCanvasHeight = Math.Clamp(Settings.Current.LedCustomCanvasHeight, 64, 16384);
            LedCustomScalingMode = Settings.Current.LedCustomScalingMode;
            var custom = LedOutputProfile.CreateCustom(
                Settings.Current.LedCustomProfileName,
                LedCustomCanvasWidth,
                LedCustomCanvasHeight,
                LedCustomScalingMode);
            LedProfiles.Add(custom);

            SelectedLedProfile = LedProfiles.FirstOrDefault(profile =>
                string.Equals(profile.Id, Settings.Current.LedOutputProfileId, StringComparison.OrdinalIgnoreCase))
                ?? LedProfiles.FirstOrDefault();
        }
        finally
        {
            _loadingLedSettings = false;
        }
    }

    partial void OnSelectedLedProfileChanged(LedOutputProfile? value)
    {
        if (value is null) return;
        if (!_loadingLedSettings && GuardIfLocked("change LED profile")) return;
        _videoPlayer.ApplyLedProfile(value);
        if (_loadingLedSettings) return;

        Settings.Current.LedOutputProfileId = value.Id;
        if (!value.IsPreset)
        {
            Settings.Current.LedCustomProfileName = value.Name;
            Settings.Current.LedCustomCanvasWidth = value.CanvasWidth;
            Settings.Current.LedCustomCanvasHeight = value.CanvasHeight;
            Settings.Current.LedCustomScalingMode = value.ScalingMode;
        }
        _ = Settings.SaveAsync();
        StatusMessage = $"LED profile: {value.Name} · {value.Readout}";
    }

    [RelayCommand]
    private void ApplyCustomLedProfile()
    {
        if (GuardIfLocked("change custom LED profile")) return;
        var custom = LedOutputProfile.CreateCustom(
            Settings.Current.LedCustomProfileName,
            LedCustomCanvasWidth,
            LedCustomCanvasHeight,
            LedCustomScalingMode);
        int index = LedProfiles.ToList().FindIndex(profile => profile.Id == "custom");
        if (index >= 0)
            LedProfiles[index] = custom;
        else
            LedProfiles.Add(custom);

        Settings.Current.LedCustomCanvasWidth = custom.CanvasWidth;
        Settings.Current.LedCustomCanvasHeight = custom.CanvasHeight;
        Settings.Current.LedCustomScalingMode = custom.ScalingMode;
        SelectedLedProfile = custom;
    }

    [RelayCommand]
    private async Task ShowSelectedLedTestPatternAsync()
    {
        if (GuardIfLocked("show test pattern")) return;
        InvalidateMixerTake();
        foreach (var cue in Playlists.SelectMany(playlist => playlist.Cues)
                     .Where(cue => cue.IsVideo && (cue.IsPlaying || cue.IsPaused))
                     .ToArray())
        {
            await cue.StopCommand.ExecuteAsync(null);
        }
        _videoPlayer.ShowTestPattern(SelectedLedTestPattern);
        StatusMessage = $"LED test pattern: {SelectedLedTestPattern}";
    }

    [RelayCommand]
    private void ClearLedTestPattern()
    {
        _videoPlayer.ClearTestPattern();
        StatusMessage = "LED test pattern cleared";
    }

    [RelayCommand]
    private async Task ToggleLedBlackoutAsync()
    {
        bool enable = !_videoPlayer.IsBlackout;
        if (!enable)
            await _mixerTakeGate.WaitAsync();
        try
        {
            if (!enable && !_videoPlayer.IsBlackout) return;
            if (IsMixerFadeToBlack)
            {
                IsMixerFadeToBlack = false;
                _videoPlayer.ClearProgramTransitionMask();
            }
            _videoPlayer.SetBlackout(enable);
            IsLedBlackout = _videoPlayer.IsBlackout;
            StatusMessage = IsLedBlackout ? "LED BLACKOUT active" : "LED BLACKOUT cleared";
        }
        finally
        {
            if (!enable)
                _mixerTakeGate.Release();
        }
    }

    [RelayCommand]
    private void ShowLedStandby()
    {
        InvalidateMixerTake();
        _videoPlayer.ShowStandby();
        IsOutputPlaying = false;
        StatusMessage = "LED STANDBY";
    }

    [RelayCommand]
    private void ToggleLedFreeze()
    {
        if (_videoPlayer.IsSafeScene)
        {
            StatusMessage = "SAFE SCENE đang giữ Program · FREEZE không cần thiết";
            return;
        }
        if (!_videoPlayer.IsFrozen) InvalidateMixerTake();
        _videoPlayer.SetFreeze(!_videoPlayer.IsFrozen);
        IsLedFrozen = _videoPlayer.IsFrozen;
        StatusMessage = IsLedFrozen ? "LED FREEZE / HOLD" : "LED FREEZE released";
    }

    [RelayCommand]
    private async Task ShowEmergencySafeSceneAsync()
    {
        InvalidateMixerTake();
        foreach (var cue in Playlists.SelectMany(playlist => playlist.Cues)
                     .Where(cue => cue.IsVideo && (cue.IsPlaying || cue.IsPaused))
                     .ToArray())
        {
            await cue.StopCommand.ExecuteAsync(null);
        }

        KaraokeOutputStopRequested?.Invoke(this, EventArgs.Empty);
        bool shown = _videoPlayer.ShowSafeScene(Settings.Current.EmergencySafeSceneImagePath);
        IsSafeScene = _videoPlayer.IsSafeScene;
        StatusMessage = shown
            ? "EMERGENCY SAFE SCENE · audio continues"
            : "SAFE SCENE fallback failed";
    }

    [RelayCommand]
    private void ShowWorkspace() => ActiveWorkspace = WorkspaceMode.Show;

    [RelayCommand]
    private void MixerWorkspace() => ActiveWorkspace = WorkspaceMode.Mixer;

    partial void OnActiveWorkspaceChanged(WorkspaceMode value)
    {
        if (value == WorkspaceMode.Mixer)
            EnsureMixerInputThumbnails();
    }

    [RelayCommand]
    private void SelectMixerPreview(CueCardViewModel? input)
    {
        if (input is null || !MixerInputs.Contains(input)) return;
        IsMixerKaraokePreview = false;
        MixerPreviewInput = input;
        StatusMessage = $"MIXER PREVIEW · {input.Title}";
    }

    [RelayCommand]
    private void SelectMixerKaraokePreview()
    {
        EnsureKaraokeTab(select: false);
        EnsureKaraokeSessionId();
        RebuildKaraokeUrls();
        MixerPreviewInput = null;
        IsMixerKaraokePreview = true;
        OnPropertyChanged(nameof(MixerPreviewTitle));
        StatusMessage = $"MIXER PREVIEW · Karaoke · session {KaraokeSessionId}";
    }

    [RelayCommand]
    private void RemoveMixerInput(CueCardViewModel? input)
    {
        if (input is null || !MixerInputs.Contains(input)) return;
        RemoveCue(input);
    }

    [RelayCommand]
    private void MoveMixerInputUp(CueCardViewModel? input) => MoveMixerInput(input, -1);

    [RelayCommand]
    private void MoveMixerInputDown(CueCardViewModel? input) => MoveMixerInput(input, 1);

    private void MoveMixerInput(CueCardViewModel? input, int offset)
    {
        if (GuardIfLocked("sắp xếp nguồn") || input is null || SelectedPlaylist is null) return;
        var oldIndex = SelectedPlaylist.Cues.IndexOf(input);
        var newIndex = oldIndex + offset;
        if (oldIndex < 0 || newIndex < 0 || newIndex >= SelectedPlaylist.Cues.Count) return;

        SelectedPlaylist.Cues.Move(oldIndex, newIndex);
        for (var i = 0; i < SelectedPlaylist.Cues.Count; i++)
            SelectedPlaylist.Cues[i].Model.SortOrder = i;
        RefreshFilter();
        MixerPreviewInput = input;
        StatusMessage = $"Đã sắp xếp nguồn: {input.Title}";
    }

    [RelayCommand]
    private void OpenMixerInputSettings(CueCardViewModel? input)
    {
        if (GuardIfLocked("mở thuộc tính nguồn") || input is null) return;
        var window = new Views.CueSettingsWindow
        {
            DataContext = new CueSettingsViewModel(input),
            Owner = Application.Current.MainWindow
        };
        window.ShowDialog();
    }

    partial void OnMixerPreviewInputChanged(CueCardViewModel? oldValue, CueCardViewModel? newValue)
    {
        oldValue?.SetMixerPreviewState(false);
        newValue?.SetMixerPreviewState(true);
    }

    partial void OnKaraokeNowTitleChanged(string value)
    {
        OnPropertyChanged(nameof(MixerPreviewTitle));
        OnPropertyChanged(nameof(MixerProgramTitle));
    }

    partial void OnMixerProgramInputChanged(CueCardViewModel? oldValue, CueCardViewModel? newValue)
    {
        oldValue?.SetMixerProgramState(false);
        newValue?.SetMixerProgramState(true);
        oldValue?.CancelMixerProgramSeek();
        if (ReferenceEquals(_mixerSeekingProgram, oldValue))
            _mixerSeekingProgram = null;
        if (ReferenceEquals(_mixerVolumeProgram, oldValue))
            _mixerVolumeProgram = null;
    }

    [RelayCommand]
    private async Task MixerCutAsync()
        => await ExecuteMixerCutAsync("CUT", swapPreviewAndProgram: false);

    [RelayCommand]
    private async Task MixerSwapAsync()
        => await ExecuteMixerCutAsync("SWAP", swapPreviewAndProgram: true);

    private async Task ExecuteMixerCutAsync(string transition, bool swapPreviewAndProgram)
    {
        if (_videoPlayer.IsFrozen)
        {
            StatusMessage = $"MIXER {transition} blocked · OUTPUT FROZEN";
            return;
        }
        if (IsMixerKaraokePreview)
        {
            MixerKaraokeTakeRequested?.Invoke(transition);
            StatusMessage = $"MIXER {transition} → Karaoke";
            return;
        }
        var target = MixerPreviewInput;
        if (target is null)
        {
            StatusMessage = $"MIXER {transition} · no Preview input";
            return;
        }
        if (!await _mixerTakeGate.WaitAsync(0))
        {
            StatusMessage = "MIXER take already in progress";
            return;
        }
        var previousProgram = _videoPlayer.ActiveOwnerId is Guid ownerId
            ? Playlists.SelectMany(playlist => playlist.Cues)
                .FirstOrDefault(cue => cue.Model.Id == ownerId)
            : null;
        var takeVersion = Interlocked.Increment(ref _mixerTakeVersion);
        _mixerTakeTargetId = target.Model.Id;
        try
        {
            bool started = await StartMixerProgramAsync(transition, takeVersion, target);
            if (started && swapPreviewAndProgram && IsMixerTakeCurrent(takeVersion)
                && previousProgram is not null && !ReferenceEquals(previousProgram, target)
                && MixerInputs.Contains(previousProgram))
            {
                MixerPreviewInput = previousProgram;
                StatusMessage = $"MIXER SWAP · PVW {previousProgram.Title} · PGM {target.Title}";
            }
        }
        finally
        {
            if (_mixerTakeTargetId == target.Model.Id)
                _mixerTakeTargetId = null;
            _mixerTakeGate.Release();
        }
    }

    [RelayCommand]
    private async Task MixerFadeAsync()
    {
        if (_videoPlayer.IsFrozen)
        {
            StatusMessage = "MIXER FADE blocked · OUTPUT FROZEN";
            return;
        }
        if (IsMixerKaraokePreview)
        {
            MixerKaraokeTakeRequested?.Invoke("FADE");
            StatusMessage = "MIXER FADE → Karaoke";
            return;
        }
        var target = MixerPreviewInput;
        if (target is null)
        {
            StatusMessage = "MIXER FADE · no Preview input";
            return;
        }
        if (!await _mixerTakeGate.WaitAsync(0))
        {
            StatusMessage = "MIXER take already in progress";
            return;
        }
        var takeVersion = Interlocked.Increment(ref _mixerTakeVersion);
        _mixerTakeTargetId = target.Model.Id;
        double transitionPhaseSeconds = Math.Clamp(MixerTransitionDurationMs, 100, 2000) / 2000d;

        try
        {
            if (!await _videoPlayer.FadeProgramMaskAsync(true, transitionPhaseSeconds))
            {
                if (IsMixerTakeCurrent(takeVersion))
                    StatusMessage = "MIXER FADE blocked · OUTPUT unavailable";
                return;
            }
            await StartMixerProgramAsync("FADE", takeVersion, target);
        }
        catch
        {
            if (IsMixerTakeCurrent(takeVersion))
                StatusMessage = "MIXER FADE failed";
        }
        finally
        {
            if (IsMixerFadeToBlack)
            {
                _videoPlayer.ClearProgramTransitionMask();
            }
            else if (!await _videoPlayer.FadeProgramMaskAsync(false, transitionPhaseSeconds))
            {
                _videoPlayer.ClearProgramTransitionMask();
            }
            if (_mixerTakeTargetId == target.Model.Id)
                _mixerTakeTargetId = null;
            _mixerTakeGate.Release();
        }
    }

    [RelayCommand]
    private async Task MixerToggleProgramPauseAsync()
    {
        var requestedProgram = GetActiveMixerProgram("PAUSE / RESUME");
        if (requestedProgram is null) return;
        await _mixerTakeGate.WaitAsync();
        try
        {
            if (!ReferenceEquals(MixerProgramInput, requestedProgram))
            {
                StatusMessage = "MIXER PAUSE / RESUME cancelled - Program changed";
                return;
            }
            var program = GetActiveMixerProgram("PAUSE / RESUME");
            if (program is null) return;
            await program.TogglePlayPauseCommand.ExecuteAsync(null);
            StatusMessage = program.IsPaused
                ? $"MIXER PROGRAM paused · {program.Title}"
                : $"MIXER PROGRAM playing · {program.Title}";
        }
        finally
        {
            _mixerTakeGate.Release();
        }
    }

    [RelayCommand]
    private async Task MixerRestartProgramAsync()
    {
        var requestedProgram = GetActiveMixerProgram("RESTART");
        if (requestedProgram is null) return;
        await _mixerTakeGate.WaitAsync();
        try
        {
            if (!ReferenceEquals(MixerProgramInput, requestedProgram))
            {
                StatusMessage = "MIXER RESTART cancelled - Program changed";
                return;
            }
            var program = GetActiveMixerProgram("RESTART");
            if (program is null) return;
            bool restarted = program.RestartOnMixerProgram();
            StatusMessage = restarted
                ? $"MIXER PROGRAM restarted · {program.Title}"
                : $"MIXER RESTART failed · {program.Title}";
        }
        finally
        {
            _mixerTakeGate.Release();
        }
    }

    [RelayCommand]
    private async Task MixerToggleProgramLoopAsync()
    {
        var requestedProgram = GetActiveMixerProgram("LOOP");
        if (requestedProgram is null) return;
        await _mixerTakeGate.WaitAsync();
        try
        {
            if (!ReferenceEquals(MixerProgramInput, requestedProgram))
            {
                StatusMessage = "MIXER LOOP cancelled - Program changed";
                return;
            }
            var program = GetActiveMixerProgram("LOOP");
            if (program is null) return;
            program.ToggleLoopCommand.Execute(null);
            StatusMessage = program.IsMixerLoopEnabled
                ? $"MIXER LOOP ON · {program.Title}"
                : $"MIXER LOOP OFF · {program.Title}";
        }
        finally
        {
            _mixerTakeGate.Release();
        }
    }

    [RelayCommand]
    private void MixerBeginProgramSeek()
    {
        CancelMixerProgramSeek();
        if (_mixerTakeGate.CurrentCount == 0) return;
        var program = GetActiveMixerProgram("SEEK");
        if (program is null) return;
        _mixerSeekingProgram = program;
        program.BeginMixerProgramSeek();
    }

    public void CancelMixerProgramSeek()
    {
        var program = _mixerSeekingProgram;
        _mixerSeekingProgram = null;
        program?.CancelMixerProgramSeek();
    }

    public async Task<bool> SeekMixerProgramAsync(double normalizedPosition)
    {
        var requestedProgram = _mixerSeekingProgram;
        _mixerSeekingProgram = null;
        if (requestedProgram is null) return false;

        await _mixerTakeGate.WaitAsync();
        try
        {
            if (!ReferenceEquals(MixerProgramInput, requestedProgram))
                return false;
            var program = GetActiveMixerProgram("SEEK");
            return program is not null && program.SeekOnMixerProgram(normalizedPosition);
        }
        finally
        {
            requestedProgram.CancelMixerProgramSeek();
            _mixerTakeGate.Release();
        }
    }

    [RelayCommand]
    private async Task MixerSkipBackwardAsync() => await MixerSkipProgramAsync(-5);

    [RelayCommand]
    private async Task MixerSkipForwardAsync() => await MixerSkipProgramAsync(5);

    private async Task MixerSkipProgramAsync(double seconds)
    {
        var requestedProgram = GetActiveMixerProgram("SKIP");
        if (requestedProgram is null) return;
        await _mixerTakeGate.WaitAsync();
        try
        {
            if (!ReferenceEquals(MixerProgramInput, requestedProgram))
            {
                StatusMessage = "MIXER SKIP cancelled - Program changed";
                return;
            }
            var program = GetActiveMixerProgram("SKIP");
            if (program is null) return;
            if (program.SkipOnMixerProgram(seconds))
                StatusMessage = $"MIXER SKIP {(seconds < 0 ? "-" : "+")}{Math.Abs(seconds):0}s - {program.Title}";
        }
        finally
        {
            _mixerTakeGate.Release();
        }
    }

    [RelayCommand]
    private async Task MixerToggleProgramMuteAsync()
    {
        var requestedProgram = GetActiveMixerProgram("MUTE");
        if (requestedProgram is null) return;
        await _mixerTakeGate.WaitAsync();
        try
        {
            if (!ReferenceEquals(MixerProgramInput, requestedProgram))
            {
                StatusMessage = "MIXER MUTE cancelled - Program changed";
                return;
            }
            if (GetActiveMixerProgram("MUTE") is null) return;
            _videoPlayer.SetMuted(!_videoPlayer.IsMuted);
            IsMixerProgramMuted = _videoPlayer.IsMuted;
            StatusMessage = IsMixerProgramMuted
                ? $"MIXER PROGRAM muted - {requestedProgram.Title}"
                : $"MIXER PROGRAM audio on - {requestedProgram.Title}";
        }
        finally
        {
            _mixerTakeGate.Release();
        }
    }

    public async Task<bool> SetMixerProgramVolumeAsync(double volume)
    {
        var requestedProgram = _mixerVolumeProgram;
        _mixerVolumeProgram = null;
        if (requestedProgram is null) return false;
        await _mixerTakeGate.WaitAsync();
        try
        {
            if (!ReferenceEquals(MixerProgramInput, requestedProgram)
                || GetActiveMixerProgram("VOLUME") is null)
                return false;
            requestedProgram.Volume = Math.Clamp(volume, 0, 1);
            return true;
        }
        finally
        {
            _mixerTakeGate.Release();
        }
    }

    public void BeginMixerProgramVolumeEdit()
    {
        CancelMixerProgramVolumeEdit();
        if (_mixerTakeGate.CurrentCount == 0) return;
        _mixerVolumeProgram = GetActiveMixerProgram("VOLUME");
    }

    public void CancelMixerProgramVolumeEdit()
        => _mixerVolumeProgram = null;

    [RelayCommand]
    private async Task MixerToggleFadeToBlackAsync()
    {
        bool toBlack = !IsMixerFadeToBlack;
        if (_videoPlayer.IsFrozen)
        {
            _videoPlayer.SetBlackout(toBlack);
            IsMixerFadeToBlack = toBlack;
            IsLedBlackout = _videoPlayer.IsBlackout;
            StatusMessage = toBlack
                ? "MIXER FTB · instant while OUTPUT FROZEN"
                : "MIXER FTB released · OUTPUT remains FROZEN";
            return;
        }

        if (toBlack && !await _mixerTakeGate.WaitAsync(0))
        {
            _videoPlayer.SetBlackout(true);
            IsMixerFadeToBlack = true;
            IsLedBlackout = _videoPlayer.IsBlackout;
            StatusMessage = "MIXER FTB · instant while TAKE completes behind black";
            return;
        }
        if (!toBlack)
            await _mixerTakeGate.WaitAsync();
        try
        {
            if (toBlack)
            {
                await _videoPlayer.FadeProgramMaskAsync(true, 0.35);
                _videoPlayer.SetBlackout(true);
                IsMixerFadeToBlack = true;
                IsLedBlackout = _videoPlayer.IsBlackout;
                _videoPlayer.ClearProgramTransitionMask();
                StatusMessage = "MIXER FTB · Program faded to black";
            }
            else
            {
                await _videoPlayer.FadeProgramMaskAsync(true, 0);
                _videoPlayer.SetBlackout(false);
                IsMixerFadeToBlack = false;
                IsLedBlackout = _videoPlayer.IsBlackout;
                if (!await _videoPlayer.FadeProgramMaskAsync(false, 0.35))
                    _videoPlayer.ClearProgramTransitionMask();
                StatusMessage = "MIXER FTB released";
            }
        }
        catch
        {
            _videoPlayer.ClearProgramTransitionMask();
            StatusMessage = "MIXER FTB failed";
        }
        finally
        {
            _mixerTakeGate.Release();
        }
    }

    private CueCardViewModel? GetActiveMixerProgram(string action)
    {
        var program = MixerProgramInput;
        if (!_videoPlayer.IsOutputArmed || program is null
            || _videoPlayer.ActiveOwnerId != program.Model.Id)
        {
            StatusMessage = $"MIXER {action} · no active Program";
            return null;
        }
        if (_videoPlayer.IsFrozen)
        {
            StatusMessage = $"MIXER {action} blocked · OUTPUT FROZEN";
            return null;
        }
        return program;
    }

    private async Task<bool> StartMixerProgramAsync(
        string transition,
        long takeVersion,
        CueCardViewModel target)
    {
        if (!IsMixerTakeCurrent(takeVersion)) return false;

        var previousOwnerId = _videoPlayer.ActiveOwnerId;
        var previous = previousOwnerId is Guid ownerId
            ? Playlists.SelectMany(playlist => playlist.Cues)
                .FirstOrDefault(cue => cue.Model.Id == ownerId)
            : null;
        double previousPosition = previous is not null ? _videoPlayer.GetPosition() : 0;

        if (!await target.PreparePlaybackAsync())
        {
            if (IsMixerTakeCurrent(takeVersion))
                StatusMessage = $"MIXER {transition} failed · {target.Title}";
            return false;
        }
        if (!IsMixerTakeCurrent(takeVersion)) return false;

        bool started = await StartPreparedMixerCueAsync(target);
        if (!IsMixerTakeCurrent(takeVersion)) return false;
        if (!started)
        {
            bool restored = false;
            if (previous is not null && !ReferenceEquals(previous, target) && !_videoPlayer.IsFrozen)
            {
                bool preparedPrevious = await previous.PreparePlaybackAsync();
                if (!IsMixerTakeCurrent(takeVersion)) return false;

                if (preparedPrevious)
                {
                    restored = await StartPreparedMixerCueAsync(previous);
                    if (!IsMixerTakeCurrent(takeVersion)) return false;
                }

                if (restored && previousPosition > 0.01)
                {
                    if (!IsMixerTakeCurrent(takeVersion)) return false;
                    _videoPlayer.Seek(previousPosition);
                    if (!IsMixerTakeCurrent(takeVersion)) return false;
                }
            }

            if (!IsMixerTakeCurrent(takeVersion)) return false;
            if (restored)
            {
                MixerProgramInput = previous;
                StatusMessage = $"MIXER {transition} failed · restored {previous!.Title}";
            }
            else if (!_videoPlayer.IsFrozen && !_videoPlayer.IsSafeScene &&
                     _videoPlayer.ActiveTestPattern is null)
            {
                _videoPlayer.ShowSafeScene(Settings.Current.EmergencySafeSceneImagePath);
                IsSafeScene = _videoPlayer.IsSafeScene;
                StatusMessage = $"MIXER {transition} failed · SAFE SCENE";
            }
            else
            {
                StatusMessage = $"MIXER {transition} failed · {target.Title}";
            }
            return false;
        }

        foreach (var other in Playlists.SelectMany(playlist => playlist.Cues)
                     .Where(cue => !ReferenceEquals(cue, target)
                         && (cue.IsPlaying || cue.IsPaused)).ToArray())
            other.MarkReplacedOnMixerProgram();

        MixerProgramInput = target;
        IsMixerKaraokeProgram = false;
        if (IsKaraokeOutputOn)
            SetKaraokeOutputOn(false);
        StatusMessage = $"MIXER {transition} → {target.Title}";
        return true;
    }

    public void SetMixerKaraokeProgramActive(bool active)
    {
        IsMixerKaraokeProgram = active;
        if (active)
        {
            foreach (var cue in Playlists.SelectMany(playlist => playlist.Cues)
                         .Where(cue => cue.IsPlaying || cue.IsPaused).ToArray())
                cue.MarkReplacedOnMixerProgram();
            MixerProgramInput = null;
            IsOutputPlaying = true;
        }
        OnPropertyChanged(nameof(MixerProgramTitle));
    }

    private bool IsMixerTakeCurrent(long takeVersion)
        => Volatile.Read(ref _mixerTakeVersion) == takeVersion && !_videoPlayer.IsFrozen;

    private async Task<bool> StartPreparedMixerCueAsync(CueCardViewModel cue)
    {
        _mixerStartingCueId = cue.Model.Id;
        try
        {
            return await cue.StartPreparedOnMixerProgramAsync();
        }
        finally
        {
            if (_mixerStartingCueId == cue.Model.Id)
                _mixerStartingCueId = null;
        }
    }

    private void InvalidateMixerTake()
    {
        Interlocked.Increment(ref _mixerTakeVersion);
        _videoPlayer.CancelPendingProgramStart();
    }

    public void CancelMixerTakeForExternalProgramChange()
        => InvalidateMixerTake();

    [RelayCommand]
    private async Task RunLedPreflightAsync()
    {
        var failures = new List<string>();
        var warnings = new List<string>();
        ValidateAllMedia();

        var screen = SelectedVideoScreen;
        if (screen is null || !VideoScreens.Any(item => item.DeviceName == screen.DeviceName))
            failures.Add("output screen unavailable");

        var profile = SelectedLedProfile;
        if (profile is null || profile.CanvasWidth < 64 || profile.CanvasHeight < 64)
            failures.Add("invalid LED profile");
        else if (screen is not null
                 && (profile.CanvasWidth != screen.Screen.Bounds.Width
                     || profile.CanvasHeight != screen.Screen.Bounds.Height))
            warnings.Add($"canvas {profile.CanvasWidth}x{profile.CanvasHeight} differs from screen {screen.Screen.Bounds.Width}x{screen.Screen.Bounds.Height}");

        if (MissingMediaCount > 0)
            failures.Add($"{MissingMediaCount} missing media file(s)");

        var visualMedia = Playlists
            .Where(playlist => !playlist.IsKaraoke)
            .SelectMany(playlist => playlist.Cues)
            .Where(cue => MetadataService.IsVideoFormat(cue.Model.FilePath))
            .Select(cue => cue.Model.FilePath)
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        int prepareFailed = 0;
        for (int i = 0; i < visualMedia.Length; i++)
        {
            StatusMessage = $"LED preflight · checking {i + 1}/{visualMedia.Length}";
            if (!await _videoPlayer.PrepareAsync(visualMedia[i]))
                prepareFailed++;
        }
        if (prepareFailed > 0)
            failures.Add($"{prepareFailed} video/image file(s) failed to prepare");

        if (failures.Count > 0)
            LedPreflightStatus = $"FAIL · {string.Join("; ", failures)}";
        else if (warnings.Count > 0)
            LedPreflightStatus = $"WARNING · {string.Join("; ", warnings)}";
        else
            LedPreflightStatus = $"PASS · {visualMedia.Length} visual media checked";

        StatusMessage = $"LED PREFLIGHT: {LedPreflightStatus}";
    }

    partial void OnIsKaraokeSingleScreenTestChanged(bool value)
    {
        Settings.Current.KaraokeSingleScreenTest = value;
        _ = Settings.SaveAsync();
        RefreshKaraokeSingleScreenTestLabel();
        RefreshKaraokeStatusUi();
        StatusMessage = value
            ? "Karaoke Test 1 màn: ON — OUTPUT mở cửa sổ trên cùng màn"
            : "Karaoke Test 1 màn: OFF — OUTPUT fullscreen màn đã chọn";
    }

    private void RefreshKaraokeSingleScreenTestLabel()
    {
        KaraokeSingleScreenTestLabel = IsKaraokeSingleScreenTest ? "Test 1 màn: ON" : "Test 1 màn: OFF";
    }

    [RelayCommand]
    private void ToggleKaraokeSingleScreenTest()
    {
        if (GuardIfLocked("change Karaoke screen mode")) return;
        IsKaraokeSingleScreenTest = !IsKaraokeSingleScreenTest;
    }

    partial void OnKaraokeSessionIdChanged(string value)
    {
        RebuildKaraokeUrls();
        Settings.Current.KaraokeSessionId = value?.Trim() ?? string.Empty;
        _ = Settings.SaveAsync();
        RefreshKaraokeStatusUi();
    }

    private void RebuildKaraokeUrls()
    {
        var baseRemote = string.IsNullOrWhiteSpace(Settings.Current.KaraokeRemoteBaseUrl)
            ? "https://huy.sale/remote?session="
            : Settings.Current.KaraokeRemoteBaseUrl;
        var playerBase = string.IsNullOrWhiteSpace(Settings.Current.KaraokePlayerUrl)
            ? "https://huy.sale/player"
            : Settings.Current.KaraokePlayerUrl.Trim();

        var session = (KaraokeSessionId ?? string.Empty).Trim();
        // Cho phép dán full URL remote — tách session
        if (session.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            var sid = ExtractSessionFromUrl(session);
            if (!string.IsNullOrEmpty(sid) && !string.Equals(sid, KaraokeSessionId, StringComparison.Ordinal))
            {
                KaraokeSessionId = sid;
                return;
            }
            if (!string.IsNullOrEmpty(sid))
                session = sid;
        }

        if (baseRemote.Contains("session=", StringComparison.OrdinalIgnoreCase))
        {
            var idx = baseRemote.IndexOf("session=", StringComparison.OrdinalIgnoreCase);
            KaraokeRemoteUrl = baseRemote[..(idx + "session=".Length)] + Uri.EscapeDataString(session);
        }
        else
        {
            KaraokeRemoteUrl = AppendOrReplaceQuery(baseRemote, "session", session);
        }

        // Operator desk flags — KTV remote ẩn QR/share khi embed ShowCue
        if (!string.IsNullOrEmpty(session))
        {
            KaraokeRemoteUrl = AppendOrReplaceQuery(KaraokeRemoteUrl, "op", "1");
            KaraokeRemoteUrl = AppendOrReplaceQuery(KaraokeRemoteUrl, "host", "showcue");
            KaraokeRemoteUrl = AppendOrReplaceQuery(KaraokeRemoteUrl, "embed", "1");
        }

        // UI layout remote: phone (mobile) | pc (desktop)
        KaraokeRemoteUrl = AppendOrReplaceQuery(KaraokeRemoteUrl, "ui", "pc");
        KaraokeRemoteUrl = AppendOrReplaceQuery(KaraokeRemoteUrl, "autonext", KaraokeAutoNextEnabled ? "1" : "0");

        // Chỉ tạo một player master cho PROGRAM; Mixer PREVIEW không chạy WebView.
        var player = StripQueryParams(playerBase, "session", "token", "embed", "host", "master", "preview");
        if (!string.IsNullOrEmpty(session))
        {
            player = AppendOrReplaceQuery(player, "session", session);
            player = AppendOrReplaceQuery(player, "embed", "1");
            player = AppendOrReplaceQuery(player, "host", "showcue");
        }
        KaraokePlayerUrl = AppendOrReplaceQuery(player, "master", "1");
        KaraokePlayerUrl = AppendOrReplaceQuery(KaraokePlayerUrl, "autonext", KaraokeAutoNextEnabled ? "1" : "0");
    }

    /// <summary>
    /// Ensure session + rebuild URLs + save — without navigating WebViews.
    /// Used when opening output so we can order: player master → remote.
    /// </summary>
    public void PrepareKaraokeSessionSilent()
    {
        EnsureKaraokeSessionId();
        RebuildKaraokeUrls();
        Settings.Current.KaraokeSessionId = KaraokeSessionId?.Trim() ?? string.Empty;
        _ = Settings.SaveAsync();
        RefreshKaraokeStatusUi();
        StatusMessage = string.IsNullOrWhiteSpace(KaraokeSessionId)
            ? "Karaoke: chưa có session"
            : $"Karaoke session: {KaraokeSessionId} · chuẩn bị player…";
    }

    /// <summary>Cập nhật state khi bật/tắt màn OUTPUT karaoke (gọi từ MainWindow).</summary>
    public void SetKaraokeOutputOn(bool on)
    {
        IsKaraokeOutputOn = on;
        if (!on)
            IsMixerKaraokeProgram = false;
        if (!on)
        {
            KaraokeMasterReady = false;
            KaraokeNowTitle = "—";
        }
        RefreshKaraokeStatusUi();
        StatusMessage = on
            ? $"Karaoke ON · session {KaraokeSessionId}"
            : "Karaoke OFF";
        if (on)
            _videoPlayer.EnsureOutputArmed();
        else
            _videoPlayer.SetOutputEnabled(IsVideoOutputEnabled);
    }

    /// <summary>Bridge từ player: ready / playing / idle / paused.</summary>
    public void ApplyKaraokeBridgeState(string? action, string? title)
    {
        if (string.IsNullOrWhiteSpace(action)) return;
        var a = action.Trim();

        if (a.Equals("ready", StringComparison.OrdinalIgnoreCase))
        {
            KaraokeMasterReady = true;
            if (string.IsNullOrWhiteSpace(KaraokeNowTitle) || KaraokeNowTitle == "—")
                KaraokeNowTitle = "Sẵn sàng";
        }
        else if (a.Equals("playing", StringComparison.OrdinalIgnoreCase) ||
                 a.Equals("play", StringComparison.OrdinalIgnoreCase) ||
                 a.Equals("resumed", StringComparison.OrdinalIgnoreCase))
        {
            KaraokeMasterReady = true;
            if (!string.IsNullOrWhiteSpace(title))
                KaraokeNowTitle = title.Trim();
        }
        else if (a.Equals("paused", StringComparison.OrdinalIgnoreCase) ||
                 a.Equals("pause", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(title))
                KaraokeNowTitle = title.Trim();
            else if (KaraokeNowTitle != "—" && !KaraokeNowTitle.StartsWith("⏸"))
                KaraokeNowTitle = $"⏸ {KaraokeNowTitle}";
        }
        else if (a.Equals("idle", StringComparison.OrdinalIgnoreCase) ||
                 a.Equals("ended", StringComparison.OrdinalIgnoreCase) ||
                 a.Equals("stopped", StringComparison.OrdinalIgnoreCase) ||
                 a.Equals("stop", StringComparison.OrdinalIgnoreCase) ||
                 a.Equals("empty", StringComparison.OrdinalIgnoreCase))
        {
            KaraokeNowTitle = "Idle";
        }

        RefreshKaraokeStatusUi();
        StatusMessage = KaraokeStatusText;
    }

    public void RefreshKaraokeStatusUi()
    {
        var session = string.IsNullOrWhiteSpace(KaraokeSessionId) ? "—" : KaraokeSessionId.Trim();
        var output = IsKaraokeOutputOn ? "OUTPUT ON" : "OUTPUT OFF";
        var master = !IsKaraokeOutputOn
            ? "master —"
            : (KaraokeMasterReady ? "master ready" : "master…");
        var now = string.IsNullOrWhiteSpace(KaraokeNowTitle) ? "—" : KaraokeNowTitle;
        var test = IsKaraokeSingleScreenTest ? " · Test1màn" : "";
        var autoNext = KaraokeAutoNextEnabled ? "AUTO ON" : "AUTO OFF";
        KaraokeStatusText = $"Session {session} · {output} · {master} · {autoNext}{test} · NOW: {now}";
        KaraokeOnOffLabel = IsKaraokeOutputOn ? "KARAOKE OFF" : "KARAOKE ON";
        KaraokeOnOffIsDanger = IsKaraokeOutputOn;
        KaraokeAutoNextLabel = KaraokeAutoNextEnabled ? "AUTO: ON" : "AUTO: OFF";
        RefreshKaraokeSingleScreenTestLabel();
    }

    [RelayCommand]
    private void ToggleKaraokeAutoNext()
    {
        if (GuardIfLocked("change Karaoke auto-next")) return;
        KaraokeAutoNextEnabled = !KaraokeAutoNextEnabled;
        KaraokeAutoNextLabel = KaraokeAutoNextEnabled ? "AUTO: ON" : "AUTO: OFF";
        Settings.Current.KaraokeAutoNextEnabled = KaraokeAutoNextEnabled;
        _ = Settings.SaveAsync();
        RebuildKaraokeUrls();
        RefreshKaraokeStatusUi();
        KaraokeAutoNextChanged?.Invoke(this, EventArgs.Empty);
        StatusMessage = KaraokeAutoNextEnabled
            ? "WebView: tự động chuyển bài ON"
            : "WebView: tự động chuyển bài OFF";
    }

    /// <summary>Tạo session 6 ký tự A-Z0-9 nếu trống (ShowCue + KTV chung mã).</summary>
    public string EnsureKaraokeSessionId()
    {
        var session = (KaraokeSessionId ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(session) || session.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            if (session.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                var sid = ExtractSessionFromUrl(session);
                if (!string.IsNullOrEmpty(sid))
                {
                    KaraokeSessionId = sid;
                    return sid;
                }
            }
            KaraokeSessionId = GenerateKaraokeSessionId();
            return KaraokeSessionId;
        }
        return session;
    }

    private static string GenerateKaraokeSessionId()
    {
        const string chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        Span<char> span = stackalloc char[6];
        for (var i = 0; i < span.Length; i++)
            span[i] = chars[Random.Shared.Next(chars.Length)];
        return new string(span);
    }

    private static string StripQueryParams(string url, params string[] keys)
    {
        if (string.IsNullOrWhiteSpace(url) || keys.Length == 0) return url;
        try
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return url;
            var parts = (uri.Query.TrimStart('?'))
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Where(p =>
                {
                    var name = p.Split('=', 2)[0];
                    return !keys.Any(k => name.Equals(k, StringComparison.OrdinalIgnoreCase));
                })
                .ToList();
            var ub = new UriBuilder(uri) { Query = parts.Count == 0 ? "" : string.Join("&", parts) };
            // UriBuilder may leave trailing '?' — normalize
            var s = ub.Uri.ToString();
            return s.EndsWith('?') ? s.TrimEnd('?') : s;
        }
        catch
        {
            return url;
        }
    }

    private static string AppendOrReplaceQuery(string url, string key, string value)
    {
        if (string.IsNullOrWhiteSpace(url)) return url;
        try
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                var sep = url.Contains('?') ? '&' : '?';
                return $"{url}{sep}{key}={Uri.EscapeDataString(value ?? "")}";
            }
            var parts = (uri.Query.TrimStart('?'))
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Where(p => !p.Split('=', 2)[0].Equals(key, StringComparison.OrdinalIgnoreCase))
                .ToList();
            parts.Add($"{key}={Uri.EscapeDataString(value ?? "")}");
            var ub = new UriBuilder(uri) { Query = string.Join("&", parts) };
            return ub.Uri.ToString();
        }
        catch
        {
            var sep = url.Contains('?') ? '&' : '?';
            return $"{url}{sep}{key}={Uri.EscapeDataString(value ?? "")}";
        }
    }

    private static string? ExtractSessionFromUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            var q = uri.Query.TrimStart('?');
            foreach (var part in q.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = part.Split('=', 2);
                if (kv.Length == 2 && kv[0].Equals("session", StringComparison.OrdinalIgnoreCase))
                    return Uri.UnescapeDataString(kv[1]);
            }
        }
        catch { /* ignore */ }
        return null;
    }

    [RelayCommand]
    private void ApplyKaraokeSession()
    {
        if (GuardIfLocked("change Karaoke session")) return;
        if (RejectKaraokeSourceChangeWhileFrozen()) return;

        EnsureKaraokeSessionId();
        RebuildKaraokeUrls();
        Settings.Current.KaraokeSessionId = KaraokeSessionId?.Trim() ?? string.Empty;
        _ = Settings.SaveAsync();
        KaraokeUrlsChanged?.Invoke(this, EventArgs.Empty);
        RefreshKaraokeStatusUi();
        StatusMessage = string.IsNullOrWhiteSpace(KaraokeSessionId)
            ? "Karaoke: chưa có session — bấm KARAOKE ON"
            : $"Karaoke session: {KaraokeSessionId} · đã đồng bộ URL (operator)";
    }

    /// <summary>Tạo session mới (operator). Không share link.</summary>
    [RelayCommand]
    private void NewKaraokeSession()
    {
        if (GuardIfLocked("create Karaoke session")) return;
        if (RejectKaraokeSourceChangeWhileFrozen()) return;

        KaraokeSessionId = GenerateKaraokeSessionId();
        KaraokeMasterReady = false;
        KaraokeNowTitle = "—";
        RebuildKaraokeUrls();
        Settings.Current.KaraokeSessionId = KaraokeSessionId;
        _ = Settings.SaveAsync();
        KaraokeUrlsChanged?.Invoke(this, EventArgs.Empty);
        RefreshKaraokeStatusUi();
        StatusMessage = $"Session mới: {KaraokeSessionId} · chỉ bạn dùng (không share)";
    }

    private bool RejectKaraokeSourceChangeWhileFrozen()
    {
        if (!_videoPlayer.IsFrozen) return false;
        StatusMessage = "OUTPUT FROZEN · hãy UNFREEZE trước khi đổi Karaoke session";
        return true;
    }

    /// <summary>1 nút: bật/tắt full karaoke desk (session + màn + remote).</summary>
    [RelayCommand]
    private void ToggleKaraokeOnOff()
    {
        KaraokeToggleOutputRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void CopyKaraokeRemoteUrl()
    {
        // Operator only — không khuyến khích share khách
        try
        {
            System.Windows.Clipboard.SetText(KaraokeRemoteUrl);
            StatusMessage = "⚠ Đã copy link operator — KHÔNG gửi khách";
        }
        catch { StatusMessage = "Không copy được clipboard"; }
    }

    /// <summary>
    /// Karaoke-only startup. Legacy media playlists remain loadable for data compatibility.
    /// </summary>
    private void EnsureDefaultPlaylists()
    {
        var karaoke = EnsureKaraokeTab(select: false);
        SelectedPlaylist = karaoke;
        ActiveWorkspace = WorkspaceMode.Show;
    }

    /// <summary>Create Karaoke Web playlist tab if missing.</summary>
    private PlaylistViewModel EnsureKaraokeTab(bool select)
    {
        var existing = Playlists.FirstOrDefault(p => p.Type == PlaylistType.Karaoke);
        if (existing is null)
        {
            existing = new PlaylistViewModel(new PlaylistModel
            {
                Name = "🌐 Karaoke Web",
                Type = PlaylistType.Karaoke
            });
            Playlists.Add(existing);
        }

        if (select)
            SelectedPlaylist = existing;

        return existing;
    }

    // ─── Commands ─────────────────────────────────────────────────

    private bool GuardIfLocked(string action)
    {
        if (!IsLiveLocked) return false;
        StatusMessage = $"Đang khóa live — không {action}";
        return true;
    }

    public bool CanMutateShowConfiguration(string action) => !GuardIfLocked(action);

    partial void OnIsLiveModeChanged(bool value)
    {
        // Laptop không sleep / tắt màn khi live (cưới, HN, YEP)
        SleepPreventer.SetActive(value);

        if (value)
        {
            IsEditMode = false;
            ValidateAllMedia();
            StatusMessage = MissingMediaCount > 0
                ? $"LIVE · ⚠ {MissingMediaCount} file thiếu — kiểm tra USB"
                : "LIVE MODE — GO · Space · L = khóa · không sleep";
        }
        else
        {
            StatusMessage = "Thoát Live mode";
        }
        RefreshNowNextBar();
    }

    partial void OnIsLiveLockedChanged(bool value)
    {
        IsShowConfigurationEnabled = !value;
        if (value)
        {
            IsEditMode = false;
            Volatile.Read(ref _importCts)?.Cancel();
        }
        StatusMessage = value
            ? "🔒 SHOW LOCK — chỉ vận hành, không sửa cấu hình"
            : "Mở khóa — có thể edit lại";
    }

    [RelayCommand]
    private void ToggleLiveMode() => IsLiveMode = !IsLiveMode;

    [RelayCommand]
    private void ToggleLiveLock() => IsLiveLocked = !IsLiveLocked;

    /// <summary>Quét toàn bộ cue — đánh dấu file thiếu (USB rút, path đổi).</summary>
    [RelayCommand]
    private void ValidateAllMedia()
    {
        int missing = 0;
        foreach (var p in Playlists)
        {
            foreach (var c in p.Cues)
            {
                c.RefreshMediaStatus();
                if (c.IsMediaMissing && !string.IsNullOrWhiteSpace(c.Model.FilePath))
                    missing++;
                else if (c.IsMediaMissing && string.IsNullOrWhiteSpace(c.Model.FilePath))
                { /* empty card — không tính */ }
                else if (c.IsMediaMissing)
                    missing++;
            }
        }

        // Chỉ đếm card có path nhưng file không tồn tại
        missing = Playlists.SelectMany(p => p.Cues)
            .Count(c => !string.IsNullOrWhiteSpace(c.Model.FilePath) && c.IsMediaMissing);

        MissingMediaCount = missing;
        StatusMessage = missing == 0
            ? "✓ Media OK — tất cả file có trên đĩa"
            : $"⚠ {missing} cue thiếu file — cắm USB / Locate";
    }

    /// <summary>Export setlist TXT/CSV cho MC / đạo diễn.</summary>
    [RelayCommand]
    private async Task ExportSetlistAsync()
    {
        if (GuardIfLocked("export setlist")) return;

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export setlist",
            Filter = "Text (*.txt)|*.txt|CSV (*.csv)|*.csv",
            FileName = $"{SanitizeFileName(ProjectName)}_setlist",
            DefaultExt = ".txt"
        };
        if (dlg.ShowDialog() != true) return;

        bool csv = dlg.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);
        var sb = new StringBuilder();
        var now = DateTime.Now;

        if (csv)
        {
            sb.AppendLine("Playlist,No,Title,Artist,Duration,Hotkey,Notes,File,Status");
        }
        else
        {
            sb.AppendLine($"SETLIST — {ProjectName}");
            sb.AppendLine($"Xuất: {now:yyyy-MM-dd HH:mm}");
            sb.AppendLine(new string('=', 48));
        }

        foreach (var pl in Playlists.Where(p => !p.IsKaraoke))
        {
            if (!csv)
            {
                sb.AppendLine();
                sb.AppendLine($"## {pl.Name} ({pl.Type})");
                sb.AppendLine(new string('-', 40));
            }

            int n = 0;
            foreach (var c in pl.Cues)
            {
                n++;
                c.RefreshMediaStatus();
                string status = string.IsNullOrWhiteSpace(c.Model.FilePath) ? "empty"
                    : c.IsMediaMissing ? "MISSING" : "ok";
                string notes = (c.Notes ?? "").Replace("\r", " ").Replace("\n", " ");
                string file = c.Model.FilePath ?? "";
                string dur = c.Duration;

                if (csv)
                {
                    sb.AppendLine(string.Join(",",
                        Csv(pl.Name), n, Csv(c.Title), Csv(c.Artist), Csv(dur),
                        Csv(c.HotkeyText), Csv(notes), Csv(file), status));
                }
                else
                {
                    sb.AppendLine($"{n,2}. {c.Title}");
                    if (!string.IsNullOrWhiteSpace(c.Artist))
                        sb.AppendLine($"     Artist : {c.Artist}");
                    sb.AppendLine($"     Time   : {dur}    Hotkey: {c.HotkeyText}");
                    if (!string.IsNullOrWhiteSpace(notes))
                        sb.AppendLine($"     Note   : {notes}");
                    if (status == "MISSING")
                        sb.AppendLine($"     ⚠ FILE THIẾU: {file}");
                    else if (!string.IsNullOrWhiteSpace(file))
                        sb.AppendLine($"     File   : {System.IO.Path.GetFileName(file)}");
                }
            }
        }

        if (!csv)
        {
            sb.AppendLine();
            sb.AppendLine(new string('=', 48));
            sb.AppendLine("7zyx Media — setlist export");
        }

        await File.WriteAllTextAsync(dlg.FileName, sb.ToString(), Encoding.UTF8);
        StatusMessage = $"Đã export setlist: {System.IO.Path.GetFileName(dlg.FileName)}";
    }

    private static string Csv(string? s)
    {
        s ??= "";
        if (s.Contains('"') || s.Contains(',') || s.Contains('\n'))
            return $"\"{s.Replace("\"", "\"\"")}\"";
        return s;
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in System.IO.Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "show" : name.Trim();
    }

    private void RefreshNowNextBar()
    {
        var playlist = SelectedPlaylist;
        if (playlist is null || playlist.IsKaraoke)
        {
            // Global: tìm bất kỳ cue đang phát
            var any = Playlists.SelectMany(p => p.Cues).FirstOrDefault(c => c.IsPlaying || c.IsPaused);
            if (any is null)
            {
                NowTitle = "—";
                NowRemaining = "0:00";
                NextTitle = "—";
                NowNotes = string.Empty;
                NowIsUrgent = false;
                NowIsPlaying = false;
                return;
            }
            ApplyNowNextFromCue(any, FindNextPlayableCue(any));
            return;
        }

        var current = playlist.Cues.FirstOrDefault(c => c.IsPlaying)
                      ?? playlist.Cues.FirstOrDefault(c => c.IsPaused);
        if (current is null)
        {
            var standby = ResolveStandbyCue(playlist, null);
            NowTitle = "Chưa phát";
            NowRemaining = "—";
            NextTitle = standby?.Title ?? "Chọn một cue";
            NowNotes = standby?.Notes?.Trim() ?? string.Empty;
            NowIsUrgent = false;
            NowIsPlaying = false;
            return;
        }

        var standbyCue = SelectedCue is not null
                         && playlist.Cues.Contains(SelectedCue)
                         && !ReferenceEquals(SelectedCue, current)
            ? SelectedCue
            : FindNextPlayableInPlaylist(playlist, current);
        ApplyNowNextFromCue(current, standbyCue);
    }

    private void ApplyNowNextFromCue(CueCardViewModel current, CueCardViewModel? next)
    {
        NowTitle = current.IsPaused ? $"⏸ {current.Title}" : current.Title;
        // Remaining already like "-1:23" — strip minus for display
        var rem = current.Remaining?.TrimStart('-') ?? "0:00";
        NowRemaining = rem;
        NextTitle = next?.Title ?? "(hết playlist)";
        NowIsPlaying = current.IsPlaying;
        NowNotes = string.IsNullOrWhiteSpace(current.Notes) ? string.Empty : current.Notes!.Trim();
        // Parse remaining roughly for urgency
        NowIsUrgent = current.IsPlaying && TryParseRemainingSeconds(rem, out var sec) && sec <= 15;
    }

    private static bool TryParseRemainingSeconds(string rem, out double seconds)
    {
        seconds = 0;
        if (string.IsNullOrWhiteSpace(rem) || rem == "—") return false;
        var parts = rem.Split(':');
        try
        {
            if (parts.Length == 2)
            {
                seconds = int.Parse(parts[0]) * 60 + int.Parse(parts[1]);
                return true;
            }
            if (parts.Length == 3)
            {
                seconds = int.Parse(parts[0]) * 3600 + int.Parse(parts[1]) * 60 + int.Parse(parts[2]);
                return true;
            }
        }
        catch { /* ignore */ }
        return false;
    }

    [RelayCommand]
    private async Task AddMediaAsync()
    {
        if (GuardIfLocked("thêm media")) return;
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Thêm media",
            Filter = "Media hỗ trợ|*.mp3;*.wav;*.flac;*.aac;*.m4a;*.aiff;*.aif;*.ogg;*.opus;*.wma;*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.m4v;*.jpg;*.jpeg;*.png;*.bmp;*.gif|Tất cả file|*.*",
            Multiselect = true
        };
        if (dlg.ShowDialog() != true) return;
        await ImportMediaAutomaticallyAsync(dlg.FileNames);
    }

    [RelayCommand]
    private async Task AddAudioAsync()
    {
        if (GuardIfLocked("thêm audio")) return;
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Add Audio Files",
            Filter = "Audio Files|*.mp3;*.wav;*.flac;*.aac;*.m4a;*.aiff;*.aif;*.ogg;*.opus;*.wma|All Files|*.*",
            Multiselect = true
        };
        if (dlg.ShowDialog() != true) return;
        var playlist = EnsurePlaylistType(PlaylistType.Audio);
        await ImportIntoPlaylistAsync(dlg.FileNames, playlist);
    }

    [RelayCommand]
    private async Task AddVideoAsync()
    {
        if (GuardIfLocked("thêm video")) return;
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Add Video / Image Files",
            Filter = "Media Files|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.m4v;*.jpg;*.jpeg;*.png;*.bmp;*.gif|Video Files|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.m4v|Image Files|*.jpg;*.jpeg;*.png;*.bmp;*.gif|All Files|*.*",
            Multiselect = true
        };
        if (dlg.ShowDialog() != true) return;
        var playlist = EnsurePlaylistType(PlaylistType.Video);
        await ImportIntoPlaylistAsync(dlg.FileNames, playlist);
    }

    [RelayCommand]
    private async Task AddPhotoAsync()
    {
        if (GuardIfLocked("thêm ảnh")) return;
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Add Photo Files",
            Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp;*.gif|All Files|*.*",
            Multiselect = true
        };
        if (dlg.ShowDialog() != true) return;
        var playlist = EnsurePlaylistType(PlaylistType.Video);
        await ImportIntoPlaylistAsync(dlg.FileNames, playlist);
    }

    private PlaylistViewModel EnsurePlaylistType(PlaylistType type, bool select = true)
    {
        var playlist = SelectedPlaylist?.Type == type
            ? SelectedPlaylist
            : Playlists.FirstOrDefault(p => p.Type == type);

        if (playlist is null)
        {
            var model = new PlaylistModel
            {
                Name = type == PlaylistType.Video
                    ? $"Video Playlist {Playlists.Count(p => p.Type == PlaylistType.Video) + 1}"
                    : $"Audio Playlist {Playlists.Count(p => p.Type == PlaylistType.Audio) + 1}",
                Type = type
            };
            playlist = new PlaylistViewModel(model);
            Playlists.Add(playlist);
        }

        if (select)
            SelectedPlaylist = playlist;

        return playlist;
    }

    [RelayCommand]
    private void OpenKaraoke()
    {
        EnsureKaraokeTab(select: true);
    }

    [RelayCommand]
    private async Task AddFolderAsync()
    {
        if (GuardIfLocked("import folder")) return;
        var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select folder to import",
            UseDescriptionForTitle = true
        };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        await ImportMediaAutomaticallyAsync(new[] { dlg.SelectedPath });
    }

    [RelayCommand]
    private async Task SaveProjectAsync()
    {
        if (string.IsNullOrEmpty(_project.CurrentFilePath))
        {
            await SaveProjectAsAsync();
            return;
        }
        SyncProjectModel();
        await _project.SaveAsync();
        RememberRecentProject(_project.CurrentFilePath);
        StatusMessage = $"Đã lưu: {System.IO.Path.GetFileName(_project.CurrentFilePath)}";
    }

    [RelayCommand]
    private async Task SaveProjectAsAsync()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save Project",
            Filter = "Show Cue Project|*.7zyx",
            DefaultExt = ".7zyx"
        };
        if (dlg.ShowDialog() != true) return;
        SyncProjectModel();
        await _project.SaveAsAsync(dlg.FileName);
        ProjectName = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName);
        RememberRecentProject(dlg.FileName);
        StatusMessage = $"Đã lưu: {dlg.FileName}";
    }

    [RelayCommand]
    private async Task OpenProjectAsync()
    {
        if (GuardIfLocked("open project")) return;
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Open Project",
            Filter = "Show Cue Project|*.7zyx|All Files|*.*"
        };
        if (dlg.ShowDialog() != true) return;
        var proj = await _project.OpenAsync(dlg.FileName);
        if (proj is null) return;
        await LoadProjectIntoGridAsync(proj);
        ProjectName = proj.Name;
        RememberRecentProject(dlg.FileName);
        StatusMessage = $"Đã mở: {proj.Name}";
    }

    private void RememberRecentProject(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        Settings.Current.LastProjectPath = path;
        var list = Settings.Current.RecentProjects ?? new List<string>();
        list.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, path);
        if (list.Count > 12) list = list.Take(12).ToList();
        Settings.Current.RecentProjects = list;
        _ = Settings.SaveAsync();
    }

    // ─── Live GO / NEXT / PREV / số 1–9 ───────────────────────────

    /// <summary>Đưa một cue vào trạng thái chờ. GO luôn ưu tiên cue này.</summary>
    [RelayCommand]
    private void SelectCue(CueCardViewModel? cue)
    {
        if (cue is null || SelectedPlaylist is null || !SelectedPlaylist.Cues.Contains(cue))
            return;

        SelectedCue = cue;
        if (string.IsNullOrWhiteSpace(cue.Model.FilePath))
            StatusMessage = $"Cue chờ: {cue.Title} · chưa gán file";
        else if (cue.IsMediaMissing)
            StatusMessage = $"Cue chờ: {cue.Title} · ⚠ thiếu file";
        else
            StatusMessage = $"Cue chờ: {cue.Title} · nhấn Space để GO";
        RefreshNowNextBar();
    }

    partial void OnSelectedCueChanged(CueCardViewModel? oldValue, CueCardViewModel? newValue)
    {
        if (oldValue is not null)
            oldValue.IsSelected = false;
        if (newValue is not null)
        {
            newValue.IsSelected = true;
            _ = PrepareStandbyCueAsync(newValue);
        }
    }

    private async Task PrepareStandbyCueAsync(CueCardViewModel cue)
    {
        try
        {
            bool ready = await cue.PreparePlaybackAsync();
            if (!ready && ReferenceEquals(SelectedCue, cue))
                StatusMessage = $"Cue chờ '{cue.Title}' không load được";
        }
        catch
        {
            if (ReferenceEquals(SelectedCue, cue))
                StatusMessage = $"Cue chờ '{cue.Title}' không load được";
        }
    }

    /// <summary>Space / GO: phát cue chờ; nếu chưa chọn thì dùng cue hợp lệ đầu tiên.</summary>
    [RelayCommand]
    private async Task GoAsync()
    {
        if (SelectedPlaylist is null) return;

        if (SelectedPlaylist.IsKaraoke)
        {
            if (RejectKaraokeSourceChangeWhileFrozen()) return;

            ApplyKaraokeSession();
            KaraokeToggleOutputRequested?.Invoke(this, EventArgs.Empty);
            StatusMessage = "GO · Karaoke output";
            return;
        }

        var current = SelectedPlaylist.Cues.FirstOrDefault(c => c.IsPlaying || c.IsPaused);
        var target = ResolveStandbyCue(SelectedPlaylist, current);

        if (current is not null && ReferenceEquals(target, current))
        {
            if (current.IsPaused)
            {
                await current.PlayCommand.ExecuteAsync(null);
                if (!current.IsPlaying)
                {
                    StatusMessage = $"Không thể GO · '{current.Title}' không tiếp tục được";
                    return;
                }
                ClearTestPatternAfterLiveStart();
                StatusMessage = $"GO · tiếp tục: {current.Title}";
                return;
            }

            target = FindNextPlayableInPlaylist(SelectedPlaylist, current);
        }

        if (target is null)
        {
            StatusMessage = current is null
                ? "GO · tab chưa có media"
                : "GO · đã hết playlist";
            return;
        }
        if (target.IsMediaMissing)
        {
            StatusMessage = $"Không thể GO · cue '{target.Title}' đang thiếu file";
            return;
        }

        await TransitionToCueAsync(current, target, "GO");
    }

    [RelayCommand]
    private async Task GoNextAsync()
    {
        if (SelectedPlaylist is null || SelectedPlaylist.IsKaraoke)
        {
            StatusMessage = "NEXT · chọn tab audio/video";
            return;
        }

        var current = SelectedPlaylist.Cues.FirstOrDefault(c => c.IsPlaying || c.IsPaused);
        CueCardViewModel? next;
        if (current is not null)
            next = FindNextPlayableInPlaylist(SelectedPlaylist, current);
        else
            next = ResolveStandbyCue(SelectedPlaylist, null);

        if (next is null)
        {
            StatusMessage = "NEXT · hết playlist";
            return;
        }

        await TransitionToCueAsync(current, next, "NEXT");
    }

    [RelayCommand]
    private async Task GoPreviousAsync()
    {
        if (SelectedPlaylist is null || SelectedPlaylist.IsKaraoke)
        {
            StatusMessage = "PREV · chọn tab audio/video";
            return;
        }

        var current = SelectedPlaylist.Cues.FirstOrDefault(c => c.IsPlaying || c.IsPaused);
        if (current is null)
        {
            CueCardViewModel? target = null;
            if (SelectedCue is not null && SelectedPlaylist.Cues.Contains(SelectedCue))
                target = FindPreviousPlayableInPlaylist(SelectedPlaylist, SelectedCue);
            if (target is null
                && _lastPlayedCue is not null
                && SelectedPlaylist.Cues.Contains(_lastPlayedCue))
            {
                target = _lastPlayedCue;
            }

            if (target is null)
            {
                StatusMessage = "PREV · chưa có cue trước đó";
                return;
            }

            await TransitionToCueAsync(null, target, "PREV");
            return;
        }

        var prev = FindPreviousPlayableInPlaylist(SelectedPlaylist, current);
        if (prev is null)
        {
            StatusMessage = "PREV · đang ở đầu playlist";
            return;
        }
        await TransitionToCueAsync(current, prev, "PREV");
    }

    /// <summary>Play cue theo số thứ tự 1–9 trong tab hiện tại.</summary>
    [RelayCommand]
    private async Task PlayCueByNumberAsync(int number)
    {
        if (SelectedPlaylist is null || SelectedPlaylist.IsKaraoke) return;
        if (number < 1) return;

        var playable = SelectedPlaylist.Cues
            .Where(IsCuePlayable)
            .ToList();
        if (number > playable.Count)
        {
            StatusMessage = $"Không có cue #{number}";
            return;
        }

        var target = playable[number - 1];
        SelectedCue = target;
        var current = SelectedPlaylist.Cues.FirstOrDefault(c => c.IsPlaying || c.IsPaused);
        await TransitionToCueAsync(current, target, $"Cue #{number}");
    }

    private CueCardViewModel? ResolveStandbyCue(PlaylistViewModel playlist, CueCardViewModel? current)
    {
        if (SelectedCue is not null
            && playlist.Cues.Contains(SelectedCue)
            && !string.IsNullOrWhiteSpace(SelectedCue.Model.FilePath))
            return SelectedCue;

        var candidate = current is null
            ? playlist.Cues.FirstOrDefault(IsCuePlayable)
            : FindNextPlayableInPlaylist(playlist, current);
        SelectedCue = candidate;
        return candidate;
    }

    private static CueCardViewModel? FindNextPlayableInPlaylist(PlaylistViewModel playlist, CueCardViewModel current)
    {
        var idx = playlist.Cues.IndexOf(current);
        if (idx < 0) return null;
        for (int i = idx + 1; i < playlist.Cues.Count; i++)
        {
            if (IsCuePlayable(playlist.Cues[i]))
                return playlist.Cues[i];
        }
        return null;
    }

    private static CueCardViewModel? FindPreviousPlayableInPlaylist(PlaylistViewModel playlist, CueCardViewModel current)
    {
        var idx = playlist.Cues.IndexOf(current);
        if (idx < 0) return null;
        for (int i = idx - 1; i >= 0; i--)
        {
            if (IsCuePlayable(playlist.Cues[i]))
                return playlist.Cues[i];
        }
        return null;
    }

    private static bool IsCuePlayable(CueCardViewModel cue)
        => !string.IsNullOrWhiteSpace(cue.Model.FilePath) && !cue.IsMediaMissing;

    [RelayCommand]
    private void StopAll()
    {
        InvalidateMixerTake();
        _audio.StopAll();
        foreach (var p in Playlists)
            foreach (var cue in p.Cues)
                if (cue.IsPlaying || cue.IsPaused)
                    _ = cue.StopCommand.ExecuteAsync(null);

        // Keep projector black — never flash Windows desktop mid-show
        _videoPlayer.Stop();

        StatusMessage = "All stopped · giữ hình Program cuối";
        RefreshOutputStatus();
    }

    /// <summary>
    /// Esc: stop only the active playlist tab + tắt hẳn STAND BY / output nếu đang bật.
    /// Karaoke tab → close secondary screen.
    /// Dùng phím B nếu chỉ muốn blackout giữ logo (không tắt output).
    /// </summary>
    [RelayCommand]
    private void StopSelectedTab()
    {
        InvalidateMixerTake();
        if (SelectedPlaylist is null)
        {
            StatusMessage = "No playlist selected";
            return;
        }

        int stopped = 0;
        bool hadVideo = false;
        // STAND BY = output armed, logo hiện, không còn media
        bool wasStandBy = _videoPlayer.IsOutputArmed && !_videoPlayer.IsPlaying;

        foreach (var cue in SelectedPlaylist.Cues)
        {
            if (!cue.IsPlaying && !cue.IsPaused) continue;
            if (cue.IsVideo) hadVideo = true;
            _ = cue.StopCommand.ExecuteAsync(null);
            stopped++;
        }

        if (SelectedPlaylist.IsKaraoke)
        {
            // MainWindow closes karaoke secondary output on this event
            KaraokeOutputStopRequested?.Invoke(this, EventArgs.Empty);
            StatusMessage = "Karaoke output OFF (Esc)";
            RefreshOutputStatus();
            return;
        }

        // Esc = TẮT hẳn màn video output (không để kẹt STAND BY / logo)
        // Phím B vẫn blackout giữ logo giữa các bài.
        bool shouldCloseOutput =
            wasStandBy
            || hadVideo
            || SelectedPlaylist.IsVideo
            || (_videoPlayer.IsOutputArmed && !_videoPlayer.IsPlaying);

        if (shouldCloseOutput && _videoPlayer.IsOutputArmed)
        {
            IsVideoOutputEnabled = false; // Disarm → đóng cửa sổ, hết STAND BY
            StatusMessage = stopped > 0
                ? $"Tab off: {SelectedPlaylist.Name} ({stopped} cue) · Output OFF"
                : $"Output OFF · STAND BY cleared ({SelectedPlaylist.Name})";
        }
        else
        {
            StatusMessage = stopped > 0
                ? $"Tab off: {SelectedPlaylist.Name} ({stopped} cue)"
                : $"Tab off: {SelectedPlaylist.Name} (nothing playing)";
        }

        RefreshOutputStatus();
    }

    /// <summary>Raised when Esc is used on Karaoke tab — MainWindow closes secondary WebView.</summary>
    public event EventHandler? KaraokeOutputStopRequested;

    /// <summary>Arm / disarm fullscreen output on the secondary monitor.</summary>
    [RelayCommand]
    private void ToggleVideoOutput()
    {
        if (GuardIfLocked("change output configuration")) return;
        IsVideoOutputEnabled = !IsVideoOutputEnabled;
    }

    /// <summary>
    /// OBS-style: chon man hinh va bat Output trong mot thao tac (chuot phai -> chon man).
    /// Neu dang phat tren man khac, chuyen ngay sang man moi.
    /// </summary>
    [RelayCommand]
    private void SelectScreenAndStartOutput(VideoScreenInfo? screen)
    {
        if (GuardIfLocked("chon man hinh output")) return;
        if (screen is null) return;

        bool wasArmed = _videoPlayer.IsOutputArmed;
        SelectedVideoScreen = screen;

        if (!wasArmed)
        {
            IsVideoOutputEnabled = true;
            StatusMessage = $"Output ON -> {screen.DisplayName}";
        }
        else
        {
            StatusMessage = $"Program chuyen sang {screen.DisplayName}";
        }
    }

    /// <summary>Tat output (dung tu menu chuot phai).</summary>
    [RelayCommand]
    private void DisableOutput()
    {
        if (GuardIfLocked("tat output")) return;
        if (!_videoPlayer.IsOutputArmed) return;
        IsVideoOutputEnabled = false;
        StatusMessage = "Output OFF";
    }

    /// <summary>OBS-style: Mo khung chieu chuong trinh dang cua so.</summary>
    [RelayCommand]
    private void StartWindowedOutput()
    {
        if (GuardIfLocked("mo cua so moi")) return;
        var primary = VideoScreens.FirstOrDefault(screen => screen.IsPrimary)
            ?? VideoScreens.FirstOrDefault();
        _videoPlayer.SetWindowedOutput(true, primary?.Screen);
        if (primary is not null)
            SelectedVideoScreen = primary;
        if (!_videoPlayer.IsOutputArmed)
        {
            IsVideoOutputEnabled = true;
            StatusMessage = "Mo khung chieu chuong trinh -> Cua so moi";
        }
        else
        {
            StatusMessage = "Chuyen chuong trinh sang Cua so moi";
        }
    }

    /// <summary>OBS-style: Chup anh man hinh chuong trinh.</summary>
    [RelayCommand]
    private void CaptureOutputScreenshot()
    {
        _videoPlayer.CaptureOutputScreenshot();
    }


    [RelayCommand]
    private void DeleteEmpty()
    {
        if (GuardIfLocked("xóa cue trống")) return;
        if (SelectedPlaylist is null) return;
        var empty = SelectedPlaylist.Cues.Where(c => string.IsNullOrEmpty(c.Model.FilePath)).ToList();
        foreach (var c in empty) { c.Dispose(); SelectedPlaylist.Cues.Remove(c); }
        if (SelectedCue is not null && empty.Contains(SelectedCue))
            SelectedCue = null;
        RefreshFilter();
        StatusMessage = $"Đã xóa {empty.Count} cue trống";
    }

    [RelayCommand]
    private void DeleteAll()
    {
        if (GuardIfLocked("xóa tất cả cue")) return;
        if (SelectedPlaylist is null) return;
        var result = MessageBox.Show("Delete all cue cards in this playlist?", "Confirm",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;
        ProtectProgramBeforeRemoving(SelectedPlaylist.Cues);
        foreach (var c in SelectedPlaylist.Cues) c.Dispose();
        SelectedPlaylist.Cues.Clear();
        SelectedCue = null;
        FilteredCues.Clear();
        RefreshMixerInputs();
        RefreshNowNextBar();
        StatusMessage = "Đã xóa toàn bộ cue trong tab";
    }

    [RelayCommand]
    private void AddEmptyCue()
    {
        if (GuardIfLocked("thêm cue")) return;
        if (SelectedPlaylist is null) return;
        var model = new CueModel
        {
            SortOrder = SelectedPlaylist.Cues.Count,
            Title = $"Cue {SelectedPlaylist.Cues.Count + 1}",
            PlaylistId = SelectedPlaylist.Model.Id.ToString(),
            ColorHex = SelectedPlaylist.Type == PlaylistType.Video ? "#FF5252" : "#6C63FF"
        };
        SelectedPlaylist.Cues.Add(CreateCueVm(model));
        RefreshFilter();
    }

    [RelayCommand]
    private void AddPlaylist()
    {
        if (GuardIfLocked("thêm playlist")) return;
        var pm = new PlaylistModel { Name = $"Audio Playlist {Playlists.Count(p => p.Type == PlaylistType.Audio) + 1}", Type = PlaylistType.Audio };
        var pvm = new PlaylistViewModel(pm);
        Playlists.Add(pvm);
        SelectedPlaylist = pvm;
    }

    [RelayCommand]
    private void AddVideoPlaylist()
    {
        if (GuardIfLocked("thêm playlist")) return;
        var pm = new PlaylistModel { Name = $"Video Playlist {Playlists.Count(p => p.Type == PlaylistType.Video) + 1}", Type = PlaylistType.Video };
        var pvm = new PlaylistViewModel(pm);
        Playlists.Add(pvm);
        SelectedPlaylist = pvm;
    }

    [RelayCommand]
    private void RemovePlaylist(PlaylistViewModel? pvm)
    {
        if (GuardIfLocked("xóa playlist")) return;
        if (pvm is null) return;
        if (Playlists.Count == 1)
        {
            MessageBox.Show("Cannot delete the last playlist.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var result = MessageBox.Show($"Are you sure you want to delete the playlist '{pvm.Name}' and all its cues? This action cannot be undone.", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result == MessageBoxResult.Yes)
        {
            // Xóa tab Karaoke → tắt OUTPUT (tránh UI kẹt ON)
            if (pvm.IsKaraoke)
                KaraokeOutputStopRequested?.Invoke(this, EventArgs.Empty);

            ProtectProgramBeforeRemoving(pvm.Cues);
            foreach (var c in pvm.Cues) c.Dispose();
            Playlists.Remove(pvm);
            if (SelectedPlaylist == pvm)
                SelectedPlaylist = Playlists.FirstOrDefault();
            RefreshMixerInputs();
        }
    }

    [RelayCommand]
    private void RenamePlaylist(PlaylistViewModel? pvm)
    {
        if (GuardIfLocked("đổi tên playlist")) return;
        if (pvm is null) return;
        var dlg = new ShowCuePlayer.Controls.Dialogs.InputDialog("Rename Playlist", "Enter new name:", pvm.Name);
        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.Result))
        {
            pvm.Name = dlg.Result;
            pvm.Model.Name = dlg.Result;
        }
    }

    [RelayCommand]
    private void OpenSettings()
    {
        if (GuardIfLocked("mở settings")) return;
        var win = new Views.SettingsView(_services.GetRequiredService<SettingsViewModel>());
        win.ShowDialog();
    }

    // ─── Public helpers called from code-behind ───────────────────

    public void RemoveCue(CueCardViewModel vm)
    {
        if (GuardIfLocked("xóa cue")) return;
        if (SelectedPlaylist is null) return;
        ProtectProgramBeforeRemoving(new[] { vm });
        var removedIndex = SelectedPlaylist.Cues.IndexOf(vm);
        bool wasSelected = ReferenceEquals(SelectedCue, vm);
        if (ReferenceEquals(_lastPlayedCue, vm))
            _lastPlayedCue = null;
        vm.PlaybackStarted -= OnCuePlaybackStarted;
        vm.VisualProgramChangeRequested -= OnVisualProgramChangeRequested;
        vm.NaturalEndReached -= OnCueNaturalEndReached;
        vm.CrossfadeWindowReached -= OnCrossfadeWindowReached;
        vm.Dispose();
        SelectedPlaylist.Cues.Remove(vm);
        if (wasSelected)
        {
            SelectedCue = SelectedPlaylist.Cues
                .Skip(Math.Max(0, removedIndex))
                .FirstOrDefault(IsCuePlayable)
                ?? SelectedPlaylist.Cues
                    .Take(Math.Max(0, removedIndex))
                    .LastOrDefault(IsCuePlayable);
        }
        RefreshFilter();
        RefreshNowNextBar();
        StatusMessage = $"Đã xóa: {vm.Title}";
    }

    private void ProtectProgramBeforeRemoving(IEnumerable<CueCardViewModel> cues)
    {
        var removing = cues.ToHashSet();
        if (removing.Count == 0) return;

        bool invalidated = false;
        if (MixerPreviewInput is not null && removing.Contains(MixerPreviewInput))
        {
            MixerPreviewInput = null;
            InvalidateMixerTake();
            invalidated = true;
        }
        if (MixerProgramInput is not null && removing.Contains(MixerProgramInput))
        {
            MixerProgramInput = null;
            if (!invalidated) InvalidateMixerTake();
            invalidated = true;
        }

        bool removesPendingTakeCue =
            (_mixerTakeTargetId is Guid targetId && removing.Any(cue => cue.Model.Id == targetId)) ||
            (_mixerStartingCueId is Guid startingId && removing.Any(cue => cue.Model.Id == startingId));
        if (removesPendingTakeCue)
        {
            if (!invalidated) InvalidateMixerTake();
            invalidated = true;
            if (_videoPlayer.IsOutputArmed)
            {
                _videoPlayer.ShowSafeScene(Settings.Current.EmergencySafeSceneImagePath);
                IsSafeScene = _videoPlayer.IsSafeScene;
                StatusMessage = "Pending Program source removed · SAFE SCENE";
            }
        }

        if (_videoPlayer.ActiveOwnerId is not Guid ownerId ||
            !removing.Any(cue => cue.Model.Id == ownerId))
            return;

        if (!invalidated) InvalidateMixerTake();
        _videoPlayer.ShowSafeScene(Settings.Current.EmergencySafeSceneImagePath);
        IsSafeScene = _videoPlayer.IsSafeScene;
        StatusMessage = "Program source removed · SAFE SCENE";
    }

    public void DuplicateCue(CueCardViewModel source)
    {
        if (GuardIfLocked("duplicate cue")) return;
        if (SelectedPlaylist is null) return;
        var model = new CueModel
        {
            Title = source.Model.Title + " (copy)",
            Artist = source.Model.Artist,
            FilePath = source.Model.FilePath,
            Volume = source.Model.Volume,
            IsLooping = source.Model.IsLooping,
            ColorHex = source.Model.ColorHex,
            FadeInSeconds = source.Model.FadeInSeconds,
            FadeOutSeconds = source.Model.FadeOutSeconds,
            CueInPoint = source.Model.CueInPoint,
            CueOutPoint = source.Model.CueOutPoint,
            CrossfadeSeconds = source.Model.CrossfadeSeconds,
            PlaylistId = SelectedPlaylist.Model.Id.ToString(),
            SortOrder = SelectedPlaylist.Cues.Count
        };
        model.PlaybackMode = source.Model.PlaybackMode;
        SelectedPlaylist.Cues.Add(CreateCueVm(model));
        RefreshFilter();
    }

    // ─── Search ───────────────────────────────────────────────────

    partial void OnSearchTextChanged(string value)
    {
        RefreshFilter();
        SearchSuggestions.Clear();
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length < 2)
        {
            ShowSearchSuggestions = false;
            return;
        }

        foreach (var text in FilteredCues
                     .SelectMany(c => new[] { c.Title, c.Artist, c.Model.Album })
                     .Where(s => !string.IsNullOrWhiteSpace(s))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .Take(8))
        {
            SearchSuggestions.Add(text);
        }
        ShowSearchSuggestions = SearchSuggestions.Count > 0;
    }
    
    partial void OnSelectedPlaylistChanged(PlaylistViewModel? value)
    {
        if (value is not null)
        {
            _project.Current.ActivePlaylistId = value.Model.Id.ToString();
            RefreshFilter();
            if (value.IsKaraoke)
                SelectedCue = null;
            else
                ResolveStandbyCue(value, value.Cues.FirstOrDefault(c => c.IsPlaying || c.IsPaused));
            RefreshNowNextBar();
        }
    }

    private void RefreshFilter()
    {
        RefreshMixerInputs();
        FilteredCues.Clear();
        if (SelectedPlaylist is null) return;
        
        var models = SelectedPlaylist.Cues.Select(c => c.Model).ToList();
        var results = _search.Search(models, SearchText);
        var resultIds = results.Select(m => m.Id).ToHashSet();
        foreach (var cue in SelectedPlaylist.Cues)
            if (resultIds.Contains(cue.Model.Id))
                FilteredCues.Add(cue);
    }

    private void RefreshMixerInputs()
    {
        var inputs = (SelectedPlaylist?.Cues ?? Enumerable.Empty<CueCardViewModel>())
            .Where(cue => !string.IsNullOrWhiteSpace(cue.Model.FilePath)
                && File.Exists(cue.Model.FilePath))
            .ToList();
        if (MixerInputs.SequenceEqual(inputs))
        {
            if (IsMixerWorkspace) EnsureMixerInputThumbnails();
            return;
        }

        MixerInputs.Clear();
        foreach (var input in inputs)
            MixerInputs.Add(input);
            
        MixerPreviewInput ??= MixerInputs.FirstOrDefault();
        if (IsMixerWorkspace) EnsureMixerInputThumbnails();
    }

    private void EnsureMixerInputThumbnails()
    {
        foreach (var input in MixerInputs)
            input.EnsureMixerThumbnailLoaded();
    }

    // ─── Volume ───────────────────────────────────────────────────

    partial void OnMasterVolumeChanged(double value)
    {
        Settings.Current.MasterVolume = Math.Clamp(value, 0, 1);
        ApplyBusVolumes();
    }

    partial void OnAudioBusVolumeChanged(double value)
    {
        Settings.Current.AudioBusVolume = Math.Clamp(value, 0, 1);
        ApplyBusVolumes();
    }

    partial void OnVideoBusVolumeChanged(double value)
    {
        Settings.Current.VideoBusVolume = Math.Clamp(value, 0, 1);
        ApplyBusVolumes();
    }

    partial void OnKaraokeBusVolumeChanged(double value)
    {
        Settings.Current.KaraokeBusVolume = Math.Clamp(value, 0, 1);
    }

    private void ApplyBusVolumes()
    {
        // Karaoke-only: audio is owned by the Program WebView, not the legacy BASS engine.
        _videoPlayer.SetMasterVolume(Math.Clamp(MasterVolume * VideoBusVolume, 0, 1));
    }

    // ─── Device ───────────────────────────────────────────────────

    private void LoadOutputDevices()
    {
        OutputDevices.Clear();
        foreach (var d in _audio.GetOutputDevices())
            OutputDevices.Add(d);
        SelectedDevice = OutputDevices.FirstOrDefault(d => d.Index == _audio.CurrentDeviceIndex)
                         ?? OutputDevices.FirstOrDefault(d => d.IsDefault);
    }

    partial void OnSelectedDeviceChanged(AudioDeviceInfo? value)
    {
        if (value is null) return;
        if (IsLiveLocked)
        {
            var current = OutputDevices.FirstOrDefault(device => device.Index == _audio.CurrentDeviceIndex)
                          ?? OutputDevices.FirstOrDefault(device => device.IsDefault);
            if (!ReferenceEquals(SelectedDevice, current))
                SelectedDevice = current;
            GuardIfLocked("đổi thiết bị audio");
            return;
        }
        _audio.SwitchDevice(value.Index);
    }

    // ─── Drag & Drop ─────────────────────────────────────────────

    public async Task HandleDropAsync(string[] paths)
    {
        if (GuardIfLocked("thêm media bằng kéo thả")) return;
        await ImportMediaAutomaticallyAsync(paths);
    }

    private async Task ImportMediaAutomaticallyAsync(IEnumerable<string> paths)
    {
        var requestedPaths = paths.ToArray();
        await RunImportOperationAsync(async ct =>
        {
            var allPaths = requestedPaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var folders = allPaths.Where(Directory.Exists).ToArray();
            var files = allPaths.Where(File.Exists).Where(MetadataService.IsSupported).ToArray();
            var audioPaths = folders
                .Concat(files.Where(path => !MetadataService.IsVideoFormat(path)))
                .ToArray();
            var videoPaths = folders
                .Concat(files.Where(MetadataService.IsVideoFormat))
                .ToArray();

            if (audioPaths.Length == 0 && videoPaths.Length == 0)
            {
                StatusMessage = "Không có file media được hỗ trợ";
                return;
            }

            var originalPlaylist = SelectedPlaylist;
            var existingAudio = Playlists.FirstOrDefault(p => p.Type == PlaylistType.Audio);
            var existingVideo = Playlists.FirstOrDefault(p => p.Type == PlaylistType.Video);
            var audioTarget = audioPaths.Length > 0 ? EnsurePlaylistType(PlaylistType.Audio, select: false) : null;
            var videoTarget = videoPaths.Length > 0 ? EnsurePlaylistType(PlaylistType.Video, select: false) : null;

            int audioCount = 0;
            int videoCount = 0;
            try
            {
                audioCount = audioTarget is null ? 0 : await ImportFilesCoreAsync(audioPaths, audioTarget, ct);
                ct.ThrowIfCancellationRequested();
                videoCount = videoTarget is null ? 0 : await ImportFilesCoreAsync(videoPaths, videoTarget, ct);
                ct.ThrowIfCancellationRequested();
            }
            finally
            {
                bool selectedPlaylistRemoved = false;
                if (existingAudio is null && audioTarget is not null && audioTarget.Cues.Count == 0)
                {
                    selectedPlaylistRemoved |= ReferenceEquals(SelectedPlaylist, audioTarget);
                    Playlists.Remove(audioTarget);
                }
                if (existingVideo is null && videoTarget is not null && videoTarget.Cues.Count == 0)
                {
                    selectedPlaylistRemoved |= ReferenceEquals(SelectedPlaylist, videoTarget);
                    Playlists.Remove(videoTarget);
                }
                if (selectedPlaylistRemoved)
                    SelectedPlaylist = originalPlaylist is not null && Playlists.Contains(originalPlaylist)
                        ? originalPlaylist
                        : Playlists.FirstOrDefault();
            }

            PlaylistViewModel? destination = null;
            if (originalPlaylist is not null
                && ((ReferenceEquals(originalPlaylist, audioTarget) && audioCount > 0)
                    || (ReferenceEquals(originalPlaylist, videoTarget) && videoCount > 0)))
            {
                destination = originalPlaylist;
            }
            else if (audioCount > 0 && videoCount == 0)
            {
                destination = audioTarget;
            }
            else if (videoCount > 0 && audioCount == 0)
            {
                destination = videoTarget;
            }
            else if (audioCount > 0 || videoCount > 0)
            {
                destination = audioTarget ?? videoTarget;
            }

            // Chỉ tự mở tab đích nếu người dùng không chuyển sang tab khác trong lúc chờ.
            if (destination is not null && ReferenceEquals(SelectedPlaylist, originalPlaylist))
                SelectedPlaylist = destination;

            int total = audioCount + videoCount;
            StatusMessage = total > 0
                ? $"Đã thêm {total} media · Audio {audioCount} · Video/Ảnh {videoCount}"
                : "Không tìm thấy media phù hợp trong lựa chọn";
        });
    }

    private Task ImportIntoPlaylistAsync(IEnumerable<string> paths, PlaylistViewModel targetPlaylist)
    {
        var requestedPaths = paths.ToArray();
        return RunImportOperationAsync(async ct =>
        {
            int count = await ImportFilesCoreAsync(requestedPaths, targetPlaylist, ct);
            StatusMessage = count > 0
                ? $"Đã thêm {count} media vào {targetPlaylist.Name}"
                : "Không tìm thấy media phù hợp trong lựa chọn";
        });
    }

    private async Task RunImportOperationAsync(Func<CancellationToken, Task> operation)
    {
        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _importCts, cts);
        previous?.Cancel();
        bool entered = false;

        try
        {
            await _importGate.WaitAsync(cts.Token);
            entered = true;
            IsImporting = true;
            ImportProgress = 0;
            ImportCurrentFile = string.Empty;
            await operation(cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (ReferenceEquals(Volatile.Read(ref _importCts), cts))
                StatusMessage = $"Không thể thêm media: {ex.Message}";
        }
        finally
        {
            if (entered)
            {
                IsImporting = false;
                ImportCurrentFile = string.Empty;
                _importGate.Release();
            }
            Interlocked.CompareExchange(ref _importCts, null, cts);
            cts.Dispose();
        }
    }

    private async Task<int> ImportFilesCoreAsync(
        IEnumerable<string> paths,
        PlaylistViewModel targetPlaylist,
        CancellationToken ct)
    {
        int imported = 0;

        var progress = new Progress<ImportProgress>(p =>
        {
            if (ct.IsCancellationRequested) return;
            ImportProgress = p.Total > 0 ? (int)(p.Processed * 100.0 / p.Total) : 0;
            ImportTotal = p.Total;
            ImportCurrentFile = System.IO.Path.GetFileName(p.CurrentFile);
            StatusMessage = p.IsComplete
                ? $"Đã đọc {p.Processed} file"
                : $"Đang thêm {p.Processed}/{p.Total}: {ImportCurrentFile}";
        });

        await foreach (var cue in _import.ImportAsync(paths, targetPlaylist.Type, progress, ct))
        {
            ct.ThrowIfCancellationRequested();
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (ct.IsCancellationRequested || IsLiveLocked) return;
                cue.PlaylistId = targetPlaylist.Model.Id.ToString();

                var emptyCard = targetPlaylist.Cues.FirstOrDefault(c => string.IsNullOrEmpty(c.Model.FilePath));
                if (emptyCard is not null)
                {
                    cue.SortOrder = emptyCard.Model.SortOrder;
                    emptyCard.LoadModel(cue);
                }
                else
                {
                    cue.SortOrder = targetPlaylist.Cues.Count;
                    targetPlaylist.Cues.Add(CreateCueVm(cue));
                }
                imported++;
                if (ReferenceEquals(SelectedPlaylist, targetPlaylist))
                    RefreshFilter();
            });
        }

        Application.Current.Dispatcher.Invoke(RefreshMixerInputs);

        if (ReferenceEquals(SelectedPlaylist, targetPlaylist))
        {
            ResolveStandbyCue(targetPlaylist, targetPlaylist.Cues.FirstOrDefault(c => c.IsPlaying || c.IsPaused));
            RefreshNowNextBar();
        }
        return imported;
    }

    // ─── Hotkeys ─────────────────────────────────────────────────

    private void OnHotkeyPressed(object? sender, Guid cueId)
    {
        var vm = Playlists.SelectMany(p => p.Cues).FirstOrDefault(c => c.Model.Id == cueId);
        if (vm is not null)
        {
            vm.PlayCommand.ExecuteAsync(null);
            return;
        }

        var effect = Settings.Current.KaraokeSoundEffects.FirstOrDefault(item =>
            Guid.TryParse(item.Id, out var effectId) && effectId == cueId);
        if (effect is not null)
            SoundEffectHotkeyPressed?.Invoke(effect.Id);
    }

    // ─── Project Sync ─────────────────────────────────────────────

    private void SyncProjectModel()
    {
        _project.Current.Cues = Playlists.SelectMany(p => p.Cues.Select(c => c.Model)).ToList();
        _project.Current.Playlists = Playlists.Select(p => p.Model).ToList();
        _project.Current.Name = ProjectName;
        _project.Current.KaraokeSoundEffects = CloneKaraokeSoundEffects(
            Settings.Current.KaraokeSoundEffects);
    }

    private async Task LoadProjectIntoGridAsync(ProjectModel proj)
    {
        ProtectProgramBeforeRemoving(Playlists.SelectMany(playlist => playlist.Cues));
        SelectedCue = null;
        _lastPlayedCue = null;
        foreach (var p in Playlists)
            foreach (var c in p.Cues)
            {
                c.PlaybackStarted -= OnCuePlaybackStarted;
                c.VisualProgramChangeRequested -= OnVisualProgramChangeRequested;
                c.NaturalEndReached -= OnCueNaturalEndReached;
                c.CrossfadeWindowReached -= OnCrossfadeWindowReached;
                c.Dispose();
            }
        
        Playlists.Clear();
        FilteredCues.Clear();
        _preload.Clear();

        if (proj.Playlists.Count == 0)
        {
            proj.Playlists.Add(new PlaylistModel { Name = "Main Music" });
        }

        foreach (var pModel in proj.Playlists)
        {
            var pvm = new PlaylistViewModel(pModel);
            var cuesForPlaylist = proj.Cues
                .Where(c => c.PlaylistId == pModel.Id.ToString() || string.IsNullOrEmpty(c.PlaylistId))
                .OrderBy(c => c.SortOrder)
                .ToList();
                
            foreach (var cue in cuesForPlaylist)
            {
                cue.PlaylistId = pModel.Id.ToString(); // Upgrade old projects
                pvm.Cues.Add(CreateCueVm(cue));
            }
            Playlists.Add(pvm);
        }

        SelectedPlaylist = Playlists.FirstOrDefault(p => p.Model.Id.ToString() == proj.ActivePlaylistId) 
                           ?? Playlists.FirstOrDefault();

        // Project cũ không có trường này (null) thì giữ Soundboard hiện tại.
        // Project mới có danh sách, kể cả rỗng, thì khôi phục đúng trạng thái đã lưu.
        if (proj.KaraokeSoundEffects is not null)
        {
            Settings.Current.KaraokeSoundEffects = CloneKaraokeSoundEffects(proj.KaraokeSoundEffects);
            await Settings.SaveAsync();
        }
    }

    private static List<KaraokeSoundEffect> CloneKaraokeSoundEffects(
        IEnumerable<KaraokeSoundEffect> effects)
        => effects.Select(effect => new KaraokeSoundEffect
        {
            Id = effect.Id,
            Name = effect.Name,
            FilePath = effect.FilePath,
            Loop = effect.Loop,
            Volume = effect.Volume,
            HotkeyText = effect.HotkeyText
        }).ToList();

    private async Task AutoSaveAsync()
    {
        if (string.IsNullOrEmpty(_project.CurrentFilePath)) return;
        SyncProjectModel();
        await _project.SaveAsync();
    }

    // ─── Cue factory + AutoFollow / AutoContinue ──────────────────

    private CueCardViewModel CreateCueVm(CueModel model)
    {
        var vm = new CueCardViewModel(
            model, _audio, _videoPlayer,
            _services.GetRequiredService<IFadeEngine>(), _hotkeys,
            _services.GetRequiredService<IMetadataService>(),
            _preload,
            Settings);
        vm.PlaybackStarted += OnCuePlaybackStarted;
        vm.VisualProgramChangeRequested += OnVisualProgramChangeRequested;
        vm.NaturalEndReached += OnCueNaturalEndReached;
        vm.CrossfadeWindowReached += OnCrossfadeWindowReached;
        return vm;
    }

    private void OnVisualProgramChangeRequested(object? sender, EventArgs e)
    {
        if (sender is CueCardViewModel { IsVideo: true })
            InvalidateMixerTake();
    }

    private void OnCuePlaybackStarted(object? sender, EventArgs e)
    {
        if (sender is not CueCardViewModel cue) return;
        _lastPlayedCue = cue;
        if (cue.IsVideo)
            MixerProgramInput = cue;

        var owner = Playlists.FirstOrDefault(p => p.Cues.Contains(cue));
        if (owner is not null && ReferenceEquals(owner, SelectedPlaylist))
            SelectedCue = FindNextPlayableInPlaylist(owner, cue);

        // Only real videos are exclusive with other videos. Audio and images stay independent.
        if (cue.IsVideo && !MetadataService.IsImageFormat(cue.Model.FilePath))
        {
            foreach (var other in Playlists.SelectMany(p => p.Cues))
            {
                if (ReferenceEquals(other, cue)
                    || !other.IsVideo
                    || MetadataService.IsImageFormat(other.Model.FilePath)) continue;
                if (other.IsPlaying || other.IsPaused)
                    _ = other.StopCommand.ExecuteAsync(null);
            }
        }

        // Preload next for instant GO/NEXT / AutoFollow
        if (Settings.Current.PreloadNextCue && !cue.IsVideo)
        {
            var next = FindNextPlayableCue(cue);
            if (next is not null && !next.IsVideo)
                _ = _preload.PreloadAsync(next.Model.FilePath);
        }

        if (cue.PlaybackMode != PlaybackMode.AutoContinue) return;

        var follow = FindNextPlayableCue(cue);
        if (follow is null) return;
        StatusMessage = $"AutoContinue → {follow.Title}";
        _ = follow.PlayCommand.ExecuteAsync(null);
        if (Settings.Current.PreloadNextCue)
        {
            var n2 = FindNextPlayableCue(follow);
            if (n2 is not null && !n2.IsVideo)
                _ = _preload.PreloadAsync(n2.Model.FilePath);
        }
    }

    public void StopLocalVideosForKaraoke()
    {
        foreach (var cue in Playlists.SelectMany(p => p.Cues))
        {
            if (!cue.IsVideo || MetadataService.IsImageFormat(cue.Model.FilePath)) continue;
            if (cue.IsPlaying || cue.IsPaused)
                _ = cue.StopCommand.ExecuteAsync(null);
        }
    }

    private void OnCrossfadeWindowReached(object? sender, EventArgs e)
    {
        if (sender is not CueCardViewModel cue) return;
        if (cue.PlaybackMode != PlaybackMode.AutoFollow) return;
        var next = FindNextPlayableCue(cue);
        if (next is null) return;
        _ = TransitionToCueAsync(cue, next, "AutoFollow×fade");
    }

    private void OnCueNaturalEndReached(object? sender, EventArgs e)
    {
        if (sender is not CueCardViewModel cue) return;

        if (cue.PlaybackMode == PlaybackMode.AutoFollow)
        {
            // Crossfade path already started next; only hard-cut if still needed
            var next = FindNextPlayableCue(cue);
            if (next is not null && !next.IsPlaying && !next.IsPaused)
            {
                StatusMessage = $"AutoFollow → {next.Title}";
                _ = TransitionToCueAsync(null, next, "AutoFollow");
                return;
            }
            if (next is null)
                StatusMessage = "AutoFollow: end of playlist";
        }

        RefreshOutputStatus();
    }

    private async Task TransitionToCueAsync(CueCardViewModel? from, CueCardViewModel to, string label)
    {
        if (to.IsVideo && _videoPlayer.IsFrozen)
        {
            StatusMessage = $"Không thể {label} · OUTPUT FROZEN · hãy UNFREEZE trước";
            return;
        }
        if (_transitionBusy)
        {
            StatusMessage = "Đang chuyển cue…";
            return;
        }
        if (to.IsPlaying) return;

        bool programGateEntered = false;
        if (to.IsVideo)
        {
            if (!await _mixerTakeGate.WaitAsync(0))
            {
                StatusMessage = "Program transition already in progress";
                return;
            }
            programGateEntered = true;
            Interlocked.Increment(ref _mixerTakeVersion);
        }

        _transitionBusy = true;
        int detachedOutHandle = 0;
        bool rollbackIncomingCue = false;
        try
        {
            if (!await to.PreparePlaybackAsync())
            {
                StatusMessage = $"Không thể {label} · '{to.Title}' không load được";
                return;
            }

            double xf = 0;
            if (from is not null && !from.IsVideo && !to.IsVideo)
            {
                xf = to.ResolveCrossfadeSeconds();
                if (xf <= 0) xf = from.ResolveCrossfadeSeconds();
            }

            // Crossfade path (audio only)
            if (from is not null && xf > 0 && from.IsPlaying && from.AudioHandle != 0
                && !to.IsVideo && !to.IsPaused)
            {
                double outVol = from.Volume;
                rollbackIncomingCue = true;
                await to.StartCrossfadeInAsync();
                int inHandle = to.AudioHandle;
                if (inHandle != 0 && inHandle != -1)
                {
                    ClearTestPatternAfterLiveStart();
                    detachedOutHandle = from.DetachHandleKeepPlaying();
                    await _crossfade.CrossfadeAsync(detachedOutHandle, inHandle, xf, outVol, to.Model.Volume);
                    from.CompleteDetachedStop();
                    detachedOutHandle = 0;
                    to.MarkPlayingAfterCrossfade();
                    rollbackIncomingCue = false;
                    StatusMessage = $"{label} · crossfade {xf:0.#}s → {to.Title}";
                }
                else
                {
                    to.RollbackFailedTransition();
                    rollbackIncomingCue = false;
                    StatusMessage = $"Không thể {label} · '{to.Title}' không khởi động được";
                    return;
                }
            }
            else
            {
                if (from is not null && (from.IsPlaying || from.IsPaused))
                    await from.StopCommand.ExecuteAsync(null);
                await to.PlayCommand.ExecuteAsync(null);
                if (!to.IsPlaying)
                {
                    StatusMessage = $"Không thể {label} · '{to.Title}' không khởi động được";
                    return;
                }
                ClearTestPatternAfterLiveStart();
                StatusMessage = $"{label} → {to.Title}";
            }

            if (Settings.Current.PreloadNextCue && !to.IsVideo)
            {
                var n2 = FindNextPlayableCue(to);
                if (n2 is not null && !n2.IsVideo)
                    _ = _preload.PreloadAsync(n2.Model.FilePath);
            }
        }
        catch (Exception ex)
        {
            if (detachedOutHandle != 0 && detachedOutHandle != -1)
            {
                try { _audio.Stop(detachedOutHandle); } catch { /* best-effort cleanup */ }
                try { from?.CompleteDetachedStop(); } catch { /* state cleanup must continue */ }
            }
            if (rollbackIncomingCue)
            {
                try { to.RollbackFailedTransition(); } catch { /* preserve original transition error */ }
            }
            StatusMessage = $"Transition error: {ex.Message}";
        }
        finally
        {
            _transitionBusy = false;
            if (programGateEntered)
                _mixerTakeGate.Release();
            RefreshOutputStatus();
        }
    }

    private void ClearTestPatternAfterLiveStart()
    {
        if (!_videoPlayer.IsFrozen && _videoPlayer.ActiveTestPattern is not null)
            _videoPlayer.ClearTestPattern();
    }

    private CueCardViewModel? FindNextPlayableCue(CueCardViewModel current)
    {
        foreach (var p in Playlists)
        {
            var idx = p.Cues.IndexOf(current);
            if (idx < 0) continue;
            for (int i = idx + 1; i < p.Cues.Count; i++)
            {
                var c = p.Cues[i];
                if (IsCuePlayable(c))
                    return c;
            }
            return null;
        }
        return null;
    }

    // ─── Dispose ─────────────────────────────────────────────────

    public void Dispose()
    {
        Interlocked.Exchange(ref _importCts, null)?.Cancel();
        _autoSaveTimer.Stop();
        _nowNextTimer.Stop();
        if (IsLiveMode)
            SleepPreventer.SetActive(false);
        _hotkeys.HotkeyPressed -= OnHotkeyPressed;
        _videoPlayer.OutputStateChanged -= OnVideoOutputStateChanged;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        foreach (var p in Playlists)
            foreach (var c in p.Cues)
            {
                c.PlaybackStarted -= OnCuePlaybackStarted;
                c.VisualProgramChangeRequested -= OnVisualProgramChangeRequested;
                c.NaturalEndReached -= OnCueNaturalEndReached;
                c.CrossfadeWindowReached -= OnCrossfadeWindowReached;
                c.Dispose();
            }
        _preload.Clear();
    }
}
