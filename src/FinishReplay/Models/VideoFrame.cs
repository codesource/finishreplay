namespace FinishReplay.Models;

/// <summary>
/// A single decoded frame delivered by an <see cref="FinishReplay.Services.Camera.ICameraStream"/>.
/// <see cref="Timestamp"/> is monotonic arrival time (not wall-clock) so it can be compared
/// against the calibration trigger time.
/// </summary>
public sealed class VideoFrame
{
    public long SequenceNumber { get; init; }

    /// <summary>Monotonic arrival time of this frame, relative to stream start.</summary>
    public TimeSpan Timestamp { get; init; }

    public int Width { get; init; }
    public int Height { get; init; }

    /// <summary>
    /// Raw pixel buffer. TODO: define a concrete pixel format (e.g. BGRA) and use a pooled
    /// buffer once the real capture backend lands; today this is a placeholder.
    /// </summary>
    public byte[] Pixels { get; init; } = Array.Empty<byte>();
}
