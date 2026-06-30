namespace FinishReplay.Models;

/// <summary>How a camera's clip is written to disk.</summary>
public enum RecordingMode
{
    /// <summary>Re-encode frames to Motion-JPEG in an AVI (works for every source; in-app editable).</summary>
    Transcode,

    /// <summary>
    /// Copy the original encoded stream losslessly to MP4 (archival quality, no re-encode).
    /// Applies to already-encoded sources such as RTSP/H.264; other sources fall back to transcode.
    /// </summary>
    Passthrough,
}
