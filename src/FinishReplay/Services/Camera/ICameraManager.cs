using FinishReplay.Models;

namespace FinishReplay.Services.Camera;

/// <summary>
/// Discovers capture devices across all providers and tracks the active selection.
/// Thin facade over <see cref="CameraProviderRegistry"/> for the view models.
/// </summary>
public interface ICameraManager
{
    CameraDevice? ActiveCamera { get; }

    /// <summary>Providers available for discovery/opening (USB, MJPEG, RTSP, ...).</summary>
    IReadOnlyList<ICameraProvider> Providers { get; }

    /// <summary>Enumerate currently available capture devices from every provider.</summary>
    Task<IReadOnlyList<CameraDevice>> DiscoverAsync(CancellationToken cancellationToken = default);

    void SelectCamera(CameraDevice? camera);
}
