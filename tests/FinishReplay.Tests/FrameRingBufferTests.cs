using FinishReplay.Services.Recording;
using Xunit;

namespace FinishReplay.Tests;

public class FrameRingBufferTests
{
    private static byte[] B(byte v) => new[] { v };

    [Fact]
    public void Retains_only_frames_within_the_time_window()
    {
        var buf = new FrameRingBuffer { Window = TimeSpan.FromSeconds(2) };

        buf.Add(TimeSpan.FromSeconds(0.0), B(1));
        buf.Add(TimeSpan.FromSeconds(1.0), B(2));
        buf.Add(TimeSpan.FromSeconds(2.5), B(3)); // now=2.5 → frame@0.0 (Δ2.5>2) dropped
        buf.Add(TimeSpan.FromSeconds(3.0), B(4)); // now=3.0 → frame@1.0 (Δ2.0, not >2) kept

        var snap = buf.Snapshot();
        Assert.Equal(new[] { B(2), B(3), B(4) }, snap);
    }

    [Fact]
    public void Zero_window_buffers_nothing()
    {
        var buf = new FrameRingBuffer { Window = TimeSpan.Zero };
        buf.Add(TimeSpan.FromSeconds(0), B(1));
        buf.Add(TimeSpan.FromSeconds(1), B(2));
        Assert.Equal(0, buf.Count);
        Assert.Empty(buf.Snapshot());
    }

    [Fact]
    public void Snapshot_is_oldest_first()
    {
        var buf = new FrameRingBuffer { Window = TimeSpan.FromSeconds(10) };
        buf.Add(TimeSpan.FromSeconds(0), B(10));
        buf.Add(TimeSpan.FromSeconds(1), B(20));
        buf.Add(TimeSpan.FromSeconds(2), B(30));
        Assert.Equal(new[] { B(10), B(20), B(30) }, buf.Snapshot());
    }
}
