using FinishReplay.Models;
using FinishReplay.Services.Camera.Providers.Ffmpeg;
using FinishReplay.Services.Media;
using Xunit;

namespace FinishReplay.Tests;

public class MediaWorkerTests
{
    private static byte[] Jpeg(byte fill, int n)
    {
        var b = new byte[n];
        Array.Fill(b, fill);
        return new byte[] { 0xFF, 0xD8 }.Concat(b).Concat(new byte[] { 0xFF, 0xD9 }).ToArray();
    }

    private sealed class FakeWorker : IStdoutProcess
    {
        public FakeWorker(byte[] stdout) => StandardOutput = new MemoryStream(stdout);
        public Stream StandardOutput { get; }
        public string StandardErrorTail => "";
        public bool Disposed { get; private set; }
        public void Dispose() { Disposed = true; StandardOutput.Dispose(); }
    }

    [Fact]
    public async Task Protocol_round_trips_frames_then_eos()
    {
        var f1 = Jpeg(0x11, 32);
        var f2 = Jpeg(0x22, 33);

        using var ms = new MemoryStream();
        await MediaWorkerProtocol.WriteFrameAsync(ms, f1);
        await MediaWorkerProtocol.WriteFrameAsync(ms, f2);
        await MediaWorkerProtocol.WriteAsync(ms, MediaWorkerProtocol.TypeEos, ReadOnlyMemory<byte>.Empty);
        ms.Position = 0;

        var messages = new List<MediaMessage>();
        await foreach (var m in MediaWorkerProtocol.ReadAsync(ms))
            messages.Add(m);

        Assert.Equal(3, messages.Count);
        Assert.Equal(MediaMessageType.Frame, messages[0].Type);
        Assert.Equal(f1, messages[0].Payload);
        Assert.Equal(f2, messages[1].Payload);
        Assert.Equal(MediaMessageType.Eos, messages[2].Type);
    }

    [Fact]
    public async Task Worker_stream_yields_frames_and_disposes_worker()
    {
        var f1 = Jpeg(0xA1, 40);
        var f2 = Jpeg(0xB2, 41);

        using var buf = new MemoryStream();
        await MediaWorkerProtocol.WriteFrameAsync(buf, f1);
        await MediaWorkerProtocol.WriteFrameAsync(buf, f2);
        await MediaWorkerProtocol.WriteAsync(buf, MediaWorkerProtocol.TypeEos, ReadOnlyMemory<byte>.Empty);

        var fake = new FakeWorker(buf.ToArray());
        var device = new CameraDevice("rtsp://x", "cam");
        await using var stream = new WorkerCameraStream(device, _ => fake);

        var frames = new List<VideoFrame>();
        await foreach (var frame in stream.ReadFramesAsync())
            frames.Add(frame);

        Assert.Equal(2, frames.Count);
        Assert.Equal(f1, frames[0].Data);
        Assert.True(fake.Disposed);
    }

    [Fact]
    public async Task Worker_crash_midframe_ends_stream_without_throwing()
    {
        var f1 = Jpeg(0xC3, 50);

        using var buf = new MemoryStream();
        await MediaWorkerProtocol.WriteFrameAsync(buf, f1);
        // Simulate a crash: a partial header with no body (truncated output).
        buf.Write(new byte[] { MediaWorkerProtocol.TypeFrame, 0x10, 0x00 }); // says 'frame len=16' but nothing follows
        var truncated = buf.ToArray();

        var fake = new FakeWorker(truncated);
        var device = new CameraDevice("rtsp://x", "cam");
        await using var stream = new WorkerCameraStream(device, _ => fake);

        var frames = new List<VideoFrame>();
        // Must not throw — the app treats a dead worker as end-of-stream.
        await foreach (var frame in stream.ReadFramesAsync())
            frames.Add(frame);

        Assert.Single(frames);         // the one complete frame before the crash
        Assert.Equal(f1, frames[0].Data);
        Assert.True(fake.Disposed);
    }
}
