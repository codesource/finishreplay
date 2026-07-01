namespace FinishReplay.Models;

/// <summary>How RTSP/USB camera capture runs its FFmpeg.</summary>
public enum VideoBackend
{
    /// <summary>Shell out to the ffmpeg executable (simple, isolated, the default).</summary>
    ExternalProcess,

    /// <summary>
    /// Embedded in-process FFmpeg (libav) hosted in a separate media-worker process, so a codec
    /// crash can't take down the app. Falls back to <see cref="ExternalProcess"/> if the worker
    /// isn't available.
    /// </summary>
    IsolatedWorker,
}
