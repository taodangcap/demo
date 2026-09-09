namespace ShowCuePlayer.Models;

public static class HotkeyActions
{
    public const string StopTab = "StopTab";
    public const string StopAll = "StopAll";
    public const string SaveProject = "SaveProject";
    public const string OpenProject = "OpenProject";
    public const string ApplyKaraoke = "ApplyKaraoke";
    public const string ToggleKaraokeOnOff = "ToggleKaraokeOnOff";

    public static IReadOnlyList<(string Id, string Category, string Name)> Catalog { get; } = new[]
    {
        (ToggleKaraokeOnOff, "Karaoke", "Bật / tắt phiên Karaoke"),
        (ApplyKaraoke, "Karaoke", "Đồng bộ session Karaoke"),
        (StopTab, "An toàn", "Dừng phiên Karaoke hiện tại"),
        (StopAll, "An toàn", "Dừng toàn bộ"),
        (SaveProject, "Dự án", "Lưu file .7zyx"),
        (OpenProject, "Dự án", "Mở file .7zyx"),
    };

    public static IReadOnlySet<string> SupportedIds { get; } = Catalog
        .Select(action => action.Id)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static Dictionary<string, string> CreateDefaults() => new(StringComparer.OrdinalIgnoreCase)
    {
        [StopTab] = "Escape",
        [StopAll] = "Shift+Escape",
        [SaveProject] = "Ctrl+S",
        [OpenProject] = "Ctrl+O",
        [ApplyKaraoke] = "Ctrl+Return",
        [ToggleKaraokeOnOff] = "Ctrl+K",
    };
}
