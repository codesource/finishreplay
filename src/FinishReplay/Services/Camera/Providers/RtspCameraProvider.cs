using FinishReplay.Models;

namespace FinishReplay.Services.Camera.Providers;

/// <summary>
/// Placeholder provider for RTSP/H.264 streams (e.g. <c>rtsp://host/live</c>).
/// Calibration is supported but latency may be less stable than MJPEG due to camera-side
/// buffering and GOP/B-frame decode delay.
///
/// TODO: open the stream via FFmpeg (process or binding) and surface decoded frames.
/// </summary>
public sealed class RtspCameraProvider : ICameraProvider
{
    public const string Type = "RTSP";
    public string ProviderName => Type;

    public Task<IReadOnlyList<CameraDevice>> DiscoverAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<CameraDevice>>(Array.Empty<CameraDevice>());

    /// <summary>Build a device for a user-supplied RTSP URL.</summary>
    public static CameraDevice CreateDevice(string url, string? displayName = null) =>
        new(url, displayName ?? $"RTSP {url}") { ProviderName = Type, SourceType = Type, SourceUrl = url };

    public Task<ICameraStream> OpenAsync(CameraDevice device, CameraSettings settings, CancellationToken cancellationToken = default)
    {
        // TODO: open device.SourceUrl through FFmpeg and decode frames.
        var info = new CameraStreamInfo { Codec = "H264" };
        ICameraStream stream = new PlaceholderCameraStream(device, info);
        return Task.FromResult(stream);
    }
}
