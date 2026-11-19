using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ShowCuePlayer.AudioEngine;
using ShowCuePlayer.Database;
using ShowCuePlayer.Helpers;
using ShowCuePlayer.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace ShowCuePlayer.Tests;

[TestClass]
public sealed class RuntimeIntegrationTests
{
    private static string CreateSilentWavFile(double seconds = 0.25, int sampleRate = 44100)
    {
        var path = Path.Combine(Path.GetTempPath(), $"scp-wav-{Guid.NewGuid():N}.wav");
        int numSamples = (int)(sampleRate * seconds);
        short channels = 2;
        short bitsPerSample = 16;
        int dataSize = numSamples * channels * (bitsPerSample / 8);

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);
        bw.Write("RIFF"u8);
        bw.Write(36 + dataSize);
        bw.Write("WAVE"u8);
        bw.Write("fmt "u8);
        bw.Write(16);
        bw.Write((short)1); // PCM
        bw.Write(channels);
        bw.Write(sampleRate);
        bw.Write(sampleRate * channels * (bitsPerSample / 8));
        bw.Write((short)(channels * (bitsPerSample / 8)));
        bw.Write(bitsPerSample);
        bw.Write("data"u8);
        bw.Write(dataSize);
        bw.Write(new byte[dataSize]);
        return path;
    }

    [TestMethod]
    public async Task BassAudioEngine_And_FadeEngine_RealBassLifecycle()
    {
        var wavPath = CreateSilentWavFile(seconds: 0.5);
        try
        {
            using var engine = new BassAudioEngine(NullLogger<BassAudioEngine>.Instance);
            using var fade = new FadeEngine(NullLogger<FadeEngine>.Instance);

            Assert.IsTrue(engine.Initialize(), "BASS should initialize on default or No-Sound device");
            engine.ConfigureBuffer(160);
            Assert.AreEqual(160, ManagedBass.Bass.PlaybackBufferLength);
            engine.ConfigureBuffer(2);
            Assert.AreEqual(30, ManagedBass.Bass.PlaybackBufferLength);
            var devices = engine.GetOutputDevices();
            Assert.IsTrue(devices.Count >= 1);

            int handle = await engine.LoadAsync(wavPath);
            Assert.AreNotEqual(-1, handle);
            Assert.AreNotEqual(0, handle);

            var duration = engine.GetDuration(handle);
            Assert.IsTrue(duration > 0.4 && duration < 0.7, $"Unexpected WAV duration: {duration}");

            engine.SetVolume(handle, 0.5);
            Assert.AreEqual(0.5, engine.GetVolume(handle), 0.05);

            engine.Play(handle);
            Assert.IsTrue(engine.IsPlaying(handle));

            await fade.FadeInAsync(handle, 0.1, 0.8, 0.05);
            Assert.AreEqual(0.8, engine.GetVolume(handle), 0.08);

            engine.Pause(handle);
            Assert.IsTrue(engine.IsPaused(handle));

            engine.Seek(handle, 0.1);
            Assert.AreEqual(0.1, engine.GetPosition(handle), 0.05);

            engine.Resume(handle);
            Assert.IsTrue(engine.IsPlaying(handle));

            fade.CancelFade(handle);
            engine.Stop(handle);
            Assert.IsFalse(engine.IsPlaying(handle));
        }
        finally
        {
            if (File.Exists(wavPath))
                File.Delete(wavPath);
        }
    }

    [TestMethod]
    public void AudioDeviceMatching_And_ScreenEnumeration_DoNotThrow()
    {
        var endpoints = WindowsAudioOutputService.GetActiveOutputs();
        Assert.IsNotNull(endpoints);
        Assert.IsTrue(System.Windows.Forms.Screen.AllScreens.Length >= 1);
    }

    [TestMethod]
    public async Task DatabaseService_V2_DropsLegacyTables_AndKeepsSettings()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"scp-db-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var dbPath = Path.Combine(tempDir, "test.db");

        try
        {
            var db = DatabaseService.CreateForPath(NullLogger<DatabaseService>.Instance, dbPath);
            await using (var seedConn = db.GetConnection())
            await using (var seedCmd = seedConn.CreateCommand())
            {
                seedCmd.CommandText = @"
                    CREATE TABLE Projects (Id TEXT PRIMARY KEY);
                    CREATE TABLE Cues (Id TEXT PRIMARY KEY);
                    CREATE TABLE Playlists (Id TEXT PRIMARY KEY);
                    CREATE TABLE RecentFiles (Id INTEGER PRIMARY KEY);
                    CREATE TABLE Settings (Key TEXT PRIMARY KEY, Value TEXT NOT NULL);
                    CREATE TABLE SchemaInfo (Version INTEGER NOT NULL);
                    INSERT INTO Settings(Key, Value) VALUES('theme', 'dark');
                    INSERT INTO SchemaInfo(Version) VALUES(1);";
                await seedCmd.ExecuteNonQueryAsync();
            }

            await db.InitializeAsync();

            var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var checkConn = db.GetConnection())
            await using (var checkCmd = checkConn.CreateCommand())
            {
                checkCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
                await using var reader = await checkCmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    tables.Add(reader.GetString(0));

                await using var verCmd = checkConn.CreateCommand();
                verCmd.CommandText = "SELECT MAX(Version) FROM SchemaInfo";
                Assert.AreEqual(2, Convert.ToInt32(await verCmd.ExecuteScalarAsync()));

                await using var settingCmd = checkConn.CreateCommand();
                settingCmd.CommandText = "SELECT Value FROM Settings WHERE Key='theme'";
                Assert.AreEqual("dark", Convert.ToString(await settingCmd.ExecuteScalarAsync()));
            }

            Assert.IsTrue(tables.Contains("Settings"));
            Assert.IsTrue(tables.Contains("SchemaInfo"));
            Assert.IsFalse(tables.Contains("Projects"));
            Assert.IsFalse(tables.Contains("Cues"));
            Assert.IsFalse(tables.Contains("Playlists"));
            Assert.IsFalse(tables.Contains("RecentFiles"));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public async Task MetadataService_DoesNotAllocateAlbumArtData()
    {
        var wavPath = CreateSilentWavFile(seconds: 0.3);
        try
        {
            var svc = new MetadataService();
            var meta = await svc.ReadAsync(wavPath);
            Assert.IsNull(meta.AlbumArtData);
            Assert.AreEqual(string.Empty, meta.AlbumArtMimeType);
            Assert.IsTrue(meta.DurationSeconds > 0.2);
        }
        finally
        {
            if (File.Exists(wavPath))
                File.Delete(wavPath);
        }
    }
}
