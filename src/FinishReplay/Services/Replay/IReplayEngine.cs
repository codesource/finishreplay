namespace FinishReplay.Services.Replay;

/// <summary>
/// Controls playback of a single recorded clip: play/pause, frame stepping and position reporting.
///
/// NOTE: currently unused. Replay is driven by <see cref="FinishReplay.ViewModels.ReplayViewModel"/>,
/// which runs its own master clock and renders decoded JPEG frames (from <c>AviMjpegReader</c>) per
/// camera, compensating each by its sync offset. Kept as the seam for a future H.264/RTSP decoder
/// backend; wire it in behind the view model when real decoding is added.
/// </summary>
public interface IReplayEngine
{
    TimeSpan Position { get; }
    TimeSpan Duration { get; }
    bool IsPlaying { get; }

    /// <summary>Nominal frame duration used for frame-step (defaults to ~30fps until probed).</summary>
    TimeSpan FrameDuration { get; }

    event EventHandler<TimeSpan>? PositionChanged;
    event EventHandler? Loaded;

    Task LoadAsync(string videoPath);
    void Play();
    void Pause();
    void StepForward();
    void StepBackward();
    void Seek(TimeSpan position);
}
