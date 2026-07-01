using System.Runtime.CompilerServices;
using FinishReplay.Models;
using FinishReplay.Services.Calibration;
using FinishReplay.Services.Camera.Providers.Mjpeg;

namespace FinishReplay.Services.Camera.Providers.Ffmpeg;

/// <summary>
/// A camera stream backed by an ffmpeg process that emits MJPEG on stdout. Used for RTSP/H.264 (and
/// any source ffmpeg can read): ffmpeg does the decode, and the resulting JPEG frames flow through
/// the same <see cref="MjpegStreamReader"/> pipeline as native MJPEG cameras, so preview, recording
/// and replay are identical. The process factory is injectable for testing.
/// </summary>
public sealed class FfmpegMjpegProcessStream : ICameraStream
{
    private readonly Func<CancellationToken, IStdoutProcess> _startProcess;

    public FfmpegMjpegProcessStream(CameraDevice device, Func<CancellationToken, IStdoutProcess> startProcess)
    {
        Device = device;
        _startProcess = startProcess;
    }

    public CameraDevice Device { get; }
    public CameraStreamInfo StreamInfo { get; } = new() { Codec = "MJPEG" };

    public async IAsyncEnumerable<VideoFrame> ReadFramesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var process = _startProcess(cancellationToken);

        var start = MonotonicClock.Now;
        long seq = 0;

        await foreach (var jpeg in MjpegStreamReader
                           .ReadFramesAsync(process.StandardOutput, cancellationToken: cancellationToken)
                           .ConfigureAwait(false))
        {
            yield return new VideoFrame
            {
                SequenceNumber = seq++,
                Timestamp = MonotonicClock.Now - start,
                Format = VideoFrameFormat.Jpeg,
                Data = jpeg,
            };
        }

        // ffmpeg produced no frames and exited — surface why (device busy, bad name, codec, etc.).
        if (seq == 0 && !cancellationToken.IsCancellationRequested)
        {
            var error = process.StandardErrorTail?.Trim();
            if (!string.IsNullOrEmpty(error))
                throw new InvalidOperationException(error);
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
