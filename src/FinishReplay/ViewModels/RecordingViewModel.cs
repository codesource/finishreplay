using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FinishReplay.Models;
using FinishReplay.Services.Calibration;
using FinishReplay.Services.Naming;
using FinishReplay.Services.Recording;
using FinishReplay.Services.Session;
using FinishReplay.Services.Settings;
using FinishReplay.Services.Timing;

namespace FinishReplay.ViewModels;

/// <summary>
/// Main/recording screen. Shows every configured camera (from settings); recording starts/stops
/// all selected cameras together. Latency is calibrated automatically across the cameras (and the
/// timing device, when connected) so their replays can be synchronized. Cameras are configured in
/// the Settings page; this screen consumes them.
/// </summary>
public partial class RecordingViewModel : ViewModelBase
{
    private readonly IRecordingEngine _recordingEngine;
    private readonly ISessionManager _sessionManager;
    private readonly ManualTimingProvider _manualTiming;
    private readonly ICameraLatencyCalibrationService _calibrationService;
    private readonly ISettingsService _settings;

    private SessionMetadata? _currentSession;

    public RecordingViewModel(
        IRecordingEngine recordingEngine,
        ISessionManager sessionManager,
        ManualTimingProvider manualTiming,
        ICameraLatencyCalibrationService calibrationService,
        ISettingsService settings)
    {
        _recordingEngine = recordingEngine;
        _sessionManager = sessionManager;
        _manualTiming = manualTiming;
        _calibrationService = calibrationService;
        _settings = settings;

        _recordingEngine.StateChanged += (_, state) => OnRecordingStateChanged(state);
        _manualTiming.TriggerReceived += (_, trigger) => OnTriggerReceived(trigger);
        _manualTiming.ConnectionChanged += (_, _) => UpdateTimingStatus();
        _settings.Changed += (_, _) => LoadFromSettings();

        // Manual timing is always available as a software trigger source.
        _ = _manualTiming.ConnectAsync();

        LoadFromSettings();
    }

    public ObservableCollection<CameraProfileRowViewModel> Cameras { get; } = new();
    public ObservableCollection<TimingTrigger> Markers { get; } = new();

