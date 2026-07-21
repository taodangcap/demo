using System.Windows.Media;

namespace ShowCuePlayer.Helpers;

/// <summary>Color conversion helpers for hex strings and WPF brushes.</summary>
public static class ColorHelper
{
    public static Color FromHex(string hex)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return Colors.SlateBlue; }
    }

    public static SolidColorBrush BrushFromHex(string hex)
    {
        var brush = new SolidColorBrush(FromHex(hex));
        brush.Freeze();
        return brush;
    }

    public static string ToHex(Color c)
        => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public static Color Lighten(Color c, float factor = 0.2f)
        => Color.FromArgb(c.A,
            (byte)Math.Min(255, c.R + 255 * factor),
            (byte)Math.Min(255, c.G + 255 * factor),
            (byte)Math.Min(255, c.B + 255 * factor));

    public static Color WithAlpha(Color c, byte alpha)
        => Color.FromArgb(alpha, c.R, c.G, c.B);

    public static string[] PredefinedColors { get; } = new string[]{
        "#6C63FF", "#FF6B6B", "#4ECDC4", "#45B7D1",
        "#96CEB4", "#FFEAA7", "#DDA0DD", "#98D8C8",
        "#F7DC6F", "#BB8FCE", "#85C1E9", "#82E0AA",
            "#F8C471", "#F1948A", "#AED6F1", "#A9DFBF"};
}
