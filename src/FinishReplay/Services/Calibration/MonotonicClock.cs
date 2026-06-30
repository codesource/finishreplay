using System.Diagnostics;

namespace FinishReplay.Services.Calibration;

/// <summary>
/// Process-wide monotonic clock used to timestamp the calibration trigger and frame arrivals
/// on the same time base. Backed by <see cref="Stopwatch"/> (never goes backwards, unaffected
/// by wall-clock adjustments).
/// </summary>
public static class MonotonicClock
{
    private static readonly Stopwatch Sw = Stopwatch.StartNew();

    public static TimeSpan Now => Sw.Elapsed;
}
