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
    private bool _initialized;
    private int _currentDevice = -1;
    private bool _disposed;
    private readonly object _lifecycleGate = new();

    public event EventHandler<int>? ChannelEnded;

    public int CurrentDeviceIndex => _currentDevice;

    public BassAudioEngine(ILogger<BassAudioEngine> logger)
    {
        _logger = logger;
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
            Bass.PlaybackBufferLength = 100;

            if (deviceIndex == -1)
            {
                deviceIndex = GetBestDefaultDeviceIndex();
            }

            // Log all available devices
            try
            {
                var logPath = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "audio_debug.log");
                System.IO.File.AppendAllText(logPath, $"[{System.DateTime.Now:yyyy-MM-dd HH:mm:ss}] --- Enumerating Audio Devices ---\n");
                int deviceCount = Bass.DeviceCount;
                System.IO.File.AppendAllText(logPath, $"Device Count: {deviceCount}\n");
                for (int i = 0; i < deviceCount; i++)
                {
                    if (Bass.GetDeviceInfo(i, out var info))
                    {
                        System.IO.File.AppendAllText(logPath, $"Device {i}: Name={info.Name}, Driver={info.Driver}, IsEnabled={info.IsEnabled}, IsDefault={info.IsDefault}, IsInitialized={info.IsInitialized}, Type={info.Type}\n");
                    }
                }
            }
            catch (Exception ex)
            {
                try
                {
                    var logPath = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "audio_debug.log");
                    System.IO.File.AppendAllText(logPath, $"Failed to enumerate devices: {ex.Message}\n");
                }
                catch {}
            }

            bool ok = Bass.Init(deviceIndex, 44100, DeviceInitFlags.Default);
            var initErr = Bass.LastError;
            try
            {
                var logPath = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "audio_debug.log");
                System.IO.File.AppendAllText(logPath, $"[{System.DateTime.Now:yyyy-MM-dd HH:mm:ss}] Initialize called: device={deviceIndex}, ok={ok}, LastError={initErr}\n");
            }
            catch {}

            if (!ok && initErr != Errors.Already)
            {
                // Fallback 1: Try default/enabled devices
                int deviceCount = Bass.DeviceCount;
                for (int i = 1; i < deviceCount; i++)
                {
                    if (Bass.GetDeviceInfo(i, out var info) && info.IsEnabled)
                    {
                        try
                        {
                            var logPath = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "audio_debug.log");
                            System.IO.File.AppendAllText(logPath, $"[{System.DateTime.Now:yyyy-MM-dd HH:mm:ss}] Fallback: Trying device {i} ({info.Name})\n");
                        }
                        catch {}
                        
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
                try
                {
                    var logPath = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "audio_debug.log");
                    System.IO.File.AppendAllText(logPath, $"[{System.DateTime.Now:yyyy-MM-dd HH:mm:ss}] Fallback: Trying device 0 (No Sound)\n");
                }
                catch {}
                
                if (Bass.Init(0, 44100, DeviceInitFlags.Default))
                {
                    ok = true;
                    deviceIndex = 0;
                    initErr = Errors.OK;
                }
            }

            if (!ok && initErr != Errors.Already)
            {
                try
                {
                    var logPath = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "audio_debug.log");
                    System.IO.File.AppendAllText(logPath, $"[{System.DateTime.Now:yyyy-MM-dd HH:mm:ss}] BASS init failed completely: {initErr}\n");
                }
                catch {}
                _logger.LogError("BASS init failed: {Error}", initErr);
                return false;
            }

            _currentDevice = deviceIndex;
            _initialized = true;
            try
            {
                var logPath = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "audio_debug.log");
                System.IO.File.AppendAllText(logPath, $"[{System.DateTime.Now:yyyy-MM-dd HH:mm:ss}] BASS initialized successfully on device {deviceIndex} (actual device {_currentDevice}).\n");
            }
            catch {}
            _logger.LogInformation("BASS initialized. Device={Device}", deviceIndex);
            return true;
        }
        catch (Exception ex)
        {
            try
            {
                var logPath = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "audio_debug.log");
                System.IO.File.AppendAllText(logPath, $"[{System.DateTime.Now:yyyy-MM-dd HH:mm:ss}] Initialize exception: {ex.Message}\n{ex.StackTrace}\n");
            }
            catch {}
            _logger.LogError(ex, "Exception initializing BASS");
            return false;
        }
    }

    public async Task<int> LoadAsync(string filePath)
    {
        try
        {
            var logPath = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "audio_debug.log");
            System.IO.File.AppendAllText(logPath, $"[{System.DateTime.Now:yyyy-MM-dd HH:mm:ss}] LoadAsync: filePath={filePath}, Exists={File.Exists(filePath)}, Initialized={_initialized}\n");
        }
        catch {}

        if (!_initialized || !File.Exists(filePath)) return -1;

        return await Task.Run(() =>
        {
            try
            {
                if (_currentDevice >= 0)
                {
                    try
                    {
                        Bass.CurrentDevice = _currentDevice;
                    }
                    catch (Exception ex)
                    {
                        try
                        {
                            var logPath = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "audio_debug.log");
                            System.IO.File.AppendAllText(logPath, $"[{System.DateTime.Now:yyyy-MM-dd HH:mm:ss}] Warning: set_CurrentDevice to {_currentDevice} failed: {ex.Message}\n");
                        }
                        catch {}
                    }
                }
                var flags = BassFlags.Prescan | BassFlags.Float;
                int handle = Bass.CreateStream(filePath, 0, 0, flags);
                var loadErr = Bass.LastError;

                if (handle == 0 && loadErr == Errors.Init)
                {
                    try
                    {
                        var logPath = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "audio_debug.log");
                        System.IO.File.AppendAllText(logPath, $"[{System.DateTime.Now:yyyy-MM-dd HH:mm:ss}] CreateStream failed with Init. Triggering BASS re-initialization...\n");
                    }
                    catch {}

                    _initialized = false;
                    Bass.Free();
                    if (InitializeCore(_currentDevice))
                    {
                        if (_currentDevice >= 0)
                        {
                            try
                            {
                                Bass.CurrentDevice = _currentDevice;
                            }
                            catch {}
                        }
                        handle = Bass.CreateStream(filePath, 0, 0, flags);
                        loadErr = Bass.LastError;
                    }
                }
                
                try
                {
                    var logPath = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "audio_debug.log");
                    System.IO.File.AppendAllText(logPath, $"[{System.DateTime.Now:yyyy-MM-dd HH:mm:ss}] CreateStream handle={handle}, LastError={loadErr}\n");
                }
                catch {}

                if (handle == 0)
                {
                    _logger.LogWarning("Failed to load {File}: {Error}", filePath, loadErr);
                    return -1;
                }

                // Register end-of-stream sync
                Bass.ChannelSetSync(handle, SyncFlags.End, 0, OnChannelEnd, handle);

                var channel = new AudioChannel(handle, filePath);
                _channels[handle] = channel;

                _logger.LogDebug("Loaded: {File} -> handle {Handle}", System.IO.Path.GetFileName(filePath), handle);
                return handle;
            }
            catch (Exception ex)
            {
                try
                {
                    var logPath = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "audio_debug.log");
                    System.IO.File.AppendAllText(logPath, $"[{System.DateTime.Now:yyyy-MM-dd HH:mm:ss}] CreateStream exception: {ex.Message}\n{ex.StackTrace}\n");
                }
                catch {}
                _logger.LogError(ex, "Exception loading {File}", filePath);
                return -1;
            }
        });
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
    {
        try
        {
            var logPath = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "audio_debug.log");
            System.IO.File.AppendAllText(logPath, $"[{System.DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
        }
        catch {}
        _logger.LogDebug(message);
    }

    public void Seek(int handle, double positionSeconds)
    {
        if (!_channels.ContainsKey(handle))
        {
            LogDebug($"Seek: channel not found for handle {handle}");
            return;
        }
        long bytes = Bass.ChannelSeconds2Bytes(handle, positionSeconds);
        bool ok = Bass.ChannelSetPosition(handle, bytes);
        LogDebug($"Seek handle={handle} to {positionSeconds}s (bytes {bytes}): ok={ok}, LastError={Bass.LastError}");
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
        if (!_channels.ContainsKey(handle))
        {
            LogDebug($"SetVolume: channel not found for handle {handle}");
            return;
        }
        if (!double.IsFinite(volume)) volume = 0;
        float vol = (float)Math.Clamp(volume, 0, 1);
        bool ok = Bass.ChannelSetAttribute(handle, ChannelAttribute.Volume, vol);
        LogDebug($"SetVolume handle={handle} to {vol}: ok={ok}, LastError={Bass.LastError}");
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
        if (!double.IsFinite(volume)) volume = 0;
        Bass.Volume = (float)Math.Clamp(volume, 0, 1);
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
            if (_initialized && _currentDevice == deviceIndex)
            {
                return true;
            }
            StopAll();
            Bass.Free();
            _initialized = false;
            return InitializeCore(deviceIndex);
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
