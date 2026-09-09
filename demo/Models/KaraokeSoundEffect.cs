using System.Text.Json.Serialization;

namespace ShowCuePlayer.Models;

/// <summary>Một nút âm thanh tùy chỉnh trên soundboard Karaoke.</summary>
public sealed class KaraokeSoundEffect
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Hiệu ứng";
    public string FilePath { get; set; } = string.Empty;
    public bool Loop { get; set; }
    /// <summary>Per-effect gain (0..1), multiplied by the Karaoke master volume.</summary>
    public double Volume { get; set; } = 1.0;
    /// <summary>Global hotkey in OBS-style format, for example Ctrl+Shift+F1.</summary>
    public string HotkeyText { get; set; } = string.Empty;

    [JsonIgnore]
    public bool IsPlaying { get; set; }

    [JsonIgnore]
    public bool IsPaused { get; set; }

    [JsonIgnore]
    public double DurationSeconds { get; set; }

    [JsonIgnore]
    public string HotkeyDisplay => string.IsNullOrWhiteSpace(HotkeyText)
        ? "HOTKEY: —"
        : $"⌨ {HotkeyText}";
}
