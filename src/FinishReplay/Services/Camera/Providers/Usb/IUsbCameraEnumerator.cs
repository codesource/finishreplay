using FinishReplay.Models;

namespace FinishReplay.Services.Camera.Providers.Usb;

/// <summary>
/// Enumerates local USB/webcam devices cheaply and in-process (no ffmpeg spawn). Returns the device
/// handle in <see cref="CameraDevice.Id"/> exactly as the capture backend expects it (the DirectShow
/// FriendlyName on Windows, the /dev/videoN path on Linux).
/// </summary>
public interface IUsbCameraEnumerator
{
    /// <summary>True when a native enumerator exists for the current platform.</summary>
    bool IsSupported { get; }

    IReadOnlyList<CameraDevice> Enumerate();
}

/// <summary>
/// Platform-selecting native enumerator: DirectShow on Windows, V4L2 (/dev + sysfs) on Linux.
/// Not supported elsewhere (e.g. macOS) — callers fall back to ffmpeg device listing.
/// </summary>
public sealed class NativeUsbCameraEnumerator : IUsbCameraEnumerator
{
    public bool IsSupported => OperatingSystem.IsWindows() || OperatingSystem.IsLinux();

    public IReadOnlyList<CameraDevice> Enumerate()
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return DirectShowUsbEnumerator.Enumerate();
            if (OperatingSystem.IsLinux())
                return V4l2UsbEnumerator.Enumerate();
        }
        catch
        {
            // Any native failure → empty; the provider can still fall back to ffmpeg.
        }

        return Array.Empty<CameraDevice>();
    }
}
