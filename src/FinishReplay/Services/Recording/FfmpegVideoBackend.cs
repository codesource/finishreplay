using FinishReplay.Models;

namespace FinishReplay.Services.Recording;

/// <summary>
/// First-pass video backend that shells out to an external <c>ffmpeg</c> process.
/// Currently a structural stub: it wires up the contract and documents the intended
/// approach, but does not yet spawn ffmpeg.
///
/// Intended MVP approach (external process):
///   1. StartPreviewAsync: launch ffmpeg reading the platform capture device
///      (dshow/avfoundation/v4l2) and continuously segment to a ring of short files,
///      OR pipe into an in-memory rolling buffer sized to PreRecord.
///   2. SaveClipAsync: on stop, concatenate [pre-roll buffer] + [live] + [post-roll]
///      into the final clip and remux to mp4.
///
/// TODO: implement process spawn + rolling segment buffer. Keep all ffmpeg argument
///       building in this class so the rest of the app stays backend-agnostic.
/// </summary>
public sealed class FfmpegVideoBackend : IVideoBackend
{
    private readonly string _ffmpegPath;

    public FfmpegVideoBackend(string ffmpegPath = "ffmpeg")
    {
        _ffmpegPath = ffmpegPath;
    }

    // TODO: probe for ffmpeg on PATH (or a bundled binary) and report real availability.
    public bool IsAvailable => false;

    public Task StartPreviewAsync(CameraDevice camera, CancellationToken ct = default)
    {
        // TODO: spawn ffmpeg capture + start rolling pre-record buffer.
        return Task.CompletedTask;
    }

    public Task StopPreviewAsync()
    {
        // TODO: tear down the ffmpeg capture process.
        return Task.CompletedTask;
    }

    public Task<string> SaveClipAsync(string outputPath, TimeSpan preRoll, TimeSpan postRoll, CancellationToken ct = default)
    {
        // TODO: assemble pre-roll + live + post-roll and mux to mp4 at outputPath.
        return Task.FromResult(outputPath);
    }
}
