using FinishReplay.Models;

namespace FinishReplay.Services.Timeline;

/// <summary>
/// Holds the timing markers for the loaded session and converts their
/// <see cref="TimingTrigger.VideoTime"/> into a 0..1 fraction for placement on the timeline bar.
/// </summary>
public sealed class TimelineEngine
{
    private readonly List<TimingTrigger> _markers = new();
    private readonly Dictionary<string, double> _cameraOffsetsMs = new();

    public IReadOnlyList<TimingTrigger> Markers => _markers;

    public event EventHandler? MarkersChanged;
    public event EventHandler? OffsetsChanged;

    public void Set(IEnumerable<TimingTrigger> markers)
    {
        _markers.Clear();
        _markers.AddRange(markers);
        MarkersChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Add(TimingTrigger marker)
    {
        _markers.Add(marker);
        MarkersChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        _markers.Clear();
        MarkersChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Replace the per-camera sync offsets used to align multi-camera replay.</summary>
    public void SetCameraOffsets(IEnumerable<CameraSyncOffset> offsets)
    {
        _cameraOffsetsMs.Clear();
        foreach (var o in offsets)
            _cameraOffsetsMs[o.CameraId] = o.OffsetMs;
        OffsetsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Sync offset (ms) for a camera; 0 if none is known.</summary>
    public double GetCameraOffsetMs(string cameraId) =>
        _cameraOffsetsMs.TryGetValue(cameraId, out var ms) ? ms : 0;

    /// <summary>
    /// Map a master timeline position to a given camera's own position, compensating for its
    /// sync offset (a later camera is shifted back so its frames line up).
    /// </summary>
    public TimeSpan ToCameraTime(string cameraId, TimeSpan masterTime) =>
        masterTime - TimeSpan.FromMilliseconds(GetCameraOffsetMs(cameraId));

    /// <summary>Position of <paramref name="marker"/> along the timeline as a 0..1 fraction.</summary>
    public static double ToFraction(TimingTrigger marker, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero) return 0;
        var fraction = marker.VideoTime.TotalSeconds / duration.TotalSeconds;
        return Math.Clamp(fraction, 0, 1);
    }
}
