using FinishReplay.Models;

namespace FinishReplay.Services.Calibration;

/// <summary>
/// Turns per-camera <em>absolute</em> latencies into <em>relative</em> sync offsets for replay.
/// The lowest-latency (earliest) camera is the reference at offset 0; every other camera is
/// shifted by how much later it is. Relative offset is what synchronized replay actually needs.
/// </summary>
/// <example>
/// Camera A latency 72ms, Camera B latency 118ms ⇒ A offset 0ms (reference), B offset 46ms.
/// </example>
public static class CameraSyncCalculator
{
    /// <summary>
    /// Compute sync offsets from each profile's effective latency (calibrated + manual).
    /// Source is reported as "manual" when a manual offset contributed, else "calibrated".
    /// </summary>
    public static IReadOnlyList<CameraSyncOffset> ComputeOffsets(IReadOnlyList<CameraProfile> cameras)
    {
        if (cameras.Count == 0)
            return Array.Empty<CameraSyncOffset>();

        var reference = cameras.Min(c => c.EffectiveLatencyMs);

        return cameras
            .Select(c => new CameraSyncOffset
            {
                CameraId = c.Id,
                OffsetMs = c.EffectiveLatencyMs - reference,
                Source = (c.ManualOffsetMs ?? 0) != 0 ? "manual" : "calibrated",
            })
            .ToList();
    }
}
