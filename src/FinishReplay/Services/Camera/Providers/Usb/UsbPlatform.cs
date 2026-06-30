namespace FinishReplay.Services.Camera.Providers.Usb;

/// <summary>Host platform, which selects the ffmpeg capture input format for USB cameras.</summary>
public enum UsbPlatform
{
    Unknown,
    Windows, // dshow
    MacOS,   // avfoundation
    Linux,   // v4l2
}

public static class UsbPlatformInfo
{
    public static UsbPlatform Current =>
        OperatingSystem.IsWindows() ? UsbPlatform.Windows
        : OperatingSystem.IsMacOS() ? UsbPlatform.MacOS
        : OperatingSystem.IsLinux() ? UsbPlatform.Linux
        : UsbPlatform.Unknown;
}
