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
    private const int CurrentSchemaVersion = 1;
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

    public SqliteConnection GetConnection()
    {
        var builder = new SqliteConnectionStringBuilder { DataSource = DatabasePath, DefaultTimeout = 15 };
        var conn = new SqliteConnection(builder.ToString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA foreign_keys=ON;";
        cmd.ExecuteNonQuery();
        return conn;
    }

    public async Task InitializeAsync()
    {
        _logger.LogInformation("Initializing database at {Path}", DatabasePath);
        try
        {
            await using var conn = GetConnection();
            var version = await GetSchemaVersionAsync(conn);
            if (version < CurrentSchemaVersion && File.Exists(DatabasePath))
                File.Copy(DatabasePath, $"{DatabasePath}.backup-{DateTime.UtcNow:yyyyMMddHHmmss}", false);

            await ExecuteAsync(conn, @"
            CREATE TABLE IF NOT EXISTS Projects (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                FilePath TEXT,
                DateCreated TEXT NOT NULL,
                DateModified TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Cues (
                Id TEXT PRIMARY KEY,
                ProjectId TEXT,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                Title TEXT NOT NULL,
                Artist TEXT,
                Album TEXT,
                FilePath TEXT NOT NULL,
                Volume REAL NOT NULL DEFAULT 1.0,
                Duration REAL NOT NULL DEFAULT 0,
                IsLooping INTEGER NOT NULL DEFAULT 0,
                IsRepeating INTEGER NOT NULL DEFAULT 0,
                PlaybackMode INTEGER NOT NULL DEFAULT 0,
                HotkeyText TEXT,
                ColorHex TEXT NOT NULL DEFAULT '#6C63FF',
                Notes TEXT,
                FadeInSeconds REAL NOT NULL DEFAULT 0,
                FadeOutSeconds REAL NOT NULL DEFAULT 0,
                CueInPoint REAL NOT NULL DEFAULT 0,
                CueOutPoint REAL NOT NULL DEFAULT 0,
                CrossfadeSeconds REAL NOT NULL DEFAULT 0,
                PlaylistId TEXT,
                DateAdded TEXT NOT NULL,
                LastPlayed TEXT,
                PlayCount INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY(ProjectId) REFERENCES Projects(Id)
            );

            CREATE TABLE IF NOT EXISTS Playlists (
                Id TEXT PRIMARY KEY,
                ProjectId TEXT,
                Name TEXT NOT NULL,
                ColorHex TEXT NOT NULL DEFAULT '#6C63FF',
                IsShuffled INTEGER NOT NULL DEFAULT 0,
                IsLooping INTEGER NOT NULL DEFAULT 0,
                CurrentIndex INTEGER NOT NULL DEFAULT 0,
                CueIds TEXT NOT NULL DEFAULT '[]',
                DateCreated TEXT NOT NULL,
                DateModified TEXT NOT NULL,
                FOREIGN KEY(ProjectId) REFERENCES Projects(Id)
            );

            CREATE TABLE IF NOT EXISTS Settings (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS RecentFiles (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                FilePath TEXT NOT NULL UNIQUE,
                LastAccessed TEXT NOT NULL,
                AccessCount INTEGER NOT NULL DEFAULT 1
            );

            CREATE TABLE IF NOT EXISTS SchemaInfo (
                Version INTEGER NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_cues_project ON Cues(ProjectId);
            CREATE INDEX IF NOT EXISTS idx_cues_sort ON Cues(SortOrder);
            CREATE INDEX IF NOT EXISTS idx_playlists_project ON Playlists(ProjectId);
            ");

            if (version < CurrentSchemaVersion)
            {
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
                File.Copy(DatabasePath, $"{DatabasePath}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}", false);
        }
        catch (Exception backupError) when (backupError is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(backupError, "Could not back up failed database {Path}", DatabasePath);
        }
    }
}
