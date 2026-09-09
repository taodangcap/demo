using ShowCuePlayer.Helpers;
using ShowCuePlayer.Models;
using System.Collections.Concurrent;

namespace ShowCuePlayer.Services;

/// <summary>Progress report from the import scanner.</summary>
public record ImportProgress(int Total, int Processed, int Skipped, string CurrentFile, bool IsComplete);

/// <summary>Contract for importing audio files and folders.</summary>
public interface IImportService
{
    /// <summary>Import one or many paths (files or folders) with recursive scanning.</summary>
    IAsyncEnumerable<CueModel> ImportAsync(
        IEnumerable<string> paths,
        PlaylistType targetType,
        IProgress<ImportProgress>? progress = null,
        CancellationToken ct = default);
}

/// <summary>
/// Background recursive audio importer.
/// Scans folders recursively, filters by supported extension,
/// reads metadata, and produces CueModels. Never blocks UI thread.
/// Supports 10,000+ files.
/// </summary>
public sealed class ImportService : IImportService
{
    private readonly IMetadataService _metadata;

    public ImportService(IMetadataService metadata) => _metadata = metadata;

    public async IAsyncEnumerable<CueModel> ImportAsync(
        IEnumerable<string> paths,
        PlaylistType targetType,
        IProgress<ImportProgress>? progress = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        if (_metadata == null)
            throw new InvalidOperationException("MetadataService not initialized");

        // Collect all matching files first
        var allFiles = await Task.Run(() => CollectFiles(paths, targetType), ct);
        allFiles.Sort(NaturalSortComparer.Instance);

        int total = allFiles.Count;
        int processed = 0, skipped = 0;

        foreach (var file in allFiles)
        {
            ct.ThrowIfCancellationRequested();

            progress?.Report(new ImportProgress(total, processed, skipped, file, false));

            CueModel? cue = null;
            try
            {
                // Verify file exists before processing
                if (!File.Exists(file))
                {
                    skipped++;
                    continue;
                }

                var meta = await _metadata.ReadAsync(file);
                ct.ThrowIfCancellationRequested();
                
                if (meta == null)
                {
                    skipped++;
                    continue;
                }

                cue = new CueModel
                {
                    Title = meta.Title ?? "Unknown",
                    Artist = meta.Artist ?? "Unknown",
                    Album = meta.Album ?? "Unknown",
                    FilePath = file,
                    Duration = MetadataService.IsImageFormat(file)
                        ? 0
                        : meta.DurationSeconds,
                    Metadata = meta,
                    ColorHex = targetType == PlaylistType.Video ? "#FF5252" : "#6C63FF"
                };
                processed++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Import error for {file}: {ex.Message}");
                skipped++;
            }

            ct.ThrowIfCancellationRequested();
            if (cue is not null)
                yield return cue;
        }

        progress?.Report(new ImportProgress(total, processed, skipped, string.Empty, true));
    }

    private static List<string> CollectFiles(IEnumerable<string> paths, PlaylistType targetType)
    {
        var files = new List<string>();
        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                if (IsMatchingType(path, targetType))
                    files.Add(path);
            }
            else if (Directory.Exists(path))
            {
                var found = Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories)
                    .Where(p => IsMatchingType(p, targetType));
                files.AddRange(found);
            }
        }
        return files;
    }

    private static bool IsMatchingType(string path, PlaylistType targetType)
    {
        if (!MetadataService.IsSupported(path)) return false;
        bool isVideo = MetadataService.IsVideoFormat(path);
        return targetType == PlaylistType.Video ? isVideo : !isVideo;
    }
}
