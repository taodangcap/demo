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
public sealed class PreloadManager : IPreloadManager, IDisposable
{
    private readonly IAudioEngine _audio;
    private readonly ILogger<PreloadManager> _logger;
    private readonly Dictionary<string, int> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _lruOrder = new();
    private readonly object _gate = new();
    private long _generation;
    private bool _disposed;
    private const int MaxCacheSize = 3;

    public PreloadManager(IAudioEngine audio, ILogger<PreloadManager> logger)
    {
        _audio = audio;
        _logger = logger;
    }

    public async Task<bool> PreloadAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return false;

        long generation;
        lock (_gate)
        {
            if (_disposed) return false;
            if (_cache.ContainsKey(filePath))
            {
                TouchLruLocked(filePath);
                return true;
            }
            generation = _generation;
        }

        _logger.LogDebug("Preloading: {File}", System.IO.Path.GetFileName(filePath));
        int handle = await _audio.LoadAsync(filePath);
        if (handle == 0 || handle == -1) return false;

        int? evictedHandle = null;
        bool stopLoaded = false;
        lock (_gate)
        {
            if (_disposed || generation != _generation || _cache.ContainsKey(filePath))
            {
                stopLoaded = true;
            }
            else
            {
                if (_cache.Count >= MaxCacheSize)
                    evictedHandle = EvictOldestLocked();
                _cache[filePath] = handle;
                _lruOrder.AddLast(filePath);
            }
        }

        if (stopLoaded)
        {
            _audio.Stop(handle);
            return !_disposed && generation == _generation;
        }
        if (evictedHandle is int evicted and not 0 and not -1)
            _audio.Stop(evicted);

        return true;
    }

    public Task<int> GetPreloadedHandleAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return Task.FromResult(0);
        lock (_gate)
        {
            if (_cache.Remove(filePath, out int handle))
            {
                _lruOrder.Remove(filePath);
                return Task.FromResult(handle);
            }
        }
        return Task.FromResult(0);
    }

    public void Clear()
    {
        List<int> handles;
        lock (_gate)
        {
            _generation++;
            handles = _cache.Values.ToList();
            _cache.Clear();
            _lruOrder.Clear();
        }
        foreach (var h in handles)
            _audio.Stop(h);
    }

    private void TouchLruLocked(string filePath)
    {
        _lruOrder.Remove(filePath);
        _lruOrder.AddLast(filePath);
    }

    private int? EvictOldestLocked()
    {
        if (_lruOrder.First is null) return null;
        var oldestKey = _lruOrder.First.Value;
        _lruOrder.RemoveFirst();
        return _cache.Remove(oldestKey, out var handle) ? handle : null;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        Clear();
    }
}
