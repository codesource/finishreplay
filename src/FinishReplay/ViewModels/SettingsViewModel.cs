using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FinishReplay.Models;
using FinishReplay.Services.Camera;
using FinishReplay.Services.Camera.Providers;
using FinishReplay.Services.Naming;
using FinishReplay.Services.Settings;

namespace FinishReplay.ViewModels;

/// <summary>
/// Tabbed settings: recording buffers + event context, storage folder, configured cameras
/// (detect/add, with per-camera filename suffix) and the clip filename format with a live preview.
/// Edits are applied to the shared <see cref="AppSettings"/> and persisted on Save.
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settings;
    private readonly ICameraManager _cameraManager;

    public SettingsViewModel(ISettingsService settings, ICameraManager cameraManager)
    {
        _settings = settings;
        _cameraManager = cameraManager;

        AvailableSourceTypes = new[] { MjpegCameraProvider.Type, RtspCameraProvider.Type };
        _newCameraType = MjpegCameraProvider.Type;

        LoadFromSettings();
    }

    public ObservableCollection<CameraSettingRowViewModel> Cameras { get; } = new();
    public IReadOnlyList<string> AvailableSourceTypes { get; }

    [ObservableProperty] private double _preRecordSeconds;
    [ObservableProperty] private double _postRecordSeconds;
    [ObservableProperty] private string _storageDirectory = "";

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(FilenamePreview))] private string _category = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(FilenamePreview))] private string _discipline = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(FilenamePreview))] private int _seriesNumber = 1;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(FilenamePreview))] private string _filenameFormat = "";

    [ObservableProperty] private string _newCameraType;
    [ObservableProperty] private string _newCameraName = "";
    [ObservableProperty] private string _newCameraUrl = "";
    [ObservableProperty] private string _newCameraSuffix = "";

    [ObservableProperty] private string _statusText = "";

    /// <summary>Live example of the clip filename produced by the current format.</summary>
    public string FilenamePreview
    {
        get
        {
            var sample = Cameras.FirstOrDefault()?.Suffix;
            if (string.IsNullOrWhiteSpace(sample)) sample = "cam";
            var ctx = new RecordingNameContext(DateTimeOffset.Now, Category, Discipline, SeriesNumber, sample);
            return FilenameFormatter.Build(FilenameFormat, ctx) + ".mp4";
        }
    }

    [RelayCommand]
    private async Task DetectCameras()
    {
        var devices = await _cameraManager.DiscoverAsync();
        foreach (var device in devices)
        {
            // Skip ones already configured (same id).
            if (Cameras.Any(c => c.Profile.Id == device.Id))
                continue;
            Cameras.Add(new CameraSettingRowViewModel(ToProfile(device, SuggestSuffix())));
        }
        OnPropertyChanged(nameof(FilenamePreview));
    }

    [RelayCommand]
    private void AddCamera()
    {
        if (string.IsNullOrWhiteSpace(NewCameraUrl))
            return;

        var device = NewCameraType == RtspCameraProvider.Type
            ? RtspCameraProvider.CreateDevice(NewCameraUrl, string.IsNullOrWhiteSpace(NewCameraName) ? null : NewCameraName)
            : MjpegCameraProvider.CreateDevice(NewCameraUrl, string.IsNullOrWhiteSpace(NewCameraName) ? null : NewCameraName);

        var suffix = string.IsNullOrWhiteSpace(NewCameraSuffix) ? SuggestSuffix() : NewCameraSuffix;
        Cameras.Add(new CameraSettingRowViewModel(ToProfile(device, suffix)));

        NewCameraName = "";
        NewCameraUrl = "";
        NewCameraSuffix = "";
        OnPropertyChanged(nameof(FilenamePreview));
    }

    [RelayCommand]
    private void RemoveCamera(CameraSettingRowViewModel? row)
    {
        if (row is not null)
            Cameras.Remove(row);
        OnPropertyChanged(nameof(FilenamePreview));
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
        Category = s.Category;
        Discipline = s.Discipline;
        SeriesNumber = s.SeriesNumber;
        FilenameFormat = string.IsNullOrWhiteSpace(s.FilenameFormat)
            ? "{date}-{category}-{discipline}-{serie}-{camera}"
            : s.FilenameFormat;

        Cameras.Clear();
        foreach (var profile in s.Cameras)
            Cameras.Add(new CameraSettingRowViewModel(profile));
    }

    private void ApplyToSettings()
    {
        var s = _settings.Current;
        s.PreRecordSeconds = PreRecordSeconds;
        s.PostRecordSeconds = PostRecordSeconds;
        s.StorageDirectory = string.IsNullOrWhiteSpace(StorageDirectory)
            ? AppSettings.DefaultStorageDirectory
            : StorageDirectory;
        s.Category = Category;
        s.Discipline = Discipline;
        s.SeriesNumber = SeriesNumber;
        s.FilenameFormat = FilenameFormat;
        s.Cameras = Cameras.Select(c => c.Profile).ToList();
    }

    private string SuggestSuffix() => $"cam-{(char)('a' + Cameras.Count)}";

    private static CameraProfile ToProfile(CameraDevice device, string suffix) => new()
    {
        Id = device.Id,
        DisplayName = device.Name,
        Suffix = suffix,
        SourceType = string.IsNullOrEmpty(device.SourceType) ? device.ProviderName : device.SourceType,
        SourceUrl = device.SourceUrl,
    };
}
