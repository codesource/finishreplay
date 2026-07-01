using System.Runtime.CompilerServices;
using FinishReplay.Models;
using FinishReplay.Services.Calibration;
using FinishReplay.Services.Camera;
using FinishReplay.Services.Camera.Providers.Ffmpeg;

namespace FinishReplay.Services.Media;

/// <summary>
/// A camera stream fed by the isolated media-worker process. The worker hosts the in-process FFmpeg
/// (libav) bindings and streams JPEG frames over <see cref="MediaWorkerProtocol"/>; this class turns
/// them into <see cref="VideoFrame"/>s. Crash isolation is inherent: the native codec runs in the
/// worker, so a decoder fault only closes the worker's stdout — the frame stream ends cleanly and the
/// main app keeps running. Plugs into the same pipeline as the other camera streams.
/// </summary>
public sealed class WorkerCameraStream : ICameraStream
{
    private readonly Func<CancellationToken, IStdoutProcess> _startWorker;

    public WorkerCameraStream(CameraDevice device, Func<CancellationToken, IStdoutProcess> startWorker)
    {
        Device = device;
        _startWorker = startWorker;
    }

    public CameraDevice Device { get; }
    public CameraStreamInfo StreamInfo { get; } = new() { Codec = "MJPEG (isolated worker)" };

    public async IAsyncEnumerable<VideoFrame> ReadFramesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var worker = _startWorker(cancellationToken);

        var start = MonotonicClock.Now;
        long seq = 0;

        string? error = null;
        await foreach (var message in MediaWorkerProtocol.ReadAsync(worker.StandardOutput, cancellationToken).ConfigureAwait(false))
        {
            if (message.Type == MediaMessageType.Frame)
            {
                yield return new VideoFrame
                {
                    SequenceNumber = seq++,
                    Timestamp = MonotonicClock.Now - start,
                    Format = VideoFrameFormat.Jpeg,
                    Data = message.Payload,
                };
            }
            else if (message.Type == MediaMessageType.Error)
            {
                error = System.Text.Encoding.UTF8.GetString(message.Payload);
                break;
            }
            else if (message.Type == MediaMessageType.Eos)
            {
                break;
            }
            // Log / Ready / Unknown are informational.
        }

        // Surface a worker error when nothing was produced, so the UI shows the cause rather than
        // hanging on "waiting for frames". A crash mid-stream just ends cleanly (app unaffected).
        if (seq == 0 && !cancellationToken.IsCancellationRequested)
        {
            error ??= worker.StandardErrorTail?.Trim();
            if (!string.IsNullOrEmpty(error))
                throw new InvalidOperationException(error);
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
