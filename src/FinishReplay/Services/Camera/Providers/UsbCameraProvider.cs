using FinishReplay.Models;
using FinishReplay.Services.Camera.Providers.Ffmpeg;
using FinishReplay.Services.Camera.Providers.Usb;
using FinishReplay.Services.Media;

namespace FinishReplay.Services.Camera.Providers;

/// <summary>
/// Provider for local USB / webcam devices via ffmpeg's platform capture inputs (dshow on Windows,
/// avfoundation on macOS, v4l2 on Linux). Devices are enumerated from ffmpeg's <c>-list_devices</c>
/// output (or <c>/dev/video*</c> on Linux) and captured as MJPEG, flowing through the shared pipeline
/// for preview, AVI recording and replay. Requires ffmpeg (configurable path / PATH).
/// </summary>
public sealed class UsbCameraProvider : ICameraProvider
{
    public const string Type = "USB";

    private readonly Func<string> _ffmpegPath;
    private readonly Func<VideoBackend> _backend;

    public UsbCameraProvider(Func<string>? ffmpegPath = null, Func<VideoBackend>? backend = null)
    {
        _ffmpegPath = ffmpegPath ?? (() => "ffmpeg");
        _backend = backend ?? (() => VideoBackend.ExternalProcess);
    }

    public string ProviderName => Type;

    public async Task<IReadOnlyList<CameraDevice>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var platform = UsbPlatformInfo.Current;
        if (platform == UsbPlatform.Linux)
            return EnumerateV4l2();

        var exe = FfmpegLocator.Resolve(_ffmpegPath());
        if (exe is null)
            return Array.Empty<CameraDevice>(); // ffmpeg required to enumerate dshow/avfoundation

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
            var workerArgs = new[] { "--url", url, "--format", fmt, "--fps", fps.ToString() };
            ICameraStream isolated = new WorkerCameraStream(device, _ => new FfmpegProcess(worker, workerArgs));
            return Task.FromResult(isolated);
        }

        var exe = FfmpegLocator.Resolve(_ffmpegPath())
            ?? throw new InvalidOperationException(
                "FFmpeg was not found. Set the FFmpeg path in Settings, or add ffmpeg to your PATH, to capture USB cameras.");

        // AVFoundation opens by index; ":none" selects the video device with no audio.
        var deviceId = platform == UsbPlatform.MacOS ? $"{device.Id}:none" : device.Id;
        var args = FfmpegArguments.ForUsbToMjpeg(platform, deviceId, fps);

        ICameraStream stream = new FfmpegMjpegProcessStream(device, _ => new FfmpegProcess(exe, args));
        return Task.FromResult(stream);
    }

    private static IReadOnlyList<CameraDevice> EnumerateV4l2()
    {
        if (!Directory.Exists("/dev"))
            return Array.Empty<CameraDevice>();

        return Directory.GetFiles("/dev", "video*")
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(path => new CameraDevice(path, path) { ProviderName = Type, SourceType = Type })
            .ToList();
    }
}
