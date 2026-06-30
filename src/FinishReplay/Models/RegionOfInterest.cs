namespace FinishReplay.Models;

/// <summary>
/// Normalized (0..1) rectangle within a frame used by flash detection during latency calibration.
/// Normalized so it is resolution-independent across different cameras.
/// </summary>
public sealed record RegionOfInterest(double X, double Y, double Width, double Height)
{
    /// <summary>Whole-frame region.</summary>
    public static RegionOfInterest Full { get; } = new(0, 0, 1, 1);
}
