namespace ShowCuePlayer.Models;

public enum LedScalingMode
{
    Fit,
    Fill,
    Stretch,
    PixelPerfect
}

public enum LedTestPattern
{
    Black,
    White,
    Red,
    Green,
    Blue,
    Grid,
    ColorBars
}

public sealed class LedOutputProfile
{
    public string Id { get; set; } = "full-hd";
    public string Name { get; set; } = "Full HD";
    public int CanvasWidth { get; set; } = 1920;
    public int CanvasHeight { get; set; } = 1080;
    public LedScalingMode ScalingMode { get; set; } = LedScalingMode.Fit;
    public bool IsPreset { get; set; } = true;

    public string Readout => $"{CanvasWidth}x{CanvasHeight} / {ScalingMode}";

    public LedOutputProfile Clone() => new()
    {
        Id = Id,
        Name = Name,
        CanvasWidth = CanvasWidth,
        CanvasHeight = CanvasHeight,
        ScalingMode = ScalingMode,
        IsPreset = IsPreset
    };

    public static IReadOnlyList<LedOutputProfile> CreatePresets() => new[]
    {
        new LedOutputProfile
        {
            Id = "full-hd",
            Name = "Full HD 16:9",
            CanvasWidth = 1920,
            CanvasHeight = 1080,
            ScalingMode = LedScalingMode.Fit
        },
        new LedOutputProfile
        {
            Id = "hd",
            Name = "HD 16:9",
            CanvasWidth = 1280,
            CanvasHeight = 720,
            ScalingMode = LedScalingMode.Fit
        },
        new LedOutputProfile
        {
            Id = "led-wide",
            Name = "LED Wide",
            CanvasWidth = 1920,
            CanvasHeight = 640,
            ScalingMode = LedScalingMode.Fit
        },
        new LedOutputProfile
        {
            Id = "led-portrait",
            Name = "LED Portrait",
            CanvasWidth = 1080,
            CanvasHeight = 1920,
            ScalingMode = LedScalingMode.Fit
        }
    };

    public static LedOutputProfile CreateCustom(
        string name,
        int width,
        int height,
        LedScalingMode scalingMode) => new()
    {
        Id = "custom",
        Name = string.IsNullOrWhiteSpace(name) ? "Custom LED" : name.Trim(),
        CanvasWidth = Math.Clamp(width, 64, 16384),
        CanvasHeight = Math.Clamp(height, 64, 16384),
        ScalingMode = scalingMode,
        IsPreset = false
    };
}
