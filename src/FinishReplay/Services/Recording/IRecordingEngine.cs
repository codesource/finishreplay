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
/// Orchestrates live preview and recording across <em>all</em> selected cameras at once, including
/// the pre-record rolling buffer and post-record tail. Backend-agnostic: delegates actual capture
/// to <see cref="IVideoBackend"/>.
/// </summary>
public interface IRecordingEngine
{
    RecordingState State { get; }

    /// <summary>Rolling buffer kept ahead of a start trigger.</summary>
    TimeSpan PreRecord { get; set; }

    /// <summary>Tail kept after a stop trigger.</summary>
    TimeSpan PostRecord { get; set; }

    event EventHandler<RecordingState>? StateChanged;

    /// <summary>Start recording every camera in <paramref name="cameras"/> simultaneously.</summary>
    Task StartAsync(IReadOnlyList<CameraDevice> cameras);

    /// <summary>Stop recording all cameras after the post-record tail and flush clips.</summary>
    Task StopAsync();
}
