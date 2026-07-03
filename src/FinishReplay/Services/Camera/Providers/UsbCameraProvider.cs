using FinishReplay.Models;
using FinishReplay.Services.Camera.Providers.Ffmpeg;
using FinishReplay.Services.Camera.Providers.Usb;
using FinishReplay.Services.Media;

namespace FinishReplay.Services.Camera.Providers;

/// <summary>
/// Provider for local USB / webcam devices. Enumeration prefers a cheap, in-process native path
/// (DirectShow on Windows, V4L2 on Linux) and only falls back to ffmpeg's <c>-list_devices</c> on
/// platforms without one (macOS/avfoundation). Capture is done by ffmpeg's platform input
/// (dshow / avfoundation / v4l2), producing MJPEG for the shared preview/record/replay pipeline.
/// </summary>
public sealed class UsbCameraProvider : ICameraProvider
{
    public const string Type = "USB";

    private readonly Func<string> _ffmpegPath;
    private readonly IUsbCameraEnumerator _nativeEnumerator;

    public UsbCameraProvider(
        Func<string>? ffmpegPath = null,
        IUsbCameraEnumerator? nativeEnumerator = null)
    {
        _ffmpegPath = ffmpegPath ?? (() => "ffmpeg");
        _nativeEnumerator = nativeEnumerator ?? new NativeUsbCameraEnumerator();
    }

    public string ProviderName => Type;

    public async Task<IReadOnlyList<CameraDevice>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        // Cheap in-process enumeration on Windows (DirectShow) and Linux (V4L2) — no ffmpeg spawn.
        if (_nativeEnumerator.IsSupported)
            return _nativeEnumerator.Enumerate();

        // Fallback for platforms without a native enumerator (e.g. macOS): ffmpeg -list_devices.
        var platform = UsbPlatformInfo.Current;
        var exe = FfmpegLocator.Resolve(_ffmpegPath());
        if (exe is null)
            return Array.Empty<CameraDevice>();

        var listArgs = FfmpegArguments.ListUsbDevices(platform);
        if (listArgs.Count == 0)
            return Array.Empty<CameraDevice>();

        string stderr;
        try
        {
            stderr = await FfmpegProbe.GetStderrAsync(exe, listArgs, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return Array.Empty<CameraDevice>();
        }

        var parsed = platform == UsbPlatform.Windows
            ? UsbDeviceParser.ParseDShow(stderr)
            : UsbDeviceParser.ParseAvFoundation(stderr);

        return parsed
            .Select(d => new CameraDevice(d.Id, d.Name) { ProviderName = Type, SourceType = Type })
            .ToList();
    }

    public Task<ICameraStream> OpenAsync(CameraDevice device, CameraSettings settings, CancellationToken cancellationToken = default)
    {
        var platform = UsbPlatformInfo.Current;
        var worker = MediaWorkerLocator.Resolve();

        // The bundled, crash-isolated worker (in-process libav) is the primary path — no external
        // ffmpeg needed. Only fall back to an external ffmpeg process if the worker isn't bundled.
        if (worker is not null)
        {
            var (fmt, url) = platform switch
            {
                UsbPlatform.Windows => ("dshow", $"video={device.Id}"),
                UsbPlatform.MacOS => ("avfoundation", device.Id),
                UsbPlatform.Linux => ("v4l2", device.Id),
                _ => throw new PlatformNotSupportedException("USB capture is not supported on this platform."),
            };
            var workerArgs = new List<string> { "--url", url, "--format", fmt };
            // Only constrain the device frame rate when the user configured one — "Auto" must not pin
            // it to 30, which would over-constrain the size/rate/format combination the device supports.
            if (settings.FrameRate is > 0)
            {
                workerArgs.Add("--fps");
                workerArgs.Add(((int)Math.Round(settings.FrameRate.Value)).ToString());
            }
            if (settings is { Width: > 0, Height: > 0 })
            {
                workerArgs.Add("--video-size");
                workerArgs.Add($"{settings.Width}x{settings.Height}");
            }
            if (!string.IsNullOrWhiteSpace(settings.PixelFormat))
            {
                workerArgs.Add("--pixel-format");
                workerArgs.Add(settings.PixelFormat);
            }
            ICameraStream isolated = new WorkerCameraStream(device, _ => new FfmpegProcess(worker, workerArgs));
            return Task.FromResult(isolated);
        }

        var exe = FfmpegLocator.Resolve(_ffmpegPath())
            ?? throw new InvalidOperationException(
                "The embedded media worker isn't available and no external FFmpeg was found.");

        // AVFoundation opens by index; ":none" selects the video device with no audio.
        var deviceId = platform == UsbPlatform.MacOS ? $"{device.Id}:none" : device.Id;
        var fps = (int)Math.Round(settings.FrameRate ?? 30);
        var args = FfmpegArguments.ForUsbToMjpeg(platform, deviceId, fps, width: settings.Width, height: settings.Height, pixelFormat: settings.PixelFormat);

        ICameraStream stream = new FfmpegMjpegProcessStream(device, _ => new FfmpegProcess(exe, args));
        return Task.FromResult(stream);
    }
}
