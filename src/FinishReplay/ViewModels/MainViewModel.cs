using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FinishReplay.Services.Calibration;
using FinishReplay.Services.Camera;
using FinishReplay.Services.Camera.Providers;
using FinishReplay.Services.Recording;
using FinishReplay.Services.Replay;
using FinishReplay.Services.Session;
using FinishReplay.Services.Settings;
using FinishReplay.Services.Timeline;
using FinishReplay.Services.Timing;

namespace FinishReplay.ViewModels;

/// <summary>
/// Shell view model. Owns the composition root for the MVP and exposes the pages
/// (Recording / Replay / Settings) plus navigation between them.
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    private ViewModelBase _currentPage;

    public RecordingViewModel Recording { get; }
    public ReplayViewModel Replay { get; }
    public SettingsViewModel Settings { get; }

    public MainViewModel()
    {
        // --- composition root (swap for a DI container later) ---
        ISettingsService settingsService = new SettingsService();
        // Settings are small; load once at startup so all pages see the same instance.
        settingsService.LoadAsync().GetAwaiter().GetResult();

        var providerRegistry = new CameraProviderRegistry(new ICameraProvider[]
        {
            new UsbCameraProvider(() => settingsService.Current.FfmpegPath, () => settingsService.Current.VideoBackend),
            new MjpegCameraProvider(),
            new RtspCameraProvider(() => settingsService.Current.FfmpegPath, () => settingsService.Current.VideoBackend),
            // TODO: register OnvifCameraProvider and other transports here.
        });
        ICameraManager cameraManager = new CameraManager(providerRegistry);
        IVideoBackend videoBackend = new FfmpegVideoBackend();
        IRecordingEngine recordingEngine = new RecordingEngine(videoBackend);
        ISessionManager sessionManager = new SessionManager();
        ICameraLatencyCalibrationService calibrationService = new FakeCameraLatencyCalibrationService();
        var timelineEngine = new TimelineEngine();
        var manualTiming = new ManualTimingProvider();

        Recording = new RecordingViewModel(providerRegistry, recordingEngine, sessionManager, manualTiming, calibrationService, settingsService);
        Replay = new ReplayViewModel(sessionManager, timelineEngine, () => settingsService.Current.FfmpegPath);
        Settings = new SettingsViewModel(settingsService, cameraManager);

        _currentPage = Recording;
    }

    public bool IsRecordingSelected => CurrentPage == Recording;
    public bool IsReplaySelected => CurrentPage == Replay;
    public bool IsSettingsSelected => CurrentPage == Settings;

    partial void OnCurrentPageChanged(ViewModelBase value)
    {
        OnPropertyChanged(nameof(IsRecordingSelected));
        OnPropertyChanged(nameof(IsReplaySelected));
        OnPropertyChanged(nameof(IsSettingsSelected));
    }

    [RelayCommand]
    private void ShowRecording() => CurrentPage = Recording;

    [RelayCommand]
    private async Task ShowReplay()
    {
        await Replay.RefreshSessionsAsync();
        CurrentPage = Replay;
    }

    [RelayCommand]
    private void ShowSettings() => CurrentPage = Settings;
}
