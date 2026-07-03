namespace FinishReplay.Services.Camera.Providers.Usb;

/// <summary>
/// Reads a USB camera's supported capture modes (format + resolution + frame rates) natively where a
/// platform reader exists (DirectShow on Windows). Returns an empty list otherwise — callers then fall
/// back to a generic set of options. V4L2 (Linux) enumeration is a future addition.
/// </summary>
public static class UsbCameraCapabilities
{
    /// <summary>True when the current platform can report a device's supported modes.</summary>
    public static bool IsSupported => OperatingSystem.IsWindows();

    /// <summary>
    /// The modes for the device identified by <paramref name="deviceId"/> (its capture handle — the
    /// DirectShow FriendlyName / DevicePath on Windows), or empty if unknown/unsupported.
    /// </summary>
    public static IReadOnlyList<UsbVideoMode> Query(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            return Array.Empty<UsbVideoMode>();

        try
        {
            if (OperatingSystem.IsWindows())
                return DirectShowUsbCapabilities.Query(deviceId);
        }
        catch
        {
            // Any native failure → empty; the config UI falls back to generic options.
        }

        return Array.Empty<UsbVideoMode>();
    }
}
