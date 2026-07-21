using Microsoft.Extensions.Logging;

namespace ShowCuePlayer.AudioEngine;

/// <summary>
/// Contract for background audio preloading.
/// Pre-decodes the next cue so playback starts instantly with zero latency.
/// </summary>
public interface IPreloadManager
{
    Task<bool> PreloadAsync(string filePath);
    Task<int> GetPreloadedHandleAsync(string filePath);
    void Clear();
}

/// <summary>
/// Preloads audio files in the background into memory-resident BASS streams.
/// Maintains a small LRU cache to avoid memory bloat.
/// </summary>
public sealed class PreloadManager : IPreloadManager
{
    private readonly IAudioEngine _audio;
    private readonly ILogger<PreloadManager> _logger;
    private readonly Dictionary<string, int> _cache = new Dictionary<string, int>();
    private const int MaxCacheSize = 3;

    public PreloadManager(IAudioEngine audio, ILogger<PreloadManager> logger)
    {
        _audio = audio;
        _logger = logger;
    }

    public async Task<bool> PreloadAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return false;
        if (_cache.ContainsKey(filePath)) return true;

        if (_cache.Count >= MaxCacheSize)
            EvictOldest();

        _logger.LogDebug("Preloading: {File}", System.IO.Path.GetFileName(filePath));
        int handle = await _audio.LoadAsync(filePath);
        if (handle != 0 && handle != -1)
        {
            _cache[filePath] = handle;
            return true;
        }
        return false;
    }

    public Task<int> GetPreloadedHandleAsync(string filePath)
    {
        _cache.TryGetValue(filePath, out int handle);
        if (handle != 0 && handle != -1) _cache.Remove(filePath); // consumed
        return Task.FromResult(handle);
    }

    public void Clear()
    {
        foreach (var h in _cache.Values)
            _audio.Stop(h);
        _cache.Clear();
    }

    private void EvictOldest()
    {
        var first = _cache.First();
        _audio.Stop(first.Value);
        _cache.Remove(first.Key);
    }
}
