using System.Globalization;
using System.Text.RegularExpressions;
using FinishReplay.Models;

namespace FinishReplay.Services.Timing;

/// <summary>
/// Parses ALGE Timy ASCII protocol lines into <see cref="TimingTrigger"/>s. The same text lines are
/// produced over the Timy's serial/USB-serial port and by the official USB DLL's <c>LineReceived</c>
/// event, so this parser is shared by any transport.
///
/// A typical event line looks like: <c>0002 C1  00:01:23.4567 00</c> — a running number, a channel
/// (<c>C0</c>/<c>C1</c>/…, optional <c>M</c> = manual key), the time, and a group/lane.
///
/// Channel→type uses the common default (C0 start, C1 finish/stop, C2–C8 intermediate). Real events
/// can remap channels; TODO: make the mapping configurable per event when needed.
/// </summary>
public static class AlgeTimyProtocolParser
{
    // Channel token: c0..c9 (any case), optional trailing M (manual key), as a whole token.
    private static readonly Regex ChannelToken = new(@"\b[cC](?<ch>[0-9])[mM]?\b", RegexOptions.Compiled);

    // Time token: HH:MM:SS.ffff or MM:SS.ffff (2–4 fractional digits).
    private static readonly Regex TimeToken = new(@"\b(?:(?<h>\d{1,2}):)?(?<m>\d{1,2}):(?<s>\d{2})\.(?<f>\d{2,4})\b", RegexOptions.Compiled);

    /// <summary>
    /// Parse a raw line. Returns null for non-events (blank, running-time/heartbeat, or anything with
    /// no channel token). <see cref="TimingTrigger.VideoTime"/> is left at zero — the recording layer
    /// fills in the clip offset from <see cref="TimingTrigger.ReceivedAt"/>.
    /// </summary>
    public static TimingTrigger? Parse(string line, DateTimeOffset receivedAt)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        var text = line.Trim();

        // Continuous running-time and status/heartbeat lines are not discrete events.
        if (text.StartsWith("RT", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("TIMY", StringComparison.OrdinalIgnoreCase))
            return null;

        var channel = ChannelToken.Match(text);
        if (!channel.Success)
            return null;

        return new TimingTrigger
        {
            Type = MapChannel(int.Parse(channel.Groups["ch"].Value, CultureInfo.InvariantCulture)),
            ReceivedAt = receivedAt,
            VideoTime = TimeSpan.Zero,
            RawMessage = text,
        };
    }

    /// <summary>Default ALGE Timy channel mapping.</summary>
    public static TimingTriggerType MapChannel(int channel) => channel switch
    {
        0 => TimingTriggerType.Start,
        1 => TimingTriggerType.Stop,
        >= 2 and <= 8 => TimingTriggerType.Intermediate,
        _ => TimingTriggerType.Unknown,
    };

    /// <summary>Best-effort parse of the device time-of-day/elapsed time in a line (for display/debug).</summary>
    public static TimeSpan? TryParseDeviceTime(string line)
    {
        var m = TimeToken.Match(line ?? "");
        if (!m.Success)
            return null;

        var hours = m.Groups["h"].Success ? int.Parse(m.Groups["h"].Value, CultureInfo.InvariantCulture) : 0;
        var minutes = int.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture);
        var seconds = int.Parse(m.Groups["s"].Value, CultureInfo.InvariantCulture);
        var frac = m.Groups["f"].Value.PadRight(4, '0')[..4];
        var tenThousandths = int.Parse(frac, CultureInfo.InvariantCulture);

        return new TimeSpan(0, hours, minutes, seconds) + TimeSpan.FromTicks(tenThousandths * (TimeSpan.TicksPerSecond / 10_000));
    }
}
