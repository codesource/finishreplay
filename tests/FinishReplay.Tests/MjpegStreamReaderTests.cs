using FinishReplay.Services.Camera.Providers.Mjpeg;
using Xunit;

namespace FinishReplay.Tests;

public class MjpegStreamReaderTests
{
    // A synthetic "JPEG": SOI (FF D8) ... payload ... EOI (FF D9). Valid framing is all the reader needs.
    private static byte[] FakeJpeg(byte fill, int payload)
    {
        var body = new byte[payload];
        Array.Fill(body, fill);
        return new byte[] { 0xFF, 0xD8 }.Concat(body).Concat(new byte[] { 0xFF, 0xD9 }).ToArray();
    }

    private static byte[] MultipartChunk(byte[] jpeg) =>
        System.Text.Encoding.ASCII.GetBytes("--frame\r\nContent-Type: image/jpeg\r\nContent-Length: " + jpeg.Length + "\r\n\r\n")
            .Concat(jpeg)
            .Concat(System.Text.Encoding.ASCII.GetBytes("\r\n"))
            .ToArray();

    private static async Task<List<byte[]>> ReadAll(byte[] streamBytes, int readBufferSize = 16 * 1024)
    {
        using var ms = new MemoryStream(streamBytes);
        var frames = new List<byte[]>();
        await foreach (var f in MjpegStreamReader.ReadFramesAsync(ms, readBufferSize))
            frames.Add(f);
        return frames;
    }

    [Fact]
    public async Task Extracts_each_frame_between_boundaries()
    {
        var j1 = FakeJpeg(0x11, 32);
        var j2 = FakeJpeg(0x22, 64);
        var j3 = FakeJpeg(0x33, 16);
        var stream = MultipartChunk(j1).Concat(MultipartChunk(j2)).Concat(MultipartChunk(j3)).ToArray();

        var frames = await ReadAll(stream);

        Assert.Equal(3, frames.Count);
        Assert.Equal(j1, frames[0]);
        Assert.Equal(j2, frames[1]);
        Assert.Equal(j3, frames[2]);
    }

    [Fact]
    public async Task Reassembles_frames_split_across_tiny_reads()
    {
        // A 1-byte read buffer forces SOI/EOI to be found across many partial reads.
        var j1 = FakeJpeg(0x44, 50);
        var j2 = FakeJpeg(0x55, 20);
        var stream = MultipartChunk(j1).Concat(MultipartChunk(j2)).ToArray();

        var frames = await ReadAll(stream, readBufferSize: 1);

        Assert.Equal(2, frames.Count);
        Assert.Equal(j1, frames[0]);
        Assert.Equal(j2, frames[1]);
    }

    [Fact]
    public async Task Ignores_trailing_partial_frame()
    {
        var j1 = FakeJpeg(0x66, 24);
        // j1 complete, then an SOI with no EOI -> must not be yielded.
        var stream = MultipartChunk(j1).Concat(new byte[] { 0xFF, 0xD8, 0x01, 0x02 }).ToArray();

        var frames = await ReadAll(stream);

        Assert.Single(frames);
        Assert.Equal(j1, frames[0]);
    }

    [Fact]
    public async Task Returns_nothing_for_a_stream_with_no_markers()
    {
        var frames = await ReadAll(System.Text.Encoding.ASCII.GetBytes("no jpeg here at all"));
        Assert.Empty(frames);
    }
}
