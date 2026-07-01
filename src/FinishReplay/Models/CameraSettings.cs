namespace FinishReplay.Models;

/// <summary>Requested capture settings when opening a camera (nulls = provider default).</summary>
public sealed class CameraSettings
{
    public int? Width { get; init; }
    public int? Height { get; init; }
    public double? FrameRate { get; init; }

    /// <summary>Requested capture pixel format / codec (e.g. "mjpeg", "yuyv422"); null = default.</summary>
    public string? PixelFormat { get; init; }

    public static CameraSettings Default { get; } = new();
}
