using FinishReplay.Services.Camera.Providers.Ffmpeg;
using Xunit;

namespace FinishReplay.Tests;

public class FfmpegLocatorTests
{
    // Use Path.Combine throughout so expectations match the host's separator (the resolver does too).
    private static string C(params string[] parts) => Path.Combine(parts);

    private static Func<string, bool> Existing(params string[] paths)
    {
        var set = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        return set.Contains;
    }

    [Fact]
    public void Explicit_existing_path_wins()
    {
        var explicitPath = C("custom", "ffmpeg.exe");
        var resolved = FfmpegLocator.ResolveCore(
            explicitPath,
            new[] { C("app"), C("path") },
            Existing(explicitPath, C("path", "ffmpeg.exe")),
            windows: true);

        Assert.Equal(explicitPath, resolved);
    }

    [Fact]
    public void Finds_executable_in_first_matching_directory_in_order()
    {
        var dirs = new[] { C("app"), C("app", "ffmpeg"), C("path") };
        var resolved = FfmpegLocator.ResolveCore(
            "ffmpeg",
            dirs,
            Existing(C("app", "ffmpeg", "ffmpeg.exe"), C("path", "ffmpeg.exe")),
            windows: true);

        Assert.Equal(C("app", "ffmpeg", "ffmpeg.exe"), resolved); // app\ffmpeg precedes PATH
    }

    [Fact]
    public void Configured_bare_name_is_tried_before_default()
    {
        var dir = C("usr", "bin");
        var resolved = FfmpegLocator.ResolveCore(
            "ffmpeg7",
            new[] { dir },
            Existing(C(dir, "ffmpeg7"), C(dir, "ffmpeg")),
            windows: false);

        Assert.Equal(C(dir, "ffmpeg7"), resolved);
    }

    [Fact]
    public void Windows_appends_exe_to_a_bare_configured_name()
    {
        var dir = C("tools");
        var resolved = FfmpegLocator.ResolveCore("ffmpeg", new[] { dir }, Existing(C(dir, "ffmpeg.exe")), windows: true);
        Assert.Equal(C(dir, "ffmpeg.exe"), resolved);
    }

    [Fact]
    public void Returns_null_when_nothing_matches()
    {
        var resolved = FfmpegLocator.ResolveCore(
            "ffmpeg",
            new[] { C("usr", "bin"), C("usr", "local", "bin") },
            Existing(C("somewhere", "else", "ffmpeg")),
            windows: false);

        Assert.Null(resolved);
    }

    [Fact]
    public void Bundled_and_appdata_directories_are_exposed()
    {
        Assert.EndsWith("ffmpeg", FfmpegLocator.BundledDirectory);
        Assert.Contains("FinishReplay", FfmpegLocator.AppDataDirectory);
    }
}
