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
public sealed class FadeEngine : IFadeEngine
{
    private readonly ILogger<FadeEngine> _logger;
    private readonly Dictionary<int, CancellationTokenSource> _fades = new Dictionary<int, CancellationTokenSource>();

    public FadeEngine(ILogger<FadeEngine> logger) => _logger = logger;

    public async Task FadeInAsync(int handle, double fromVolume, double toVolume, double durationSeconds)
        => await RunFadeAsync(handle, fromVolume, toVolume, durationSeconds);

    public async Task FadeOutAsync(int handle, double fromVolume, double durationSeconds)
        => await RunFadeAsync(handle, fromVolume, 0.0, durationSeconds);

    private async Task RunFadeAsync(int handle, double from, double to, double duration)
    {
        CancelFade(handle);
        var cts = new CancellationTokenSource();
        _fades[handle] = cts;

        try
        {
            const int tickMs = 10;
            int steps = Math.Max(1, (int)(duration * 1000 / tickMs));
            double delta = (to - from) / steps;
            double current = from;

            for (int i = 0; i < steps; i++)
            {
                if (cts.Token.IsCancellationRequested) return;
                current += delta;
                Bass.ChannelSetAttribute(handle, ChannelAttribute.Volume, (float)Math.Clamp(current, 0, 1));
                await Task.Delay(tickMs, cts.Token);
            }
            Bass.ChannelSetAttribute(handle, ChannelAttribute.Volume, (float)Math.Clamp(to, 0, 1));
        }
        catch (OperationCanceledException) { /* intentional */ }
        catch (Exception ex) { _logger.LogError(ex, "Fade error on handle {H}", handle); }
        finally { _fades.Remove(handle); }
    }

    public void CancelFade(int handle)
    {
        if (_fades.TryGetValue(handle, out var cts))
        {
            cts.Cancel();
            _fades.Remove(handle);
        }
    }
}
