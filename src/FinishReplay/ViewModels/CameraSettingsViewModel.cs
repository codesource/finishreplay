using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FinishReplay.Models;
using FinishReplay.Services.Camera;
using FinishReplay.Services.Camera.Providers;
using FinishReplay.Services.Settings;

namespace FinishReplay.ViewModels;

/// <summary>
/// Owns the Cameras settings: the configured-camera list and a type-aware "add camera" form.
/// Reachability is checked on demand only (no background polling): USB cameras by device presence,
/// and network cameras via a per-camera "Test" button. Selecting the USB type populates the list of
/// currently plugged-in devices. Name and suffix are required to add a camera.
/// </summary>
public partial class CameraSettingsViewModel : ObservableObject
{
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(2);

    private readonly ISettingsService _settings;
    private readonly ICameraManager _cameraManager;
    private readonly HttpClient _http = new();

    private HashSet<string> _usbIds = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<CameraDevice> _lastUsb = Array.Empty<CameraDevice>();

    public CameraSettingsViewModel(ISettingsService settings, ICameraManager cameraManager)
    {
        _settings = settings;
        _cameraManager = cameraManager;

        CameraTypes = new[] { UsbCameraProvider.Type, MjpegCameraProvider.Type, RtspCameraProvider.Type };
        _newType = MjpegCameraProvider.Type;
    }

    public ObservableCollection<CameraSettingRowViewModel> Cameras { get; } = new();

    /// <summary>Plugged-in USB devices not yet configured — offered in the add form.</summary>
    public ObservableCollection<CameraDevice> DetectedUsbDevices { get; } = new();

    public IReadOnlyList<string> CameraTypes { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUsbType))]
    [NotifyPropertyChangedFor(nameof(IsNetworkType))]
    [NotifyPropertyChangedFor(nameof(CanAdd))]
    [NotifyCanExecuteChangedFor(nameof(AddCameraCommand))]
    private string _newType;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAdd))]
    [NotifyCanExecuteChangedFor(nameof(AddCameraCommand))]
    private CameraDevice? _newUsbDevice;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAdd))]
    [NotifyCanExecuteChangedFor(nameof(AddCameraCommand))]
    private string _newUrl = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAdd))]
    [NotifyCanExecuteChangedFor(nameof(AddCameraCommand))]
    private string _newName = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAdd))]
    [NotifyCanExecuteChangedFor(nameof(AddCameraCommand))]
    private string _newSuffix = "";

    public bool IsUsbType => NewType == UsbCameraProvider.Type;
    public bool IsNetworkType => !IsUsbType;

    public bool CanAdd =>
        !string.IsNullOrWhiteSpace(NewName) &&
        !string.IsNullOrWhiteSpace(NewSuffix) &&
        (IsUsbType ? NewUsbDevice is not null : !string.IsNullOrWhiteSpace(NewUrl));

    /// <summary>Load cameras from settings and do a one-time status check of the added cameras.</summary>
    public void Load()
    {
        Cameras.Clear();
        foreach (var profile in _settings.Current.Cameras)
            Cameras.Add(new CameraSettingRowViewModel(profile));

        _ = InitializeAsync();
    }

    /// <summary>Write the current camera list back into settings (called by Save).</summary>
    public void ApplyTo(AppSettings s) => s.Cameras = Cameras.Select(c => c.Profile).ToList();

    partial void OnNewTypeChanged(string value)
    {
        // Selecting USB populates the list of plugged-in devices.
        if (IsUsbType)
            _ = RefreshUsbDevicesAsync();
    }

    partial void OnNewUsbDeviceChanged(CameraDevice? value)
    {
        // Default the name to the USB device name (still editable/required).
        if (value is not null && string.IsNullOrWhiteSpace(NewName))
            NewName = value.Name;
    }

    [RelayCommand]
    private Task RefreshUsbDevices() => RefreshUsbDevicesAsync();

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void AddCamera()
    {
        CameraProfile profile;
        if (IsUsbType)
        {
            if (NewUsbDevice is null) return;
            profile = new CameraProfile
            {
                Id = NewUsbDevice.Id,
                DisplayName = NewName.Trim(),
                Suffix = NewSuffix.Trim(),
                SourceType = UsbCameraProvider.Type,
            };
            DetectedUsbDevices.Remove(NewUsbDevice);
        }
        else
        {
            profile = new CameraProfile
            {
                Id = NewUrl.Trim(),
                DisplayName = NewName.Trim(),
                Suffix = NewSuffix.Trim(),
                SourceType = NewType,
                SourceUrl = NewUrl.Trim(),
            };
        }

        var row = new CameraSettingRowViewModel(profile);
        Cameras.Add(row);

        NewName = "";
        NewSuffix = "";
        NewUrl = "";
        NewUsbDevice = null;

        _ = TestCameraAsync(row);
    }

    [RelayCommand]
    private void RemoveCamera(CameraSettingRowViewModel? row)
    {
        if (row is not null)
            Cameras.Remove(row);
    }

    /// <summary>Check one camera's reachability on demand (the per-row "Test" button).</summary>
    [RelayCommand]
    private Task TestCamera(CameraSettingRowViewModel? row) =>
        row is null ? Task.CompletedTask : TestCameraAsync(row);

    private async Task InitializeAsync()
    {
        await RefreshUsbAsync();
        foreach (var row in Cameras.ToList())
            await TestCameraAsync(row);
    }

    private async Task TestCameraAsync(CameraSettingRowViewModel row)
    {
        row.Status = CameraStatus.Checking;
        var profile = row.Profile;

        if (profile.SourceType == UsbCameraProvider.Type)
        {
            await RefreshUsbAsync();
            row.Status = _usbIds.Contains(profile.Id) ? CameraStatus.Reachable : CameraStatus.Unreachable;
            return;
        }

        bool ok;
        if (profile.SourceType == RtspCameraProvider.Type)
        {
            var (host, port) = CameraReachability.ParseRtspEndpoint(profile.SourceUrl);
            ok = await CameraReachability.CheckTcpAsync(host, port, CheckTimeout);
        }
        else
        {
            ok = await CameraReachability.CheckHttpAsync(profile.SourceUrl, _http, CheckTimeout);
        }

        row.Status = ok ? CameraStatus.Reachable : CameraStatus.Unreachable;
    }

    private async Task RefreshUsbDevicesAsync()
    {
        await RefreshUsbAsync();

        var configured = new HashSet<string>(Cameras.Select(c => c.Profile.Id), StringComparer.OrdinalIgnoreCase);
        DetectedUsbDevices.Clear();
        foreach (var device in _lastUsb.Where(d => !configured.Contains(d.Id)))
            DetectedUsbDevices.Add(device);
    }

    private async Task RefreshUsbAsync()
    {
        try
        {
            var devices = await _cameraManager.DiscoverAsync();
            var usb = devices.Where(d => d.SourceType == UsbCameraProvider.Type).ToList();
            _lastUsb = usb;
            _usbIds = new HashSet<string>(usb.Select(d => d.Id), StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            // Enumeration failed (e.g. no native enumerator / ffmpeg) — keep the previous set.
        }
    }
}
