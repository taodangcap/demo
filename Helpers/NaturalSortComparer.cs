using System.Runtime.InteropServices;

namespace ShowCuePlayer.Helpers;

/// <summary>
/// Natural sort comparer for file names (e.g. Track 2 before Track 10).
/// Uses Windows StrCmpLogicalW for locale-correct ordering.
/// </summary>
public sealed class NaturalSortComparer : IComparer<string>
{
    public static readonly NaturalSortComparer Instance = new();

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int StrCmpLogicalW(string x, string y);

    public int Compare(string? x, string? y)
    {
        if (x is null && y is null) return 0;
        if (x is null) return -1;
        if (y is null) return 1;
        return StrCmpLogicalW(x, y);
    }
}
