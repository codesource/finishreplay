namespace FinishReplay.Models;

/// <summary>
/// How far a camera must be shifted during synchronized replay so all cameras line up.
/// Computed relative to the earliest (lowest-latency) camera; see
/// <see cref="FinishReplay.Services.Calibration.CameraSyncCalculator"/>.
/// </summary>
public sealed class CameraSyncOffset
{
    public string CameraId { get; init; } = "";

    /// <summary>Milliseconds to shift this camera relative to the reference camera.</summary>
    public double OffsetMs { get; init; }

    /// <summary>Where the offset came from: "calibrated", "manual", or "timing-marker".</summary>
    public string Source { get; init; } = "";
}
