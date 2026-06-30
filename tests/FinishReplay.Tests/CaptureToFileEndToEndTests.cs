using System.Net;
using System.Net.Sockets;
using System.Text;
using FinishReplay.Services.Camera.Providers;
using FinishReplay.Services.Camera.Providers.Mjpeg;
using FinishReplay.Services.Recording.Mjpeg;
using Xunit;

namespace FinishReplay.Tests;

/// <summary>End-to-end: live MJPEG HTTP stream -> AVI file on disk -> frames read back.</summary>
public class CaptureToFileEndToEndTests
{
    private static byte[] FakeJpeg(byte fill, int payload)
    {
        var body = new byte[payload];
        Array.Fill(body, fill);
        return new byte[] { 0xFF, 0xD8 }.Concat(body).Concat(new byte[] { 0xFF, 0xD9 }).ToArray();
    }

    private static (int port, Task done) StartMjpegServer(IReadOnlyList<byte[]> frames, CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var done = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync(ct);
            await using var net = client.GetStream();
            var reqBuf = new byte[4096];
            if (await net.ReadAsync(reqBuf, ct) == 0) return;

            await net.WriteAsync(Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\nContent-Type: multipart/x-mixed-replace; boundary=frame\r\nConnection: close\r\n\r\n"), ct);
            foreach (var frame in frames)
            {
                await net.WriteAsync(Encoding.ASCII.GetBytes($"--frame\r\nContent-Type: image/jpeg\r\nContent-Length: {frame.Length}\r\n\r\n"), ct);
                await net.WriteAsync(frame, ct);
                await net.WriteAsync(Encoding.ASCII.GetBytes("\r\n"), ct);
            }
            await net.FlushAsync(ct);
            client.Client.Shutdown(SocketShutdown.Send);
            listener.Stop();
        }, ct);

        return (port, done);
    }

    [Fact]
    public async Task Captures_live_stream_to_avi_and_reads_it_back()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var sent = new List<byte[]> { FakeJpeg(0x10, 60), FakeJpeg(0x20, 31), FakeJpeg(0x30, 90) };
        var (port, serverDone) = StartMjpegServer(sent, cts.Token);

        var device = MjpegCameraProvider.CreateDevice($"http://127.0.0.1:{port}/video");
        await using var stream = new MjpegCameraStream(device);

        var path = Path.Combine(Path.GetTempPath(), $"finishreplay_test_{Guid.NewGuid():N}.avi");
        try
        {
            var written = await CameraStreamAviRecorder.RecordAsync(stream, path, fps: 30, cancellationToken: cts.Token);
            await serverDone;

            Assert.Equal(sent.Count, written);
            Assert.True(File.Exists(path));

            using var file = File.OpenRead(path);
            var read = AviMjpegReader.ReadFrames(file);

            Assert.Equal(sent.Count, read.Count);
            Assert.Equal(sent[0], read[0]);
            Assert.Equal(sent[1], read[1]); // odd-length frame survives padding
            Assert.Equal(sent[2], read[2]);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
