namespace ShowCuePlayer.Models;

/// <summary>Application-wide settings persisted in SQLite and embedded in project files.</summary>
public class AppSettings
{
    // Audio
    public int AudioDeviceIndex { get; set; } = -1;   // -1 = default
    public string AudioDeviceName { get; set; } = "Default";
    /// <summary>Windows render endpoint used by the Karaoke WebView and external mixer.</summary>
    public string KaraokeAudioEndpointId { get; set; } = string.Empty;
    public string KaraokeAudioEndpointName { get; set; } = string.Empty;
    public double MasterVolume { get; set; } = 1.0;
    public double AudioBusVolume { get; set; } = 1.0;
    public double VideoBusVolume { get; set; } = 1.0;
    public double KaraokeBusVolume { get; set; } = 1.0;
    public double CrossfadeSeconds { get; set; } = 2.0;
    public int AudioBufferMs { get; set; } = 100;
    public bool UseWasapi { get; set; }

    // UI
    public string AccentColorHex { get; set; } = "#6C63FF";
    public bool ShowCardArtwork { get; set; } = true;
    public int CardColumns { get; set; }               // 0 = auto
    public bool EditModeEnabled { get; set; }

    // Project
    public bool AutoSaveEnabled { get; set; } = true;
    public int AutoSaveIntervalSeconds { get; set; } = 30;
    public string DefaultImportFolder { get; set; } = string.Empty;
    public string LastProjectPath { get; set; } = string.Empty;
    public List<string> RecentProjects { get; set; } = new List<string>();

    // Playback
    public bool PreloadNextCue { get; set; } = true;
    public bool StopAllOnEscape { get; set; } = true;
    public string Language { get; set; } = "en";

    // Dual-monitor video output (event / stage)
    /// <summary>DeviceName of preferred output monitor (e.g. \\.\DISPLAY2).</summary>
    public string VideoOutputDeviceName { get; set; } = string.Empty;
    /// <summary>Prefer non-primary screen as output when no saved preference.</summary>
    public bool PreferSecondaryOutput { get; set; } = true;
    /// <summary>Keep black fullscreen on projector when media stops (never flash desktop).</summary>

    // LED event output
    public string LedOutputProfileId { get; set; } = "full-hd";
    public string LedCustomProfileName { get; set; } = "Custom LED";
    public int LedCustomCanvasWidth { get; set; } = 1920;
    public int LedCustomCanvasHeight { get; set; } = 1080;
    public LedScalingMode LedCustomScalingMode { get; set; } = LedScalingMode.Fit;
    public string EmergencySafeSceneImagePath { get; set; } = string.Empty;

    // Karaoke (huy.sale)
    public string KaraokeSessionId { get; set; } = string.Empty;
    public string KaraokeRemoteBaseUrl { get; set; } = "https://huy.sale/remote?session=";
    public string KaraokePlayerUrl { get; set; } = "https://huy.sale/player";
    /// <summary>Remote WebView layout: phone | pc</summary>
    public string KaraokeRemoteUiMode { get; set; } = "phone";
    /// <summary>Automatically advance to the next queued karaoke song.</summary>
    public bool KaraokeAutoNextEnabled { get; set; }
    /// <summary>One-time migration marker for the safer manual-TAKE karaoke default.</summary>
    public bool KaraokeSafeTakeDefaultsApplied { get; set; }
    /// <summary>Test 1 màn: OUTPUT karaoke dạng cửa sổ trên cùng màn control (không fullscreen màn 2).</summary>
    public bool KaraokeSingleScreenTest { get; set; } = true;
    /// <summary>Các nút âm thanh tùy chỉnh của soundboard Karaoke.</summary>
    public List<KaraokeSoundEffect> KaraokeSoundEffects { get; set; } = new();

    /// <summary>OBS-style hotkeys: actionId → "Ctrl+Shift+A". Empty = unbound.</summary>
    public Dictionary<string, string> Hotkeys { get; set; } = HotkeyActions.CreateDefaults();

    /// <summary>Keep only Karaoke actions and merge defaults for new installs/upgrades.</summary>
    public void EnsureHotkeys()
    {
        var defaults = HotkeyActions.CreateDefaults();
        Hotkeys ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var legacyAction in Hotkeys.Keys
                     .Where(action => !HotkeyActions.SupportedIds.Contains(action))
                     .ToArray())
            Hotkeys.Remove(legacyAction);
        foreach (var kv in defaults)
        {
            if (!Hotkeys.ContainsKey(kv.Key))
                Hotkeys[kv.Key] = kv.Value;
        }
    }

    public void Validate()
    {
        MasterVolume = ClampVolume(MasterVolume);
        AudioBusVolume = ClampVolume(AudioBusVolume);
        VideoBusVolume = ClampVolume(VideoBusVolume);
        KaraokeBusVolume = ClampVolume(KaraokeBusVolume);
        CrossfadeSeconds = double.IsFinite(CrossfadeSeconds) ? Math.Clamp(CrossfadeSeconds, 0, 60) : 2;
        AudioBufferMs = Math.Clamp(AudioBufferMs, 5, 5000);
        KaraokeAudioEndpointId = KaraokeAudioEndpointId?.Trim() ?? string.Empty;
        KaraokeAudioEndpointName = KaraokeAudioEndpointName?.Trim() ?? string.Empty;
        AutoSaveIntervalSeconds = Math.Clamp(AutoSaveIntervalSeconds, 5, 3600);
        RecentProjects ??= new List<string>();
        RecentProjects = RecentProjects.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToList();
        KaraokeSoundEffects ??= new List<KaraokeSoundEffect>();
        var soundEffectIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        KaraokeSoundEffects = KaraokeSoundEffects
            .Where(effect => effect is not null && !string.IsNullOrWhiteSpace(effect.FilePath))
            .Take(200)
            .Select(effect =>
            {
                effect.Id = string.IsNullOrWhiteSpace(effect.Id) || !soundEffectIds.Add(effect.Id)
                    ? Guid.NewGuid().ToString("N")
                    : effect.Id;
                soundEffectIds.Add(effect.Id);
                effect.Name = string.IsNullOrWhiteSpace(effect.Name)
                    ? Path.GetFileNameWithoutExtension(effect.FilePath)
                    : effect.Name.Trim();
                effect.HotkeyText = effect.HotkeyText?.Trim() ?? string.Empty;
                effect.Volume = ClampVolume(effect.Volume);
                return effect;
            })
            .ToList();
        EnsureHotkeys();
    }

    private static double ClampVolume(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 1;
}
