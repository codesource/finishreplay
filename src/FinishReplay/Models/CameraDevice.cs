namespace FinishReplay.Models;

/// <summary>
/// A capture device discovered by an <see cref="FinishReplay.Services.Camera.ICameraProvider"/>.
/// <paramref name="Id"/> is the provider-specific handle used to open the device/stream.
/// </summary>
public sealed record CameraDevice(string Id, string Name)
{
    /// <summary>Provider that discovered this device (e.g. "USB", "MJPEG", "RTSP").</summary>
    public string ProviderName { get; init; } = "";

    /// <summary>Transport/source kind for this device (e.g. "USB", "MJPEG", "RTSP").</summary>
    public string SourceType { get; init; } = "";

    /// <summary>Connection URL for network sources; empty for local USB devices.</summary>
    public string SourceUrl { get; init; } = "";

    // Shown directly in the camera selector.
    public override string ToString() => Name;
}
