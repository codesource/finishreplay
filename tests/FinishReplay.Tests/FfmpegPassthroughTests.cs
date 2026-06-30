using FinishReplay.Services.Camera.Providers.Ffmpeg;
using Xunit;

namespace FinishReplay.Tests;

public class FfmpegPassthroughTests
{
    [Fact]
    public void Rtsp_passthrough_copies_video_to_mp4_without_reencode()
    {
        var args = FfmpegArguments.ForRtspPassthroughMp4("rtsp://cam/live", "/clips/race.mp4").ToList();

        Assert.Equal("rtsp://cam/live", args[args.IndexOf("-i") + 1]);
        Assert.Equal("copy", args[args.IndexOf("-c:v") + 1]);          // no re-encode
        Assert.Equal("mp4", args[args.IndexOf("-f") + 1]);
        Assert.Contains("+frag_keyframe+empty_moov", args);            // playable even if killed
        Assert.Equal("/clips/race.mp4", args[^1]);                     // output path last
        Assert.DoesNotContain("-nostdin", args);                      // stdin stays open for "q" stop
        Assert.Contains("-rtsp_transport", args);
    }

    [Fact]
    public void File_to_mjpeg_decodes_a_clip_for_replay()
    {
        var args = FfmpegArguments.ForFileToMjpeg("/clips/race.mp4").ToList();

        Assert.Equal("/clips/race.mp4", args[args.IndexOf("-i") + 1]);
        Assert.Equal("mjpeg", args[args.IndexOf("-f") + 1]);
        Assert.Equal("pipe:1", args[^1]);
    }
}
