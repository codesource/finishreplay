using System.Text.Json.Serialization;

namespace FinishReplay.Models;

/// <summary>The kind of timing event reported by a <see cref="FinishReplay.Services.Timing.ITimingProvider"/>.</summary>
public enum TimingTriggerType
{
    Start,
    Stop,
    Intermediate,
    Unknown,
}

/// <summary>
/// A single timing event (e.g. from an ALGE TimY3). Immutable once created.
/// <see cref="VideoTime"/> is the offset into the recorded clip; <see cref="ReceivedAt"/> is wall-clock time.
/// Serialized with the video offset in milliseconds (<c>videoTimeMs</c>) to match the session schema.
/// </summary>
public sealed class TimingTrigger
{
    public TimingTriggerType Type { get; init; }

    /// <summary>Wall-clock time the trigger was received by the app.</summary>
    public DateTimeOffset ReceivedAt { get; init; }

    /// <summary>Offset of this trigger from the start of the recorded clip. Not serialized directly.</summary>
    [JsonIgnore]
    public TimeSpan VideoTime { get; init; }

    /// <summary>JSON view of <see cref="VideoTime"/> in milliseconds.</summary>
    [JsonPropertyName("videoTimeMs")]
    public double VideoTimeMs
    {
        get => VideoTime.TotalMilliseconds;
        init => VideoTime = TimeSpan.FromMilliseconds(value);
    }

    /// <summary>Raw provider message, kept verbatim for debugging/replay of unparsed data.</summary>
    public string RawMessage { get; init; } = "";
}
