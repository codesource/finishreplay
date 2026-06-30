namespace FinishReplay.Models;

/// <summary>
/// One camera's contribution to a recorded session: its source, the file it wrote, and the
/// latency/offset values needed to synchronize it against the other cameras during replay.
/// </summary>
public sealed class SessionCamera
{
    public string CameraId { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>Transport kind: "USB", "MJPEG", "RTSP", ...</summary>
    public string SourceType { get; set; } = "";
    public string SourceUrl { get; set; } = "";

    /// <summary>Video file for this camera, relative to the session folder.</summary>
    public string VideoFile { get; set; } = "";

    public double? CalibratedLatencyMs { get; set; }
    public double ManualOffsetMs { get; set; }

    /// <summary>Computed shift relative to the reference camera for synchronized replay.</summary>
    public double SyncOffsetMs { get; set; }
}
