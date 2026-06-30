using FinishReplay.Models;

namespace FinishReplay.Services.Recording;

/// <summary>
/// Abstraction over the concrete video capture/encoding implementation.
/// The recording engine talks only to this interface, so the backend
/// (FFmpeg process, FFmpeg.AutoGen, Media Foundation, ...) can be swapped freely.
/// </summary>
public interface IVideoBackend
{
    /// <summary>True once a backend dependency (e.g. ffmpeg on PATH) is available.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Begin live capture + preview from <paramref name="camera"/> and start filling the
    /// rolling pre-record buffer. Does not yet persist a clip.
    /// </summary>
    Task StartPreviewAsync(CameraDevice camera, CancellationToken ct = default);

    Task StopPreviewAsync();

    /// <summary>
    /// Persist a clip to <paramref name="outputPath"/> spanning from <paramref name="preRoll"/>
    /// before the start trigger through <paramref name="postRoll"/> after the stop trigger.
    /// </summary>
    /// <returns>The path actually written.</returns>
    Task<string> SaveClipAsync(string outputPath, TimeSpan preRoll, TimeSpan postRoll, CancellationToken ct = default);
}
