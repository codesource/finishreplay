using System.Net;
using System.Net.Sockets;
using System.Text;
using FinishReplay.Models;
using FinishReplay.Services.Camera.Providers;
using FinishReplay.Services.Camera.Providers.Mjpeg;
using Xunit;

namespace FinishReplay.Tests;

public class MjpegCameraStreamTests
{
    private static byte[] FakeJpeg(byte fill, int payload)
    {
        var body = new byte[payload];
        Array.Fill(body, fill);
        return new byte[] { 0xFF, 0xD8 }.Concat(body).Concat(new byte[] { 0xFF, 0xD9 }).ToArray();
    }

    /// <summary>
    /// Minimal TCP server (no HttpListener URL-ACL needed) that answers one request with an MJPEG
    /// multipart body of the given frames, then closes the connection.
    /// </summary>
    private static (int port, Task done) StartMjpegServer(IReadOnlyList<byte[]> frames, CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var done = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync(ct);
            await using var net = client.GetStream();

            // Read the request headers (until blank line) so the client's GET completes.
            var reqBuf = new byte[4096];
            await net.ReadAsync(reqBuf, ct);

            var header =
                "HTTP/1.1 200 OK\r\n" +
                "Content-Type: multipart/x-mixed-replace; boundary=frame\r\n" +
                "Connection: close\r\n\r\n";
            await net.WriteAsync(Encoding.ASCII.GetBytes(header), ct);

            foreach (var frame in frames)
            {
                var part = Encoding.ASCII.GetBytes($"--frame\r\nContent-Type: image/jpeg\r\nContent-Length: {frame.Length}\r\n\r\n");
                await net.WriteAsync(part, ct);
                await net.WriteAsync(frame, ct);
                await net.WriteAsync(Encoding.ASCII.GetBytes("\r\n"), ct);
            }

            await net.FlushAsync(ct);
            client.Client.Shutdown(SocketShutdown.Send); // signal end-of-body to the client
            listener.Stop();
        }, ct);

        return (port, done);
    }

    [Fact]
    public async Task Reads_frames_from_a_live_http_mjpeg_stream()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var sent = new List<byte[]> { FakeJpeg(0xA1, 40), FakeJpeg(0xB2, 80), FakeJpeg(0xC3, 24) };
        var (port, serverDone) = StartMjpegServer(sent, cts.Token);

        var device = MjpegCameraProvider.CreateDevice($"http://127.0.0.1:{port}/video");
        await using var stream = new MjpegCameraStream(device);

        var received = new List<VideoFrame>();
        await foreach (var frame in stream.ReadFramesAsync(cts.Token))
        {
            received.Add(frame);
            if (received.Count == sent.Count)
                break;
        }

        Assert.Equal(sent.Count, received.Count);
        Assert.All(received, f => Assert.Equal(VideoFrameFormat.Jpeg, f.Format));
        Assert.Equal(sent[0], received[0].Data);
        Assert.Equal(sent[2], received[2].Data);
        Assert.Equal(0, received[0].SequenceNumber);
        Assert.Equal(2, received[2].SequenceNumber);

        await serverDone;
    }
}
