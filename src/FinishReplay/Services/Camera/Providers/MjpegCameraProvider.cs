using FinishReplay.Models;
using FinishReplay.Services.Camera.Providers.Mjpeg;

namespace FinishReplay.Services.Camera.Providers;

/// <summary>
/// Placeholder provider for MJPEG HTTP streams (e.g. <c>http://host/video</c>).
/// MJPEG is ideal for latency calibration: every frame is an independent JPEG, so there is
/// no GOP / B-frame decode delay and flash detection is straightforward frame-by-frame.
///
/// MJPEG sources are not auto-discoverable, so <see cref="DiscoverAsync"/> returns nothing;
/// the user adds a URL manually via <see cref="CreateDevice"/>. <see cref="OpenAsync"/> returns a
/// live <see cref="MjpegCameraStream"/> reading the HTTP multipart stream.
/// </summary>
public sealed class MjpegCameraProvider : ICameraProvider
{
    public const string Type = "MJPEG";
    public string ProviderName => Type;

    public Task<IReadOnlyList<CameraDevice>> DiscoverAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<CameraDevice>>(Array.Empty<CameraDevice>());

    /// <summary>Build a device for a user-supplied MJPEG URL.</summary>
    public static CameraDevice CreateDevice(string url, string? displayName = null) =>
        new(url, displayName ?? $"MJPEG {url}") { ProviderName = Type, SourceType = Type, SourceUrl = url };

    public Task<ICameraStream> OpenAsync(CameraDevice device, CameraSettings settings, CancellationToken cancellationToken = default)
    {
        ICameraStream stream = new MjpegCameraStream(device);
        return Task.FromResult(stream);
    }
}
