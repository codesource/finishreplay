using FinishReplay.Models;

namespace FinishReplay.Services.Recording;

public enum RecordingState
{
    Idle,
    Previewing,
    Recording,
    Stopping,
}

/// <summary>
/// Orchestrates live preview and recording, including the pre-record rolling buffer
/// and post-record tail. Backend-agnostic: delegates actual capture to <see cref="IVideoBackend"/>.
/// </summary>
public interface IRecordingEngine
{
    RecordingState State { get; }

    /// <summary>Rolling buffer kept ahead of a start trigger.</summary>
    TimeSpan PreRecord { get; set; }

    /// <summary>Tail kept after a stop trigger.</summary>
    TimeSpan PostRecord { get; set; }

    event EventHandler<RecordingState>? StateChanged;

    Task StartPreviewAsync(CameraDevice camera);

    /// <summary>Begin recording (manual or triggered). Includes the pre-record buffer.</summary>
    Task StartRecordingAsync();

    /// <summary>Stop recording after the post-record tail and flush the clip to disk.</summary>
    /// <returns>The written clip path, or null if nothing was recorded.</returns>
    Task<string?> StopRecordingAsync(string outputPath);
}
