using System.Windows.Forms;

namespace ShowCuePlayer.Models;

/// <summary>
/// Friendly wrapper around a physical monitor for dual-screen event setups.
/// Control (primary) vs Output (secondary projector / LED wall).
/// </summary>
public sealed class VideoScreenInfo
{
    public Screen Screen { get; }
    public int Index { get; }
    public string DeviceName => Screen.DeviceName;
    public bool IsPrimary => Screen.Primary;
    public int Width => Screen.Bounds.Width;
    public int Height => Screen.Bounds.Height;
    public System.Drawing.Rectangle Bounds => Screen.Bounds;

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
            role = "Màn duy nhất";
        else if (screen.Primary)
            role = "Chính";
        else
            role = "Phụ";

        ShortName = $"M{index}";
        DisplayName = $"Màn {index} ({role}) · {Width}×{Height}";
    }

    public override string ToString() => DisplayName;
}
