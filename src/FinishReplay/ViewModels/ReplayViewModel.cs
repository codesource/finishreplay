using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FinishReplay.Models;
using FinishReplay.Services.Camera.Providers.Ffmpeg;
using FinishReplay.Services.Recording.Mjpeg;
using FinishReplay.Services.Session;
using FinishReplay.Services.Timeline;

namespace FinishReplay.ViewModels;

/// <summary>
/// Replay screen. Browse sessions (newest first); loading one lists its cameras and markers and
/// reads each camera's recorded frames. Several cameras can be selected and played together under a
/// single master clock, each shifted by its sync offset so the views stay aligned. Markers render as
/// clickable ticks on the timeline (and a list); clicking one seeks the master clock so every camera
/// jumps to that moment.
/// </summary>
public partial class ReplayViewModel : ViewModelBase
{
    private readonly ISessionManager _sessionManager;
    private readonly TimelineEngine _timelineEngine;
    private readonly Func<string> _ffmpegPath;
    private readonly DispatcherTimer _clock;

    private double _fps = 30;
    private TimeSpan _frameStep = TimeSpan.FromSeconds(1.0 / 30.0);

    public ReplayViewModel(ISessionManager sessionManager, TimelineEngine timelineEngine, Func<string>? ffmpegPath = null)
    {
        _sessionManager = sessionManager;
        _timelineEngine = timelineEngine;
        _ffmpegPath = ffmpegPath ?? (() => "ffmpeg");

        _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.0 / 30.0) };
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

        var sessionDir = Path.GetDirectoryName(session.MetadataFilePath) ?? "";
        _fps = metadata.Cameras.FirstOrDefault()?.Fps is > 0 and var f ? f : 30;
        _frameStep = TimeSpan.FromSeconds(1.0 / _fps);

        _timelineEngine.SetCameraOffsets(metadata.Cameras.Select(c => new CameraSyncOffset
        {
            CameraId = c.CameraId,
            OffsetMs = c.SyncOffsetMs,
            Source = "calibrated",
        }));

        var maxFrames = 0;
        foreach (var c in metadata.Cameras)
        {
            var vm = new ReplayCameraViewModel(c);
            var frames = await ReadFramesAsync(Path.Combine(sessionDir, c.VideoFile));
            vm.LoadFrames(frames, c.Fps);
            maxFrames = Math.Max(maxFrames, frames.Count);
            Cameras.Add(vm);
        }

        Duration = maxFrames > 0 ? TimeSpan.FromSeconds(maxFrames / _fps) : TimeSpan.Zero;
        Position = TimeSpan.Zero;

        foreach (var m in metadata.TimingMarkers)
            Markers.Add(new ReplayMarkerViewModel(m, FractionFor(m.VideoTime)));

        UpdateCameraTimes();
        HasSession = true;
    }

    [RelayCommand]
    private void PlayPause()
    {
        if (!HasSession || Duration <= TimeSpan.Zero) return;

        if (IsPlaying)
        {
            Pause();
        }
        else
        {
            if (Position >= Duration)
                Seek(TimeSpan.Zero);
            IsPlaying = true;
            _clock.Start();
        }
    }

    [RelayCommand]
    private void StepForward() { Pause(); Seek(Position + _frameStep); }

    [RelayCommand]
    private void StepBackward() { Pause(); Seek(Position - _frameStep); }

    /// <summary>Seek the master clock to a marker; every selected camera jumps with it.</summary>
    [RelayCommand]
    private void JumpToMarker(ReplayMarkerViewModel? marker)
    {
        if (marker is null) return;
        Pause();
        Seek(marker.VideoTime);
    }

    private void Pause()
    {
        _clock.Stop();
        IsPlaying = false;
    }

    private void Seek(TimeSpan target)
    {
        var clamped = target < TimeSpan.Zero ? TimeSpan.Zero : target > Duration ? Duration : target;
        Position = clamped;
        UpdateCameraTimes();
    }

    private void OnClockTick()
    {
        var next = Position + _frameStep;
        if (next >= Duration)
        {
            Seek(Duration);
            Pause();
        }
        else
        {
            Seek(next);
        }
    }

    private void UpdateCameraTimes()
    {
        foreach (var cam in Cameras)
            cam.UpdateTime(Position);
    }

    /// <summary>
    /// Load a clip's frames for replay. AVI (MJPEG) is read directly; MP4/H.264 passthrough clips are
    /// decoded back to JPEG frames via ffmpeg (whole clip buffered — fine for short clips).
    /// </summary>
    private async Task<IReadOnlyList<byte[]>> ReadFramesAsync(string path)
    {
        try
        {
            if (!File.Exists(path))
                return Array.Empty<byte[]>();

            if (path.EndsWith(".avi", StringComparison.OrdinalIgnoreCase))
            {
                using var file = File.OpenRead(path);
                return AviMjpegReader.ReadFrames(file);
            }

            // MP4 (or other ffmpeg-decodable) → transcode to MJPEG frames for rendering.
            var exe = FfmpegLocator.Resolve(_ffmpegPath());
            if (exe is null)
                return Array.Empty<byte[]>();

            var device = new CameraDevice(path, path);
            var args = FfmpegArguments.ForFileToMjpeg(path);
            await using var stream = new FfmpegMjpegProcessStream(device, _ => new FfmpegProcess(exe, args));

            var frames = new List<byte[]>();
            await foreach (var frame in stream.ReadFramesAsync())
                frames.Add(frame.Data);
            return frames;
        }
        catch
        {
            return Array.Empty<byte[]>();
        }
    }

    private double FractionFor(TimeSpan videoTime) =>
        Duration > TimeSpan.Zero ? Math.Clamp(videoTime.TotalSeconds / Duration.TotalSeconds, 0, 1) : 0;
}
