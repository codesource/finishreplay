using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FinishReplay.Models;
using FinishReplay.Services.Calibration;
using FinishReplay.Services.Camera;
using FinishReplay.Services.Camera.Providers;
using FinishReplay.Services.Recording;
using FinishReplay.Services.Session;
using FinishReplay.Services.Timing;

namespace FinishReplay.ViewModels;

/// <summary>
/// Main/recording screen: multi-camera selection (USB/MJPEG/RTSP), live preview placeholder,
/// manual + triggered recording, pre/post-record settings, TimY3 status, latency calibration
/// and recent sessions.
/// </summary>
public partial class RecordingViewModel : ViewModelBase
{
    private readonly ICameraManager _cameraManager;
    private readonly IRecordingEngine _recordingEngine;
    private readonly ISessionManager _sessionManager;
    private readonly ManualTimingProvider _manualTiming;
    private readonly ICameraLatencyCalibrationService _calibrationService;

    private SessionMetadata? _currentSession;

    public RecordingViewModel(
        ICameraManager cameraManager,
        IRecordingEngine recordingEngine,
        ISessionManager sessionManager,
        ManualTimingProvider manualTiming,
        ICameraLatencyCalibrationService calibrationService)
    {
        _cameraManager = cameraManager;
        _recordingEngine = recordingEngine;
        _sessionManager = sessionManager;
        _manualTiming = manualTiming;
        _calibrationService = calibrationService;

        _recordingEngine.StateChanged += (_, state) => OnRecordingStateChanged(state);
        _manualTiming.TriggerReceived += (_, trigger) => OnTriggerReceived(trigger);

        SessionsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            "FinishReplay");

        AvailableSourceTypes = new[] { MjpegCameraProvider.Type, RtspCameraProvider.Type };
        NewCameraType = MjpegCameraProvider.Type;

