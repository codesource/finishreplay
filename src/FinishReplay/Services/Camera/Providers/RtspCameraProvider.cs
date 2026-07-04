using FinishReplay.Models;
using FinishReplay.Services.Camera.Providers.Ffmpeg;
using FinishReplay.Services.Media;

namespace FinishReplay.Services.Camera.Providers;

/// <summary>
/// Provider for RTSP/H.264 streams (e.g. <c>rtsp://host/live</c>). Capture decodes the stream and
/// emits MJPEG frames that flow through the shared MJPEG pipeline (preview, AVI recording, replay).
/// It uses the bundled, crash-isolated media worker (embedded libav) — no external ffmpeg required —
/// and only falls back to an external <c>ffmpeg</c> process if the worker isn't bundled.
///
/// Calibration is supported but latency may be less stable than native MJPEG due to camera-side
/// buffering and GOP/B-frame decode delay.
/// </summary>
public sealed class RtspCameraProvider : ICameraProvider
{
    public const string Type = "RTSP";

    private readonly Func<string> _ffmpegPath;

    /// <param name="ffmpegPath">
    /// Returns the configured ffmpeg path for the fallback path (re-read per open so Settings changes
    /// take effect). Defaults to "ffmpeg" (PATH lookup).
    /// </param>
    public RtspCameraProvider(Func<string>? ffmpegPath = null)
    {
        _ffmpegPath = ffmpegPath ?? (() => "ffmpeg");
    }

    public string ProviderName => Type;

    public Task<IReadOnlyList<CameraDevice>> DiscoverAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<CameraDevice>>(Array.Empty<CameraDevice>());

    /// <summary>Build a device for a user-supplied RTSP URL.</summary>
    public static CameraDevice CreateDevice(string url, string? displayName = null) =>
        new(url, displayName ?? $"RTSP {url}") { ProviderName = Type, SourceType = Type, SourceUrl = url };

    public async Task<ICameraStream> OpenAsync(CameraDevice device, CameraSettings settings, CancellationToken cancellationToken = default)
    {
        var fps = (int)Math.Round(settings.FrameRate ?? 30);

        // Resolve a .local (mDNS) host to its IP — libav's resolver can't do mDNS on Windows.
        var url = await MdnsResolver.ResolveUrlAsync(device.SourceUrl, TimeSpan.FromSeconds(2), cancellationToken)
            .ConfigureAwait(false);

        var worker = MediaWorkerLocator.Resolve();

        // The bundled, crash-isolated worker (in-process libav) is the primary path — no external
        // ffmpeg needed. Only fall back to an external ffmpeg process if the worker isn't bundled.
        if (worker is not null)
        {
            var workerArgs = new[] { "--url", url, "--rtsp-tcp", "--fps", fps.ToString() };
            return new WorkerCameraStream(device, _ => new FfmpegProcess(worker, workerArgs));
        }

        var exe = FfmpegLocator.Resolve(_ffmpegPath())
            ?? throw new InvalidOperationException(
                "The embedded media worker isn't available and no external FFmpeg was found.");

        var args = FfmpegArguments.ForRtspToMjpeg(url, fps);
        return new FfmpegMjpegProcessStream(device, _ => new FfmpegProcess(exe, args));
    }
}
