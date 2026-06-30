using FinishReplay.Models;

namespace FinishReplay.Services.Camera;

/// <summary>
/// A pluggable source of cameras for one transport (USB, MJPEG, RTSP, ONVIF, ...).
/// Capture is provider-based so no single method is hardcoded; new transports are added
/// by implementing this interface and registering it with the <see cref="CameraProviderRegistry"/>.
/// </summary>
public interface ICameraProvider
{
    /// <summary>Stable provider name, also used as the device <see cref="CameraDevice.SourceType"/>.</summary>
    string ProviderName { get; }

    /// <summary>Discover devices this provider can offer right now.</summary>
    Task<IReadOnlyList<CameraDevice>> DiscoverAsync(CancellationToken cancellationToken = default);

    /// <summary>Open a live stream for <paramref name="device"/>.</summary>
    Task<ICameraStream> OpenAsync(CameraDevice device, CameraSettings settings, CancellationToken cancellationToken = default);
}
