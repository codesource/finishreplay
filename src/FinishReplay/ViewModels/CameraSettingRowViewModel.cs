using CommunityToolkit.Mvvm.ComponentModel;
using FinishReplay.Models;

namespace FinishReplay.ViewModels;

/// <summary>
/// Editable row for one configured camera in the Settings panel: display name, filename suffix,
/// source, and whether it is enabled for recording. Edits write straight back to the wrapped profile.
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
    public string SourceUrl => string.IsNullOrEmpty(Profile.SourceUrl) ? "local device" : Profile.SourceUrl;

    [ObservableProperty] private string _displayName;
    [ObservableProperty] private string _suffix;
    [ObservableProperty] private bool _enabled;

    partial void OnDisplayNameChanged(string value) => Profile.DisplayName = value;
    partial void OnSuffixChanged(string value) => Profile.Suffix = value;
    partial void OnEnabledChanged(bool value) => Profile.Enabled = value;
}
