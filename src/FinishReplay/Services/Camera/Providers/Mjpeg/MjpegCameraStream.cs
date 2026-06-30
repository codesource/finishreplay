using System.Runtime.CompilerServices;
using FinishReplay.Models;
using FinishReplay.Services.Calibration;

namespace FinishReplay.Services.Camera.Providers.Mjpeg;

/// <summary>
/// Live MJPEG stream over HTTP. Performs a streaming GET against the camera URL and yields each JPEG
/// frame (via <see cref="MjpegStreamReader"/>) as a <see cref="VideoFrame"/> stamped with monotonic
/// arrival time. Frames are emitted as encoded JPEG (<see cref="VideoFrameFormat.Jpeg"/>).
/// </summary>
public sealed class MjpegCameraStream : ICameraStream
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    public MjpegCameraStream(CameraDevice device, HttpClient? http = null)
    {
        Device = device;
        _ownsHttp = http is null;
        _http = http ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
    }

    public CameraDevice Device { get; }

    // Dimensions are unknown until a frame is decoded; left at 0 for the encoded-JPEG MVP.
    public CameraStreamInfo StreamInfo { get; } = new() { Codec = "MJPEG" };

    public async IAsyncEnumerable<VideoFrame> ReadFramesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var response = await _http
            .GetAsync(Device.SourceUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        var start = MonotonicClock.Now;
        long seq = 0;

        await foreach (var jpeg in MjpegStreamReader.ReadFramesAsync(stream, cancellationToken: cancellationToken)
                           .WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return new VideoFrame
            {
                SequenceNumber = seq++,
                Timestamp = MonotonicClock.Now - start,
                Format = VideoFrameFormat.Jpeg,
                Data = jpeg,
            };
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_ownsHttp)
            _http.Dispose();
        return ValueTask.CompletedTask;
    }
}
