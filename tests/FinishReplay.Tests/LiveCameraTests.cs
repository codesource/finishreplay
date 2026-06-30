using System.Net;
using System.Net.Sockets;
using System.Text;
using FinishReplay.Models;
using FinishReplay.Services.Camera;
using FinishReplay.Services.Camera.Providers;
using FinishReplay.Services.Recording.Mjpeg;
using Xunit;

namespace FinishReplay.Tests;

/// <summary>Verifies the LiveCamera controller the UI drives: it both raises preview frames and
/// tees the same frames into a recorded AVI, from a live HTTP MJPEG stream.</summary>
public class LiveCameraTests
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
    public async Task Raises_preview_frames_and_records_them_to_avi()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var sent = new List<byte[]> { FakeJpeg(0x10, 50), FakeJpeg(0x20, 51), FakeJpeg(0x30, 77) };
        var (port, serverDone) = StartMjpegServer(sent, cts.Token);

        var registry = new CameraProviderRegistry(new ICameraProvider[] { new MjpegCameraProvider() });
        var profile = new CameraProfile
        {
            Id = "cam-a",
            DisplayName = "Cam A",
            SourceType = MjpegCameraProvider.Type,
            SourceUrl = $"http://127.0.0.1:{port}/video",
        };

        var previewFrames = new System.Collections.Concurrent.ConcurrentQueue<byte[]>();
        var live = new LiveCamera(registry, profile);
        live.FrameReady += previewFrames.Enqueue;

        var path = Path.Combine(Path.GetTempPath(), $"finishreplay_live_{Guid.NewGuid():N}.avi");
        try
        {
            live.StartRecording(path, fps: 30, FinishReplay.Models.RecordingMode.Transcode);
            live.Start();

            // Wait for the controller to drain the (finite) stream before stopping it.
            while (previewFrames.Count < sent.Count && !cts.IsCancellationRequested)
                await Task.Delay(20, cts.Token);

            live.StopRecording();       // finalize the AVI while frames are captured
            await live.DisposeAsync();  // stop the read loop
            await serverDone;

            // Preview path saw every frame...
            Assert.Equal(sent.Count, previewFrames.Count);
            Assert.Equal(sent[0], previewFrames.First());

            // ...and the recording path wrote a real, readable AVI with the same frames.
            using var file = File.OpenRead(path);
            var recorded = AviMjpegReader.ReadFrames(file);
            Assert.Equal(sent, recorded);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
