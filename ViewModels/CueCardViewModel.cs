using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShowCuePlayer.AudioEngine;
using ShowCuePlayer.Helpers;
using ShowCuePlayer.Models;
using ShowCuePlayer.Services;
using System.Collections.Concurrent;
using System.IO;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ShowCuePlayer.ViewModels;

/// <summary>
/// ViewModel for a single cue card. Manages playback state, timers,
/// volume, loop, hotkey display, and visual state (playing border, glow).
/// </summary>
public sealed partial class CueCardViewModel : ObservableObject, IDisposable
{
    private static readonly SemaphoreSlim MixerThumbnailGate = new(1, 1);
    private static readonly ConcurrentDictionary<string, ImageSource> MixerThumbnailCache =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentQueue<string> MixerThumbnailCacheOrder = new();
    private const int MixerThumbnailCacheLimit = 128;
    private readonly IAudioEngine _audio;
    private readonly IVideoPlayerService _videoPlayer;
    private readonly IFadeEngine _fade;
    private readonly IHotkeyService _hotkeys;
    private readonly IMetadataService _metadata;
    private readonly IPreloadManager? _preload;
    private readonly ISettingsService? _settings;
    private readonly DispatcherTimer _timer;
    private int _handle = 0;
    private bool _isSeeking;
    private bool _disposed;
    private int _mixerThumbnailLoadVersion;
    private bool _mixerThumbnailRequested;
    private string? _mixerThumbnailPath;
    private bool _isUpdatingVolume;
    private bool _suppressNaturalEnd;
    private bool _videoEndHooked;
    private bool _crossfadeWindowFired;
    private bool _cueOutHandled;

    // ─── Backing model ────────────────────────────────────────────
    public CueModel Model { get; private set; }

    /// <summary>Current BASS handle (0 if none).</summary>
    public int AudioHandle => _handle;

    /// <summary>Fired when playback starts successfully (for AutoContinue).</summary>
    public event EventHandler? PlaybackStarted;

    /// <summary>Fired before a SHOW transport command changes the visual Program.</summary>
    public event EventHandler? VisualProgramChangeRequested;

    /// <summary>Fired when cue reaches natural end (for AutoFollow / Repeat).</summary>
    public event EventHandler? NaturalEndReached;

    /// <summary>Fired when remaining time ≤ crossfade window (start next early).</summary>
    public event EventHandler? CrossfadeWindowReached;

    public int DisplayNumber => Model != null ? Model.SortOrder + 1 : 0;

