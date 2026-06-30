using FinishReplay.Services.Camera.Providers.Usb;

namespace FinishReplay.Services.Camera.Providers.Ffmpeg;

/// <summary>Builds ffmpeg command-line arguments for the capture pipelines.</summary>
public static class FfmpegArguments
{
    /// <summary>Args that make ffmpeg print the available USB capture devices to stderr.</summary>
    public static IReadOnlyList<string> ListUsbDevices(UsbPlatform platform) => platform switch
    {
        UsbPlatform.Windows => new[] { "-hide_banner", "-list_devices", "true", "-f", "dshow", "-i", "dummy" },
        UsbPlatform.MacOS => new[] { "-hide_banner", "-list_devices", "true", "-f", "avfoundation", "-i", "" },
        _ => Array.Empty<string>(), // Linux enumerates /dev/video* directly
    };

    /// <summary>
    /// Capture a local USB camera and emit MJPEG on stdout, using the platform's ffmpeg input format
    /// (dshow / avfoundation / v4l2). <paramref name="deviceId"/> is the platform handle:
    /// the dshow device name, the avfoundation index (e.g. "0:none"), or the v4l2 path (/dev/videoN).
    /// </summary>
    public static IReadOnlyList<string> ForUsbToMjpeg(UsbPlatform platform, string deviceId, int fps = 30, int quality = 5)
    {
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-nostdin" };

        if (fps > 0)
        {
            args.Add("-framerate"); // input option: requested capture rate
            args.Add(fps.ToString());
        }

        switch (platform)
        {
            case UsbPlatform.Windows:
                args.Add("-f"); args.Add("dshow");
                args.Add("-i"); args.Add($"video={deviceId}");
                break;
            case UsbPlatform.MacOS:
                args.Add("-f"); args.Add("avfoundation");
                args.Add("-i"); args.Add(deviceId);
                break;
            case UsbPlatform.Linux:
                args.Add("-f"); args.Add("v4l2");
                args.Add("-i"); args.Add(deviceId);
                break;
            default:
                throw new PlatformNotSupportedException("USB capture is not supported on this platform.");
        }

        args.Add("-an");
        args.Add("-q:v"); args.Add(quality.ToString());
        args.Add("-f"); args.Add("mjpeg");
        args.Add("pipe:1");
        return args;
    }

    /// <summary>
    /// Decode an RTSP/H.264 source and emit a Motion-JPEG stream on stdout (<c>pipe:1</c>), which the
    /// app parses with <see cref="Mjpeg.MjpegStreamReader"/> — the same path used for native MJPEG
    /// cameras. TCP transport is more reliable than the UDP default on lossy networks.
    /// </summary>
    public static IReadOnlyList<string> ForRtspToMjpeg(string url, int fps = 30, int quality = 5, bool useTcp = true)
    {
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-nostdin" };

        if (useTcp)
        {
            args.Add("-rtsp_transport");
            args.Add("tcp");
        }

        args.Add("-i");
        args.Add(url);

        args.Add("-an"); // no audio

        if (fps > 0)
        {
            args.Add("-r");
            args.Add(fps.ToString());
        }

        args.Add("-q:v");
        args.Add(quality.ToString()); // 2 (best) .. 31 (worst)
        args.Add("-f");
        args.Add("mjpeg");
        args.Add("pipe:1");

        return args;
    }

    /// <summary>
    /// Archival passthrough: copy the RTSP source's encoded video stream to MP4 with no re-encode
    /// (lossless, original H.264). Fragmented MP4 flags keep the file playable even if ffmpeg is
    /// killed. stdin is left open so a graceful "q" can finalize the file. <paramref name="outputPath"/>
    /// is the file to write.
    /// </summary>
    public static IReadOnlyList<string> ForRtspPassthroughMp4(string url, string outputPath, bool useTcp = true)
    {
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-y" };

        if (useTcp)
        {
            args.Add("-rtsp_transport");
            args.Add("tcp");
        }

        args.Add("-i");
        args.Add(url);
        args.Add("-an");
        args.Add("-c:v");
        args.Add("copy");
        args.Add("-movflags");
        args.Add("+frag_keyframe+empty_moov");
        args.Add("-f");
        args.Add("mp4");
        args.Add(outputPath);
        return args;
    }

    /// <summary>
    /// Decode a recorded file (e.g. an MP4/H.264 passthrough clip) back to an MJPEG stream on stdout,
    /// so replay can render its frames through the shared MJPEG pipeline.
    /// </summary>
    public static IReadOnlyList<string> ForFileToMjpeg(string inputPath, int quality = 5)
        => new[] { "-hide_banner", "-loglevel", "error", "-nostdin", "-i", inputPath, "-an", "-q:v", quality.ToString(), "-f", "mjpeg", "pipe:1" };
}
