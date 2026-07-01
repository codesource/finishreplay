using FinishReplay.Models;

namespace FinishReplay.Services.Timing;

/// <summary>
/// Software timing provider used for testing and for operators with no hardware device.
/// UI buttons call <see cref="Emit"/> to inject Start/Stop/Intermediate triggers.
/// </summary>
public sealed class ManualTimingProvider : ITimingProvider
{
    public string Name => "Manual";

    public bool IsConnected { get; private set; }

    public event EventHandler<TimingTrigger>? TriggerReceived;
    public event EventHandler<bool>? ConnectionChanged;

    public Task ConnectAsync(CancellationToken ct = default)
    {
        IsConnected = true;
        ConnectionChanged?.Invoke(this, true);
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        IsConnected = false;
        ConnectionChanged?.Invoke(this, false);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Inject a trigger as if it had arrived from a device. The clip-relative <c>VideoTime</c> is left
    /// at zero and filled in by the recording layer (same as hardware providers).
    /// </summary>
    public void Emit(TimingTriggerType type)
    {
        TriggerReceived?.Invoke(this, new TimingTrigger
        {
            Type = type,
            ReceivedAt = DateTimeOffset.Now,
            VideoTime = TimeSpan.Zero,
            RawMessage = $"MANUAL:{type}",
        });
    }

    public void Dispose() { }
}
