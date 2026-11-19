using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ShowCuePlayer.Database;
using ShowCuePlayer.Database.Repositories;
using ShowCuePlayer.Models;
using ShowCuePlayer.Services;
using System;
using System.IO;
using System.Threading.Tasks;

namespace ShowCuePlayer.Tests;

[TestClass]
public sealed class SettingsPersistenceTests
{
    private sealed class TempDatabaseService : IDatabaseService, IDisposable
    {
        public string DatabasePath { get; }

        public TempDatabaseService()
        {
            DatabasePath = Path.Combine(Path.GetTempPath(), $"scp-test-{Guid.NewGuid():N}.db");
        }

        public SqliteConnection GetConnection()
        {
            var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DatabasePath }.ToString());
            conn.Open();
            return conn;
        }

        public async Task InitializeAsync()
        {
            await using var conn = GetConnection();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                PRAGMA journal_mode=WAL;
                CREATE TABLE IF NOT EXISTS Settings (
                    Key TEXT PRIMARY KEY,
                    Value TEXT NOT NULL
                );";
            await cmd.ExecuteNonQueryAsync();
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                var path = DatabasePath + suffix;
                if (File.Exists(path))
                    try { File.Delete(path); } catch { }
            }
        }
    }

    [TestMethod]
    public async Task SettingsRepository_RoundTripAndCorruptJsonRecovery()
    {
        using var db = new TempDatabaseService();
        await db.InitializeAsync();
        var repo = new SettingsRepository(db, NullLogger<SettingsRepository>.Instance);

        var saved = new AppSettings
        {
            KaraokeSessionId = "ROOM99",
            KaraokeSessionPassword = "9876",
            MasterVolume = 0.72,
            KaraokeAutoNextEnabled = true
        };
        await repo.SaveAsync(saved);

        var loaded = await repo.LoadAsync();
        Assert.AreEqual("ROOM99", loaded.KaraokeSessionId);
        Assert.AreEqual("9876", loaded.KaraokeSessionPassword);
        Assert.AreEqual(0.72, loaded.MasterVolume, 0.001);
        Assert.IsTrue(loaded.KaraokeAutoNextEnabled);

        await using (var conn = db.GetConnection())
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "UPDATE Settings SET Value='{not-valid-json' WHERE Key='AppSettings'";
            await cmd.ExecuteNonQueryAsync();
        }

        var recovered = await repo.LoadAsync();
        Assert.AreEqual("MIXH88", recovered.KaraokeDefaultSessionId);
    }

    [TestMethod]
    public async Task SettingsService_DebouncedSaveSupersedesPreviousRequestsWithoutError()
    {
        using var db = new TempDatabaseService();
        await db.InitializeAsync();
        var repo = new SettingsRepository(db, NullLogger<SettingsRepository>.Instance);
        using var service = new SettingsService(repo);
        await service.LoadAsync();

        service.Current.KaraokeSessionId = "FIRST1";
        await service.SaveDebouncedAsync(80);
        service.Current.KaraokeSessionId = "SECOND";
        await service.SaveDebouncedAsync(80);
        service.Current.KaraokeSessionId = "FINAL9";
        await service.SaveDebouncedAsync(40);

        AppSettings? persisted = null;
        for (int i = 0; i < 40; i++)
        {
            await Task.Delay(50);
            persisted = await repo.LoadAsync();
            if (persisted.KaraokeSessionId == "FINAL9") break;
        }
        Assert.AreEqual("FINAL9", persisted?.KaraokeSessionId);
    }
}
