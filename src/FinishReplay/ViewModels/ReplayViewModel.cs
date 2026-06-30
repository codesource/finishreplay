using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FinishReplay.Models;
using FinishReplay.Services.Replay;
using FinishReplay.Services.Session;
using FinishReplay.Services.Timeline;

namespace FinishReplay.ViewModels;

/// <summary>
/// Replay screen. Browse sessions (newest first); loading one shows its markers and cameras. Several
/// cameras can be selected and played at once — a single master clock drives them and each camera is
/// time-shifted by its sync offset so they stay aligned. Markers are drawn on the timeline and in a
/// list; clicking one seeks the master clock so every camera jumps together.
/// </summary>
public partial class ReplayViewModel : ViewModelBase
{
    private static readonly TimeSpan FrameInterval = TimeSpan.FromSeconds(1.0 / 30.0);

    private readonly IReplayEngine _replayEngine;
    private readonly ISessionManager _sessionManager;
    private readonly TimelineEngine _timelineEngine;
    private readonly DispatcherTimer _clock;

    public ReplayViewModel(
        IReplayEngine replayEngine,
        ISessionManager sessionManager,
        TimelineEngine timelineEngine)
    {
        _replayEngine = replayEngine;
        _sessionManager = sessionManager;
        _timelineEngine = timelineEngine;

        _replayEngine.PositionChanged += (_, pos) => OnEnginePosition(pos);

        // Master clock: advances the engine position while playing so all cameras move together.
        _clock = new DispatcherTimer { Interval = FrameInterval };
        _clock.Tick += (_, _) => OnClockTick();

        SessionsDirectory = AppSettings.DefaultStorageDirectory;
    }

    public string SessionsDirectory { get; private set; }

    public ObservableCollection<SessionInfo> RecentSessions { get; } = new();
    public ObservableCollection<ReplayCameraViewModel> Cameras { get; } = new();
    public ObservableCollection<ReplayMarkerViewModel> Markers { get; } = new();

    [ObservableProperty] private SessionInfo? _selectedSession;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PositionText))]
    [NotifyPropertyChangedFor(nameof(PositionFraction))]
    private TimeSpan _position;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DurationText))]
    [NotifyPropertyChangedFor(nameof(PositionFraction))]
    private TimeSpan _duration;

    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private bool _hasSession;

    public string PositionText => Position.ToString(@"mm\:ss\.fff");
    public string DurationText => Duration.ToString(@"mm\:ss\.fff");
    public double PositionFraction =>
        Duration > TimeSpan.Zero ? Position.TotalSeconds / Duration.TotalSeconds : 0;

    /// <summary>Refresh the session list from disk (newest first).</summary>
    public async Task RefreshSessionsAsync()
    {
        SessionsDirectory = AppSettings.DefaultStorageDirectory;
        RecentSessions.Clear();
        foreach (var s in _sessionManager.GetRecentSessions(SessionsDirectory))
            RecentSessions.Add(s);
        await Task.CompletedTask;
    }

    partial void OnSelectedSessionChanged(SessionInfo? value)
    {
        if (value is not null)
            _ = LoadSessionAsync(value);
    }

    private async Task LoadSessionAsync(SessionInfo session)
    {
        Pause();

        var metadata = await _sessionManager.LoadAsync(session.MetadataFilePath);

        Cameras.Clear();
        Markers.Clear();
        _timelineEngine.Clear();

        if (metadata is null)
        {
            HasSession = false;
            return;
        }

        // Load the reference (first) camera into the engine to establish the master duration.
        var sessionDir = Path.GetDirectoryName(session.MetadataFilePath) ?? "";
        var primary = metadata.Cameras.FirstOrDefault();
        await _replayEngine.LoadAsync(primary is not null ? Path.Combine(sessionDir, primary.VideoFile) : "");

        Duration = _replayEngine.Duration;
        Position = TimeSpan.Zero;

        _timelineEngine.SetCameraOffsets(metadata.Cameras.Select(c => new CameraSyncOffset
        {
            CameraId = c.CameraId,
            OffsetMs = c.SyncOffsetMs,
            Source = "calibrated",
        }));

        foreach (var c in metadata.Cameras)
            Cameras.Add(new ReplayCameraViewModel(c));

        foreach (var m in metadata.TimingMarkers)
            Markers.Add(new ReplayMarkerViewModel(m, FractionFor(m.VideoTime)));

        UpdateCameraTimes();
        HasSession = true;
    }

    [RelayCommand]
    private void PlayPause()
    {
        if (!HasSession) return;

        if (_replayEngine.IsPlaying)
        {
            Pause();
        }
        else
        {
            if (Position >= Duration)
                _replayEngine.Seek(TimeSpan.Zero);
            _replayEngine.Play();
            _clock.Start();
            IsPlaying = true;
        }
    }

    [RelayCommand]
    private void StepForward()
    {
        Pause();
        _replayEngine.StepForward();
    }

    [RelayCommand]
    private void StepBackward()
    {
        Pause();
        _replayEngine.StepBackward();
    }

    /// <summary>Seek the master clock to a marker; every selected camera jumps with it.</summary>
    [RelayCommand]
    private void JumpToMarker(ReplayMarkerViewModel? marker)
    {
        if (marker is null) return;
        Pause();
        _replayEngine.Seek(marker.VideoTime);
    }

    private void Pause()
    {
        _clock.Stop();
        _replayEngine.Pause();
        IsPlaying = false;
    }

    private void OnClockTick()
    {
        if (!_replayEngine.IsPlaying) return;

        var next = _replayEngine.Position + FrameInterval;
        if (next >= Duration)
        {
            _replayEngine.Seek(Duration);
            Pause();
        }
        else
        {
            _replayEngine.Seek(next);
        }
    }

    private void OnEnginePosition(TimeSpan pos)
    {
        Position = pos;
        UpdateCameraTimes();
    }

    private void UpdateCameraTimes()
    {
        foreach (var cam in Cameras)
            cam.UpdateTime(Position);
    }

    private double FractionFor(TimeSpan videoTime) =>
        Duration > TimeSpan.Zero ? Math.Clamp(videoTime.TotalSeconds / Duration.TotalSeconds, 0, 1) : 0;
}
