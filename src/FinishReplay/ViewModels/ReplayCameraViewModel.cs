using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using FinishReplay.Models;

namespace FinishReplay.ViewModels;

/// <summary>
/// One camera within a loaded replay session. Selectable (several can play at once). Holds the
/// recorded JPEG frames and decodes the one matching the master position (compensated by this
/// camera's sync offset) so all selected cameras stay frame-aligned.
/// </summary>
public partial class ReplayCameraViewModel : ObservableObject
{
    private IReadOnlyList<byte[]> _frames = Array.Empty<byte[]>();
    private double _fps = 30;
    private int _currentIndex = -1;

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

    public int FrameCount => _frames.Count;

    [ObservableProperty] private bool _isSelected = true;
    [ObservableProperty] private string _cameraTimeText = "00:00.000";
    [ObservableProperty] private Bitmap? _currentFrame;

    /// <summary>Provide the decoded clip's frames so this camera can render them during replay.</summary>
    public void LoadFrames(IReadOnlyList<byte[]> frames, double fps)
    {
        _frames = frames;
        _fps = fps <= 0 ? 30 : fps;
        _currentIndex = -1;
        OnPropertyChanged(nameof(FrameCount));
    }

    /// <summary>Render the frame for the given master position, shifted by this camera's offset.</summary>
    public void UpdateTime(TimeSpan master)
    {
        var t = master - TimeSpan.FromMilliseconds(SyncOffsetMs);
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        CameraTimeText = t.ToString(@"mm\:ss\.fff");

        if (_frames.Count == 0)
            return;

        var index = Math.Clamp((int)(t.TotalSeconds * _fps), 0, _frames.Count - 1);
        if (index == _currentIndex)
            return;
        _currentIndex = index;

        try
        {
            using var ms = new MemoryStream(_frames[index]);
            var bmp = new Bitmap(ms);
            var old = CurrentFrame;
            CurrentFrame = bmp;
            old?.Dispose();
        }
        catch
        {
            // ignore undecodable frame
        }
    }
}
