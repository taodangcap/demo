using ManagedBass;
using Microsoft.Extensions.Logging;

namespace ShowCuePlayer.AudioEngine;

/// <summary>Contract for fade in/out operations on audio channels.</summary>
public interface IFadeEngine
{
    Task FadeInAsync(int handle, double fromVolume, double toVolume, double durationSeconds);
    Task FadeOutAsync(int handle, double fromVolume, double durationSeconds);
    void CancelFade(int handle);
}

/// <summary>
/// Smooth, timer-based fade engine. Uses 10ms ticks for sample-accurate fading.
/// Cancellable per-channel via CancellationToken.
/// </summary>
public sealed class FadeEngine : IFadeEngine, IDisposable
{
    private readonly ILogger<FadeEngine> _logger;
    private readonly Dictionary<int, CancellationTokenSource> _fades = new();
    private readonly object _gate = new();
    private bool _disposed;

    public FadeEngine(ILogger<FadeEngine> logger) => _logger = logger;

    public async Task FadeInAsync(int handle, double fromVolume, double toVolume, double durationSeconds)
        => await RunFadeAsync(handle, fromVolume, toVolume, durationSeconds);

    public async Task FadeOutAsync(int handle, double fromVolume, double durationSeconds)
        => await RunFadeAsync(handle, fromVolume, 0.0, durationSeconds);

    private async Task RunFadeAsync(int handle, double from, double to, double duration)
    {
        if (duration <= 0)
        {
            CancelFade(handle);
            Bass.ChannelSetAttribute(handle, ChannelAttribute.Volume, (float)Math.Clamp(to, 0, 1));
            return;
        }

        CancellationTokenSource? cts = null;
        CancellationToken token;
        lock (_gate)
        {
            if (_disposed) return;
            if (_fades.Remove(handle, out var prev))
                prev.Cancel();
            cts = new CancellationTokenSource();
            _fades[handle] = cts;
            token = cts.Token;
        }

        try
        {
            const int tickMs = 10;
            int steps = Math.Max(1, (int)(duration * 1000 / tickMs));
            double delta = (to - from) / steps;
            double current = from;

            for (int i = 0; i < steps; i++)
            {
                if (token.IsCancellationRequested) return;
                current += delta;
                Bass.ChannelSetAttribute(handle, ChannelAttribute.Volume, (float)Math.Clamp(current, 0, 1));
                await Task.Delay(tickMs, token);
            }
            if (!token.IsCancellationRequested)
                Bass.ChannelSetAttribute(handle, ChannelAttribute.Volume, (float)Math.Clamp(to, 0, 1));
        }
        catch (OperationCanceledException) { /* intentional */ }
        catch (Exception ex) { _logger.LogError(ex, "Fade error on handle {H}", handle); }
        finally
        {
            lock (_gate)
            {
                if (cts is not null && _fades.TryGetValue(handle, out var current) && ReferenceEquals(current, cts))
                    _fades.Remove(handle);
            }
            cts?.Dispose();
        }
    }

    public void CancelFade(int handle)
    {
        CancellationTokenSource? prev;
        lock (_gate)
        {
            _fades.Remove(handle, out prev);
        }
        prev?.Cancel();
    }

    public void Dispose()
    {
        List<CancellationTokenSource> active;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            active = _fades.Values.ToList();
            _fades.Clear();
        }
        foreach (var cts in active)
            cts.Cancel();
    }
}
