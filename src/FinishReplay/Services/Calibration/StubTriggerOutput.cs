namespace FinishReplay.Services.Calibration;

/// <summary>
/// Placeholder trigger output. Reports the monotonic time of the "flash" but drives no hardware.
///
/// TODO: implement real outputs (e.g. USB relay over serial, GPIO) behind <see cref="ITriggerOutput"/>.
/// </summary>
public sealed class StubTriggerOutput : ITriggerOutput
{
    public string Id => "stub";
    public string Name => "Simulated flash (no hardware)";
    public bool IsAvailable => true;

    public Task<TimeSpan> FireAsync(CancellationToken cancellationToken = default)
    {
        // TODO: actuate the relay/LED here; capture the timestamp as close to actuation as possible.
        return Task.FromResult(MonotonicClock.Now);
    }
}
