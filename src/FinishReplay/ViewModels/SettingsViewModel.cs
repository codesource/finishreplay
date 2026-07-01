using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FinishReplay.Models;
using FinishReplay.Services.Camera;
using FinishReplay.Services.Camera.Providers.Ffmpeg;
using FinishReplay.Services.Naming;
using FinishReplay.Services.Settings;
using FinishReplay.Services.Timing;

namespace FinishReplay.ViewModels;

/// <summary>
/// Tabbed settings: recording buffers, storage folder, ffmpeg/video backend, timing device, the
/// clip filename format with a live preview, and the cameras (delegated to
/// <see cref="CameraSettingsViewModel"/>). Edits are applied to the shared <see cref="AppSettings"/>
/// and persisted on Save.
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settings;

    public SettingsViewModel(ISettingsService settings, ICameraManager cameraManager)
    {
        _settings = settings;
        CameraSettings = new CameraSettingsViewModel(settings, cameraManager);

        LoadFromSettings();
    }

    /// <summary>The Cameras tab (list with live status + type-aware add form).</summary>
    public CameraSettingsViewModel CameraSettings { get; }

    public IReadOnlyList<RecordingMode> RecordingModes { get; } =
        new[] { RecordingMode.Transcode, RecordingMode.Passthrough };

    public IReadOnlyList<TimingSource> TimingSources { get; } =
        new[] { TimingSource.Manual, TimingSource.AlgeTimySerial };

    public IReadOnlyList<VideoBackend> VideoBackends { get; } =
        new[] { VideoBackend.ExternalProcess, VideoBackend.IsolatedWorker };

    public IReadOnlyList<int> BaudRates { get; } = new[] { 9600, 19200, 38400, 57600, 115200 };

    public ObservableCollection<string> SerialPorts { get; } = new();

    [ObservableProperty] private double _preRecordSeconds;
    [ObservableProperty] private double _postRecordSeconds;
    [ObservableProperty] private string _storageDirectory = "";
    [ObservableProperty] private string _ffmpegPath = "";
    [ObservableProperty] private string _ffmpegStatus = "";
    [ObservableProperty] private RecordingMode _recordingMode;
    [ObservableProperty] private VideoBackend _videoBackend;
    [ObservableProperty] private TimingSource _timingSource;
    [ObservableProperty] private string _timingSerialPort = "";
    [ObservableProperty] private int _timingBaudRate = 9600;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(FilenamePreview))] private string _filenameFormat = "";

    [ObservableProperty] private string _statusText = "";

    partial void OnFfmpegPathChanged(string value) => RefreshFfmpegStatus();

    private void RefreshFfmpegStatus()
    {
        var resolved = FfmpegLocator.Resolve(string.IsNullOrWhiteSpace(FfmpegPath) ? "ffmpeg" : FfmpegPath);
        FfmpegStatus = resolved is not null
            ? $"✓ Found: {resolved}"
            : "✗ Not found — RTSP and USB cameras need ffmpeg. Install it or set the path above.";
    }

    [RelayCommand]
    private void DetectFfmpeg()
    {
        var resolved = FfmpegLocator.Resolve(string.IsNullOrWhiteSpace(FfmpegPath) ? "ffmpeg" : FfmpegPath);
        if (resolved is not null)
            FfmpegPath = resolved; // persist the discovered absolute path
        else
            RefreshFfmpegStatus();
    }

    [RelayCommand]
    private void RefreshSerialPorts()
    {
        SerialPorts.Clear();
        foreach (var port in AlgeTimy3TimingProvider.GetAvailablePorts().OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            SerialPorts.Add(port);

        // Keep the configured port visible even if it's not currently connected.
        if (!string.IsNullOrWhiteSpace(TimingSerialPort) && !SerialPorts.Contains(TimingSerialPort))
            SerialPorts.Add(TimingSerialPort);
    }

    [RelayCommand]
    private static void OpenFfmpegDownload()
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://ffmpeg.org/download.html") { UseShellExecute = true });
        }
        catch
        {
            // ignore — user can browse manually
        }
    }

    /// <summary>Live example of the clip filename produced by the current format.</summary>
    public string FilenamePreview
    {
        get
        {
            var sample = CameraSettings.Cameras.FirstOrDefault()?.Suffix;
            if (string.IsNullOrWhiteSpace(sample)) sample = "cam";
            var s = _settings.Current; // event context is edited on the Recording screen
            var ctx = new RecordingNameContext(DateTimeOffset.Now, s.Category, s.Discipline, s.SeriesNumber, sample);
            return FilenameFormatter.Build(FilenameFormat, ctx) + ".mp4";
        }
    }

    [RelayCommand]
    private async Task Save()
    {
        ApplyToSettings();
        await _settings.SaveAsync();
        StatusText = $"Saved {DateTime.Now:HH:mm:ss}";
    }

    private void LoadFromSettings()
    {
        var s = _settings.Current;
        PreRecordSeconds = s.PreRecordSeconds;
        PostRecordSeconds = s.PostRecordSeconds;
        StorageDirectory = s.StorageDirectory;
        FfmpegPath = s.FfmpegPath;
        RecordingMode = s.RecordingMode;
        VideoBackend = s.VideoBackend;
        TimingSource = s.TimingSource;
        TimingSerialPort = s.TimingSerialPort;
        TimingBaudRate = s.TimingBaudRate <= 0 ? 9600 : s.TimingBaudRate;
        RefreshSerialPorts();
        FilenameFormat = string.IsNullOrWhiteSpace(s.FilenameFormat)
            ? "{date}-{category}-{discipline}-{serie}-{camera}"
            : s.FilenameFormat;

        CameraSettings.Load();

        RefreshFfmpegStatus();
    }

    private void ApplyToSettings()
    {
        var s = _settings.Current;
        s.PreRecordSeconds = PreRecordSeconds;
        s.PostRecordSeconds = PostRecordSeconds;
        s.StorageDirectory = string.IsNullOrWhiteSpace(StorageDirectory)
            ? AppSettings.DefaultStorageDirectory
            : StorageDirectory;
        s.FfmpegPath = string.IsNullOrWhiteSpace(FfmpegPath) ? "ffmpeg" : FfmpegPath;
        s.RecordingMode = RecordingMode;
        s.VideoBackend = VideoBackend;
        s.TimingSource = TimingSource;
        s.TimingSerialPort = TimingSerialPort ?? "";
        s.TimingBaudRate = TimingBaudRate <= 0 ? 9600 : TimingBaudRate;
        s.FilenameFormat = FilenameFormat;
        CameraSettings.ApplyTo(s);
    }
}
