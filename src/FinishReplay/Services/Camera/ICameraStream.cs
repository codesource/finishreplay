using FinishReplay.Models;

namespace FinishReplay.Services.Camera;

/// <summary>
/// A live, readable camera stream. Frames are pulled as an async sequence so callers
/// (preview, recording, calibration) consume them without knowing the transport.
/// </summary>
public interface ICameraStream : IAsyncDisposable
{
    CameraDevice Device { get; }
    CameraStreamInfo StreamInfo { get; }

    /// <summary>Yields frames until the stream ends or <paramref name="cancellationToken"/> fires.</summary>
    IAsyncEnumerable<VideoFrame> ReadFramesAsync(CancellationToken cancellationToken = default);
}
