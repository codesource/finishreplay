namespace FinishReplay.Models;

/// <summary>Pixel/encoding layout of a <see cref="VideoFrame"/>'s <see cref="VideoFrame.Data"/>.</summary>
public enum VideoFrameFormat
{
    /// <summary>Encoded JPEG bytes (one independent image, as delivered by MJPEG sources).</summary>
    Jpeg,

    /// <summary>Raw 32-bit BGRA pixels, row-major, no padding.</summary>
    Bgra32,
}

/// <summary>
/// A single frame delivered by an <see cref="FinishReplay.Services.Camera.ICameraStream"/>.
/// <see cref="Timestamp"/> is monotonic arrival time (not wall-clock) so it can be compared against
/// the calibration trigger time. <see cref="Data"/> holds either encoded JPEG or raw pixels depending
/// on <see cref="Format"/>.
/// </summary>
public sealed class VideoFrame
{
    public long SequenceNumber { get; init; }

    /// <summary>Monotonic arrival time of this frame, relative to stream start.</summary>
    public TimeSpan Timestamp { get; init; }

    public int Width { get; init; }
    public int Height { get; init; }

    public VideoFrameFormat Format { get; init; } = VideoFrameFormat.Jpeg;

    /// <summary>Encoded JPEG bytes (Format == Jpeg) or raw BGRA pixels (Format == Bgra32).</summary>
    public byte[] Data { get; init; } = Array.Empty<byte>();
}
