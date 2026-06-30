using FinishReplay.Models;

namespace FinishReplay.Services.Replay;

/// <summary>
/// MVP replay engine. Tracks position/duration purely in memory so the UI (timeline,
/// frame stepping, markers, seek) is fully interactive without a real decoder yet.
///
/// TODO: back this with an actual media engine and surface real frames in the preview:
///   - decode + present video frames,
///   - drive PositionChanged from the decoder clock,
///   - probe Duration and FrameDuration from the file instead of metadata defaults.
/// </summary>
public sealed class ReplayEngine : IReplayEngine
{
    private TimeSpan _position;

    public TimeSpan Position
    {
        get => _position;
        private set
        {
            var clamped = Clamp(value, TimeSpan.Zero, Duration);
            if (clamped == _position) return;
            _position = clamped;
            PositionChanged?.Invoke(this, clamped);
        }
    }

    public TimeSpan Duration { get; private set; }
    public bool IsPlaying { get; private set; }

    // TODO: probe real frame rate; 30fps is a reasonable placeholder for stepping.
    public TimeSpan FrameDuration { get; private set; } = TimeSpan.FromSeconds(1.0 / 30.0);

    public event EventHandler<TimeSpan>? PositionChanged;
    public event EventHandler? Loaded;

    public Task LoadAsync(string videoPath)
    {
        // TODO: open the file with the real backend and probe Duration/FrameDuration.
        // Placeholder duration keeps the timeline usable for UI development.
        Duration = TimeSpan.FromSeconds(30);
        Position = TimeSpan.Zero;
        IsPlaying = false;
        Loaded?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public void Play() => IsPlaying = true;   // TODO: start decoder clock.
    public void Pause() => IsPlaying = false; // TODO: pause decoder clock.

    public void StepForward()
    {
        Pause();
        Position += FrameDuration;
    }

    public void StepBackward()
    {
        Pause();
        Position -= FrameDuration;
    }

    public void Seek(TimeSpan position) => Position = position;

    private static TimeSpan Clamp(TimeSpan value, TimeSpan min, TimeSpan max)
        => value < min ? min : value > max ? max : value;
}
