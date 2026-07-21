using System.Windows.Input;

namespace ShowCuePlayer.Helpers;

/// <summary>Parse / format / match hotkey strings like OBS (Ctrl+Shift+A).</summary>
public static class HotkeyUtil
{
    public static string Format(Key key, ModifierKeys modifiers)
    {
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin
            or Key.None or Key.System)
            return string.Empty;

        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(NormalizeKeyName(key));
        return string.Join("+", parts);
    }

    public static bool TryParse(string? text, out Key key, out ModifierKeys modifiers)
    {
        key = Key.None;
        modifiers = ModifierKeys.None;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)
                || part.Equals("Control", StringComparison.OrdinalIgnoreCase))
                modifiers |= ModifierKeys.Control;
            else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                modifiers |= ModifierKeys.Shift;
            else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                modifiers |= ModifierKeys.Alt;
            else if (part.Equals("Win", StringComparison.OrdinalIgnoreCase)
                     || part.Equals("Windows", StringComparison.OrdinalIgnoreCase))
                modifiers |= ModifierKeys.Windows;
            else if (part.Equals("Space", StringComparison.OrdinalIgnoreCase))
                key = Key.Space;
            else if (part.Equals("Esc", StringComparison.OrdinalIgnoreCase)
                     || part.Equals("Escape", StringComparison.OrdinalIgnoreCase))
                key = Key.Escape;
            else if (part.Equals("Return", StringComparison.OrdinalIgnoreCase)
                     || part.Equals("Enter", StringComparison.OrdinalIgnoreCase))
                key = Key.Return;
            else if (part.Equals("Left", StringComparison.OrdinalIgnoreCase)
                     || part.Equals("LeftArrow", StringComparison.OrdinalIgnoreCase))
                key = Key.Left;
            else if (part.Equals("Right", StringComparison.OrdinalIgnoreCase)
                     || part.Equals("RightArrow", StringComparison.OrdinalIgnoreCase))
                key = Key.Right;
            else if (part.Equals("Up", StringComparison.OrdinalIgnoreCase))
                key = Key.Up;
            else if (part.Equals("Down", StringComparison.OrdinalIgnoreCase))
                key = Key.Down;
            else if (Enum.TryParse(part, true, out Key parsed))
                key = parsed;
        }

        return key != Key.None;
    }

    public static bool Matches(string? binding, Key key, ModifierKeys modifiers)
    {
        if (!TryParse(binding, out var k, out var m)) return false;
        // Normalize System key
        if (key == Key.System) return false;
        return k == key && m == modifiers;
    }

    private static string NormalizeKeyName(Key key) => key switch
    {
        Key.Space => "Space",
        Key.Escape => "Escape",
        Key.Return => "Return",
        Key.Left => "Left",
        Key.Right => "Right",
        Key.Up => "Up",
        Key.Down => "Down",
        _ => key.ToString()
    };
}
