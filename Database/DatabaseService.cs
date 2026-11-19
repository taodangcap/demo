using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace ShowCuePlayer.Database;

/// <summary>Contract for database initialization and connection management.</summary>
public interface IDatabaseService
{
    Task InitializeAsync();
    SqliteConnection GetConnection();
    string DatabasePath { get; }
}

/// <summary>
/// SQLite database service. Creates tables on first run and provides
/// connection factory for repositories. Uses WAL mode for performance.
/// </summary>
public sealed class DatabaseService : IDatabaseService
{
    private const int CurrentSchemaVersion = 2;
    private readonly ILogger<DatabaseService> _logger;
    public string DatabasePath { get; }

    public DatabaseService(ILogger<DatabaseService> logger)
    {
        _logger = logger;
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(appData, "ShowCuePlayer");
        Directory.CreateDirectory(dir);
        DatabasePath = Path.Combine(dir, "showcueplayer.db");
        MigrateLegacyDatabase(DatabasePath);
    }

    private DatabaseService(ILogger<DatabaseService> logger, string databasePath)
    {
        _logger = logger;
        var dir = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        DatabasePath = databasePath;
    }

    /// <summary>Creates a database service isolated to an explicit SQLite file path.</summary>
    public static DatabaseService CreateForPath(ILogger<DatabaseService> logger, string databasePath)
        => new(logger, databasePath);

    public SqliteConnection GetConnection()
    {
        var builder = new SqliteConnectionStringBuilder { DataSource = DatabasePath, DefaultTimeout = 15 };
        var conn = new SqliteConnection(builder.ToString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA synchronous=NORMAL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
        cmd.ExecuteNonQuery();
        return conn;
    }

    public async Task InitializeAsync()
    {
        _logger.LogInformation("Initializing database at {Path}", DatabasePath);
        try
        {
            await using var conn = GetConnection();
            await ExecuteAsync(conn, "PRAGMA journal_mode=WAL;");
            var version = await GetSchemaVersionAsync(conn);
            if (version > 0 && version < CurrentSchemaVersion && File.Exists(DatabasePath))
            {
                await ExecuteAsync(conn, "PRAGMA wal_checkpoint(TRUNCATE);");
                var suffix = Guid.NewGuid().ToString("N")[..6];
                File.Copy(DatabasePath, $"{DatabasePath}.backup-{DateTime.UtcNow:yyyyMMddHHmmss}-{suffix}", false);
            }

            await ExecuteAsync(conn, @"
            CREATE TABLE IF NOT EXISTS Settings (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS SchemaInfo (
                Version INTEGER NOT NULL
            );
            ");

            if (version < CurrentSchemaVersion)
            {
                // Retire unused legacy project/cue/playlist/recent-file tables removed in v2.
                await ExecuteAsync(conn, @"
                DROP INDEX IF EXISTS idx_cues_project;
                DROP INDEX IF EXISTS idx_cues_sort;
                DROP INDEX IF EXISTS idx_playlists_project;
                DROP TABLE IF EXISTS Cues;
                DROP TABLE IF EXISTS Playlists;
                DROP TABLE IF EXISTS Projects;
                DROP TABLE IF EXISTS RecentFiles;
                ");
                await ExecuteAsync(conn, $"DELETE FROM SchemaInfo; INSERT INTO SchemaInfo(Version) VALUES({CurrentSchemaVersion});");
            }

            _logger.LogInformation("Database initialized successfully at schema {Version}", CurrentSchemaVersion);
        }
        catch (SqliteException ex)
        {
            TryBackupCorruptDatabase();
            _logger.LogCritical(ex, "Database initialization failed; original database was preserved at {Path}", DatabasePath);
            throw;
        }
    }

    private static async Task ExecuteAsync(SqliteConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<int> GetSchemaVersionAsync(SqliteConnection conn)
    {
        await using var exists = conn.CreateCommand();
        exists.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='SchemaInfo'";
        if (Convert.ToInt32(await exists.ExecuteScalarAsync()) == 0) return 0;
        await using var command = conn.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(Version), 0) FROM SchemaInfo";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static void MigrateLegacyDatabase(string destination)
    {
        if (File.Exists(destination)) return;
        var legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ShowCuePlayer", "showcueplayer.db");
        if (!File.Exists(legacy)) return;
        File.Copy(legacy, destination, false);
    }

    private void TryBackupCorruptDatabase()
    {
        try
        {
            if (File.Exists(DatabasePath))
                File.Copy(DatabasePath, $"{DatabasePath}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6]}", false);
        }
        catch (Exception backupError) when (backupError is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(backupError, "Could not back up failed database {Path}", DatabasePath);
        }
    }
}