        _ = DiscoverCameras();
    }

    /// <summary>Where clips and their metadata are written.</summary>
    public string SessionsDirectory { get; }

    public ObservableCollection<CameraProfileRowViewModel> Cameras { get; } = new();
    public ObservableCollection<TimingTrigger> Markers { get; } = new();

    public IReadOnlyList<string> AvailableSourceTypes { get; }

    [ObservableProperty]
    private string _newCameraType;

    [ObservableProperty]
    private string _newCameraUrl = "";

    [ObservableProperty]
    private double _preRecordSeconds = 5;

    [ObservableProperty]
    private double _postRecordSeconds = 3;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartRecording))]
    [NotifyPropertyChangedFor(nameof(CanStopRecording))]
    private bool _isRecording;

    [ObservableProperty]
    private string _statusText = "Idle";

    [ObservableProperty]
    private bool _isTimingConnected;

    [ObservableProperty]
    private string _timingStatusText = "TimY3: not connected";

    [ObservableProperty]
    private bool _isCalibrating;

    [ObservableProperty]
    private string _calibrationSummary = "";

    public bool CanStartRecording => SelectedCameras.Count > 0 && !IsRecording;
    public bool CanStopRecording => IsRecording;

    private List<CameraProfileRowViewModel> SelectedCameras =>
        Cameras.Where(c => c.IsSelected).ToList();

    [RelayCommand]
    private async Task DiscoverCameras()
    {
        var devices = await _cameraManager.DiscoverAsync();

        // Keep manually-added network cameras; refresh the discovered (USB) ones.
        var manual = Cameras.Where(c => c.SourceType is MjpegCameraProvider.Type or RtspCameraProvider.Type).ToList();
        Cameras.Clear();
        foreach (var device in devices)
            Cameras.Add(new CameraProfileRowViewModel(ToProfile(device)));
        foreach (var m in manual)
            Cameras.Add(m);

        OnPropertyChanged(nameof(CanStartRecording));
    }

    [RelayCommand]
    private void AddCamera()
    {
        if (string.IsNullOrWhiteSpace(NewCameraUrl))
            return;

        var device = NewCameraType == RtspCameraProvider.Type
            ? RtspCameraProvider.CreateDevice(NewCameraUrl)
            : MjpegCameraProvider.CreateDevice(NewCameraUrl);

        Cameras.Add(new CameraProfileRowViewModel(ToProfile(device)));
        NewCameraUrl = "";
        OnPropertyChanged(nameof(CanStartRecording));
    }

    [RelayCommand]
    private void RemoveCamera(CameraProfileRowViewModel? row)
    {
        if (row is not null)
            Cameras.Remove(row);
        OnPropertyChanged(nameof(CanStartRecording));
    }

    [RelayCommand]
    private async Task CalibrateLatency()
    {
        var selected = SelectedCameras;
        if (selected.Count == 0 || IsCalibrating)
            return;

        IsCalibrating = true;
        CalibrationSummary = "Calibrating…";
        try
        {
            var profiles = selected.Select(c => c.Profile).ToList();
            var result = await _calibrationService.CalibrateAsync(profiles, new CalibrationSettings());

            foreach (var camResult in result.CameraResults)
            {
                var row = selected.FirstOrDefault(c => c.Id == camResult.CameraId);
                row?.ApplyCalibration(camResult.LatencyMs, camResult.Confidence, camResult.Success, camResult.ErrorMessage);
            }

            ApplySyncOffsets(selected);
            CalibrationSummary = BuildCalibrationSummary(selected);
        }
        finally
        {
            IsCalibrating = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartRecording))]
    private async Task StartRecording()
    {
        var selected = SelectedCameras;
        if (selected.Count == 0) return;

        Directory.CreateDirectory(SessionsDirectory);

        _recordingEngine.PreRecord = TimeSpan.FromSeconds(PreRecordSeconds);
        _recordingEngine.PostRecord = TimeSpan.FromSeconds(PostRecordSeconds);

        var sessionId = $"session_{DateTime.Now:yyyyMMdd_HHmmss}";
        _currentSession = _sessionManager.CreateSession(
            sessionId,
            _recordingEngine.PreRecord,
            _recordingEngine.PostRecord,
            _manualTiming.Name);

        ApplySyncOffsets(selected);
        _currentSession.Cameras = selected.Select(c => ToSessionCamera(sessionId, c)).ToList();

        Markers.Clear();

        // TODO: open + record each selected camera stream in parallel via the providers.
        var primary = ToDevice(selected[0].Profile);
        await _recordingEngine.StartPreviewAsync(primary);
        await _recordingEngine.StartRecordingAsync();
    }

    [RelayCommand(CanExecute = nameof(CanStopRecording))]
    private async Task StopRecording()
    {
        if (_currentSession is null) return;

        await _recordingEngine.StopRecordingAsync(
            Path.Combine(SessionsDirectory, _currentSession.Cameras.FirstOrDefault()?.VideoFile ?? $"{_currentSession.SessionId}.mp4"));

        _currentSession.TimingMarkers = Markers.ToList();
        await _sessionManager.SaveAsync(SessionsDirectory, _currentSession);
        _currentSession = null;
    }

    // --- manual timing triggers (also exercise the ITimingProvider pipeline) ---

    [RelayCommand]
    private void TriggerStart() => Emit(TimingTriggerType.Start);

    [RelayCommand]
    private void TriggerIntermediate() => Emit(TimingTriggerType.Intermediate);

    [RelayCommand]
    private void TriggerStop() => Emit(TimingTriggerType.Stop);

    private void Emit(TimingTriggerType type)
    {
        var videoTime = _currentSession is null
            ? TimeSpan.Zero
            : DateTimeOffset.Now - _currentSession.CreatedAt;

        _manualTiming.Emit(type, videoTime < TimeSpan.Zero ? TimeSpan.Zero : videoTime, DateTimeOffset.Now);
    }

    private void OnTriggerReceived(TimingTrigger trigger)
    {
        Markers.Add(trigger);
        // TODO: route Start/Stop to auto start/stop recording once the rolling buffer is real.
    }

    private void OnRecordingStateChanged(RecordingState state)
    {
        IsRecording = state == RecordingState.Recording;
        StatusText = state switch
        {
            RecordingState.Idle => "Idle",
            RecordingState.Previewing => "Live preview",
            RecordingState.Recording => "Recording…",
            RecordingState.Stopping => "Finishing…",
            _ => state.ToString(),
        };
    }

    private void ApplySyncOffsets(IReadOnlyList<CameraProfileRowViewModel> rows)
    {
        var offsets = CameraSyncCalculator.ComputeOffsets(rows.Select(r => r.Profile).ToList());
        foreach (var offset in offsets)
        {
            var row = rows.FirstOrDefault(r => r.Id == offset.CameraId);
            if (row is not null)
                row.SyncOffsetMs = offset.OffsetMs;
        }
    }

    private SessionCamera ToSessionCamera(string sessionId, CameraProfileRowViewModel row) => new()
    {
        CameraId = row.Id,
        Name = row.DisplayName,
        SourceType = row.SourceType,
        SourceUrl = row.SourceUrl,
        VideoFile = $"{sessionId}_{Sanitize(row.Id)}.mp4",
        CalibratedLatencyMs = row.CalibratedLatencyMs,
        ManualOffsetMs = row.ManualOffsetMs,
        SyncOffsetMs = row.SyncOffsetMs,
    };

    private static string BuildCalibrationSummary(IReadOnlyList<CameraProfileRowViewModel> rows)
    {
        var lines = rows.Select(r =>
            $"{r.DisplayName}: {r.CalibratedLatencyMs:0.#} ms (sync +{r.SyncOffsetMs:0.#} ms)");
        return string.Join("\n", lines);
    }

    private static CameraProfile ToProfile(CameraDevice device) => new()
    {
        Id = device.Id,
        DisplayName = device.Name,
        SourceType = string.IsNullOrEmpty(device.SourceType) ? device.ProviderName : device.SourceType,
        SourceUrl = device.SourceUrl,
    };

    private static CameraDevice ToDevice(CameraProfile profile) => new(profile.Id, profile.DisplayName)
    {
        ProviderName = profile.SourceType,
        SourceType = profile.SourceType,
        SourceUrl = profile.SourceUrl,
    };

    private static string Sanitize(string id)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            id = id.Replace(c, '-');
        return id.Replace(':', '-').Replace('/', '-');
    }
}
