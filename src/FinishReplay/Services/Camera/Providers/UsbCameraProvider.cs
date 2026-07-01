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
    private readonly Func<VideoBackend> _backend;
    private readonly IUsbCameraEnumerator _nativeEnumerator;

    public UsbCameraProvider(
        Func<string>? ffmpegPath = null,
        Func<VideoBackend>? backend = null,
        IUsbCameraEnumerator? nativeEnumerator = null)
    {
        _ffmpegPath = ffmpegPath ?? (() => "ffmpeg");
        _backend = backend ?? (() => VideoBackend.ExternalProcess);
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
        var fps = (int)Math.Round(settings.FrameRate ?? 30);

        // Embedded, crash-isolated worker (in-process libav in a child process), when selected & present.
        if (_backend() == VideoBackend.IsolatedWorker && MediaWorkerLocator.Resolve() is { } worker)
        {
            var (fmt, url) = platform switch
            {
                UsbPlatform.Windows => ("dshow", $"video={device.Id}"),
                UsbPlatform.MacOS => ("avfoundation", device.Id),
                UsbPlatform.Linux => ("v4l2", device.Id),
                _ => throw new PlatformNotSupportedException("USB capture is not supported on this platform."),
            };
            var workerArgs = new List<string> { "--url", url, "--format", fmt, "--fps", fps.ToString() };
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
                "FFmpeg was not found. Set the FFmpeg path in Settings, or add ffmpeg to your PATH, to capture USB cameras.");

        // AVFoundation opens by index; ":none" selects the video device with no audio.
        var deviceId = platform == UsbPlatform.MacOS ? $"{device.Id}:none" : device.Id;
        var args = FfmpegArguments.ForUsbToMjpeg(platform, deviceId, fps, width: settings.Width, height: settings.Height, pixelFormat: settings.PixelFormat);

        ICameraStream stream = new FfmpegMjpegProcessStream(device, _ => new FfmpegProcess(exe, args));
        return Task.FromResult(stream);
    }
}
