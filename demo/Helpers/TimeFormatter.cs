namespace ShowCuePlayer.Helpers;

/// <summary>Formats time durations for display on cue cards.</summary>
public static class TimeFormatter
{
    /// <summary>Formats seconds as MM:SS or HH:MM:SS.</summary>
    public static string Format(double totalSeconds)
    {
        if (totalSeconds < 0) totalSeconds = 0;
        var ts = TimeSpan.FromSeconds(totalSeconds);
        return ts.TotalHours >= 1
            ? ts.ToString(@"h\:mm\:ss")
            : ts.ToString(@"m\:ss");
    }

    /// <summary>Formats remaining time with leading minus sign.</summary>
    public static string FormatRemaining(double elapsed, double total)
    {
        double remaining = Math.Max(0, total - elapsed);
        return $"-{Format(remaining)}";
    }
}
