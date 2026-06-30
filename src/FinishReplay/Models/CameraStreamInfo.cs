namespace FinishReplay.Models;

/// <summary>Negotiated properties of an open camera stream.</summary>
public sealed class CameraStreamInfo
{
    public int Width { get; init; }
    public int Height { get; init; }
    public double FrameRate { get; init; }

    /// <summary>Codec/container hint (e.g. "MJPEG", "H264"); informational.</summary>
    public string Codec { get; init; } = "";
}
