using Microsoft.Data.Sqlite;
using ShowCuePlayer.Models;
using System.Text.Json;

namespace ShowCuePlayer.Database.Repositories;

/// <summary>Repository for app settings stored as key-value pairs in SQLite.</summary>
public interface ISettingsRepository
{
    Task<AppSettings> LoadAsync();
    Task SaveAsync(AppSettings settings);
}

public sealed class SettingsRepository : ISettingsRepository
{
    private readonly IDatabaseService _db;
    public SettingsRepository(IDatabaseService db) => _db = db;

    public async Task<AppSettings> LoadAsync()
    {
        await using var conn = _db.GetConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Value FROM Settings WHERE Key='AppSettings'";
        var json = (string?)await cmd.ExecuteScalarAsync();
        if (string.IsNullOrEmpty(json)) return new AppSettings();
        return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
    }

    public async Task SaveAsync(AppSettings settings)
    {
        await using var conn = _db.GetConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO Settings(Key,Value) VALUES('AppSettings',@v)
                            ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value";
        cmd.Parameters.AddWithValue("@v", JsonSerializer.Serialize(settings));
        await cmd.ExecuteNonQueryAsync();
    }
}
