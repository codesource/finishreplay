namespace FinishReplay.Services.Camera.Providers.Usb;

/// <summary>
/// One capture mode a USB camera advertises: a pixel format / codec together with a frame size and the
/// frame rates supported at that (format, size). <see cref="Format"/> is the libav name the capture
/// pipeline uses (e.g. "mjpeg", "yuyv422", "nv12", "bgr24") so it can be passed straight through as the
/// worker's <c>--pixel-format</c>. Frame rates are whole/decimal fps, highest first.
/// </summary>
public sealed record UsbVideoMode(string Format, int Width, int Height, IReadOnlyList<double> FrameRates)
{
    public string Resolution => $"{Width}×{Height}";
}
