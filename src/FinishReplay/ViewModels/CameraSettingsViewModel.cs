using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FinishReplay.Models;
using FinishReplay.Services.Camera;
using FinishReplay.Services.Camera.Providers;
using FinishReplay.Services.Settings;

namespace FinishReplay.ViewModels;

/// <summary>
/// Owns the Cameras settings: the configured-camera list with live reachability status, and a
/// type-aware "add camera" form. USB cameras are auto-detected while plugged in (they appear in the
/// add form); network cameras (MJPEG/RTSP) are added by URL and their status is polled on an interval.
/// Name and suffix are required to add a camera.
/// </summary>
public partial class CameraSettingsViewModel : ObservableObject
{
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(2);
    private const double UsbRescanSeconds = 15; // USB enumeration is costly (ffmpeg) — throttle it

    private readonly ISettingsService _settings;
    private readonly ICameraManager _cameraManager;
    private readonly HttpClient _http = new();
    private readonly DispatcherTimer _timer;

    private bool _busy;
    private double _secondsSinceUsbScan = UsbRescanSeconds; // force a scan on first tick
    private HashSet<string> _usbIds = new(StringComparer.OrdinalIgnoreCase);

    public CameraSettingsViewModel(ISettingsService settings, ICameraManager cameraManager)
    {
        _settings = settings;
        _cameraManager = cameraManager;

        CameraTypes = new[] { UsbCameraProvider.Type, MjpegCameraProvider.Type, RtspCameraProvider.Type };
        _newType = MjpegCameraProvider.Type;

        _timer = new DispatcherTimer();
        _timer.Tick += (_, _) => _ = RefreshAsync();
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

    [ObservableProperty] private int _statusIntervalSeconds = 5;

    public bool IsUsbType => NewType == UsbCameraProvider.Type;
    public bool IsNetworkType => !IsUsbType;

    public bool CanAdd =>
        !string.IsNullOrWhiteSpace(NewName) &&
        !string.IsNullOrWhiteSpace(NewSuffix) &&
        (IsUsbType ? NewUsbDevice is not null : !string.IsNullOrWhiteSpace(NewUrl));

    /// <summary>Load cameras from settings and start status polling.</summary>
    public void Load()
    {
        var s = _settings.Current;
        StatusIntervalSeconds = s.CameraStatusIntervalSeconds < 1 ? 5 : s.CameraStatusIntervalSeconds;

        Cameras.Clear();
        foreach (var profile in s.Cameras)
            Cameras.Add(new CameraSettingRowViewModel(profile));

        _timer.Interval = TimeSpan.FromSeconds(StatusIntervalSeconds);
        _timer.Start();
        _ = RefreshAsync();
    }

    /// <summary>Write the current camera list back into settings (called by Save).</summary>
    public void ApplyTo(AppSettings s)
    {
        s.Cameras = Cameras.Select(c => c.Profile).ToList();
        s.CameraStatusIntervalSeconds = StatusIntervalSeconds < 1 ? 1 : StatusIntervalSeconds;
    }

    partial void OnStatusIntervalSecondsChanged(int value)
    {
        var seconds = Math.Max(1, value);
        _timer.Interval = TimeSpan.FromSeconds(seconds);
    }

    partial void OnNewUsbDeviceChanged(CameraDevice? value)
    {
        // Default the name to the USB device name (still editable/required).
        if (value is not null && string.IsNullOrWhiteSpace(NewName))
            NewName = value.Name;
    }

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

        Cameras.Add(new CameraSettingRowViewModel(profile));
        DetectedUsbDevices.Remove(NewUsbDevice!);

        NewName = "";
        NewSuffix = "";
        NewUrl = "";
        NewUsbDevice = null;

        _ = RefreshAsync();
    }

    [RelayCommand]
    private void RemoveCamera(CameraSettingRowViewModel? row)
    {
        if (row is not null)
            Cameras.Remove(row);
    }

    [RelayCommand]
    private Task Refresh() => RefreshAsync();

    private async Task RefreshAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            await RescanUsbIfDueAsync();
            UpdateDetectedUsb();
            await UpdateStatusesAsync();
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task RescanUsbIfDueAsync()
    {
        _secondsSinceUsbScan += StatusIntervalSeconds;
        if (_secondsSinceUsbScan < UsbRescanSeconds && _usbIds.Count > 0)
            return;

        _secondsSinceUsbScan = 0;
        try
        {
            var devices = await _cameraManager.DiscoverAsync();
            var usb = devices.Where(d => d.SourceType == UsbCameraProvider.Type).ToList();
            _usbIds = new HashSet<string>(usb.Select(d => d.Id), StringComparer.OrdinalIgnoreCase);
            _lastUsb = usb;
        }
        catch
        {
            // Enumeration failed (e.g. ffmpeg missing) — leave the previous set.
        }
    }

    private IReadOnlyList<CameraDevice> _lastUsb = Array.Empty<CameraDevice>();

    private void UpdateDetectedUsb()
    {
        var configured = new HashSet<string>(Cameras.Select(c => c.Profile.Id), StringComparer.OrdinalIgnoreCase);

        DetectedUsbDevices.Clear();
        foreach (var device in _lastUsb.Where(d => !configured.Contains(d.Id)))
            DetectedUsbDevices.Add(device);
    }

    private async Task UpdateStatusesAsync()
    {
        foreach (var row in Cameras)
        {
            var profile = row.Profile;
            bool ok;

            if (profile.SourceType == UsbCameraProvider.Type)
            {
                ok = _usbIds.Contains(profile.Id);
            }
            else if (profile.SourceType == RtspCameraProvider.Type)
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
    }
}