    [ObservableProperty] private double _preRecordSeconds;
    [ObservableProperty] private double _postRecordSeconds;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartRecording))]
    [NotifyPropertyChangedFor(nameof(CanStopRecording))]
    private bool _isRecording;

    [ObservableProperty] private string _statusText = "Idle";
    [ObservableProperty] private string _timingStatusText = "Timing: manual";
    [ObservableProperty] private bool _isCalibrating;
    [ObservableProperty] private string _calibrationSummary = "Not calibrated yet.";

    public string StorageDirectory => _settings.Current.StorageDirectory;

    public bool CanStartRecording => SelectedCameras.Count > 0 && !IsRecording;
    public bool CanStopRecording => IsRecording;

    public bool HasCameras => Cameras.Count > 0;

    private List<CameraProfileRowViewModel> SelectedCameras =>
        Cameras.Where(c => c.IsSelected).ToList();

    /// <summary>Reload the camera list and recording buffers from settings.</summary>
    private void LoadFromSettings()
    {
        var s = _settings.Current;
        PreRecordSeconds = s.PreRecordSeconds;
        PostRecordSeconds = s.PostRecordSeconds;

        Cameras.Clear();
        foreach (var profile in s.Cameras)
            Cameras.Add(new CameraProfileRowViewModel(profile) { IsSelected = profile.Enabled });

        OnPropertyChanged(nameof(StorageDirectory));
        OnPropertyChanged(nameof(HasCameras));
        OnPropertyChanged(nameof(CanStartRecording));

        // Auto-calibrate latency across the configured cameras so offsets are ready before recording.
        if (Cameras.Count > 0)
            _ = AutoCalibrateAsync();
    }

    /// <summary>
    /// Automatically measure each camera's latency and compute relative sync offsets. Runs on load
    /// and again right before recording. Uses the timing device as the reference when connected.
    /// </summary>
    private async Task AutoCalibrateAsync()
    {
        var targets = SelectedCameras.Count > 0 ? SelectedCameras : Cameras.ToList();
        if (targets.Count == 0 || IsCalibrating)
            return;

        IsCalibrating = true;
        CalibrationSummary = "Calibrating latency…";
        try
        {
            var profiles = targets.Select(c => c.Profile).ToList();
            var result = await _calibrationService.CalibrateAsync(profiles, new CalibrationSettings());

            foreach (var camResult in result.CameraResults)
            {
                var row = targets.FirstOrDefault(c => c.Id == camResult.CameraId);
                row?.ApplyCalibration(camResult.LatencyMs, camResult.Confidence, camResult.Success, camResult.ErrorMessage);
            }

            ApplySyncOffsets(targets);
            CalibrationSummary = BuildCalibrationSummary(targets);
        }
        finally
        {
            IsCalibrating = false;
        }
    }

    [RelayCommand]
    private Task Calibrate() => AutoCalibrateAsync();

    [RelayCommand(CanExecute = nameof(CanStartRecording))]
    private async Task StartRecording()
    {
        var selected = SelectedCameras;
        if (selected.Count == 0) return;

        // Make sure offsets are current before the clip starts.
        if (selected.Any(c => c.CalibratedLatencyMs is null))
            await AutoCalibrateAsync();
        else
            ApplySyncOffsets(selected);

        var s = _settings.Current;
        Directory.CreateDirectory(s.StorageDirectory);

        _recordingEngine.PreRecord = TimeSpan.FromSeconds(s.PreRecordSeconds);
        _recordingEngine.PostRecord = TimeSpan.FromSeconds(s.PostRecordSeconds);

        var sessionId = BuildSessionId(selected);
        _currentSession = _sessionManager.CreateSession(
            sessionId,
            _recordingEngine.PreRecord,
            _recordingEngine.PostRecord,
            _manualTiming.IsConnected ? _manualTiming.Name : "none");

        _currentSession.Cameras = selected.Select(c => ToSessionCamera(c)).ToList();
        Markers.Clear();

        var devices = selected.Select(c => c.Profile.ToDevice()).ToList();
        await _recordingEngine.StartAsync(devices);
    }

    [RelayCommand(CanExecute = nameof(CanStopRecording))]
    private async Task StopRecording()
    {
        if (_currentSession is null) return;

        await _recordingEngine.StopAsync();

        _currentSession.TimingMarkers = Markers.ToList();
        await _sessionManager.SaveAsync(_settings.Current.StorageDirectory, _currentSession);

        // Advance the series number for the next recording and persist it.
        _settings.Current.SeriesNumber++;
        await _settings.SaveAsync();

        _currentSession = null;
    }

    // --- manual timing triggers (also exercise the ITimingProvider pipeline) ---

    [RelayCommand] private void TriggerStart() => Emit(TimingTriggerType.Start);
    [RelayCommand] private void TriggerIntermediate() => Emit(TimingTriggerType.Intermediate);
    [RelayCommand] private void TriggerStop() => Emit(TimingTriggerType.Stop);

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
            RecordingState.Recording => $"Recording {SelectedCameras.Count} camera(s)…",
            RecordingState.Stopping => "Finishing…",
            _ => state.ToString(),
        };
    }

    private void UpdateTimingStatus() =>
        TimingStatusText = _manualTiming.IsConnected ? "Timing: manual (ready)" : "Timing: not connected";

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

    private SessionCamera ToSessionCamera(CameraProfileRowViewModel row) => new()
    {
        CameraId = row.Id,
        Name = row.DisplayName,
        SourceType = row.SourceType,
        SourceUrl = row.SourceUrl,
        VideoFile = BuildFilename(row.Profile.Suffix) + ".mp4",
        CalibratedLatencyMs = row.CalibratedLatencyMs,
        ManualOffsetMs = row.ManualOffsetMs,
        SyncOffsetMs = row.SyncOffsetMs,
    };

    private string BuildSessionId(IReadOnlyList<CameraProfileRowViewModel> cameras)
    {
        // Session id mirrors the filename template without the per-camera suffix.
        var s = _settings.Current;
        var ctx = new RecordingNameContext(DateTimeOffset.Now, s.Category, s.Discipline, s.SeriesNumber, "");
        var id = FilenameFormatter.Build(s.FilenameFormat, ctx);
        return string.IsNullOrWhiteSpace(id) ? $"session_{DateTime.Now:yyyyMMdd_HHmmss}" : id;
    }

    private string BuildFilename(string cameraSuffix)
    {
        var s = _settings.Current;
        var ctx = new RecordingNameContext(DateTimeOffset.Now, s.Category, s.Discipline, s.SeriesNumber, cameraSuffix);
        return FilenameFormatter.Build(s.FilenameFormat, ctx);
    }

    private static string BuildCalibrationSummary(IReadOnlyList<CameraProfileRowViewModel> rows)
    {
        if (rows.Count == 0) return "No cameras to calibrate.";
        var lines = rows.Select(r =>
            $"{r.DisplayName}: {r.CalibratedLatencyMs:0.#} ms (sync +{r.SyncOffsetMs:0.#} ms)");
        return string.Join("\n", lines);
    }
}
