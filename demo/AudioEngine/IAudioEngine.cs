using ShowCuePlayer.Models;

namespace ShowCuePlayer.AudioEngine;

/// <summary>
/// Contract for the core audio playback engine.
/// Implemented by BassAudioEngine using ManagedBass.
/// </summary>
public interface IAudioEngine : IDisposable
{
    /// <summary>Initialize the audio engine with the configured output device.</summary>
    bool Initialize(int deviceIndex = -1);

    /// <summary>Load an audio file and return a channel handle. Returns -1 on failure.</summary>
    Task<int> LoadAsync(string filePath);

    /// <summary>Play a loaded channel.</summary>
    void Play(int handle);

    /// <summary>Pause a playing channel.</summary>
    void Pause(int handle);

    /// <summary>Resume a paused channel.</summary>
    void Resume(int handle);

    /// <summary>Stop and free a channel.</summary>
    void Stop(int handle);

    /// <summary>Stop all currently active channels.</summary>
    void StopAll();

    /// <summary>Seek to a position in seconds.</summary>
    void Seek(int handle, double positionSeconds);

    /// <summary>Get the current playback position in seconds.</summary>
    double GetPosition(int handle);

    /// <summary>Get the total duration of a channel in seconds.</summary>
    double GetDuration(int handle);

    /// <summary>Set the volume of a channel (0.0 – 1.0).</summary>
    void SetVolume(int handle, double volume);

    /// <summary>Get the volume of a channel (0.0 - 1.0).</summary>
    double GetVolume(int handle);

    /// <summary>Set stereo pan (-1 left, 0 center, 1 right).</summary>
    void SetPan(int handle, double pan);

    /// <summary>Set master output volume (0.0 – 1.0).</summary>
    void SetMasterVolume(double volume);

    /// <summary>Set loop mode for a channel.</summary>
    void SetLoop(int handle, bool loop);

    /// <summary>Returns true if the channel is currently playing.</summary>
    bool IsPlaying(int handle);

    /// <summary>Returns true if the channel is paused.</summary>
    bool IsPaused(int handle);

    /// <summary>Get the currently active device index.</summary>
    int CurrentDeviceIndex { get; }

    /// <summary>Get available output devices.</summary>
    IReadOnlyList<AudioDeviceInfo> GetOutputDevices();

    /// <summary>Switch to a different output device.</summary>
    bool SwitchDevice(int deviceIndex);

    /// <summary>Fired when a channel reaches its natural end.</summary>
    event EventHandler<int> ChannelEnded;
}

/// <summary>Describes an available audio output device.</summary>
public record AudioDeviceInfo(int Index, string Name, bool IsDefault, bool IsEnabled);
