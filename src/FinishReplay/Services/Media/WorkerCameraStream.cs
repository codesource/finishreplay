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

        await foreach (var message in MediaWorkerProtocol.ReadAsync(worker.StandardOutput, cancellationToken).ConfigureAwait(false))
        {
            switch (message.Type)
            {
                case MediaMessageType.Frame:
                    yield return new VideoFrame
                    {
                        SequenceNumber = seq++,
                        Timestamp = MonotonicClock.Now - start,
                        Format = VideoFrameFormat.Jpeg,
                        Data = message.Payload,
                    };
                    break;

                case MediaMessageType.Error:
                case MediaMessageType.Eos:
                    yield break;

                // Log / Ready / Unknown are informational.
            }
        }

        // Stream ended (normal EOS or worker crash) — enumeration completes; the app is unaffected.
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
