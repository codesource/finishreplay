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

    // Per-frame times in ms from clip start. Real capture timestamps when a sidecar exists (captures
    // WiFi jitter/variable latency); otherwise synthesized from the nominal fps.
    private double[] _frameTimesMs = Array.Empty<double>();

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

    /// <summary>The clip's frame rate (used to place this camera's frames on the master timeline).</summary>
    public double Fps => _fps;

    [ObservableProperty] private bool _isSelected = true;
    [ObservableProperty] private string _cameraTimeText = "00:00.000";
    [ObservableProperty] private Bitmap? _currentFrame;

    /// <summary>
    /// Provide the decoded clip's frames so this camera can render them during replay. When
    /// <paramref name="frameTimesMs"/> is supplied (and matches the frame count), those real per-frame
    /// timestamps drive playback; otherwise times are synthesized from <paramref name="fps"/>.
    /// </summary>
    public void LoadFrames(IReadOnlyList<byte[]> frames, double fps, IReadOnlyList<double>? frameTimesMs = null)
    {
        _frames = frames;
        _fps = fps <= 0 ? 30 : fps;
        _currentIndex = -1;

        if (frameTimesMs is not null && frameTimesMs.Count == frames.Count && frames.Count > 0)
        {
            _frameTimesMs = frameTimesMs.ToArray();
        }
        else
        {
            _frameTimesMs = new double[frames.Count];
            for (var i = 0; i < frames.Count; i++)
                _frameTimesMs[i] = i / _fps * 1000.0;
        }

        OnPropertyChanged(nameof(FrameCount));
    }

    /// <summary>This camera's frame times on the master timeline (clip time + sync offset), in ms.</summary>
    public IEnumerable<double> MasterFrameTimesMs => _frameTimesMs.Select(ms => ms + SyncOffsetMs);

    /// <summary>Render the frame for the given master position, shifted by this camera's offset.</summary>
    public void UpdateTime(TimeSpan master)
    {
        var t = master - TimeSpan.FromMilliseconds(SyncOffsetMs);
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        CameraTimeText = t.ToString(@"mm\:ss\.fff");

        if (_frames.Count == 0)
            return;

        var index = FrameIndexAt(t.TotalMilliseconds);
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

    /// <summary>
    /// Index of the latest frame whose timestamp is ≤ <paramref name="tMs"/> — i.e. hold the previous
    /// frame until this camera actually has a newer one. Binary search over the per-frame times.
    /// </summary>
    private int FrameIndexAt(double tMs)
    {
        var times = _frameTimesMs;
        if (times.Length == 0)
            return 0;
        if (tMs <= times[0])
            return 0;
        if (tMs >= times[^1])
            return times.Length - 1;

        int lo = 0, hi = times.Length - 1;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (times[mid] <= tMs) lo = mid;
            else hi = mid - 1;
        }
        return lo;
    }
}
