namespace FinishReplay.Services.Recording;

/// <summary>
/// A time-windowed ring of recent JPEG frames used for the pre-record buffer: it always retains the
/// frames from the last <see cref="Window"/> of stream time, dropping older ones. When recording
/// starts, the snapshot is the pre-roll prepended to the clip. Not thread-safe — callers guard it.
/// </summary>
public sealed class FrameRingBuffer
{
    private readonly Queue<(TimeSpan Time, byte[] Data)> _frames = new();

    /// <summary>How much stream time to retain. Zero disables buffering.</summary>
    public TimeSpan Window { get; set; } = TimeSpan.Zero;

    public int Count => _frames.Count;

    /// <summary>Add a frame stamped with its stream time, then drop frames older than the window.</summary>
    public void Add(TimeSpan time, byte[] data)
    {
        if (Window <= TimeSpan.Zero)
        {
            _frames.Clear();
            return;
        }

        _frames.Enqueue((time, data));
        while (_frames.Count > 0 && time - _frames.Peek().Time > Window)
            _frames.Dequeue();
    }

    /// <summary>The retained frames, oldest first — the pre-roll to prepend to a clip.</summary>
    public IReadOnlyList<byte[]> Snapshot() => _frames.Select(f => f.Data).ToList();

    /// <summary>The retained frames with their stream timestamps, oldest first.</summary>
    public IReadOnlyList<(TimeSpan Time, byte[] Data)> SnapshotTimed() => _frames.ToList();

    public void Clear() => _frames.Clear();
}
