using FinishReplay.Models;

namespace FinishReplay.Services.Timing;

/// <summary>
/// A source of <see cref="TimingTrigger"/> events (manual, ALGE TimY3, or future devices).
/// Implementations raise <see cref="TriggerReceived"/> as events arrive; the recording/timeline
/// layers subscribe without knowing the concrete device.
/// </summary>
public interface ITimingProvider : IDisposable
{
    string Name { get; }

    bool IsConnected { get; }

    event EventHandler<TimingTrigger>? TriggerReceived;
    event EventHandler<bool>? ConnectionChanged;

    Task ConnectAsync(CancellationToken ct = default);
    Task DisconnectAsync();
}
