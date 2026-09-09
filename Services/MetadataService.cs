using ShowCuePlayer.Models;
using TagLib;

namespace ShowCuePlayer.Services;

/// <summary>Contract for reading audio file metadata via TagLib#.</summary>
public interface IMetadataService
{
    Task<AudioMetadata> ReadAsync(string filePath);
}

/// <summary>
/// Reads ID3 / Vorbis / FLAC / AAC tags using TagLib#.
/// Falls back to filename when tags are missing.
/// </summary>
public sealed class MetadataService : IMetadataService
{
    private static readonly HashSet<string> SupportedAudioExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3", ".wav", ".flac", ".aac", ".m4a",
            ".aiff", ".aif", ".ogg", ".opus", ".wma"
        };

    private static readonly HashSet<string> SupportedVideoExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".wmv", ".avi", ".mkv", ".mov", ".m4v"
        };

    private static readonly HashSet<string> SupportedImageExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".bmp", ".gif"
        };

    public static bool IsSupported(string path)
    {
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return SupportedAudioExtensions.Contains(ext) || SupportedVideoExtensions.Contains(ext) || SupportedImageExtensions.Contains(ext);
    }

    public static bool IsVideoFormat(string path)
    {
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return SupportedVideoExtensions.Contains(ext) || SupportedImageExtensions.Contains(ext);
    }

    public static bool IsImageFormat(string path)
    {
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return SupportedImageExtensions.Contains(ext);
    }

    public Task<AudioMetadata> ReadAsync(string filePath)
        => Task.Run(() => ReadInternal(filePath));

    private static AudioMetadata ReadInternal(string filePath)
    {
        var meta = new AudioMetadata { FilePath = filePath, IsVideo = IsVideoFormat(filePath) };

        try
        {
            var info = new System.IO.FileInfo(filePath);
            meta.FileSizeBytes = info.Length;

            using var file = TagLib.File.Create(filePath);
            var tag = file.Tag;
            var props = file.Properties;

            meta.Title = tag.Title ?? System.IO.Path.GetFileNameWithoutExtension(filePath);
            meta.Artist = tag.FirstPerformer ?? string.Empty;
            meta.Album = tag.Album ?? string.Empty;
            meta.Genre = tag.FirstGenre ?? string.Empty;
            meta.Year = tag.Year;
            meta.DurationSeconds = props.Duration.TotalSeconds;
            meta.Bitrate = props.AudioBitrate;
            meta.SampleRate = props.AudioSampleRate;
            meta.Channels = props.AudioChannels;
            meta.Codec = props.Description;

            // Album art
            var pic = tag.Pictures?.FirstOrDefault();
            if (pic is not null)
            {
                meta.AlbumArtData = pic.Data.Data;
                meta.AlbumArtMimeType = pic.MimeType;
            }
        }
        catch
        {
            meta.Title = System.IO.Path.GetFileNameWithoutExtension(filePath);
        }

        return meta;
    }
}
