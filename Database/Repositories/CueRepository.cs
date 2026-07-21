using Microsoft.Data.Sqlite;
using ShowCuePlayer.Models;

namespace ShowCuePlayer.Database.Repositories;

/// <summary>CRUD repository for CueModel using raw ADO.NET / SQLite.</summary>
public interface ICueRepository
{
    Task<List<CueModel>> GetByProjectAsync(string projectId);
    Task<CueModel?> GetByIdAsync(string id);
    Task UpsertAsync(CueModel cue, string projectId);
    Task DeleteAsync(string id);
    Task UpdateSortOrderAsync(IEnumerable<(string Id, int Order)> items);
}

public sealed class CueRepository : ICueRepository
{
    private readonly IDatabaseService _db;
    public CueRepository(IDatabaseService db) => _db = db;

    public async Task<List<CueModel>> GetByProjectAsync(string projectId)
    {
        await using var conn = _db.GetConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM Cues WHERE ProjectId=@pid ORDER BY SortOrder ASC";
        cmd.Parameters.AddWithValue("@pid", projectId);
        var result = new List<CueModel>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(MapRow(reader));
        return result;
    }

    public async Task<CueModel?> GetByIdAsync(string id)
    {
        await using var conn = _db.GetConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM Cues WHERE Id=@id";
        cmd.Parameters.AddWithValue("@id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapRow(reader) : null;
    }

    public async Task UpsertAsync(CueModel c, string projectId)
    {
        await using var conn = _db.GetConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Cues (Id,ProjectId,SortOrder,Title,Artist,Album,FilePath,Volume,Duration,
                IsLooping,IsRepeating,PlaybackMode,HotkeyText,ColorHex,Notes,FadeInSeconds,
                FadeOutSeconds,CueInPoint,CueOutPoint,CrossfadeSeconds,PlaylistId,DateAdded,LastPlayed,PlayCount)
            VALUES (@id,@pid,@so,@ti,@ar,@al,@fp,@vo,@du,@lo,@re,@pm,@hk,@co,@no,@fi,@fo,@ci,@cx,@cf,@pl,@da,@lp,@pc)
            ON CONFLICT(Id) DO UPDATE SET
                SortOrder=excluded.SortOrder, Title=excluded.Title, Artist=excluded.Artist,
                Album=excluded.Album, FilePath=excluded.FilePath, Volume=excluded.Volume,
                Duration=excluded.Duration, IsLooping=excluded.IsLooping, IsRepeating=excluded.IsRepeating,
                PlaybackMode=excluded.PlaybackMode, HotkeyText=excluded.HotkeyText, ColorHex=excluded.ColorHex,
                Notes=excluded.Notes, FadeInSeconds=excluded.FadeInSeconds, FadeOutSeconds=excluded.FadeOutSeconds,
                CueInPoint=excluded.CueInPoint, CueOutPoint=excluded.CueOutPoint,
                CrossfadeSeconds=excluded.CrossfadeSeconds, PlaylistId=excluded.PlaylistId,
                LastPlayed=excluded.LastPlayed, PlayCount=excluded.PlayCount";

        cmd.Parameters.AddWithValue("@id", c.Id.ToString());
        cmd.Parameters.AddWithValue("@pid", projectId);
        cmd.Parameters.AddWithValue("@so", c.SortOrder);
        cmd.Parameters.AddWithValue("@ti", c.Title);
        cmd.Parameters.AddWithValue("@ar", c.Artist ?? "");
        cmd.Parameters.AddWithValue("@al", c.Album ?? "");
        cmd.Parameters.AddWithValue("@fp", c.FilePath);
        cmd.Parameters.AddWithValue("@vo", c.Volume);
        cmd.Parameters.AddWithValue("@du", c.Duration);
        cmd.Parameters.AddWithValue("@lo", c.IsLooping ? 1 : 0);
        cmd.Parameters.AddWithValue("@re", c.IsRepeating ? 1 : 0);
        cmd.Parameters.AddWithValue("@pm", (int)c.PlaybackMode);
        cmd.Parameters.AddWithValue("@hk", c.HotkeyText ?? "");
        cmd.Parameters.AddWithValue("@co", c.ColorHex);
        cmd.Parameters.AddWithValue("@no", c.Notes ?? "");
        cmd.Parameters.AddWithValue("@fi", c.FadeInSeconds);
        cmd.Parameters.AddWithValue("@fo", c.FadeOutSeconds);
        cmd.Parameters.AddWithValue("@ci", c.CueInPoint);
        cmd.Parameters.AddWithValue("@cx", c.CueOutPoint);
        cmd.Parameters.AddWithValue("@cf", c.CrossfadeSeconds);
        cmd.Parameters.AddWithValue("@pl", c.PlaylistId ?? "");
        cmd.Parameters.AddWithValue("@da", c.DateAdded.ToString("O"));
        cmd.Parameters.AddWithValue("@lp", c.LastPlayed?.ToString("O") ?? "");
        cmd.Parameters.AddWithValue("@pc", c.PlayCount);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(string id)
    {
        await using var conn = _db.GetConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Cues WHERE Id=@id";
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpdateSortOrderAsync(IEnumerable<(string Id, int Order)> items)
    {
        await using var conn = _db.GetConnection();
        await using var tx = conn.BeginTransaction();
        foreach (var (id, order) in items)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE Cues SET SortOrder=@o WHERE Id=@id";
            cmd.Parameters.AddWithValue("@o", order);
            cmd.Parameters.AddWithValue("@id", id);
            await cmd.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
    }

    private static CueModel MapRow(SqliteDataReader r) => new()
    {
        Id = Guid.Parse(r.GetString(r.GetOrdinal("Id"))),
        SortOrder = r.GetInt32(r.GetOrdinal("SortOrder")),
        Title = r.GetString(r.GetOrdinal("Title")),
        Artist = r.GetString(r.GetOrdinal("Artist")),
        Album = r.GetString(r.GetOrdinal("Album")),
        FilePath = r.GetString(r.GetOrdinal("FilePath")),
        Volume = r.GetDouble(r.GetOrdinal("Volume")),
        Duration = r.GetDouble(r.GetOrdinal("Duration")),
        IsLooping = r.GetInt32(r.GetOrdinal("IsLooping")) == 1,
        IsRepeating = r.GetInt32(r.GetOrdinal("IsRepeating")) == 1,
        PlaybackMode = (Models.PlaybackMode)r.GetInt32(r.GetOrdinal("PlaybackMode")),
        HotkeyText = r.GetString(r.GetOrdinal("HotkeyText")),
        ColorHex = r.GetString(r.GetOrdinal("ColorHex")),
        Notes = r.GetString(r.GetOrdinal("Notes")),
        FadeInSeconds = r.GetDouble(r.GetOrdinal("FadeInSeconds")),
        FadeOutSeconds = r.GetDouble(r.GetOrdinal("FadeOutSeconds")),
        CueInPoint = r.GetDouble(r.GetOrdinal("CueInPoint")),
        CueOutPoint = r.GetDouble(r.GetOrdinal("CueOutPoint")),
        CrossfadeSeconds = r.GetDouble(r.GetOrdinal("CrossfadeSeconds")),
        PlaylistId = r.GetString(r.GetOrdinal("PlaylistId")),
        DateAdded = DateTime.Parse(r.GetString(r.GetOrdinal("DateAdded"))),
        PlayCount = r.GetInt32(r.GetOrdinal("PlayCount")),
    };
}
