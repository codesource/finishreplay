using System.Text;
using FinishReplay.Services.Recording.Mjpeg;
using Xunit;

namespace FinishReplay.Tests;

public class AviMjpegTests
{
    private static byte[] FakeJpeg(byte fill, int payload)
    {
        var body = new byte[payload];
        Array.Fill(body, fill);
        return new byte[] { 0xFF, 0xD8 }.Concat(body).Concat(new byte[] { 0xFF, 0xD9 }).ToArray();
    }

    private static string FourCc(byte[] data, int offset) => Encoding.ASCII.GetString(data, offset, 4);

    [Fact]
    public void Roundtrips_frames_through_writer_and_reader()
    {
        // Odd-length payload exercises word-alignment padding.
        var f1 = FakeJpeg(0x11, 33);
        var f2 = FakeJpeg(0x22, 100);
        var f3 = FakeJpeg(0x33, 7);

        using var ms = new MemoryStream();
        using (var writer = new AviMjpegWriter(ms, fps: 25, width: 320, height: 240))
        {
            writer.AddFrame(f1);
            writer.AddFrame(f2);
            writer.AddFrame(f3);
            writer.Finish();
        }

        var bytes = ms.ToArray();

        // Valid RIFF/AVI container with movi + idx1.
        Assert.Equal("RIFF", FourCc(bytes, 0));
        Assert.Equal("AVI ", FourCc(bytes, 8));
        Assert.Contains("movi", Encoding.ASCII.GetString(bytes));
        Assert.Contains("idx1", Encoding.ASCII.GetString(bytes));

        var read = AviMjpegReader.ReadFrames(new MemoryStream(bytes));

        Assert.Equal(3, read.Count);
        Assert.Equal(f1, read[0]);
        Assert.Equal(f2, read[1]);
        Assert.Equal(f3, read[2]);
    }

    [Fact]
    public void Infers_dimensions_from_first_frame_when_not_provided()
    {
        // Craft a JPEG with an SOF0 segment declaring 64x48.
        var jpeg = new byte[]
        {
            0xFF, 0xD8,                                     // SOI
            0xFF, 0xC0, 0x00, 0x11, 0x08, 0x00, 0x30, 0x00, 0x40, // SOF0: height=0x0030(48), width=0x0040(64)
            0x03, 0x01, 0x22, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01,
            0xFF, 0xD9,                                     // EOI
        };

        Assert.True(JpegInfo.TryGetDimensions(jpeg, out var w, out var h));
        Assert.Equal(64, w);
        Assert.Equal(48, h);

        using var ms = new MemoryStream();
        using (var writer = new AviMjpegWriter(ms, fps: 30))
        {
            writer.AddFrame(jpeg);
            writer.Finish();
        }

        // Reads back the single frame intact.
        var read = AviMjpegReader.ReadFrames(new MemoryStream(ms.ToArray()));
        Assert.Single(read);
        Assert.Equal(jpeg, read[0]);
    }

    [Fact]
    public void Empty_recording_still_produces_a_valid_container()
    {
        using var ms = new MemoryStream();
        using (var writer = new AviMjpegWriter(ms, fps: 30, width: 100, height: 100))
        {
            writer.Finish();
        }

        var read = AviMjpegReader.ReadFrames(new MemoryStream(ms.ToArray()));
        Assert.Empty(read);
    }
}
