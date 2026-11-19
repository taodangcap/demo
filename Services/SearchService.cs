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
        if (q.Length == 0)
            return cues.ToList();

        return cues.Where(c =>
            ContainsNormalized(c.Title, q) ||
            ContainsNormalized(c.Artist, q) ||
            ContainsNormalized(c.Album, q) ||
            ContainsNormalized(System.IO.Path.GetFileName(c.FilePath), q) ||
            ContainsNormalized(c.HotkeyText, q) ||
            ContainsNormalized(c.ColorHex, q) ||
            ContainsNormalized(c.Notes, q))
        .ToList();
    }

    private static bool ContainsNormalized(string? value, string normalizedQuery)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (IsAscii(value) && IsAscii(normalizedQuery))
            return value.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase);
        return Normalize(value).Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAscii(string value)
    {
        foreach (var ch in value)
            if (ch > 0x7F) return false;
        return true;
    }

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        if (IsAscii(value)) return value.ToLowerInvariant();
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