    // ─── Observable properties ────────────────────────────────────
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _artist = string.Empty;
    [ObservableProperty] private string _duration = "0:00";
    [ObservableProperty] private string _elapsed = "0:00";
    [ObservableProperty] private string _remaining = "-0:00";
    [ObservableProperty] private double _position;        // 0.0 – 1.0
    /// <summary>True when playing and ≤15s left (countdown đỏ/vàng trên card).</summary>
    [ObservableProperty] private bool _isCountdownUrgent;
    [ObservableProperty] private bool _isCountdownWarning; // ≤30s
    /// <summary>File path empty or file missing on disk.</summary>
    [ObservableProperty] private bool _isMediaMissing;
    [ObservableProperty] private string _mediaStatusTip = string.Empty;
    [ObservableProperty] private double _volume = 1.0;   // 0.0 – 1.0
    [ObservableProperty] private double _volumePercent = 100;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private bool _isPaused;
    [ObservableProperty] private bool _isStopped = true;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMixerLoopEnabled))]
    private bool _isLooping;
    [ObservableProperty] private bool _isRepeating;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isMixerPreview;
    [ObservableProperty] private bool _isMixerProgram;
    [ObservableProperty] private CueStatus _status = CueStatus.Ready;
    [ObservableProperty] private string _statusText = "READY";
    [ObservableProperty] private string _hotkeyText = string.Empty;
    [ObservableProperty] private string _colorHex = "#6C63FF";
    [ObservableProperty] private SolidColorBrush _colorBrush = new(Colors.SlateBlue);
    [ObservableProperty] private bool _isActive;         // playing = active, triggers glow
    [ObservableProperty] private string? _notes;
    [ObservableProperty] private bool _isEditMode;
    [ObservableProperty] private ImageSource? _albumArt;
    [ObservableProperty] private ImageSource? _mixerThumbnail;

    [ObservableProperty] private double _pan;
    [ObservableProperty] private bool _displayArtwork;
    [ObservableProperty] private string _syncChannels = string.Empty;
    [ObservableProperty] private double _fadeInSeconds;
    [ObservableProperty] private double _fadeOutSeconds;
    [ObservableProperty] private double _cueInPoint;
    [ObservableProperty] private double _cueOutPoint;
    [ObservableProperty] private double _crossfadeSeconds;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMixerLoopEnabled))]
    private PlaybackMode _playbackMode;

    public bool IsVideo => MetadataService.IsVideoFormat(Model.FilePath);
    public bool IsImage => MetadataService.IsImageFormat(Model.FilePath);
    public bool IsMixerLoopEnabled => IsLooping || PlaybackMode == PlaybackMode.Loop;

    public CueCardViewModel(
        CueModel model,
        IAudioEngine audio,
        IVideoPlayerService videoPlayer,
        IFadeEngine fade,
        IHotkeyService hotkeys,
        IMetadataService metadata,
        IPreloadManager? preload = null,
        ISettingsService? settings = null)
    {
        _audio = audio;
        _videoPlayer = videoPlayer;
        _fade = fade;
        _hotkeys = hotkeys;
        _metadata = metadata;
        _preload = preload;
        _settings = settings;
        Model = model;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _timer.Tick += OnTimerTick;

        LoadModel(model);
    }

    public void LoadModel(CueModel model)
    {
        if (Model.Id != model.Id)
        {
            _hotkeys.Unregister(Model.Id);
            HotkeyText = string.Empty;
        }
        Model = model;
        if (IsImage)
            model.Duration = 0;
        Title = model.Title;
        Artist = model.Artist;
        ResetTimelineDisplay();
        Volume = model.Volume;
        VolumePercent = Math.Round(model.Volume * 100);
        IsLooping = model.IsLooping;
        IsRepeating = model.IsRepeating;
        HotkeyText = model.HotkeyText;
        ColorHex = model.ColorHex;
        ColorBrush = ColorHelper.BrushFromHex(model.ColorHex);
        Notes = model.Notes;
        Pan = model.Pan;
        DisplayArtwork = model.DisplayArtwork;
        SyncChannels = model.SyncChannels;
        FadeInSeconds = model.FadeInSeconds;
        FadeOutSeconds = model.FadeOutSeconds;
        CueInPoint = model.CueInPoint;
        CueOutPoint = model.CueOutPoint;
        CrossfadeSeconds = model.CrossfadeSeconds;
        PlaybackMode = model.PlaybackMode;

        RefreshMediaStatus();
        UpdateStatus(CueStatus.Ready);
        OnPropertyChanged(nameof(DisplayNumber));
        OnPropertyChanged(nameof(HasNotes));
        OnPropertyChanged(nameof(IsVideo));
        OnPropertyChanged(nameof(IsImage));
        ResetMixerThumbnail();
    }

    public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);

    /// <summary>Re-check file exists (call after USB plug / before show).</summary>
    public void RefreshMediaStatus()
    {
        if (string.IsNullOrWhiteSpace(Model.FilePath))
        {
            IsMediaMissing = true;
            MediaStatusTip = "Chưa gán file";
            return;
        }

        bool exists = File.Exists(Model.FilePath);
        IsMediaMissing = !exists;
        MediaStatusTip = exists
            ? Model.FilePath
            : $"THIẾU FILE: {Model.FilePath}";

        if (!exists && Status != CueStatus.Playing && Status != CueStatus.Paused)
        {
            StatusText = "MISSING";
            Status = CueStatus.Error;
            IsPlaying = false;
            IsPaused = false;
            IsStopped = true;
            IsActive = false;
        }
    }

    public void ApplySettings(Models.CueModel tempModel)
    {
        Title = tempModel.Title;
        Model.Title = tempModel.Title;
        
        ColorHex = tempModel.ColorHex;
        Model.ColorHex = tempModel.ColorHex;
        ColorBrush = ColorHelper.BrushFromHex(tempModel.ColorHex);

        IsLooping = tempModel.IsLooping;
        Model.IsLooping = tempModel.IsLooping;

        FadeInSeconds = tempModel.FadeInSeconds;
        Model.FadeInSeconds = tempModel.FadeInSeconds;

        FadeOutSeconds = tempModel.FadeOutSeconds;
        Model.FadeOutSeconds = tempModel.FadeOutSeconds;

        CueInPoint = tempModel.CueInPoint;
        Model.CueInPoint = tempModel.CueInPoint;
        CueOutPoint = tempModel.CueOutPoint;
        Model.CueOutPoint = tempModel.CueOutPoint;
        CrossfadeSeconds = tempModel.CrossfadeSeconds;
        Model.CrossfadeSeconds = tempModel.CrossfadeSeconds;
        Duration = IsImage ? "STILL" : FormatEffectiveDuration(Model.Duration);

        Pan = tempModel.Pan;
        Model.Pan = tempModel.Pan;

        DisplayArtwork = tempModel.DisplayArtwork;
        Model.DisplayArtwork = tempModel.DisplayArtwork;

        SyncChannels = tempModel.SyncChannels;
        Model.SyncChannels = tempModel.SyncChannels;

        Notes = tempModel.Notes;
        Model.Notes = tempModel.Notes;
        OnPropertyChanged(nameof(HasNotes));

        PlaybackMode = tempModel.PlaybackMode;
        Model.PlaybackMode = tempModel.PlaybackMode;

        // Keep loop flag in sync with playback mode
        if (tempModel.PlaybackMode == PlaybackMode.Loop)
        {
            IsLooping = true;
            Model.IsLooping = true;
        }
        ApplyNativeLoopMode();

        RefreshMediaStatus();
    }

    public async Task ReplaceAudioAsync(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath)) return;

        try
        {
            UpdateStatus(CueStatus.Loading);
            var meta = await _metadata.ReadAsync(filePath);
            Model.FilePath = filePath;
            Model.Title = meta.Title;
            Model.Artist = meta.Artist;
            Model.Duration = meta.DurationSeconds;
            Model.Metadata = meta;

            Title = meta.Title;
            Artist = meta.Artist;
            if (MetadataService.IsImageFormat(filePath))
                Model.Duration = 0;
            ResetTimelineDisplay();
            RefreshMediaStatus();
            OnPropertyChanged(nameof(IsVideo));
            OnPropertyChanged(nameof(IsImage));
            UpdateStatus(CueStatus.Ready);
        }
        catch (Exception)
        {
            Model.FilePath = filePath;
            Model.Title = System.IO.Path.GetFileNameWithoutExtension(filePath);
            Model.Duration = 0;
            
            Title = Model.Title;
            Artist = string.Empty;
            ResetTimelineDisplay();
            RefreshMediaStatus();
            OnPropertyChanged(nameof(IsVideo));
            OnPropertyChanged(nameof(IsImage));
            UpdateStatus(CueStatus.Error);
            StatusText = "LOAD FAILED";
        }
        ResetMixerThumbnail();
    }

    public void EnsureMixerThumbnailLoaded()
    {
        _mixerThumbnailRequested = true;
        if (_disposed) return;

        var path = Model.FilePath;
        if (!MetadataService.IsImageFormat(path))
        {
            MixerThumbnail = null;
            _mixerThumbnailPath = null;
            return;
        }
        if (string.Equals(_mixerThumbnailPath, path, StringComparison.OrdinalIgnoreCase))
            return;

        _mixerThumbnailPath = path;
        var loadVersion = Interlocked.Increment(ref _mixerThumbnailLoadVersion);
        MixerThumbnail = null;
        _ = LoadMixerThumbnailAsync(path, loadVersion);
    }

    private void ResetMixerThumbnail()
    {
        Interlocked.Increment(ref _mixerThumbnailLoadVersion);
        MixerThumbnail = null;
        _mixerThumbnailPath = null;
        if (_mixerThumbnailRequested)
            EnsureMixerThumbnailLoaded();
    }

    private async Task LoadMixerThumbnailAsync(string path, int loadVersion)
    {
        await MixerThumbnailGate.WaitAsync();
        ImageSource? thumbnail;
        try
        {
            if (_disposed || loadVersion != Volatile.Read(ref _mixerThumbnailLoadVersion))
                return;
            thumbnail = await Task.Run(() => LoadMixerThumbnailCore(path));
        }
        finally
        {
            MixerThumbnailGate.Release();
        }

        if (thumbnail is null)
        {
            if (loadVersion == Volatile.Read(ref _mixerThumbnailLoadVersion))
                _mixerThumbnailPath = null;
            return;
        }
        if (_disposed || loadVersion != Volatile.Read(ref _mixerThumbnailLoadVersion))
            return;

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            return;
        await dispatcher.InvokeAsync(() =>
        {
            if (!_disposed && loadVersion == Volatile.Read(ref _mixerThumbnailLoadVersion)
                && string.Equals(Model.FilePath, path, StringComparison.OrdinalIgnoreCase))
                MixerThumbnail = thumbnail;
        });
    }

    private static ImageSource? LoadMixerThumbnailCore(string path)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists) return null;
            var cacheKey = $"{path}|{file.Length}|{file.LastWriteTimeUtc.Ticks}";
            if (MixerThumbnailCache.TryGetValue(cacheKey, out var cached))
                return cached;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.DecodePixelWidth = 320;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();

            if (MixerThumbnailCache.TryAdd(cacheKey, bitmap))
            {
                MixerThumbnailCacheOrder.Enqueue(cacheKey);
                while (MixerThumbnailCache.Count > MixerThumbnailCacheLimit
                       && MixerThumbnailCacheOrder.TryDequeue(out var expired))
                    MixerThumbnailCache.TryRemove(expired, out _);
            }
            return MixerThumbnailCache.TryGetValue(cacheKey, out cached) ? cached : bitmap;
        }
        catch
        {
            return null;
        }
    }

    // ─── Playback Commands ────────────────────────────────────────

    public async Task<bool> PreparePlaybackAsync()
    {
        if (_disposed) return false;
        RefreshMediaStatus();
        if (string.IsNullOrWhiteSpace(Model.FilePath) || IsMediaMissing)
            return false;
        if (IsPlaying || IsPaused)
            return true;
        if (IsVideo)
            return await _videoPlayer.PrepareAsync(Model.FilePath);
        if (_preload is null)
            return true;
        return await _preload.PreloadAsync(Model.FilePath);
    }

    public async Task<bool> PlayOnMixerProgramAsync()
    {
        if (_disposed) return false;
        if (IsVideo && IsPlaying && _videoPlayer.ActiveOwnerId == Model.Id) return true;
        if (!IsVideo && IsPlaying && _handle != 0) return true;
        if (!await PreparePlaybackAsync()) return false;
        return await StartPreparedOnMixerProgramAsync();
    }

    public async Task<bool> StartPreparedOnMixerProgramAsync()
    {
        if (_disposed) return false;
        if (IsVideo && IsPlaying && _videoPlayer.ActiveOwnerId == Model.Id) return true;
        if (!IsVideo && IsPlaying && _handle != 0) return true;
        await PlayInternalAsync(startVolume: null, forCrossfadeIn: false, notifyVisualProgramChange: false);
        return IsVideo ? (IsPlaying && _videoPlayer.ActiveOwnerId == Model.Id) : (IsPlaying && _handle != 0);
    }

    public bool RestartOnMixerProgram()
    {
        if (_disposed) return false;
        if (IsVideo && _videoPlayer.ActiveOwnerId != Model.Id) return false;
        if (!IsVideo && _handle == 0) return false;

        if (IsVideo)
        {
            if (!_videoPlayer.Restart(Model.Id))
                return false;
            if (IsImage)
                UnhookVideoEnd();
            else
                HookVideoEnd();
        }
        else
        {
            _audio.Seek(_handle, GetRegionStart());
        }

        _suppressNaturalEnd = IsImage;
        _crossfadeWindowFired = false;
        _cueOutHandled = false;

        double mediaStart = GetRegionStart();
        if (IsVideo && !IsImage && mediaStart > 0.01)
            _videoPlayer.Seek(mediaStart);

        ResetTimelineDisplay();
        Model.LastPlayed = DateTime.UtcNow;
        Model.PlayCount++;
        UpdateStatus(CueStatus.Playing);
        if (!IsImage)
            _timer.Start();
        NotifyStarted();
        return true;
    }

    public void BeginMixerProgramSeek()
    {
        if (!IsImage)
            _isSeeking = true;
    }

    public void CancelMixerProgramSeek() => _isSeeking = false;

    public bool SeekOnMixerProgram(double normalizedPosition)
    {
        _isSeeking = false;
        if (_disposed) return false;
        if (IsImage) return false;
        if (IsVideo && _videoPlayer.ActiveOwnerId != Model.Id) return false;
        if (!IsVideo && _handle == 0) return false;

        double fileDuration = IsVideo ? _videoPlayer.GetDuration() : Model.Duration;
        double mediaStart = GetRegionStart();
        double mediaEnd = GetRegionEnd(fileDuration);
        double length = Math.Max(0.001, mediaEnd - mediaStart);
        double target = mediaStart + Math.Clamp(normalizedPosition, 0, 1) * length;

        _crossfadeWindowFired = false;
        _cueOutHandled = false;
        if (IsVideo)
            _videoPlayer.Seek(target);
        else
            _audio.Seek(_handle, target);
            
        UpdateMediaTimeline(target, fileDuration);
        return true;
    }

    public bool SkipOnMixerProgram(double deltaSeconds)
    {
        if (_disposed) return false;
        if (IsImage) return false;
        if (IsVideo && _videoPlayer.ActiveOwnerId != Model.Id) return false;
        if (!IsVideo && _handle == 0) return false;

        double fileDuration = IsVideo ? _videoPlayer.GetDuration() : Model.Duration;
        double currentPos = IsVideo ? _videoPlayer.GetPosition() : _audio.GetPosition(_handle);
        
        double mediaStart = GetRegionStart();
        double mediaEnd = GetRegionEnd(fileDuration);
        double target = Math.Clamp(currentPos + deltaSeconds, mediaStart, mediaEnd);

        _crossfadeWindowFired = false;
        _cueOutHandled = false;
        if (IsVideo)
            _videoPlayer.Seek(target);
        else
            _audio.Seek(_handle, target);
            
        UpdateMediaTimeline(target, fileDuration);
        return true;
    }

    public void MarkReplacedOnMixerProgram()
    {
        _suppressNaturalEnd = true;
        if (IsVideo)
        {
            UnhookVideoEnd();
        }
        else
        {
            _audio.ChannelEnded -= OnChannelEnded;
            try { if (_handle != 0) _audio.Stop(_handle); } catch { }
            _handle = 0;
        }
        _timer.Stop();
        ResetTimelineDisplay();
        UpdateStatus(CueStatus.Stopped);
    }

    public void SetMixerPreviewState(bool value) => IsMixerPreview = value;

    public void SetMixerProgramState(bool value) => IsMixerProgram = value;

    [RelayCommand]
    private async Task TogglePlayPauseAsync()
    {
        if (IsPlaying)
            await PauseAsync();
        else
            await PlayAsync();
    }

    [RelayCommand]
    private async Task RestartAsync()
    {
        await StopAsync();
        await PlayAsync();
    }

    [RelayCommand]
    private void ToggleLoop()
    {
        bool effectiveLoop = IsLooping || PlaybackMode == PlaybackMode.Loop;
        if (effectiveLoop && PlaybackMode == PlaybackMode.Loop)
        {
            PlaybackMode = PlaybackMode.OneShot;
            Model.PlaybackMode = PlaybackMode.OneShot;
        }
        IsLooping = !effectiveLoop;
    }

    [RelayCommand]
    private async Task PlayAsync()
    {
        if (IsPlaying) return;
        await PlayInternalAsync(startVolume: null, forCrossfadeIn: false);
    }

    /// <summary>Start audio at 0 volume for crossfade-in (already loaded/playing after this).</summary>
    public async Task StartCrossfadeInAsync()
    {
        if (IsPlaying || IsVideo) return;
        await PlayInternalAsync(startVolume: 0, forCrossfadeIn: true);
    }

    private async Task PlayInternalAsync(
        double? startVolume,
        bool forCrossfadeIn,
        bool notifyVisualProgramChange = true)
    {
        if (_disposed) return;
        RefreshMediaStatus();
        if (IsMediaMissing && !string.IsNullOrWhiteSpace(Model.FilePath))
        {
            UpdateStatus(CueStatus.Error);
            StatusText = "MISSING";
            return;
        }
        if (string.IsNullOrWhiteSpace(Model.FilePath))
        {
            UpdateStatus(CueStatus.Error);
            StatusText = "NO FILE";
            return;
        }

        bool loopRequested = IsLoopPlaybackEnabled && !forCrossfadeIn;
        bool nativeLoop = loopRequested && !RequiresManagedLoop;

        if (IsVideo)
        {
            bool isImage = IsImage;
            if (notifyVisualProgramChange)
                VisualProgramChangeRequested?.Invoke(this, EventArgs.Empty);

            if (IsPaused && _videoPlayer.ActiveOwnerId == Model.Id)
            {
                if (_videoPlayer.IsFrozen)
                {
                    UpdateStatus(CueStatus.Error);
                    StatusText = "OUTPUT FROZEN";
                    return;
                }
                _videoPlayer.Resume();
                UpdateStatus(CueStatus.Playing);
                if (isImage)
                    ResetTimelineDisplay();
                else
                    _timer.Start();
                return;
            }

            if (isImage)
                UnhookVideoEnd();
            else
                HookVideoEnd();
            _suppressNaturalEnd = isImage;
            _crossfadeWindowFired = false;
            _cueOutHandled = false;
            bool started = await _videoPlayer.PlayAsync(
                Model.Id, Model.FilePath, Model.Volume, nativeLoop);
            if (!started)
            {
                UnhookVideoEnd();
                UpdateStatus(CueStatus.Error);
                StatusText = _videoPlayer.IsFrozen ? "OUTPUT FROZEN" : "START FAILED";
                return;
            }
            var mediaStart = GetRegionStart();
            if (!isImage && mediaStart > 0.01)
                _videoPlayer.Seek(mediaStart);
            Model.LastPlayed = DateTime.UtcNow;
            Model.PlayCount++;
            UpdateStatus(CueStatus.Playing);
            if (isImage)
                ResetTimelineDisplay();
            else
                _timer.Start();
            NotifyStarted();
            return;
        }

        if (IsPaused && _handle != 0 && !forCrossfadeIn)
        {
            _audio.Play(_handle);
            UpdateStatus(CueStatus.Playing);
            _timer.Start();
            
            if (Model.FadeInSeconds > 0)
                await _fade.FadeInAsync(_handle, Volume, Model.Volume, Model.FadeInSeconds);
            else
            {
                _isUpdatingVolume = true;
                Volume = Model.Volume;
                _isUpdatingVolume = false;
                _audio.SetVolume(_handle, Model.Volume);
            }
            return;
        }

        UpdateStatus(CueStatus.Loading);

        // Prefer preloaded stream
        int handle = 0;
        if (_preload is not null)
            handle = await _preload.GetPreloadedHandleAsync(Model.FilePath);

        if (handle == 0 || handle == -1)
            handle = await _audio.LoadAsync(Model.FilePath);

        if (handle == 0 || handle == -1)
        {
            UpdateStatus(CueStatus.Error);
            StatusText = "LOAD FAILED";
            return;
        }

        _handle = handle;
        _suppressNaturalEnd = false;
        _crossfadeWindowFired = forCrossfadeIn; // don't re-fire while we're the incoming cue mid-xfade
        _cueOutHandled = false;

        _audio.ChannelEnded -= OnChannelEnded;
        _audio.ChannelEnded += OnChannelEnded;
        _audio.SetLoop(_handle, nativeLoop);
        _audio.SetPan(_handle, Model.Pan);

        // Cue In
        double start = GetRegionStart();
        if (start > 0.01)
            _audio.Seek(_handle, start);

        double targetVol = Model.Volume;
        bool doFadeIn = !forCrossfadeIn && startVolume is null && Model.FadeInSeconds > 0;
        double initialVol = forCrossfadeIn ? 0 : (doFadeIn ? 0 : (startVolume ?? targetVol));

        _audio.SetVolume(_handle, initialVol);
        _isUpdatingVolume = true;
        Volume = initialVol;
        _isUpdatingVolume = false;

        _audio.Play(_handle);
        UpdateStatus(CueStatus.Playing);
        _timer.Start();

        if (doFadeIn)
        {
            await _fade.FadeInAsync(_handle, 0, targetVol, Model.FadeInSeconds);
            _isUpdatingVolume = true;
            Volume = targetVol;
            _isUpdatingVolume = false;
        }

        Model.LastPlayed = DateTime.UtcNow;
        Model.PlayCount++;
        if (!forCrossfadeIn)
            NotifyStarted();
    }

    /// <summary>
    /// Detach BASS handle while stream keeps playing (for crossfade-out).
    /// Caller must Stop(handle) after fade.
    /// </summary>
    public int DetachHandleKeepPlaying()
    {
        _suppressNaturalEnd = true;
        _crossfadeWindowFired = true;
        _audio.ChannelEnded -= OnChannelEnded;
        _timer.Stop();
        int h = _handle;
        _handle = 0;
        UpdateStatus(CueStatus.Ready);
        return h;
    }

    public void CompleteDetachedStop()
    {
        _handle = 0;
        Position = 1.0;
        UpdateStatus(CueStatus.Ready);
    }

    public void RollbackFailedTransition()
    {
        _suppressNaturalEnd = true;
        _audio.ChannelEnded -= OnChannelEnded;
        UnhookVideoEnd();

        if (IsVideo)
        {
            try { _videoPlayer.Stop(Model.Id); } catch { /* best-effort rollback */ }
        }
        else
        {
            int handle = _handle;
            _handle = 0;
            if (handle != 0 && handle != -1)
            {
                try { _audio.Stop(handle); } catch { /* best-effort rollback */ }
            }
        }

        _timer.Stop();
        _isUpdatingVolume = true;
        Volume = Model.Volume;
        _isUpdatingVolume = false;
        ResetTimelineDisplay();
        UpdateStatus(CueStatus.Stopped);
    }

    public void MarkPlayingAfterCrossfade()
    {
        _crossfadeWindowFired = false;
        _cueOutHandled = false;
        _suppressNaturalEnd = false;
        if (_handle != 0)
        {
            _audio.ChannelEnded -= OnChannelEnded;
            _audio.ChannelEnded += OnChannelEnded;
            _timer.Start();
            UpdateStatus(CueStatus.Playing);
            NotifyStarted();
        }
    }

    public double ResolveCrossfadeSeconds()
    {
        if (Model.CrossfadeSeconds > 0) return Model.CrossfadeSeconds;
        if (_settings?.Current.CrossfadeSeconds > 0) return _settings.Current.CrossfadeSeconds;
        return 0;
    }

    private bool IsLoopPlaybackEnabled => IsLooping || PlaybackMode == PlaybackMode.Loop;

    private bool RequiresManagedLoop
    {
        get
        {
            double start = GetRegionStart();
            return start > 0.01 || Model.CueOutPoint > start;
        }
    }

    private double GetRegionStart() => Math.Max(0, Model.CueInPoint);

    private double GetRegionEnd(double fileDuration)
    {
        if (fileDuration <= 0) fileDuration = Model.Duration;
        double start = GetRegionStart();
        if (Model.CueOutPoint > start)
            return Math.Min(Model.CueOutPoint, fileDuration > 0 ? fileDuration : Model.CueOutPoint);
        return fileDuration > 0 ? fileDuration : Math.Max(start, Model.Duration);
    }

    private string FormatEffectiveDuration(double fileDuration)
    {
        double start = GetRegionStart();
        double end = GetRegionEnd(fileDuration);
        return TimeFormatter.Format(Math.Max(0, end - start));
    }

    private void NotifyStarted()
    {
        PlaybackStarted?.Invoke(this, EventArgs.Empty);
    }

    private void HookVideoEnd()
    {
        if (_videoEndHooked) return;
        _videoPlayer.MediaEnded += OnVideoMediaEnded;
        _videoEndHooked = true;
    }

    private void UnhookVideoEnd()
    {
        if (!_videoEndHooked) return;
        _videoPlayer.MediaEnded -= OnVideoMediaEnded;
        _videoEndHooked = false;
    }

    private void OnVideoMediaEnded(object? sender, Guid ownerId)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            if (ownerId != Model.Id || !IsVideo || IsImage || _suppressNaturalEnd) return;
            // Loop is handled inside VideoWindow — natural end only when not looping
            if (IsLoopPlaybackEnabled && RestartVideoLoopAtCueIn()) return;
            HandleNaturalEnd();
        });
    }

    [RelayCommand]
    private async Task PauseAsync()
    {
        if (!IsPlaying) return;

        if (IsVideo)
        {
            if (_videoPlayer.ActiveOwnerId != Model.Id) return;
            VisualProgramChangeRequested?.Invoke(this, EventArgs.Empty);
            _videoPlayer.Pause();
            UpdateStatus(CueStatus.Paused);
            _timer.Stop();
            return;
        }
        
        if (Model.FadeOutSeconds > 0)
        {
            await _fade.FadeOutAsync(_handle, Volume, Model.FadeOutSeconds);
        }
        
        _audio.Pause(_handle);
        UpdateStatus(CueStatus.Paused);
        _timer.Stop();
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        _suppressNaturalEnd = true;
        _audio.ChannelEnded -= OnChannelEnded;
        UnhookVideoEnd();

        if (IsVideo)
        {
            if (_videoPlayer.ActiveOwnerId == Model.Id)
                VisualProgramChangeRequested?.Invoke(this, EventArgs.Empty);
            _videoPlayer.Stop(Model.Id);
        }
        else if (_handle != 0)
        {
            if (Model.FadeOutSeconds > 0 && IsPlaying)
            {
                await _fade.FadeOutAsync(_handle, Volume, Model.FadeOutSeconds);
            }
            _audio.Stop(_handle);
            _handle = 0;
        }
        
        _timer.Stop();
        
        _isUpdatingVolume = true;
        Volume = Model.Volume;
        _isUpdatingVolume = false;
        
        ResetTimelineDisplay();
        UpdateStatus(CueStatus.Stopped);
    }

    private void OnChannelEnded(object? sender, int handle)
    {
        if (handle != _handle) return;
        Application.Current?.Dispatcher.Invoke(() =>
        {
            if (_suppressNaturalEnd) return;
            // Bass loop mode should not fire end; if it does, ignore when looping
            if (IsLoopPlaybackEnabled && RestartAudioLoopAtCueIn()) return;
            HandleNaturalEnd();
        });
    }

    private void HandleNaturalEnd()
    {
        _timer.Stop();
        _audio.ChannelEnded -= OnChannelEnded;
        UnhookVideoEnd();
        if (_handle != 0)
        {
            // Stream may still be free'd by Bass on natural end; safe Stop if active
            try { _audio.Stop(_handle); } catch { /* ignore */ }
        }
        _handle = 0;
        Position = 1.0;
        UpdateStatus(CueStatus.Ready);

        if (PlaybackMode == PlaybackMode.Repeat || IsRepeating)
        {
            _ = PlayAsync();
            return;
        }

        // If crossfade already started next cue, skip AutoFollow double-fire
        if (_crossfadeWindowFired && PlaybackMode == PlaybackMode.AutoFollow)
            return;

        NaturalEndReached?.Invoke(this, EventArgs.Empty);
    }

    private async Task EndAtCueOutAsync()
    {
        if (_cueOutHandled) return;
        _cueOutHandled = true;

        if (IsLoopPlaybackEnabled && RestartAudioLoopAtCueIn()) return;

        if (Model.FadeOutSeconds > 0 && _handle != 0 && IsPlaying)
        {
            try { await _fade.FadeOutAsync(_handle, Volume, Model.FadeOutSeconds); }
            catch { /* ignore */ }
        }

        if (IsLoopPlaybackEnabled && RestartAudioLoopAtCueIn()) return;

        if (_handle != 0)
        {
            _audio.ChannelEnded -= OnChannelEnded;
            _audio.Stop(_handle);
            _handle = 0;
        }
        _timer.Stop();
        Position = 1.0;
        UpdateStatus(CueStatus.Ready);

        if (PlaybackMode == PlaybackMode.Repeat || IsRepeating)
        {
            _ = PlayAsync();
            return;
        }

        if (_crossfadeWindowFired && PlaybackMode == PlaybackMode.AutoFollow)
            return;

        NaturalEndReached?.Invoke(this, EventArgs.Empty);
    }

    // ─── Seek ─────────────────────────────────────────────────────

    [RelayCommand]
    private void BeginSeek() => _isSeeking = true;

    [RelayCommand]
    private void EndSeek(double normalizedPosition)
    {
        _isSeeking = false;
        if (IsImage) return;
        if (IsVideo && _videoPlayer.ActiveOwnerId == Model.Id)
        {
            double mediaStart = GetRegionStart();
            double mediaEnd = GetRegionEnd(_videoPlayer.GetDuration());
            _videoPlayer.Seek(mediaStart + Math.Clamp(normalizedPosition, 0, 1) * Math.Max(0.001, mediaEnd - mediaStart));
            return;
        }
        if (_handle == 0) return;
        double fileDur = _audio.GetDuration(_handle);
        double start = GetRegionStart();
        double end = GetRegionEnd(fileDur);
        double secs = start + Math.Clamp(normalizedPosition, 0, 1) * Math.Max(0.001, end - start);
        _audio.Seek(_handle, secs);
    }

    // ─── Timer tick ───────────────────────────────────────────────

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (_isSeeking) return;
        if (IsImage)
        {
            _timer.Stop();
            ResetTimelineDisplay();
            return;
        }
        if (IsVideo)
        {
            if (_videoPlayer.ActiveOwnerId != Model.Id) return;
            UpdateMediaTimeline(_videoPlayer.GetPosition(), _videoPlayer.GetDuration());
            return;
        }
        if (_handle == 0) return;
        double pos = _audio.GetPosition(_handle);
        double fileDur = _audio.GetDuration(_handle);
        double start = GetRegionStart();
        double end = GetRegionEnd(fileDur);
        double len = Math.Max(0.001, end - start);

        if (IsLoopPlaybackEnabled && RequiresManagedLoop && pos >= end - 0.03)
        {
            if (RestartAudioLoopAtCueIn()) return;
        }

        // Cue Out
        if (!_cueOutHandled && Model.CueOutPoint > start && pos >= Model.CueOutPoint - 0.03)
        {
            _ = EndAtCueOutAsync();
            return;
        }

        // AutoFollow: open crossfade window before end
        if (!_crossfadeWindowFired && !IsVideo && !IsLooping && PlaybackMode == PlaybackMode.AutoFollow)
        {
            double xf = ResolveCrossfadeSeconds();
            if (xf > 0 && end - pos <= xf + 0.04)
            {
                _crossfadeWindowFired = true;
                CrossfadeWindowReached?.Invoke(this, EventArgs.Empty);
            }
        }

        double rel = Math.Clamp(pos - start, 0, len);
        double left = Math.Max(0, len - rel);
        Elapsed = TimeFormatter.Format(rel);
        Remaining = TimeFormatter.FormatRemaining(rel, len);
        Position = rel / len;
        IsCountdownUrgent = IsPlaying && left <= 15;
        IsCountdownWarning = IsPlaying && left <= 30 && left > 15;

        double currentVol = _audio.GetVolume(_handle);
        if (currentVol >= 0 && Math.Abs(currentVol - Volume) > 0.005)
        {
            _isUpdatingVolume = true;
            Volume = currentVol;
            _isUpdatingVolume = false;
        }
    }

    private void UpdateMediaTimeline(double pos, double fileDuration)
    {
        if (IsImage)
        {
            ResetTimelineDisplay();
            return;
        }
        double start = GetRegionStart();
        double end = GetRegionEnd(fileDuration);
        double len = Math.Max(0.001, end - start);

        if (IsLoopPlaybackEnabled && RequiresManagedLoop && pos >= end - 0.03)
        {
            _videoPlayer.Seek(start);
            ResetLoopTimeline(len);
            return;
        }

        if (!_cueOutHandled && Model.CueOutPoint > start && pos >= Model.CueOutPoint - 0.03)
        {
            if (IsLooping || PlaybackMode == PlaybackMode.Loop)
            {
                _cueOutHandled = true;
                _videoPlayer.Seek(start);
                _crossfadeWindowFired = false;
                _cueOutHandled = false;
                ResetLoopTimeline(len);
                return;
            }

            _cueOutHandled = true;
            _videoPlayer.Stop(Model.Id);
            HandleNaturalEnd();
            return;
        }

        double rel = Math.Clamp(pos - start, 0, len);
        double left = Math.Max(0, len - rel);
        Elapsed = TimeFormatter.Format(rel);
        Remaining = TimeFormatter.FormatRemaining(rel, len);
        Position = rel / len;
        IsCountdownUrgent = IsPlaying && left <= 15;
        IsCountdownWarning = IsPlaying && left <= 30 && left > 15;
    }

    // ─── Volume ───────────────────────────────────────────────────

    private bool RestartAudioLoopAtCueIn()
    {
        if (_handle == 0) return false;

        try
        {
            double start = GetRegionStart();
            double end = GetRegionEnd(_audio.GetDuration(_handle));
            _audio.SetVolume(_handle, Model.Volume);
            _isUpdatingVolume = true;
            Volume = Model.Volume;
            _isUpdatingVolume = false;
            _audio.Seek(_handle, start);
            _audio.Play(_handle);
            ResetLoopTimeline(Math.Max(0.001, end - start));
            UpdateStatus(CueStatus.Playing);
            _timer.Start();
            return true;
        }
        catch
        {
            _isUpdatingVolume = false;
            return false;
        }
    }

    private bool RestartVideoLoopAtCueIn()
    {
        if (_videoPlayer.ActiveOwnerId != Model.Id || !_videoPlayer.Restart(Model.Id))
            return false;

        double start = GetRegionStart();
        if (start > 0.01)
            _videoPlayer.Seek(start);
        double end = GetRegionEnd(_videoPlayer.GetDuration());
        ResetLoopTimeline(Math.Max(0.001, end - start));
        UpdateStatus(CueStatus.Playing);
        _timer.Start();
        return true;
    }

    private void ResetLoopTimeline(double length)
    {
        _crossfadeWindowFired = false;
        _cueOutHandled = false;
        Elapsed = "0:00";
        Remaining = TimeFormatter.FormatRemaining(0, length);
        Position = 0;
        IsCountdownUrgent = IsPlaying && length <= 15;
        IsCountdownWarning = IsPlaying && length <= 30 && length > 15;
    }

    private void ResetTimelineDisplay()
    {
        Position = 0;
        IsCountdownUrgent = false;
        IsCountdownWarning = false;
        if (IsImage)
        {
            Duration = "STILL";
            Elapsed = "—";
            Remaining = "—";
            return;
        }

        Duration = FormatEffectiveDuration(Model.Duration);
        Elapsed = "0:00";
        Remaining = $"-{Duration}";
    }

    partial void OnVolumeChanged(double value)
    {
        VolumePercent = Math.Round(value * 100);
        
        if (!_isUpdatingVolume)
        {
            Model.Volume = value;
            if (IsVideo)
            {
                if (_videoPlayer.ActiveOwnerId == Model.Id)
                    _videoPlayer.SetVolume(value);
            }
            else if (_handle != 0) 
            {
                _audio.SetVolume(_handle, value);
            }
        }
    }

    partial void OnIsLoopingChanged(bool value)
    {
        Model.IsLooping = value;
        ApplyNativeLoopMode();
    }

    private void ApplyNativeLoopMode()
    {
        bool nativeLoop = IsLoopPlaybackEnabled && !RequiresManagedLoop;
        if (_handle != 0) _audio.SetLoop(_handle, nativeLoop);
        if (IsVideo && _videoPlayer.ActiveOwnerId == Model.Id)
            _videoPlayer.SetLoop(nativeLoop);
    }

    partial void OnPanChanged(double value)
    {
        Model.Pan = Math.Clamp(value, -1, 1);
        if (!IsVideo && _handle != 0)
            _audio.SetPan(_handle, Model.Pan);
    }

    // ─── Status helpers ───────────────────────────────────────────

    private void UpdateStatus(CueStatus s)
    {
        Status = s;
        IsPlaying = s == CueStatus.Playing;
        IsPaused = s == CueStatus.Paused;
        IsStopped = s is CueStatus.Stopped or CueStatus.Ready;
        IsActive = s == CueStatus.Playing;
        StatusText = s switch
        {
            CueStatus.Playing => "PLAYING",
            CueStatus.Paused  => "PAUSED",
            CueStatus.Loading => "LOADING",
            CueStatus.Error   => "ERROR",
            _                 => "READY"
        };
        if (!IsPlaying)
        {
            IsCountdownUrgent = false;
            IsCountdownWarning = false;
        }
    }

    // ─── Color / Hotkey ──────────────────────────────────────────

    public void SetColor(string hex)
    {
        ColorHex = hex;
        ColorBrush = ColorHelper.BrushFromHex(hex);
        Model.ColorHex = hex;
    }

    // ─── Dispose ─────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Interlocked.Increment(ref _mixerThumbnailLoadVersion);
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
        _hotkeys.Unregister(Model.Id);
        UnhookVideoEnd();
        _suppressNaturalEnd = true;
        if (_handle != 0)
        {
            _audio.ChannelEnded -= OnChannelEnded;
            _audio.Stop(_handle);
        }
    }

    // ─── Hotkey Handlers ──────────────────────────────────────────

    partial void OnHotkeyTextChanged(string? oldValue, string newValue)
    {
        if (string.IsNullOrEmpty(newValue))
        {
            _hotkeys.Unregister(Model.Id);
            return;
        }

        if (TryParseHotkey(newValue, out var key, out var modifiers))
        {
            bool ok = _hotkeys.Register(Model.Id, newValue, key, modifiers);
            if (!ok)
            {
                System.Windows.MessageBox.Show($"Hotkey '{newValue}' conflict with another cue card!", "Conflict",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);

                System.Windows.Application.Current.Dispatcher.BeginInvoke(() => {
                    HotkeyText = oldValue ?? string.Empty;
                });
            }
        }
    }

    private bool TryParseHotkey(string hotkeyText, out Key key, out ModifierKeys modifiers)
    {
        key = Key.None;
        modifiers = ModifierKeys.None;
        if (string.IsNullOrEmpty(hotkeyText)) return false;

        var parts = hotkeyText.Split('+');
        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i].Trim();
            if (part.Equals("Ctrl", System.StringComparison.OrdinalIgnoreCase))
                modifiers |= ModifierKeys.Control;
            else if (part.Equals("Shift", System.StringComparison.OrdinalIgnoreCase))
                modifiers |= ModifierKeys.Shift;
            else if (part.Equals("Alt", System.StringComparison.OrdinalIgnoreCase))
                modifiers |= ModifierKeys.Alt;
            else if (part.Equals("Win", System.StringComparison.OrdinalIgnoreCase))
                modifiers |= ModifierKeys.Windows;
            else
            {
                if (System.Enum.TryParse<Key>(part, true, out var parsedKey))
                {
                    key = parsedKey;
                }
            }
        }

        return key != Key.None;
    }
}
