namespace FinishReplay.Models;

/// <summary>
/// Persisted, user-facing configuration for a single camera, independent of any one session.
/// Holds the source description plus its measured latency and any manual correction.
/// </summary>
public sealed class CameraProfile
{
    public string Id { get; init; } = "";
    public string DisplayName { get; set; } = "";

    /// <summary>Short token appended to clip filenames to identify this camera (e.g. "finish", "side").</summary>
    public string Suffix { get; set; } = "";

    /// <summary>Whether this camera participates in recording by default.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Transport kind: "USB", "MJPEG", "RTSP", ... (matches a provider's source type).</summary>
    public string SourceType { get; set; } = "";

    /// <summary>Connection URL for network sources; empty for local USB devices.</summary>
    public string SourceUrl { get; set; } = "";

    /// <summary>End-to-end latency measured by calibration, in milliseconds (null = not calibrated).</summary>
    public double? CalibratedLatencyMs { get; set; }

    /// <summary>Operator-applied correction added on top of the calibrated value, in milliseconds.</summary>
    public double? ManualOffsetMs { get; set; }

    /// <summary>Effective latency used for sync: calibrated + manual (nulls treated as 0).</summary>
    public double EffectiveLatencyMs =>
        (CalibratedLatencyMs ?? 0) + (ManualOffsetMs ?? 0);

    /// <summary>Build the openable device descriptor for this profile.</summary>
    public CameraDevice ToDevice() => new(Id, DisplayName)
    {
        ProviderName = SourceType,
        SourceType = SourceType,
        SourceUrl = SourceUrl,
    };
}
