using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FinishReplay.Models;
using FinishReplay.Services.Calibration;
using FinishReplay.Services.Camera;
using FinishReplay.Services.Camera.Providers;
using FinishReplay.Services.Naming;
using FinishReplay.Services.Recording;
using FinishReplay.Services.Session;
using FinishReplay.Services.Settings;
using FinishReplay.Services.Timing;

namespace FinishReplay.ViewModels;

/// <summary>
/// Main/recording screen. Shows every configured camera (from settings) with a live preview;
/// "Record all" starts/stops the selected cameras together. MJPEG cameras capture for real (live
/// preview + recording to a Motion-JPEG AVI per camera); other transports remain placeholders.
/// Latency is calibrated automatically so the clips can be synchronized on replay.
/// </summary>
public partial class RecordingViewModel : ViewModelBase
{
    private const double CaptureFps = 30;

    private readonly CameraProviderRegistry _registry;
    private readonly IRecordingEngine _recordingEngine;
    private readonly ISessionManager _sessionManager;
    private readonly ManualTimingProvider _manualTiming;
    private readonly ICameraLatencyCalibrationService _calibrationService;
    private readonly ISettingsService _settings;

    // Live capture controllers keyed by camera id (MJPEG cameras only for now).
    private readonly Dictionary<string, LiveCamera> _live = new();

    // Optional hardware timing provider (e.g. ALGE TimY3) built from settings; manual is always on.
    private ITimingProvider? _hardwareTiming;

    private SessionMetadata? _currentSession;

