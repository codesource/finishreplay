using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using FinishReplay.Models;
using FinishReplay.Services.Camera.Providers;

namespace FinishReplay.ViewModels;

/// <summary>
/// Editable row for one configured camera in the Settings panel: display name, filename suffix,
/// source and enabled state, plus a live reachability status. Edits write straight back to the
/// wrapped profile.
/// </summary>
public partial class CameraSettingRowViewModel : ObservableObject
{
    public CameraSettingRowViewModel(CameraProfile profile)
    {
        Profile = profile;
        _displayName = profile.DisplayName;
        _suffix = profile.Suffix;
        _enabled = profile.Enabled;
    }

    public CameraProfile Profile { get; }

    public string SourceType => Profile.SourceType;
    public bool IsUsb => Profile.SourceType == UsbCameraProvider.Type;

    /// <summary>"USB", or "MJPEG — http://…" for network cameras.</summary>
    public string SourceLabel => IsUsb
        ? "USB device"
        : $"{Profile.SourceType} — {Profile.SourceUrl}";

    [ObservableProperty] private string _displayName;
    [ObservableProperty] private string _suffix;
    [ObservableProperty] private bool _enabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(StatusBrush))]
    private CameraStatus _status = CameraStatus.Unknown;

    public string StatusText => Status switch
    {
        CameraStatus.Reachable => "Online",
        CameraStatus.Unreachable => "Offline",
        CameraStatus.Checking => "Checking…",
        _ => "Unknown",
    };

    public IBrush StatusBrush => new SolidColorBrush(Status switch
    {
        CameraStatus.Reachable => Color.Parse("#16A34A"),   // green
        CameraStatus.Unreachable => Color.Parse("#DC2626"), // red
        CameraStatus.Checking => Color.Parse("#9CA3AF"),    // gray
        _ => Color.Parse("#D1D5DB"),                         // light gray
    });

    partial void OnDisplayNameChanged(string value) => Profile.DisplayName = value;
    partial void OnSuffixChanged(string value) => Profile.Suffix = value;
    partial void OnEnabledChanged(bool value) => Profile.Enabled = value;
}
