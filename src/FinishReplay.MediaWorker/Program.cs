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

        // Build the option set for a device open. When `constrain` is false we drop the options that
        // pin the hardware to a specific capture mode (video_size / framerate / pixel_format). DirectShow
        // (and, to a lesser extent, v4l2/avfoundation) rejects an unsupported size/rate/format *combination*
        // with EIO (-5) instead of negotiating, so we first try the requested mode, then fall back to the
        // device's own default mode so preview/record still works. Transport-level options (rtsp_transport)
        // are always kept — they don't constrain a device.
        MediaDictionary? BuildOptions(bool constrain)
        {
            MediaDictionary? options = null;
            void SetOption(string key, string value) => (options ??= new MediaDictionary())[key] = value;

            if (o.RtspTcp)
                SetOption("rtsp_transport", "tcp");
            if (constrain)
            {
                if (o.Fps > 0)
                    SetOption("framerate", o.Fps.ToString());
                if (!string.IsNullOrWhiteSpace(o.VideoSize))
                    SetOption("video_size", o.VideoSize);
                if (!string.IsNullOrWhiteSpace(o.PixelFormat))
                {
                    if (o.PixelFormat.Equals("mjpeg", StringComparison.OrdinalIgnoreCase))
                        SetOption("vcodec", "mjpeg");           // dshow
                    else
                    {
                        SetOption("pixel_format", o.PixelFormat); // dshow / avfoundation
                        SetOption("input_format", o.PixelFormat); // v4l2
                    }
                }
            }
            return options;
        }

        bool isDevice = inputFormat is not null;
        bool hasConstraints = o.Fps > 0 || !string.IsNullOrWhiteSpace(o.VideoSize) || !string.IsNullOrWhiteSpace(o.PixelFormat);

        FormatContext input;
        try
        {
            input = FormatContext.OpenInputUrl(o.Url, inputFormat, BuildOptions(constrain: true));
        }
        catch when (isDevice && hasConstraints)
        {
            // Requested capture mode not supported by this device — retry with its default mode.
            input = FormatContext.OpenInputUrl(o.Url, inputFormat, BuildOptions(constrain: false));
        }

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
            TimeBase = new AVRational(1, o.Fps),
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
