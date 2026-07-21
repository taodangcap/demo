using System.Text.Json.Serialization;

namespace ShowCuePlayer.Models;

/// <summary>
/// Represents the playback status of a cue card.
/// </summary>
public enum CueStatus
{
    Ready,
    Playing,
    Paused,
    Stopped,
    Loading,
    Error
}

/// <summary>
/// Represents the playback mode for a cue.
/// </summary>
public enum PlaybackMode
{
    OneShot,    // Play once and stop
    Loop,       // Loop continuously
    Repeat,     // Repeat the same cue
    AutoFollow, // Start next cue when THIS cue finishes
    AutoContinue // Start next cue exactly when THIS cue starts
}

/// <summary>
/// Core data model for a single audio cue card.
/// Stored in SQLite and serialized to .showcue project files.
/// </summary>
public class CueModel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int SortOrder { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Album { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public double Volume { get; set; } = 1.0;          // 0.0 – 1.0
    public double Duration { get; set; }               // seconds
    public bool IsLooping { get; set; }
    public bool IsRepeating { get; set; }
    public PlaybackMode PlaybackMode { get; set; } = PlaybackMode.OneShot;
    public string HotkeyText { get; set; } = string.Empty;
    public string ColorHex { get; set; } = "#6C63FF";  // default accent
    public string? Notes { get; set; }
    public bool IsFavorite { get; set; }
    public double FadeInSeconds { get; set; }
    public double FadeOutSeconds { get; set; }
    public double CueInPoint { get; set; }             // start offset in seconds
    public double CueOutPoint { get; set; }            // end point (0 = end of file)
    public double CrossfadeSeconds { get; set; }
    public string? PlaylistId { get; set; }
    public DateTime DateAdded { get; set; } = DateTime.UtcNow;
    public DateTime? LastPlayed { get; set; }
    public double Pan { get; set; } = 0.0;             // -1.0 to 1.0 (Left to Right)
    public bool DisplayArtwork { get; set; } = true;
    public string SyncChannels { get; set; } = string.Empty; // Comma separated list of channel IDs to sync with
    public int PlayCount { get; set; }

    [JsonIgnore]
    public AudioMetadata? Metadata { get; set; }
}
