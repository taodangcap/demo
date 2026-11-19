using ManagedBass;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace ShowCuePlayer.AudioEngine;

/// <summary>
/// ManagedBass-based audio engine. Supports unlimited concurrent channels,
/// ultra-low latency, per-channel volume, loop, seek, and device switching.
/// Thread-safe. All BASS calls on the correct thread context.
/// </summary>
public sealed class BassAudioEngine : IAudioEngine
{
    private readonly ILogger<BassAudioEngine> _logger;
    private readonly ConcurrentDictionary<int, AudioChannel> _channels = new ConcurrentDictionary<int, AudioChannel>();
    // Keep a single rooted delegate instance so GC cannot collect it while native bass.dll holds the pointer.
    private readonly SyncProcedure _channelEndProc;
    private bool _initialized;
    private int _currentDevice = -1;
    private int _bufferMs = 100;
    private bool _disposed;
    private readonly object _lifecycleGate = new();

    public event EventHandler<int>? ChannelEnded;

    public int CurrentDeviceIndex => _currentDevice;

    public BassAudioEngine(ILogger<BassAudioEngine> logger)
    {
        _logger = logger;
        _channelEndProc = OnChannelEnd;
    }

    public void ConfigureBuffer(int bufferMs)
    {
        lock (_lifecycleGate)
        {
            // ManagedBass expects PlaybackBufferLength >= ~30 ms on Windows output devices; match AppSettings upper bound 5000 ms.
            _bufferMs = Math.Clamp(bufferMs, 30, 5000);
            if (_initialized)
                Bass.PlaybackBufferLength = _bufferMs;
        }
    }

    public bool Initialize(int deviceIndex = -1)
    {
        lock (_lifecycleGate)
        {
            return InitializeCore(deviceIndex);
        }
    }

    private bool InitializeCore(int deviceIndex)
    {
        try
        {
            if (_disposed) return false;
            if (_initialized) return true;

            Bass.UpdatePeriod = 5;
            Bass.PlaybackBufferLength = _bufferMs;

            if (deviceIndex == -1)
            {
                deviceIndex = GetBestDefaultDeviceIndex();
            }

            LogAvailableDevices();

            bool ok = Bass.Init(deviceIndex, 44100, DeviceInitFlags.Default);
            var initErr = Bass.LastError;
            _logger.LogDebug("BASS Initialize: device={Device}, ok={Ok}, LastError={Error}", deviceIndex, ok, initErr);

            if (!ok && initErr != Errors.Already)
            {
                // Fallback 1: Try default/enabled devices
                int deviceCount = Bass.DeviceCount;
                for (int i = 1; i < deviceCount; i++)
                {
                    if (Bass.GetDeviceInfo(i, out var info) && info.IsEnabled)
                    {
                        _logger.LogDebug("BASS fallback trying device {Index} ({Name})", i, info.Name);
                        if (Bass.Init(i, 44100, DeviceInitFlags.Default))
                        {
                            ok = true;
                            deviceIndex = i;
                            initErr = Errors.OK;
                            break;
                        }
                    }
                }
            }

            if (!ok && initErr != Errors.Already)
            {
                // Fallback 2: Try device 0 (No Sound device) so the app doesn't fail completely
                _logger.LogDebug("BASS fallback trying device 0 (No Sound)");
                if (Bass.Init(0, 44100, DeviceInitFlags.Default))
                {
                    ok = true;
                    deviceIndex = 0;
                    initErr = Errors.OK;
                }
            }

            if (!ok && initErr != Errors.Already)
            {
                _logger.LogError("BASS init failed: {Error}", initErr);
                return false;
            }

            _currentDevice = initErr == Errors.Already ? Bass.CurrentDevice : deviceIndex;
            _initialized = true;
            _logger.LogInformation("BASS initialized. Device={Device}", _currentDevice);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception initializing BASS");
            return false;
        }
    }

