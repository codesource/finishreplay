using FinishReplay.Models;

namespace FinishReplay.Services.Camera.Providers;

/// <summary>
/// Placeholder provider for local USB/webcam devices.
///
/// TODO: enumerate real devices and open capture per platform:
///   - Windows: Media Foundation / DirectShow (or ffmpeg "dshow").
///   - macOS:   AVFoundation.
///   - Linux:   V4L2 (/dev/video*).
/// </summary>
public sealed class UsbCameraProvider : ICameraProvider
{
    public const string Type = "USB";
    public string ProviderName => Type;

    public Task<IReadOnlyList<CameraDevice>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        // TODO: real enumeration. Placeholder devices keep the selector populated.
        IReadOnlyList<CameraDevice> devices = new[]
        {
            new CameraDevice("usb:0", "USB Camera 0 (placeholder)") { ProviderName = Type, SourceType = Type },
            new CameraDevice("usb:1", "USB Camera 1 (placeholder)") { ProviderName = Type, SourceType = Type },
        };
        return Task.FromResult(devices);
    }

    public Task<ICameraStream> OpenAsync(CameraDevice device, CameraSettings settings, CancellationToken cancellationToken = default)
    {
        // TODO: open the real device with the requested settings.
        var info = new CameraStreamInfo { Width = settings.Width ?? 1920, Height = settings.Height ?? 1080, FrameRate = settings.FrameRate ?? 30, Codec = "RAW" };
        ICameraStream stream = new PlaceholderCameraStream(device, info);
        return Task.FromResult(stream);
    }
}
