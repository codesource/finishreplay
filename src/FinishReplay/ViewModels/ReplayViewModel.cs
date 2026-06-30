using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FinishReplay.Models;
using FinishReplay.Services.Replay;
using FinishReplay.Services.Session;
using FinishReplay.Services.Timeline;

namespace FinishReplay.ViewModels;

/// <summary>
/// Replay screen: play/pause, frame stepping, a scrubbable timeline, timing markers
/// drawn on the bar and a marker list that jumps playback to the selected moment.
/// </summary>
public partial class ReplayViewModel : ViewModelBase
{
    private readonly IReplayEngine _replayEngine;
    private readonly ISessionManager _sessionManager;
    private readonly TimelineEngine _timelineEngine;

    public ReplayViewModel(
        IReplayEngine replayEngine,
        ISessionManager sessionManager,
        TimelineEngine timelineEngine)
    {
        _replayEngine = replayEngine;
        _sessionManager = sessionManager;
        _timelineEngine = timelineEngine;

        _replayEngine.PositionChanged += (_, pos) => HandleEnginePosition(pos);
        _replayEngine.Loaded += (_, _) => HandleEngineLoaded();

        SessionsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            "FinishReplay");
    }

    public string SessionsDirectory { get; }

    public ObservableCollection<SessionInfo> RecentSessions { get; } = new();
    public ObservableCollection<TimingTrigger> Markers { get; } = new();

    /// <summary>Cameras in the open session, with their computed sync offsets.</summary>
    public ObservableCollection<SessionCamera> Cameras { get; } = new();

    [ObservableProperty]
    private SessionInfo? _selectedSession;

    [ObservableProperty]
    private TimingTrigger? _selectedMarker;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PositionText))]
    [NotifyPropertyChangedFor(nameof(PositionFraction))]
    private TimeSpan _position;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DurationText))]
    [NotifyPropertyChangedFor(nameof(PositionFraction))]
    private TimeSpan _duration;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private bool _hasSession;

    public string PositionText => Format(Position);
    public string DurationText => Format(Duration);

    /// <summary>0..1 progress for the timeline bar.</summary>
    public double PositionFraction =>
        Duration > TimeSpan.Zero ? Position.TotalSeconds / Duration.TotalSeconds : 0;

    public async Task RefreshSessionsAsync()
    {
        RecentSessions.Clear();
        foreach (var s in _sessionManager.GetRecentSessions(SessionsDirectory))
            RecentSessions.Add(s);

        await Task.CompletedTask;
    }

    partial void OnSelectedSessionChanged(SessionInfo? value)
    {
        if (value is not null)
            _ = OpenSessionAsync(value);
    }

    private async Task OpenSessionAsync(SessionInfo session)
    {
        var metadata = await _sessionManager.LoadAsync(session.MetadataFilePath);

        Markers.Clear();
        Cameras.Clear();
        _timelineEngine.Clear();

        if (metadata is not null)
        {
            // Master timeline plays the reference camera; others are offset-compensated.
            var sessionDir = Path.GetDirectoryName(session.MetadataFilePath) ?? "";
            var primary = metadata.Cameras.FirstOrDefault();
            var videoPath = primary is not null
                ? Path.Combine(sessionDir, primary.VideoFile)
                : "";

            await _replayEngine.LoadAsync(videoPath);

            _timelineEngine.Set(metadata.TimingMarkers);
            _timelineEngine.SetCameraOffsets(metadata.Cameras.Select(c => new CameraSyncOffset
            {
                CameraId = c.CameraId,
                OffsetMs = c.SyncOffsetMs,
                Source = "calibrated",
            }));

            foreach (var m in metadata.TimingMarkers)
                Markers.Add(m);
            foreach (var c in metadata.Cameras)
                Cameras.Add(c);
        }
        else
        {
            await _replayEngine.LoadAsync("");
        }

        HasSession = true;
    }

    [RelayCommand]
    private void PlayPause()
    {
        if (_replayEngine.IsPlaying)
            _replayEngine.Pause();
        else
            _replayEngine.Play();

        IsPlaying = _replayEngine.IsPlaying;
    }

    [RelayCommand]
    private void StepForward()
    {
        _replayEngine.StepForward();
        IsPlaying = _replayEngine.IsPlaying;
    }

    [RelayCommand]
    private void StepBackward()
    {
        _replayEngine.StepBackward();
        IsPlaying = _replayEngine.IsPlaying;
    }

    [RelayCommand]
    private void JumpToMarker(TimingTrigger? marker)
    {
        if (marker is null) return;
        _replayEngine.Seek(marker.VideoTime);
    }

    /// <summary>Fraction (0..1) of a marker along the timeline, for positioning its tick.</summary>
    public double MarkerFraction(TimingTrigger marker) => TimelineEngine.ToFraction(marker, Duration);

    private void HandleEnginePosition(TimeSpan pos)
    {
        Position = pos;
        IsPlaying = _replayEngine.IsPlaying;
    }

    private void HandleEngineLoaded()
    {
        Duration = _replayEngine.Duration;
        Position = _replayEngine.Position;
        IsPlaying = _replayEngine.IsPlaying;
    }

    private static string Format(TimeSpan t) => t.ToString(@"mm\:ss\.fff");
}
