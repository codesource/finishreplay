using CommunityToolkit.Mvvm.ComponentModel;
using FinishReplay.Models;

namespace FinishReplay.ViewModels;

/// <summary>
/// One camera within a loaded replay session. Selectable (several can play at once) and shows the
/// camera's own playback time, which is the master timeline position compensated by its sync offset
/// so all selected cameras stay frame-aligned.
/// </summary>
public partial class ReplayCameraViewModel : ObservableObject
{
    public ReplayCameraViewModel(SessionCamera camera)
    {
        Camera = camera;
    }

    public SessionCamera Camera { get; }

    public string CameraId => Camera.CameraId;
    public string Name => Camera.Name;
    public string SourceType => Camera.SourceType;
    public double SyncOffsetMs => Camera.SyncOffsetMs;
    public string VideoFile => Camera.VideoFile;

    [ObservableProperty] private bool _isSelected = true;
    [ObservableProperty] private string _cameraTimeText = "00:00.000";

    /// <summary>Recompute this camera's displayed time from the master position and its offset.</summary>
    public void UpdateTime(TimeSpan master)
    {
        var t = master - TimeSpan.FromMilliseconds(SyncOffsetMs);
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        CameraTimeText = t.ToString(@"mm\:ss\.fff");
    }
}
