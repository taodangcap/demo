namespace ShowCuePlayer.Models;

/// <summary>
/// Audio file metadata read from tags via TagLib#.
/// </summary>
public class AudioMetadata
{
    public bool IsVideo { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Album { get; set; } = string.Empty;
    public string Genre { get; set; } = string.Empty;
    public uint Year { get; set; }
    public double DurationSeconds { get; set; }
    public int Bitrate { get; set; }         // kbps
    public int SampleRate { get; set; }      // Hz
    public int Channels { get; set; }
    public string Codec { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public byte[]? AlbumArtData { get; set; }
    public string AlbumArtMimeType { get; set; } = string.Empty;
}
