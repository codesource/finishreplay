using FinishReplay.Models;
using FinishReplay.Services.Camera.Providers.Ffmpeg;

namespace FinishReplay.Services.Camera.Providers;

/// <summary>
/// Provider for RTSP/H.264 streams (e.g. <c>rtsp://host/live</c>). Capture is done by ffmpeg, which
/// decodes the stream and emits MJPEG frames that flow through the shared MJPEG pipeline (preview,
/// AVI recording, replay). The ffmpeg executable is resolved from the configured path or PATH.
///
/// Calibration is supported but latency may be less stable than native MJPEG due to camera-side
/// buffering and GOP/B-frame decode delay.
/// </summary>
public sealed class RtspCameraProvider : ICameraProvider
{
    public const string Type = "RTSP";

    private readonly Func<string> _ffmpegPath;

    /// <param name="ffmpegPath">
    /// Returns the configured ffmpeg path (re-read per open so Settings changes take effect).
    /// Defaults to "ffmpeg" (PATH lookup).
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

    public Task<ICameraStream> OpenAsync(CameraDevice device, CameraSettings settings, CancellationToken cancellationToken = default)
    {
        var exe = FfmpegLocator.Resolve(_ffmpegPath())
            ?? throw new InvalidOperationException(
                "FFmpeg was not found. Set the FFmpeg path in Settings, or add ffmpeg to your PATH, to capture RTSP cameras.");

        var fps = (int)Math.Round(settings.FrameRate ?? 30);
        var args = FfmpegArguments.ForRtspToMjpeg(device.SourceUrl, fps);

        ICameraStream stream = new FfmpegMjpegProcessStream(device, _ => new FfmpegProcess(exe, args));
        return Task.FromResult(stream);
    }
}
