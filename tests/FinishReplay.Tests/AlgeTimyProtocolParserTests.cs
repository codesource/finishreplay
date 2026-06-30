using FinishReplay.Models;
using FinishReplay.Services.Timing;
using Xunit;

namespace FinishReplay.Tests;

public class AlgeTimyProtocolParserTests
{
    private static readonly DateTimeOffset At = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("0001 C0M 00:00:00.0000 00", TimingTriggerType.Start)]
    [InlineData("0002 C1  00:01:23.4567 00", TimingTriggerType.Stop)]
    [InlineData("0003 C2  00:00:30.0000 00", TimingTriggerType.Intermediate)]
    [InlineData("0008 C8  00:02:00.1200 01", TimingTriggerType.Intermediate)]
    public void Maps_channel_to_trigger_type(string line, TimingTriggerType expected)
    {
        var trigger = AlgeTimyProtocolParser.Parse(line, At);

        Assert.NotNull(trigger);
        Assert.Equal(expected, trigger!.Type);
        Assert.Equal(At, trigger.ReceivedAt);
        Assert.Equal(line, trigger.RawMessage);
        Assert.Equal(TimeSpan.Zero, trigger.VideoTime); // clip offset filled in by the recording layer
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("RT 12:00:00.0000")]            // running time / heartbeat
    [InlineData("TIMY: some status")]            // status line
    [InlineData("just some banner text")]        // no channel token
    public void Ignores_non_events(string line)
    {
        Assert.Null(AlgeTimyProtocolParser.Parse(line, At));
    }

    [Fact]
    public void Channel_mapping_defaults()
    {
        Assert.Equal(TimingTriggerType.Start, AlgeTimyProtocolParser.MapChannel(0));
        Assert.Equal(TimingTriggerType.Stop, AlgeTimyProtocolParser.MapChannel(1));
        Assert.Equal(TimingTriggerType.Intermediate, AlgeTimyProtocolParser.MapChannel(5));
        Assert.Equal(TimingTriggerType.Unknown, AlgeTimyProtocolParser.MapChannel(9));
    }

    [Fact]
    public void Parses_device_time_with_and_without_hours()
    {
        Assert.Equal(new TimeSpan(0, 1, 23) + TimeSpan.FromMilliseconds(456.7),
            AlgeTimyProtocolParser.TryParseDeviceTime("0002 C1 00:01:23.4567 00"));

        Assert.Equal(new TimeSpan(0, 0, 12, 30, 0),
            AlgeTimyProtocolParser.TryParseDeviceTime("0003 C2 12:30.0000 00"));

        Assert.Null(AlgeTimyProtocolParser.TryParseDeviceTime("no time here"));
    }
}
