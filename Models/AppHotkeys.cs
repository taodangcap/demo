namespace ShowCuePlayer.Models;

public static class HotkeyActions
{
    public const string Go = "Go";
    public const string GoNext = "GoNext";
    public const string GoPrevious = "GoPrevious";
    public const string StopTab = "StopTab";
    public const string StopAll = "StopAll";
    public const string ToggleLiveMode = "ToggleLiveMode";
    public const string ToggleLiveLock = "ToggleLiveLock";
    public const string ToggleVideoOutput = "ToggleVideoOutput";
    public const string ToggleLedBlackout = "ToggleLedBlackout";
    public const string ShowEmergencySafeScene = "ShowEmergencySafeScene";
    public const string SaveProject = "SaveProject";
    public const string OpenProject = "OpenProject";
    public const string ApplyKaraoke = "ApplyKaraoke";
    public const string ToggleKaraokeOnOff = "ToggleKaraokeOnOff";

    public static IReadOnlyList<(string Id, string Category, string Name)> Catalog { get; } = new[]
    {
        (Go, "Playback", "GO - phát cue đang chờ"),
        (GoNext, "Playback", "NEXT cue"),
        (GoPrevious, "Playback", "PREVIOUS cue"),
        (StopTab, "Playback", "Stop current tab"),
        (StopAll, "Playback", "Stop all"),
        (ToggleVideoOutput, "Output", "Enable / disable Program output"),
        (ToggleLedBlackout, "Output", "Toggle LED safety blackout"),
        (ShowEmergencySafeScene, "Output", "Show emergency Safe Scene"),
        (ToggleLiveMode, "Live desk", "Toggle LIVE mode"),
        (ToggleLiveLock, "Live desk", "Toggle live lock"),
        (SaveProject, "Project", "Save project"),
        (OpenProject, "Project", "Open project"),
        (ApplyKaraoke, "Karaoke", "Sync karaoke session"),
        (ToggleKaraokeOnOff, "Karaoke", "Karaoke output ON / OFF"),
    };

    public static Dictionary<string, string> CreateDefaults() => new(StringComparer.OrdinalIgnoreCase)
    {
        [Go] = "Space",
        [GoNext] = "Right",
        [GoPrevious] = "Left",
        [StopTab] = "Escape",
        [StopAll] = "Shift+Escape",
        [ToggleVideoOutput] = "",
        [ToggleLedBlackout] = "B",
        [ShowEmergencySafeScene] = "Ctrl+Shift+S",
        [ToggleLiveMode] = "F",
        [ToggleLiveLock] = "L",
        [SaveProject] = "Ctrl+S",
        [OpenProject] = "Ctrl+O",
        [ApplyKaraoke] = "Ctrl+Return",
        [ToggleKaraokeOnOff] = "Ctrl+K",
    };
}
