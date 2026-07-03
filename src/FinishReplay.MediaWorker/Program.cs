using System.Buffers.Binary;
using Sdcb.FFmpeg.Codecs;
using Sdcb.FFmpeg.Formats;
using Sdcb.FFmpeg.Raw;
using Sdcb.FFmpeg.Swscales;
using Sdcb.FFmpeg.Toolboxs.Extensions;
using Sdcb.FFmpeg.Utils;

namespace FinishReplay.MediaWorker;

/// <summary>
/// Isolated media worker. Opens a source with the in-process FFmpeg (libav) bindings, decodes video,
/// re-encodes to MJPEG, and writes each JPEG frame to stdout using the framed protocol shared with
/// the main app (1 type byte + 4-byte little-endian length + payload). Running in its own process is
/// what gives crash isolation: if libav faults, only this process dies and the app's frame stream
/// ends cleanly.
///
/// Args:
///   --url &lt;string&gt;      input URL (rtsp://…, http://…, file path, or "video=Name" with --format dshow)
///   --format &lt;name&gt;     optional libav input format (e.g. dshow, v4l2)
///   --rtsp-tcp           use TCP transport for RTSP
///   --fps &lt;int&gt;         output MJPEG frame rate (default 30)
/// </summary>
internal static class Program
{
    // Protocol (keep in sync with MediaWorkerProtocol in the main app).
    private const byte TypeFrame = (byte)'F';
    private const byte TypeError = (byte)'E';
    private const byte TypeEos = (byte)'X';
    private const byte TypeReady = (byte)'R';

    private static readonly Stream Out = Console.OpenStandardOutput();

    private static int Main(string[] args)
    {
        try
        {
            RunDecodeLoop(ParseArgs(args));
            Write(TypeEos, ReadOnlySpan<byte>.Empty);
            return 0;
        }
        catch (Exception ex)
        {
            // Report the failure over the protocol so the host can show it; never bring the app down.
            Write(TypeError, System.Text.Encoding.UTF8.GetBytes(ex.Message));
            return 1;
        }
    }

