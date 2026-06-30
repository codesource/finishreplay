using FinishReplay.Models;
using FinishReplay.Services.Camera;

namespace FinishReplay.Services.Recording.Mjpeg;

/// <summary>
/// Records a camera's JPEG frames straight into a Motion-JPEG AVI file. Runs until the stream ends
/// or <paramref name="cancellationToken"/> is cancelled (the normal "stop recording" path), then
/// finalizes a valid container. This is the concrete MJPEG recording sink the engine drives.
/// </summary>
public static class CameraStreamAviRecorder
{
    /// <summary>Record <paramref name="stream"/> to <paramref name="filePath"/>; returns frames written.</summary>
    public static async Task<int> RecordAsync(
        ICameraStream stream,
        string filePath,
        double fps = 30,
        CancellationToken cancellationToken = default)
    {
        await using var file = File.Create(filePath);
        using var writer = new AviMjpegWriter(file, fps);

        var count = 0;
        try
        {
            await foreach (var frame in stream.ReadFramesAsync(cancellationToken).ConfigureAwait(false))
            {
                // TODO: transcode non-JPEG (e.g. RTSP/H.264) frames; only JPEG is stored today.
                if (frame.Format != VideoFrameFormat.Jpeg)
                    continue;

                writer.AddFrame(frame.Data);
                count++;
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on stop — fall through and finalize what we captured.
        }

        writer.Finish();
        return count;
    }
}
