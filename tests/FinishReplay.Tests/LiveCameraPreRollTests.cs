using System.Net;
using System.Net.Sockets;
using System.Text;
using FinishReplay.Models;
using FinishReplay.Services.Camera;
using FinishReplay.Services.Camera.Providers;
using FinishReplay.Services.Recording.Mjpeg;
using Xunit;

namespace FinishReplay.Tests;

public class LiveCameraPreRollTests
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
    public async Task Recording_started_after_the_stream_still_captures_the_pre_roll_buffer()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var sent = new List<byte[]> { FakeJpeg(0x11, 40), FakeJpeg(0x22, 41), FakeJpeg(0x33, 60), FakeJpeg(0x44, 25) };
        var (port, serverDone) = StartMjpegServer(sent, cts.Token);

        var registry = new CameraProviderRegistry(new ICameraProvider[] { new MjpegCameraProvider() });
        var profile = new CameraProfile
        {
            Id = "cam",
            SourceType = MjpegCameraProvider.Type,
            SourceUrl = $"http://127.0.0.1:{port}/video",
        };

        // Large pre-roll window so all streamed frames stay buffered.
        var live = new LiveCamera(registry, profile) { PreRecordSeconds = 100 };
        live.Start();

        var path = Path.Combine(Path.GetTempPath(), $"finishreplay_preroll_{Guid.NewGuid():N}.avi");
        try
        {
            // Wait until every frame is buffered (and the server has finished).
            while (live.BufferedFrameCount < sent.Count && !cts.IsCancellationRequested)
                await Task.Delay(20, cts.Token);
            await serverDone;

            // Start recording AFTER the stream ended: only the pre-roll buffer can supply frames.
            live.StartRecording(path, fps: 30, RecordingMode.Transcode, postRecordSeconds: 0);
            var written = await live.StopRecordingAsync();
            await live.DisposeAsync();

            Assert.Equal(sent.Count, written);

            using var file = File.OpenRead(path);
            var recorded = AviMjpegReader.ReadFrames(file);
            Assert.Equal(sent, recorded); // the clip is exactly the buffered pre-roll
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