    private static void RunDecodeLoop(Options o)
    {
        // Input devices (dshow / v4l2 / avfoundation) live in libavdevice and are NOT registered by
        // libavformat automatically — without this, FindByShortName("dshow") returns null and libav
        // tries to open "video=Name" as a file (ENOENT / error -2).
        ffmpeg.avdevice_register_all();

        InputFormat? inputFormat = o.Format is null ? null : InputFormat.FindByShortName(o.Format);
        bool isDevice = inputFormat is not null;
        bool isDshow = string.Equals(o.Format, "dshow", StringComparison.OrdinalIgnoreCase);

        // Build a device-open option set. `size`/`rate` toggle the constraints that pin the hardware to a
        // specific capture mode; `pixOverride` forces a pixel format / codec (null → use the requested one).
        // Transport-level options (rtsp_transport) are always kept — they don't constrain a device.
        MediaDictionary? BuildOptions(bool size, bool rate, string? pixOverride)
        {
            MediaDictionary? options = null;
            void SetOption(string key, string value) => (options ??= new MediaDictionary())[key] = value;

            if (o.RtspTcp)
                SetOption("rtsp_transport", "tcp");
            if (rate && o.Fps > 0)
                SetOption("framerate", o.Fps.ToString());
            if (size && !string.IsNullOrWhiteSpace(o.VideoSize))
                SetOption("video_size", o.VideoSize);

            var pix = pixOverride ?? o.PixelFormat;
            if (!string.IsNullOrWhiteSpace(pix))
            {
                if (pix.Equals("mjpeg", StringComparison.OrdinalIgnoreCase))
                    SetOption("vcodec", "mjpeg");           // dshow
                else
                {
                    SetOption("pixel_format", pix);         // dshow / avfoundation
                    SetOption("input_format", pix);         // v4l2
                }
            }
            return options;
        }

        // Ordered open attempts. Honor the configured mode first. DirectShow rejects an unsupported
        // size/rate/format *combination* with EIO (-5) instead of negotiating; HD webcams typically
        // expose large frame sizes only through MJPEG, so when a size is requested with format left on
        // Auto we retry forcing mjpeg to keep the resolution. Only as a last resort do we drop the
        // constraints and let the device pick its own default mode, so preview/record still works.
        var attempts = new List<Func<MediaDictionary?>> { () => BuildOptions(size: true, rate: true, pixOverride: null) };
        if (isDevice)
        {
            if (isDshow && !string.IsNullOrWhiteSpace(o.VideoSize) && string.IsNullOrWhiteSpace(o.PixelFormat))
                attempts.Add(() => BuildOptions(size: true, rate: true, pixOverride: "mjpeg"));
            attempts.Add(() => BuildOptions(size: false, rate: false, pixOverride: null));
        }

        FormatContext? input = null;
        Exception? lastError = null;
        foreach (var buildOptions in attempts)
        {
            try
            {
                input = FormatContext.OpenInputUrl(o.Url, inputFormat, buildOptions());
                break;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        if (input is null)
            throw lastError ?? new InvalidOperationException($"Could not open '{o.Url}'.");

        using (input)
        {
            RunPipeline(input, o);
        }
    }

    private static void RunPipeline(FormatContext input, Options o)
    {
        input.LoadStreamInfo();

        MediaStream inStream = input.FindBestStreamOrNull(AVMediaType.Video)
            ?? throw new InvalidOperationException("No video stream in the source.");

        using var decoder = new CodecContext(Codec.FindDecoderById(inStream.Codecpar!.CodecId));
        decoder.FillParameters(inStream.Codecpar);
        decoder.Open();

        using var encoder = new CodecContext(Codec.FindEncoderById(AVCodecID.Mjpeg))
        {
            Width = decoder.Width,
            Height = decoder.Height,
            PixelFormat = AVPixelFormat.Yuvj420p,
            TimeBase = new AVRational(1, o.Fps > 0 ? o.Fps : 30),
        };
        encoder.Open();

        using var converter = new VideoFrameConverter();

        Write(TypeReady, ReadOnlySpan<byte>.Empty);

        // libav pipeline: read packets → decode → convert to yuvj420p → mjpeg-encode → emit.
        IEnumerable<Packet> packets = input.ReadPackets(inStream.Index);
        IEnumerable<Frame> decoded = packets.DecodePackets(decoder);
        IEnumerable<Frame> converted = decoded.ConvertFrames(encoder);
        foreach (Packet jpeg in converted.EncodeFrames(encoder))
        {
            Write(TypeFrame, jpeg.Data.AsSpan());
            jpeg.Unref();
        }
    }

    private static void Write(byte type, ReadOnlySpan<byte> payload)
    {
        Span<byte> header = stackalloc byte[5];
        header[0] = type;
        BinaryPrimitives.WriteUInt32LittleEndian(header[1..], (uint)payload.Length);
        Out.Write(header);
        if (!payload.IsEmpty)
            Out.Write(payload);
        Out.Flush();
    }

    private static Options ParseArgs(string[] args)
    {
        var o = new Options();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--url" when i + 1 < args.Length: o.Url = args[++i]; break;
                case "--format" when i + 1 < args.Length: o.Format = args[++i]; break;
                case "--fps" when i + 1 < args.Length && int.TryParse(args[i + 1], out var f): o.Fps = f; i++; break;
                case "--video-size" when i + 1 < args.Length: o.VideoSize = args[++i]; break;
                case "--pixel-format" when i + 1 < args.Length: o.PixelFormat = args[++i]; break;
                case "--rtsp-tcp": o.RtspTcp = true; break;
            }
        }
        if (string.IsNullOrWhiteSpace(o.Url))
            throw new ArgumentException("Missing --url.");
        return o;
    }

    private sealed class Options
    {
        public string Url = "";
        public string? Format;
        public bool RtspTcp;
        public int Fps = 30;
        public string? VideoSize;
        public string? PixelFormat;
    }
}
