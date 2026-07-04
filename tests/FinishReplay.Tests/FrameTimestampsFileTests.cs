using System;
using System.IO;
using FinishReplay.Services.Recording;
using Xunit;

namespace FinishReplay.Tests;

public class FrameTimestampsFileTests
{
    [Fact]
    public void Write_then_Read_round_trips_millisecond_values()
    {
        var video = Path.Combine(Path.GetTempPath(), $"clip_{Guid.NewGuid():N}.avi");
        try
        {
            FrameTimestampsFile.Write(video, new[] { 0.0, 33.4, 66.9, 250.0 });
            var read = FrameTimestampsFile.Read(video);

            Assert.NotNull(read);
            Assert.Equal(new double[] { 0, 33, 67, 250 }, read!); // rounded to whole ms
        }
        finally
        {
            File.Delete(FrameTimestampsFile.PathFor(video));
        }
    }

    [Fact]
    public void Read_returns_null_when_no_sidecar_exists() =>
        Assert.Null(FrameTimestampsFile.Read(Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid():N}.avi")));

    [Fact]
    public void PathFor_appends_the_ftime_json_suffix() =>
        Assert.Equal("race_001-cam.avi.ftime.json", FrameTimestampsFile.PathFor("race_001-cam.avi"));
}
