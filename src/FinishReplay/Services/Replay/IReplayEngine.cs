namespace FinishReplay.Services.Replay;

/// <summary>
/// Controls playback of a recorded clip: play/pause, frame stepping and position reporting.
/// Implementation is backend-specific (e.g. LibVLCSharp, FFmpeg, or Avalonia media).
///
/// TODO (multi-camera): extend to drive several streams from one master clock, asking
/// <see cref="FinishReplay.Services.Timeline.TimelineEngine.ToCameraTime"/> for each camera's
/// compensated position so latency-different cameras stay frame-aligned during synchronized replay.
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
