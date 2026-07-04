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

        _recordingEngine.StateChanged += (_, state) => Dispatcher.UIThread.Post(() => OnRecordingStateChanged(state));
        _manualTiming.TriggerReceived += (_, trigger) => Dispatcher.UIThread.Post(() => OnTriggerReceived(trigger));
        // SettingsService.SaveAsync raises Changed off the UI thread (ConfigureAwait(false)), and
        // LoadFromSettings touches UI-bound state (Cameras collection, command CanExecute) — marshal it.
        _settings.Changed += (_, _) => Dispatcher.UIThread.Post(LoadFromSettings);

        _ = _manualTiming.ConnectAsync();

        LoadFromSettings();
    }

    public ObservableCollection<CameraProfileRowViewModel> Cameras { get; } = new();
    public ObservableCollection<TimingTrigger> Markers { get; } = new();

    [ObservableProperty] private double _preRecordSeconds;
    [ObservableProperty] private double _postRecordSeconds;

    // Event context (required to record) — persisted into settings and used for clip/session names.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartRecording))]
    [NotifyPropertyChangedFor(nameof(HasEventContext))]
    [NotifyCanExecuteChangedFor(nameof(StartRecordingCommand))]
    private string _category = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartRecording))]
    [NotifyPropertyChangedFor(nameof(HasEventContext))]
    [NotifyCanExecuteChangedFor(nameof(StartRecordingCommand))]
    private string _discipline = "";

    [ObservableProperty] private int _seriesNumber = 1;

    partial void OnCategoryChanged(string value) => _settings.Current.Category = value?.Trim() ?? "";
    partial void OnDisciplineChanged(string value) => _settings.Current.Discipline = value?.Trim() ?? "";
    partial void OnSeriesNumberChanged(int value) => _settings.Current.SeriesNumber = value < 1 ? 1 : value;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartRecording))]
    [NotifyPropertyChangedFor(nameof(CanStopRecording))]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyCanExecuteChangedFor(nameof(StartRecordingCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopRecordingCommand))]
    private bool _isRecording;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartRecording))]
    [NotifyPropertyChangedFor(nameof(CanStopRecording))]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyCanExecuteChangedFor(nameof(StartRecordingCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopRecordingCommand))]
    private bool _isFinishing;

    [ObservableProperty] private string _statusText = "Idle";
    [ObservableProperty] private string _timingStatusText = "Timing: manual";
    [ObservableProperty] private bool _isCalibrating;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectTimingCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisconnectTimingCommand))]
    private bool _isTimingConnected;

    /// <summary>True when a hardware timing device (not just manual) is configured.</summary>
    public bool HasTimingDevice => _hardwareTiming is not null;

    public string StorageDirectory => _settings.Current.StorageDirectory;

    public bool CanStartRecording =>
        SelectedCameras.Count > 0 && !IsRecording && !IsFinishing && HasEventContext;
    public bool CanStopRecording => IsRecording && !IsFinishing;

    /// <summary>True while a session is recording or finishing — used to lock out Settings.</summary>
    public bool IsBusy => IsRecording || IsFinishing;

    public bool HasCameras => Cameras.Count > 0;

    /// <summary>Category and discipline are required before a session can be recorded.</summary>
    public bool HasEventContext =>
        !string.IsNullOrWhiteSpace(Category) && !string.IsNullOrWhiteSpace(Discipline);

    private List<CameraProfileRowViewModel> SelectedCameras =>
        Cameras.Where(c => c.IsSelected).ToList();

    private bool _isReloading;
    private bool _reloadRequested;
    private string? _cameraSignature;

    // Fires on construction and whenever settings are saved. Serialized so overlapping saves can't run
    // two rebuilds at once — the second is coalesced into one follow-up pass after the first completes.
    private async void LoadFromSettings()
    {
        if (_isReloading)
        {
            _reloadRequested = true;
            return;
        }

        _isReloading = true;
        try
        {
            do
            {
                _reloadRequested = false;
                await ReloadAsync().ConfigureAwait(true);
            }
            while (_reloadRequested);
        }
        finally
        {
            _isReloading = false;
        }
    }

    private async Task ReloadAsync()
    {
        var s = _settings.Current;
        PreRecordSeconds = s.PreRecordSeconds;
        PostRecordSeconds = s.PostRecordSeconds;
        Category = s.Category;
        Discipline = s.Discipline;
        SeriesNumber = s.SeriesNumber < 1 ? 1 : s.SeriesNumber;

        RebuildTimingProvider(s);

        // Only rebuild the live previews when the camera set/config actually changed. Otherwise (e.g. the
        // series number auto-bumps after a recording, or the latency is nudged) keep the previews running
        // instead of tearing the devices down and reopening them — which is the visible "refresh".
        var signature = CameraSignature(s);
        if (signature == _cameraSignature)
        {
            foreach (var live in _live.Values)
                live.PreRecordSeconds = s.PreRecordSeconds;
            return;
        }
        _cameraSignature = signature;

        // Tear down previous live controllers and WAIT for each to release its device before reopening.
        // A USB webcam is held exclusively by its worker process; starting the new capture before the
        // old worker has exited makes the device open fail "in use".
        var previous = _live.Values.ToList();
        _live.Clear();
        foreach (var cam in previous)
        {
            try { await cam.DisposeAsync().ConfigureAwait(true); }
            catch { /* ignore teardown errors */ }
        }

        Cameras.Clear();
        // Only enabled cameras take part in a recording session — unchecking "On" in Settings drops
        // the camera from the recording grid (no preview tile, no capture, not recorded).
        foreach (var profile in s.Cameras.Where(p => p.Enabled))
        {
            var row = new CameraProfileRowViewModel(profile) { IsSelected = true };
            row.RefreshCommand = RefreshCameraCommand; // right-click → refresh this camera
            Cameras.Add(row);

            // Real live capture: MJPEG (native HTTP), RTSP and USB (via ffmpeg). Start preview now.
            if (HasLiveCapture(profile.SourceType))
                StartLive(row);
        }

        OnPropertyChanged(nameof(StorageDirectory));
        OnPropertyChanged(nameof(HasCameras));
        OnPropertyChanged(nameof(CanStartRecording));

        if (Cameras.Count > 0)
            _ = AutoCalibrateAsync();
    }

    /// <summary>Open the live capture for a row and tee its frames to the preview tile.</summary>
    private void StartLive(CameraProfileRowViewModel row)
    {
        var live = new LiveCamera(_registry, row.Profile, () => _settings.Current.FfmpegPath)
        {
            PreRecordSeconds = _settings.Current.PreRecordSeconds, // rolling pre-roll kept ready
        };
        live.FrameReady += row.SubmitJpeg;
        live.Error += row.SetError;
        live.Start();
        _live[row.Id] = live;
    }

    /// <summary>Restart one camera's capture (right-click → Refresh on its tile). Disabled while recording.</summary>
    [RelayCommand]
    private async Task RefreshCamera(CameraProfileRowViewModel? row)
    {
        if (row is null || IsBusy || !HasLiveCapture(row.SourceType))
            return;

        if (_live.Remove(row.Id, out var existing))
        {
            try { await existing.DisposeAsync().ConfigureAwait(true); }
            catch { /* ignore teardown errors */ }
        }

        row.PrepareReconnect(); // clear stale frame/error so the tile shows it's reconnecting
        StartLive(row);
    }

    /// <summary>Tear down all live cameras and their worker processes (called on app exit).</summary>
    public void ShutdownCameras()
    {
        var cams = _live.Values.ToList();
        _live.Clear();
        try { Task.WhenAll(cams.Select(c => c.DisposeAsync().AsTask())).Wait(TimeSpan.FromSeconds(4)); }
        catch { /* best-effort on exit */ }
        try { _hardwareTiming?.Dispose(); }
        catch { /* ignore */ }
    }

    private async Task AutoCalibrateAsync()
    {
        var targets = SelectedCameras.Count > 0 ? SelectedCameras : Cameras.ToList();
        if (targets.Count == 0 || IsCalibrating)
            return;

        IsCalibrating = true;
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

        // Don't overwrite an existing recording with the same category/discipline/series — version it.
        EnsureUniqueSession(s.StorageDirectory);

        Markers.Clear();

        // Automatic marker at the record moment so replay has a clear "start" tick.
        AddSessionMarker(TimingTriggerType.Start, _currentSession.CreatedAt, "Recording started");

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

        // Automatic marker at the stop moment (captured before the post-roll delay).
        AddSessionMarker(TimingTriggerType.Stop, DateTimeOffset.Now, "Recording stopped");

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
        SeriesNumber = _settings.Current.SeriesNumber; // reflect the bump in the UI
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
        Markers.Add(new TimingTrigger
        {
            Type = trigger.Type,
            ReceivedAt = trigger.ReceivedAt,
            VideoTime = VideoTimeFor(trigger.ReceivedAt),
            RawMessage = trigger.RawMessage,
        });

        // TODO: optionally auto start/stop recording on Start/Stop triggers.
    }

    /// <summary>Add a marker generated by the app itself (e.g. the automatic record start/stop ticks).</summary>
    private void AddSessionMarker(TimingTriggerType type, DateTimeOffset at, string message) =>
        Markers.Add(new TimingTrigger
        {
            Type = type,
            ReceivedAt = at,
            VideoTime = VideoTimeFor(at),
            RawMessage = message,
        });

    /// <summary>
    /// Clip-relative time for a wall-clock instant: measured from the session start, shifted by the
    /// pre-roll for transcode clips so markers line up with the recorded frames.
    /// </summary>
    private TimeSpan VideoTimeFor(DateTimeOffset at)
    {
        if (_currentSession is null)
            return TimeSpan.Zero;

        var videoTime = at - _currentSession.CreatedAt;
        if (videoTime < TimeSpan.Zero) videoTime = TimeSpan.Zero;
        if (_settings.Current.RecordingMode == RecordingMode.Transcode)
            videoTime += TimeSpan.FromSeconds(_settings.Current.PreRecordSeconds);
        return videoTime;
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

    /// <summary>
    /// Fingerprint of the enabled cameras' capture-relevant configuration. Used to decide whether a
    /// settings change actually requires reopening the devices. Deliberately excludes latency/calibration
    /// (replay-only) and the event fields (category/discipline/series), so those never restart previews.
    /// </summary>
    private static string CameraSignature(AppSettings s) =>
        string.Join("\n", s.Cameras
            .Where(c => c.Enabled)
            .Select(c => string.Join("|", c.Id, c.SourceType, c.SourceUrl, c.DisplayName, c.Suffix,
                                          c.Width, c.Height, c.FrameRate, c.PixelFormat)));

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

    /// <summary>
    /// If the session id or any camera file would collide with an existing recording on disk, append a
    /// version tag (<c>-v2</c>, <c>-v3</c>, …) to the session id and every camera file so nothing is
    /// overwritten. Applied to the whole session together so the files stay grouped.
    /// </summary>
    private void EnsureUniqueSession(string dir)
    {
        if (_currentSession is null)
            return;

        var baseId = _currentSession.SessionId;
        var baseFiles = _currentSession.Cameras.Select(c => c.VideoFile).ToList();

        var version = 1;
        while (VersionExists(dir, baseId, baseFiles, version))
            version++;

        if (version == 1)
            return; // base names are free

        var tag = $"-v{version}";
        _currentSession.SessionId = baseId + tag;
        for (var i = 0; i < _currentSession.Cameras.Count; i++)
            _currentSession.Cameras[i].VideoFile = InsertBeforeExtension(baseFiles[i], tag);
    }

    private static bool VersionExists(string dir, string baseId, IReadOnlyList<string> baseFiles, int version)
    {
        var tag = version == 1 ? "" : $"-v{version}";
        if (File.Exists(Path.Combine(dir, baseId + tag + ".timing.json")))
            return true;
        return baseFiles.Any(f => File.Exists(Path.Combine(dir, InsertBeforeExtension(f, tag))));
    }

    private static string InsertBeforeExtension(string fileName, string tag)
    {
        if (string.IsNullOrEmpty(tag))
            return fileName;
        var ext = Path.GetExtension(fileName);
        return Path.GetFileNameWithoutExtension(fileName) + tag + ext;
    }
}
