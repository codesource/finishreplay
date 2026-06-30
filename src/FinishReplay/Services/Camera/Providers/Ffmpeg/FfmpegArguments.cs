namespace FinishReplay.Services.Camera.Providers.Ffmpeg;

/// <summary>Builds ffmpeg command-line arguments for the capture pipelines.</summary>
public static class FfmpegArguments
{
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
}
