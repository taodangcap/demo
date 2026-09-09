using Microsoft.Extensions.Logging;

namespace ShowCuePlayer.AudioEngine;

/// <summary>Contract for crossfade between two audio channels.</summary>
public interface ICrossfadeEngine
{
    /// <param name="outFromVolume">Volume of outgoing channel at start of fade (0–1).</param>
    /// <param name="inToVolume">Target volume of incoming channel (0–1).</param>
    Task CrossfadeAsync(int outHandle, int inHandle, double durationSeconds,
        double outFromVolume = 1.0, double inToVolume = 1.0);
}

/// <summary>
/// Equal-power crossfade engine. Fades out the outgoing channel while simultaneously
/// fading in the incoming channel for a seamless musical transition.
/// </summary>
public sealed class CrossfadeEngine : ICrossfadeEngine
{
    private readonly IAudioEngine _audio;
    private readonly IFadeEngine _fade;
    private readonly ILogger<CrossfadeEngine> _logger;

    public CrossfadeEngine(IAudioEngine audio, IFadeEngine fade, ILogger<CrossfadeEngine> logger)
    {
        _audio = audio;
        _fade = fade;
        _logger = logger;
    }

    public async Task CrossfadeAsync(int outHandle, int inHandle, double durationSeconds,
        double outFromVolume = 1.0, double inToVolume = 1.0)
    {
        outFromVolume = Math.Clamp(outFromVolume, 0, 1);
        inToVolume = Math.Clamp(inToVolume, 0, 1);

        if (durationSeconds <= 0)
        {
            if (outHandle != 0 && outHandle != -1) _audio.Stop(outHandle);
            if (inHandle != 0 && inHandle != -1)
            {
                _audio.SetVolume(inHandle, inToVolume);
                _audio.Play(inHandle);
            }
            return;
        }

        _logger.LogDebug("Crossfade {Out}->{In} over {Dur}s (outVol={OutV}, inVol={InV})",
            outHandle, inHandle, durationSeconds, outFromVolume, inToVolume);

        if (inHandle != 0 && inHandle != -1)
        {
            _audio.SetVolume(inHandle, 0.0);
            if (!_audio.IsPlaying(inHandle))
                _audio.Play(inHandle);
        }

        var fadeOut = (outHandle != 0 && outHandle != -1)
            ? _fade.FadeOutAsync(outHandle, outFromVolume, durationSeconds)
            : Task.CompletedTask;

        var fadeIn = (inHandle != 0 && inHandle != -1)
            ? _fade.FadeInAsync(inHandle, 0.0, inToVolume, durationSeconds)
            : Task.CompletedTask;

        await Task.WhenAll(fadeOut, fadeIn);

        if (outHandle != 0 && outHandle != -1) _audio.Stop(outHandle);
    }
}