    private void LogAvailableDevices()
    {
        if (!_logger.IsEnabled(LogLevel.Debug)) return;
        try
        {
            int deviceCount = Bass.DeviceCount;
            _logger.LogDebug("BASS Device Count: {Count}", deviceCount);
            for (int i = 0; i < deviceCount; i++)
            {
                if (Bass.GetDeviceInfo(i, out var info))
                {
                    _logger.LogDebug("Device {Index}: Name={Name}, IsEnabled={Enabled}, IsDefault={Default}, Type={Type}",
                        i, info.Name, info.IsEnabled, info.IsDefault, info.Type);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to enumerate BASS audio devices");
        }
    }

    public async Task<int> LoadAsync(string filePath)
    {
        if (!_initialized || string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return -1;

        return await Task.Run(() =>
        {
            try
            {
                int handle;
                Errors loadErr;
                var flags = BassFlags.Prescan | BassFlags.Float;

                lock (_lifecycleGate)
                {
                    if (_disposed || !_initialized) return -1;
                    SetActiveBassDevice(_currentDevice);
                    handle = Bass.CreateStream(filePath, 0, 0, flags);
                    loadErr = Bass.LastError;

                    if (handle == 0 && loadErr == Errors.Init)
                    {
                        _logger.LogWarning("BASS CreateStream returned Errors.Init; re-initializing device {Device}", _currentDevice);
                        _initialized = false;
                        Bass.Free();
                        if (InitializeCore(_currentDevice))
                        {
                            SetActiveBassDevice(_currentDevice);
                            handle = Bass.CreateStream(filePath, 0, 0, flags);
                            loadErr = Bass.LastError;
                        }
                    }
                }

                if (handle == 0)
                {
                    _logger.LogWarning("Failed to load {File}: {Error}", filePath, loadErr);
                    return -1;
                }

                // Register end-of-stream sync using the rooted delegate instance
                Bass.ChannelSetSync(handle, SyncFlags.End, 0, _channelEndProc, handle);

                var channel = new AudioChannel(handle, filePath);
                _channels[handle] = channel;

                _logger.LogDebug("Loaded: {File} -> handle {Handle}", System.IO.Path.GetFileName(filePath), handle);
                return handle;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception loading {File}", filePath);
                return -1;
            }
        });
    }

    private void SetActiveBassDevice(int deviceIndex)
    {
        if (deviceIndex < 0) return;
        try
        {
            Bass.CurrentDevice = deviceIndex;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "set_CurrentDevice to {Device} failed", deviceIndex);
        }
    }

    private void OnChannelEnd(int handle, int channel, int data, nint user)
    {
        _logger.LogDebug("Channel {Handle} ended", handle);
        ChannelEnded?.Invoke(this, handle);
    }

    public void Play(int handle)
    {
        if (!_channels.ContainsKey(handle)) return;
        Bass.ChannelPlay(handle, false);
    }

    public void Pause(int handle)
    {
        if (!_channels.ContainsKey(handle)) return;
        Bass.ChannelPause(handle);
    }

    public void Resume(int handle)
    {
        if (!_channels.ContainsKey(handle)) return;
        Bass.ChannelPlay(handle, false);
    }

    public void Stop(int handle)
    {
        lock (_lifecycleGate)
        {
            if (!_channels.TryRemove(handle, out _)) return;
            if (!Bass.ChannelStop(handle))
                _logger.LogDebug("BASS stop returned {Error} for handle {Handle}", Bass.LastError, handle);
            if (!Bass.StreamFree(handle))
                _logger.LogWarning("BASS stream free failed: {Error}; handle={Handle}", Bass.LastError, handle);
        }
    }

    public void StopAll()
    {
        foreach (var key in _channels.Keys.ToList())
            Stop(key);
    }

    private void LogDebug(string message)
        => _logger.LogDebug("{Message}", message);

    public void Seek(int handle, double positionSeconds)
    {
        if (!_channels.ContainsKey(handle)) return;
        long bytes = Bass.ChannelSeconds2Bytes(handle, Math.Max(0, positionSeconds));
        if (!Bass.ChannelSetPosition(handle, bytes))
            _logger.LogDebug("Seek handle={Handle} to {Pos}s failed: {Error}", handle, positionSeconds, Bass.LastError);
    }

    public double GetPosition(int handle)
    {
        if (!_channels.ContainsKey(handle)) return 0;
        long pos = Bass.ChannelGetPosition(handle);
        return Bass.ChannelBytes2Seconds(handle, pos);
    }

    public double GetDuration(int handle)
    {
        if (!_channels.ContainsKey(handle)) return 0;
        long len = Bass.ChannelGetLength(handle);
        return Bass.ChannelBytes2Seconds(handle, len);
    }

    public void SetVolume(int handle, double volume)
    {
        if (!_channels.ContainsKey(handle)) return;
        if (!double.IsFinite(volume)) volume = 0;
        float vol = (float)Math.Clamp(volume, 0, 1);
        if (!Bass.ChannelSetAttribute(handle, ChannelAttribute.Volume, vol))
            _logger.LogDebug("SetVolume handle={Handle} failed: {Error}", handle, Bass.LastError);
    }

    public double GetVolume(int handle)
    {
        if (!_channels.ContainsKey(handle)) return 0;
        Bass.ChannelGetAttribute(handle, ChannelAttribute.Volume, out float vol);
        return vol;
    }

    public void SetPan(int handle, double pan)
    {
        if (!_channels.ContainsKey(handle)) return;
        Bass.ChannelSetAttribute(handle, ChannelAttribute.Pan, (float)Math.Clamp(pan, -1, 1));
    }

    public void SetMasterVolume(double volume)
    {
        if (!_initialized) return; // Not initialized, skip
        
        try
        {
            if (!double.IsFinite(volume)) volume = 0;
            Bass.Volume = (float)Math.Clamp(volume, 0, 1);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SetMasterVolume error: {ex.Message}");
        }
    }

    public void SetLoop(int handle, bool loop)
    {
        if (!_channels.ContainsKey(handle)) return;
        if (loop)
            Bass.ChannelFlags(handle, BassFlags.Loop, BassFlags.Loop);
        else
            Bass.ChannelFlags(handle, BassFlags.Default, BassFlags.Loop);
    }

    public bool IsPlaying(int handle)
    {
        return _channels.ContainsKey(handle) &&
               Bass.ChannelIsActive(handle) == PlaybackState.Playing;
    }

    public bool IsPaused(int handle)
    {
        return _channels.ContainsKey(handle) &&
               Bass.ChannelIsActive(handle) == PlaybackState.Paused;
    }

    public IReadOnlyList<AudioDeviceInfo> GetOutputDevices()
    {
        var devices = new List<AudioDeviceInfo>();
        for (int i = 0; Bass.GetDeviceInfo(i, out var info); i++)
        {
            devices.Add(new AudioDeviceInfo(i, info.Name,
                info.IsDefault, info.IsEnabled));
        }
        return devices;
    }

    public bool SwitchDevice(int deviceIndex)
    {
        lock (_lifecycleGate)
        {
            return SwitchDeviceCore(deviceIndex);
        }
    }

    private bool SwitchDeviceCore(int deviceIndex)
    {
        try
        {
            if (_disposed) return false;
            if (_initialized && _currentDevice == deviceIndex)
                return true;

            var previousDevice = _currentDevice;
            StopAll();
            Bass.Free();
            _initialized = false;
            if (InitializeCore(deviceIndex))
                return true;

            if (previousDevice != deviceIndex)
                InitializeCore(previousDevice);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to switch device");
            return false;
        }
    }

    public void Dispose()
    {
        lock (_lifecycleGate)
        {
            if (_disposed) return;
            StopAll();
            if (_initialized && !Bass.Free())
                _logger.LogWarning("BASS shutdown failed: {Error}", Bass.LastError);
            _initialized = false;
            _currentDevice = -1;
            _disposed = true;
        }
    }

    private int GetBestDefaultDeviceIndex()
    {
        try
        {
            int deviceCount = Bass.DeviceCount;
            // 1st pass: Look for an enabled, default device that is NOT named "Default" or "No sound"
            for (int i = 1; i < deviceCount; i++)
            {
                if (Bass.GetDeviceInfo(i, out var info) && info.IsEnabled && info.IsDefault)
                {
                    if (!string.Equals(info.Name, "Default", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(info.Name, "No sound", StringComparison.OrdinalIgnoreCase))
                    {
                        return i;
                    }
                }
            }
            // 2nd pass: Look for the first enabled device that is NOT named "Default" or "No sound"
            for (int i = 1; i < deviceCount; i++)
            {
                if (Bass.GetDeviceInfo(i, out var info) && info.IsEnabled)
                {
                    if (!string.Equals(info.Name, "Default", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(info.Name, "No sound", StringComparison.OrdinalIgnoreCase))
                    {
                        return i;
                    }
                }
            }
            // 3rd pass: Any enabled device (including Default virtual mapper)
            for (int i = 1; i < deviceCount; i++)
            {
                if (Bass.GetDeviceInfo(i, out var info) && info.IsEnabled)
                {
                    return i;
                }
            }
        }
        catch {}
        return -1; // Fallback to BASS default
    }
}
