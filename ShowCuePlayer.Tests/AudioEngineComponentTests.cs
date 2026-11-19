using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ShowCuePlayer.AudioEngine;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ShowCuePlayer.Tests;

[TestClass]
public sealed class AudioEngineComponentTests
{
    private sealed class FakeAudioEngine : IAudioEngine
    {
        private int _nextHandle = 100;
        public ConcurrentBag<int> StoppedHandles { get; } = new();
        public ConcurrentBag<int> PlayedHandles { get; } = new();
        public Dictionary<int, double> Volumes { get; } = new();
        public TaskCompletionSource<bool>? BlockLoad { get; set; }

        public int CurrentDeviceIndex => 1;
        public event EventHandler<int>? ChannelEnded;

        public void ConfigureBuffer(int bufferMs) { }
        public bool Initialize(int deviceIndex = -1) => true;

        public async Task<int> LoadAsync(string filePath)
        {
            if (BlockLoad is not null)
                await BlockLoad.Task;
            return Interlocked.Increment(ref _nextHandle);
        }

        public void Play(int handle) => PlayedHandles.Add(handle);
        public void Pause(int handle) { }
        public void Resume(int handle) { }
        public void Stop(int handle) => StoppedHandles.Add(handle);
        public void StopAll() { }
        public void Seek(int handle, double positionSeconds) { }
        public double GetPosition(int handle) => 0;
        public double GetDuration(int handle) => 10;
        public void SetVolume(int handle, double volume) => Volumes[handle] = volume;
        public double GetVolume(int handle) => Volumes.GetValueOrDefault(handle, 1.0);
        public void SetPan(int handle, double pan) { }
        public void SetMasterVolume(double volume) { }
        public void SetLoop(int handle, bool loop) { }
        public bool IsPlaying(int handle) => false;
        public bool IsPaused(int handle) => false;
        public IReadOnlyList<AudioDeviceInfo> GetOutputDevices() => Array.Empty<AudioDeviceInfo>();
        public bool SwitchDevice(int deviceIndex) => true;
        public void Dispose() { }
        public void RaiseEnded(int handle) => ChannelEnded?.Invoke(this, handle);
    }

    private sealed class FakeFadeEngine : IFadeEngine
    {
        public List<(int Handle, double From, double To, double Duration)> Calls { get; } = new();
        public Task FadeInAsync(int handle, double fromVolume, double toVolume, double durationSeconds)
        {
            Calls.Add((handle, fromVolume, toVolume, durationSeconds));
            return Task.CompletedTask;
        }

        public Task FadeOutAsync(int handle, double fromVolume, double durationSeconds)
        {
            Calls.Add((handle, fromVolume, 0, durationSeconds));
            return Task.CompletedTask;
        }

        public void CancelFade(int handle) { }
    }

    [TestMethod]
    public async Task PreloadManager_EvictsOldestLruAndPreventsClearRaceLeaks()
    {
        var audio = new FakeAudioEngine();
        using var preload = new PreloadManager(audio, NullLogger<PreloadManager>.Instance);

        Assert.IsTrue(await preload.PreloadAsync(@"C:\1.mp3"));
        Assert.IsTrue(await preload.PreloadAsync(@"C:\2.mp3"));
        Assert.IsTrue(await preload.PreloadAsync(@"C:\3.mp3"));
        // Touch 1 so 2 becomes least recently used
        Assert.IsTrue(await preload.PreloadAsync(@"C:\1.mp3"));
        Assert.IsTrue(await preload.PreloadAsync(@"C:\4.mp3"));

        Assert.AreEqual(1, audio.StoppedHandles.Count); // handle for 2.mp3 was evicted & stopped
        Assert.AreEqual(0, await preload.GetPreloadedHandleAsync(@"C:\2.mp3"));
        Assert.AreNotEqual(0, await preload.GetPreloadedHandleAsync(@"C:\1.mp3"));

        // Clear during an in-flight LoadAsync must stop the late handle instead of leaking
        audio.BlockLoad = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = preload.PreloadAsync(@"C:\5.mp3");
        preload.Clear();
        audio.BlockLoad.SetResult(true);
        Assert.IsFalse(await pending);
        Assert.AreEqual(0, await preload.GetPreloadedHandleAsync(@"C:\5.mp3"));
    }

    [TestMethod]
    public async Task CrossfadeEngine_ClampsVolumesAndStopsOutgoingHandle()
    {
        var audio = new FakeAudioEngine();
        var fade = new FakeFadeEngine();
        var crossfade = new CrossfadeEngine(audio, fade, NullLogger<CrossfadeEngine>.Instance);

        await crossfade.CrossfadeAsync(outHandle: 10, inHandle: 20, durationSeconds: 1.5, outFromVolume: 1.8, inToVolume: 0.7);

        Assert.AreEqual(0.0, audio.Volumes[20], 0.001);
        Assert.IsTrue(audio.PlayedHandles.Contains(20));
        Assert.IsTrue(audio.StoppedHandles.Contains(10));
        Assert.AreEqual(2, fade.Calls.Count);
        Assert.AreEqual((10, 1.0, 0.0, 1.5), fade.Calls[0]);
        Assert.AreEqual((20, 0.0, 0.7, 1.5), fade.Calls[1]);
    }
}
