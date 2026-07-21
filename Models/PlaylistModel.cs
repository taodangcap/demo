namespace ShowCuePlayer.Models;

public enum PlaylistType
{
    Audio,
    Video,
    Karaoke
}

/// <summary>Represents a named playlist containing ordered cue references.</summary>
public class PlaylistModel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New Playlist";
    public string ColorHex { get; set; } = "#6C63FF";
    public PlaylistType Type { get; set; } = PlaylistType.Audio;
    public bool IsShuffled { get; set; }
    public bool IsLooping { get; set; }
    public int CurrentIndex { get; set; }
    public List<Guid> CueIds { get; set; } = new List<Guid>();
    public DateTime DateCreated { get; set; } = DateTime.UtcNow;
    public DateTime DateModified { get; set; } = DateTime.UtcNow;
}
