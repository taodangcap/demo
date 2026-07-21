namespace ShowCuePlayer.AudioEngine;

/// <summary>Tracks a single loaded BASS channel and its metadata.</summary>
public sealed class AudioChannel
{
    public int Handle { get; }
    public string FilePath { get; }
    public DateTime LoadedAt { get; } = DateTime.UtcNow;
    public double Volume { get; set; } = 1.0;
    public bool IsLooping { get; set; }

    public AudioChannel(int handle, string filePath)
    {
        Handle = handle;
        FilePath = filePath;
    }
}
