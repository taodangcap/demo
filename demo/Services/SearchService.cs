using ShowCuePlayer.Models;
using System.Globalization;
using System.Text;

namespace ShowCuePlayer.Services;

/// <summary>Contract for real-time cue search.</summary>
public interface ISearchService
{
    IReadOnlyList<CueModel> Search(IEnumerable<CueModel> cues, string query);
}

/// <summary>
/// Real-time search across title, artist, album, filename, hotkey, color.
/// Case-insensitive. Returns all results when query is empty.
/// </summary>
public sealed class SearchService : ISearchService
{
    public IReadOnlyList<CueModel> Search(IEnumerable<CueModel> cues, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return cues.ToList();

        var q = Normalize(query);
        return cues.Where(c =>
            Normalize(c.Title).Contains(q, StringComparison.OrdinalIgnoreCase) ||
            Normalize(c.Artist).Contains(q, StringComparison.OrdinalIgnoreCase) ||
            Normalize(c.Album).Contains(q, StringComparison.OrdinalIgnoreCase) ||
            Normalize(System.IO.Path.GetFileName(c.FilePath)).Contains(q, StringComparison.OrdinalIgnoreCase) ||
            Normalize(c.HotkeyText).Contains(q, StringComparison.OrdinalIgnoreCase) ||
            Normalize(c.ColorHex).Contains(q, StringComparison.OrdinalIgnoreCase) ||
            Normalize(c.Notes ?? string.Empty).Contains(q, StringComparison.OrdinalIgnoreCase))
        .ToList();
    }

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        value = value.Replace('đ', 'd').Replace('Đ', 'D').Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                builder.Append(char.ToLowerInvariant(ch));
        }
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
