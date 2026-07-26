namespace ShowCuePlayer.Models;

/// <summary>
/// Root project file model. Serialized to/from .showcue (JSON).
/// Contains everything needed to fully restore a session.
/// </summary>
public class ProjectModel
{
    public string Version { get; set; } = "1.0.0";
    public string Name { get; set; } = "Untitled Project";
    public DateTime DateCreated { get; set; } = DateTime.UtcNow;
    public DateTime DateModified { get; set; } = DateTime.UtcNow;
    public List<CueModel> Cues { get; set; } = new List<CueModel>();
    public List<PlaylistModel> Playlists { get; set; } = new List<PlaylistModel>();
    public AppSettings Settings { get; set; } = new();
    /// <summary>
    /// Null với project cũ chưa từng lưu Soundboard; danh sách rỗng nghĩa là
    /// project mới chủ động lưu một Soundboard không có hiệu ứng.
    /// </summary>
    public List<KaraokeSoundEffect>? KaraokeSoundEffects { get; set; }
    public string? ActivePlaylistId { get; set; }
    public string FilePath { get; set; } = string.Empty;
}
