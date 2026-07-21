using System.Windows.Forms;

namespace ShowCuePlayer.Models;

/// <summary>
/// Friendly wrapper around a physical monitor for dual-screen event setups.
/// Control (primary) vs Output (secondary projector / LED wall).
/// </summary>
public sealed class VideoScreenInfo
{
    public Screen? Screen { get; }
    public int Index { get; }
    public string DeviceName => Screen?.DeviceName ?? $"MockDisplay{Index}";
    public bool IsPrimary => Screen?.Primary ?? false;
    public int Width => Screen?.Bounds.Width ?? 1920;
    public int Height => Screen?.Bounds.Height ?? 1080;
    public System.Drawing.Rectangle Bounds => Screen?.Bounds ?? new System.Drawing.Rectangle(1920, 0, 1920, 1080);

    /// <summary>Short label for toolbar / status, e.g. "Output · Display 2".</summary>
    public string ShortName { get; }

    /// <summary>Full label for combobox, e.g. "Display 2 (Output) 1920×1080".</summary>
    public string DisplayName { get; }

    public VideoScreenInfo(Screen screen, int index, int totalScreens)
    {
        Screen = screen;
        Index = index;

        string role;
        if (totalScreens <= 1)
            role = "Only screen";
        else if (screen.Primary)
            role = "Control";
        else
            role = "Output";

        ShortName = screen.Primary
            ? $"Control · D{index}"
            : $"Output · D{index}";

        DisplayName = $"Display {index} ({role})  {Width}×{Height}";
    }

    // Mock constructor for virtual/mock display
    public VideoScreenInfo(int index, string displayName, string shortName)
    {
        Screen = null;
        Index = index;
        DisplayName = displayName;
        ShortName = shortName;
    }

    public override string ToString() => DisplayName;
}
