using Microsoft.Data.Sqlite;
using ShowCuePlayer.Models;
using System.Text.Json;
using Microsoft.Extensions.Logging;

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
    private readonly ILogger<SettingsRepository> _logger;
    public SettingsRepository(IDatabaseService db, ILogger<SettingsRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<AppSettings> LoadAsync()
    {
        await using var conn = _db.GetConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Value FROM Settings WHERE Key='AppSettings'";
        var json = (string?)await cmd.ExecuteScalarAsync();
        if (string.IsNullOrEmpty(json)) return new AppSettings();
        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            settings.Validate();
            return settings;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Stored application settings are invalid; defaults will be used");
            return new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        await using var conn = _db.GetConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO Settings(Key,Value) VALUES('AppSettings',@v)
                            ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value";
        cmd.Parameters.AddWithValue("@v", JsonSerializer.Serialize(settings));
        await cmd.ExecuteNonQueryAsync();
    }
}
