using FinishReplay.Models;
using FinishReplay.Services.Camera.Providers.Ffmpeg;
using Xunit;

namespace FinishReplay.Tests;

public class FfmpegRtspTests
{
    private static byte[] FakeJpeg(byte fill, int payload)
    {
        var body = new byte[payload];
        Array.Fill(body, fill);
        return new byte[] { 0xFF, 0xD8 }.Concat(body).Concat(new byte[] { 0xFF, 0xD9 }).ToArray();
    }

    /// <summary>Fake child process whose stdout is a fixed byte stream; records disposal.</summary>
    private sealed class FakeProcess : IStdoutProcess
    {
        public FakeProcess(byte[] stdout) => StandardOutput = new MemoryStream(stdout);
        public Stream StandardOutput { get; }
        public string StandardErrorTail => "";
        public bool Disposed { get; private set; }
        public void Dispose() { Disposed = true; StandardOutput.Dispose(); }
    }

    [Fact]
    public void Rtsp_args_decode_to_mjpeg_on_stdout_over_tcp()
    {
        var args = FfmpegArguments.ForRtspToMjpeg("rtsp://cam/live", fps: 25, quality: 6);

        // input before output, TCP transport, mjpeg to pipe:1
        var i = args.ToList();
        Assert.Contains("-rtsp_transport", i);
        Assert.Equal("tcp", i[i.IndexOf("-rtsp_transport") + 1]);
        Assert.Equal("rtsp://cam/live", i[i.IndexOf("-i") + 1]);
        Assert.Equal("mjpeg", i[i.IndexOf("-f") + 1]);
        Assert.Equal("25", i[i.IndexOf("-r") + 1]);
        Assert.Equal("6", i[i.IndexOf("-q:v") + 1]);
        Assert.Equal("pipe:1", i[^1]);
        Assert.True(i.IndexOf("-i") < i.IndexOf("-f")); // input options precede output options
    }

    [Fact]
    public async Task Process_stream_yields_jpeg_frames_and_disposes_the_process()
    {
        var f1 = FakeJpeg(0xAA, 40);
        var f2 = FakeJpeg(0xBB, 41);
        var stdout = f1.Concat(f2).ToArray(); // ffmpeg -f mjpeg emits concatenated JPEGs

        var fake = new FakeProcess(stdout);
        var device = new CameraDevice("rtsp://cam/live", "Cam") { SourceType = "RTSP" };
        await using var stream = new FfmpegMjpegProcessStream(device, _ => fake);

        var frames = new List<VideoFrame>();
        await foreach (var frame in stream.ReadFramesAsync())
            frames.Add(frame);

        Assert.Equal(2, frames.Count);
        Assert.Equal(VideoFrameFormat.Jpeg, frames[0].Format);
        Assert.Equal(f1, frames[0].Data);
        Assert.Equal(f2, frames[1].Data);
        Assert.True(fake.Disposed); // the stream tears the process down when enumeration ends
    }

    [Fact]
    public void Locator_uses_an_explicit_existing_path_verbatim()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ffmpeg_{Guid.NewGuid():N}.exe");
        File.WriteAllBytes(path, new byte[] { 0 });
        try
        {
            Assert.Equal(path, FfmpegLocator.Resolve(path));
            Assert.True(FfmpegLocator.IsAvailable(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