    public RecordingViewModel(
        CameraProviderRegistry registry,
        IRecordingEngine recordingEngine,
        ISessionManager sessionManager,
        ManualTimingProvider manualTiming,
        ICameraLatencyCalibrationService calibrationService,
        ISettingsService settings)
    {
        _registry = registry;
        _recordingEngine = recordingEngine;
        _sessionManager = sessionManager;
        _manualTiming = manualTiming;
        _calibrationService = calibrationService;
        _settings = settings;

        _recordingEngine.StateChanged += (_, state) => OnRecordingStateChanged(state);
        _manualTiming.TriggerReceived += (_, trigger) => Dispatcher.UIThread.Post(() => OnTriggerReceived(trigger));
        _settings.Changed += (_, _) => LoadFromSettings();

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
    [NotifyCanExecuteChangedFor(nameof(StartRecordingCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopRecordingCommand))]
    private bool _isRecording;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartRecording))]
    [NotifyPropertyChangedFor(nameof(CanStopRecording))]
    [NotifyCanExecuteChangedFor(nameof(StartRecordingCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopRecordingCommand))]
    private bool _isFinishing;

    [ObservableProperty] private string _statusText = "Idle";
    [ObservableProperty] private string _timingStatusText = "Timing: manual";
    [ObservableProperty] private bool _isCalibrating;
    [ObservableProperty] private string _calibrationSummary = "Not calibrated yet.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectTimingCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisconnectTimingCommand))]
    private bool _isTimingConnected;

    /// <summary>True when a hardware timing device (not just manual) is configured.</summary>
    public bool HasTimingDevice => _hardwareTiming is not null;

    public string StorageDirectory => _settings.Current.StorageDirectory;

    public bool CanStartRecording => SelectedCameras.Count > 0 && !IsRecording && !IsFinishing;
    public bool CanStopRecording => IsRecording && !IsFinishing;
    public bool HasCameras => Cameras.Count > 0;

    private List<CameraProfileRowViewModel> SelectedCameras =>
        Cameras.Where(c => c.IsSelected).ToList();

    private void LoadFromSettings()
    {
        var s = _settings.Current;
        PreRecordSeconds = s.PreRecordSeconds;
        PostRecordSeconds = s.PostRecordSeconds;

        // Tear down previous live controllers before rebuilding.
        foreach (var cam in _live.Values)
            _ = cam.DisposeAsync();
        _live.Clear();

        RebuildTimingProvider(s);

        Cameras.Clear();
        foreach (var profile in s.Cameras)
        {
            var row = new CameraProfileRowViewModel(profile) { IsSelected = profile.Enabled };
            Cameras.Add(row);

            // Real live capture: MJPEG (native HTTP), RTSP and USB (via ffmpeg). Start preview now.
            if (HasLiveCapture(profile.SourceType))
            {
                var live = new LiveCamera(_registry, profile, () => _settings.Current.FfmpegPath)
                {
                    PreRecordSeconds = s.PreRecordSeconds, // keep this much rolling pre-roll ready
                };
                live.FrameReady += row.SubmitJpeg;
                live.Start();
                _live[profile.Id] = live;
            }
        }

        OnPropertyChanged(nameof(StorageDirectory));
        OnPropertyChanged(nameof(HasCameras));
        OnPropertyChanged(nameof(CanStartRecording));

        if (Cameras.Count > 0)
            _ = AutoCalibrateAsync();
    }

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

        if (selected.Any(c => c.CalibratedLatencyMs is null))
            await AutoCalibrateAsync();
        else
            ApplySyncOffsets(selected);

        var s = _settings.Current;
        Directory.CreateDirectory(s.StorageDirectory);

        _recordingEngine.PreRecord = TimeSpan.FromSeconds(s.PreRecordSeconds);
        _recordingEngine.PostRecord = TimeSpan.FromSeconds(s.PostRecordSeconds);

        var sessionId = BuildSessionId();
        _currentSession = _sessionManager.CreateSession(
            sessionId,
            _recordingEngine.PreRecord,
            _recordingEngine.PostRecord,
            _manualTiming.IsConnected ? _manualTiming.Name : "none");

        ApplySyncOffsets(selected);
        _currentSession.Cameras = selected.Select(ToSessionCamera).ToList();
        Markers.Clear();

        // Begin real recording on each capture-capable camera; flips engine state for the UI.
        // Each camera prepends its rolling pre-roll buffer automatically (transcode).
        var mode = s.RecordingMode;
        foreach (var (row, cam) in selected.Zip(_currentSession.Cameras))
        {
            if (_live.TryGetValue(row.Id, out var live))
                live.StartRecording(Path.Combine(s.StorageDirectory, cam.VideoFile), CaptureFps, mode, s.PostRecordSeconds);
        }

        await _recordingEngine.StartAsync(selected.Select(c => c.Profile.ToDevice()).ToList());
    }

    [RelayCommand(CanExecute = nameof(CanStopRecording))]
    private async Task StopRecording()
    {
        if (_currentSession is null) return;

        // Honour the post-record tail: each camera keeps capturing for PostRecord seconds, then
        // finalizes. Surface that as a "Finishing…" state until every clip is closed.
        IsFinishing = true;
        StatusText = _settings.Current.PostRecordSeconds > 0
            ? $"Finishing… (+{_settings.Current.PostRecordSeconds:0}s)"
            : "Finishing…";

        await Task.WhenAll(_live.Values.Select(live => live.StopRecordingAsync()));

        await _recordingEngine.StopAsync();

        _currentSession.TimingMarkers = Markers.ToList();
        await _sessionManager.SaveAsync(_settings.Current.StorageDirectory, _currentSession);

        _settings.Current.SeriesNumber++;
        await _settings.SaveAsync();

        _currentSession = null;
        IsFinishing = false;
    }

    [RelayCommand] private void TriggerStart() => Emit(TimingTriggerType.Start);
    [RelayCommand] private void TriggerIntermediate() => Emit(TimingTriggerType.Intermediate);
    [RelayCommand] private void TriggerStop() => Emit(TimingTriggerType.Stop);

    private void Emit(TimingTriggerType type) => _manualTiming.Emit(type);

    /// <summary>
    /// Called for every trigger (manual or hardware). Computes the clip-relative time from the
    /// trigger's wall-clock arrival and the active session, shifting by the pre-roll for transcode
    /// clips so markers line up with frame 0.
    /// </summary>
    private void OnTriggerReceived(TimingTrigger trigger)
    {
        var videoTime = TimeSpan.Zero;
        if (_currentSession is not null)
        {
            videoTime = trigger.ReceivedAt - _currentSession.CreatedAt;
            if (videoTime < TimeSpan.Zero) videoTime = TimeSpan.Zero;
            if (_settings.Current.RecordingMode == RecordingMode.Transcode)
                videoTime += TimeSpan.FromSeconds(_settings.Current.PreRecordSeconds);
        }

        Markers.Add(new TimingTrigger
        {
            Type = trigger.Type,
            ReceivedAt = trigger.ReceivedAt,
            VideoTime = videoTime,
            RawMessage = trigger.RawMessage,
        });

        // TODO: optionally auto start/stop recording on Start/Stop triggers.
    }

    // --- hardware timing (ALGE TimY3) ---

    private bool CanConnectTiming => HasTimingDevice && !IsTimingConnected;
    private bool CanDisconnectTiming => HasTimingDevice && IsTimingConnected;

    [RelayCommand(CanExecute = nameof(CanConnectTiming))]
    private async Task ConnectTiming()
    {
        if (_hardwareTiming is null) return;
        try { await _hardwareTiming.ConnectAsync(); }
        catch (Exception ex) { TimingStatusText = $"Timing: connection failed — {ex.Message}"; }
    }

    [RelayCommand(CanExecute = nameof(CanDisconnectTiming))]
    private async Task DisconnectTiming()
    {
        if (_hardwareTiming is null) return;
        await _hardwareTiming.DisconnectAsync();
    }

    private void RebuildTimingProvider(AppSettings s)
    {
        // Dispose any previous hardware provider.
        if (_hardwareTiming is not null)
        {
            try { _hardwareTiming.Dispose(); } catch { /* ignore */ }
            _hardwareTiming = null;
        }

        if (s.TimingSource == TimingSource.AlgeTimySerial && !string.IsNullOrWhiteSpace(s.TimingSerialPort))
        {
            var provider = new AlgeTimy3TimingProvider(s.TimingSerialPort, s.TimingBaudRate);
            provider.TriggerReceived += (_, t) => Dispatcher.UIThread.Post(() => OnTriggerReceived(t));
            provider.ConnectionChanged += (_, connected) => Dispatcher.UIThread.Post(() =>
            {
                IsTimingConnected = connected;
                UpdateTimingStatus();
            });
            _hardwareTiming = provider;
        }

        IsTimingConnected = false;
        OnPropertyChanged(nameof(HasTimingDevice));
        ConnectTimingCommand.NotifyCanExecuteChanged();
        DisconnectTimingCommand.NotifyCanExecuteChanged();
        UpdateTimingStatus();
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

    private void UpdateTimingStatus()
    {
        if (_hardwareTiming is null)
        {
            TimingStatusText = "Timing: manual (no device)";
            return;
        }
        TimingStatusText = IsTimingConnected
            ? $"Timing: {_hardwareTiming.Name} — connected"
            : $"Timing: {_hardwareTiming.Name} — not connected";
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

    private static bool HasLiveCapture(string sourceType) =>
        sourceType is MjpegCameraProvider.Type or RtspCameraProvider.Type or UsbCameraProvider.Type;

    private SessionCamera ToSessionCamera(CameraProfileRowViewModel row)
    {
        // Passthrough RTSP archives original H.264 to MP4; everything captured else records MJPEG AVI.
        var passthroughRtsp = _settings.Current.RecordingMode == RecordingMode.Passthrough
            && row.SourceType == RtspCameraProvider.Type;
        var ext = passthroughRtsp ? ".mp4" : (HasLiveCapture(row.SourceType) ? ".avi" : ".mp4");
        return new SessionCamera
        {
            CameraId = row.Id,
            Name = row.DisplayName,
            SourceType = row.SourceType,
            SourceUrl = row.SourceUrl,
            VideoFile = BuildFilename(row.Profile.Suffix) + ext,
            Fps = CaptureFps,
            CalibratedLatencyMs = row.CalibratedLatencyMs,
            ManualOffsetMs = row.ManualOffsetMs,
            SyncOffsetMs = row.SyncOffsetMs,
        };
    }

    private string BuildSessionId()
    {
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
