namespace FinishReplay.Services.Calibration;

/// <summary>
/// A device that produces a visible calibration flash (LED via USB relay, GPIO, ...).
/// Abstracted so different trigger hardware can be added later.
/// </summary>
public interface ITriggerOutput
{
    string Id { get; }
    string Name { get; }
    bool IsAvailable { get; }

    /// <summary>
    /// Fire the flash and return the monotonic timestamp at which it was triggered
    /// (from <see cref="MonotonicClock"/>), so it can be compared against frame arrival times.
    /// </summary>
    Task<TimeSpan> FireAsync(CancellationToken cancellationToken = default);
}
