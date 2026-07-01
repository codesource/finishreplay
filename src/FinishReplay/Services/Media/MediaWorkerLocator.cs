namespace FinishReplay.Services.Media;

/// <summary>
/// Locates the FinishReplay media-worker executable (the isolated FFmpeg host). Looks next to the app
/// (the packaged/bundled location) and, in a dev build, at the sibling project's output. Returns null
/// when it can't be found so callers fall back to the external-ffmpeg backend.
/// </summary>
public static class MediaWorkerLocator
{
    public static string ExecutableName =>
        OperatingSystem.IsWindows() ? "FinishReplay.MediaWorker.exe" : "FinishReplay.MediaWorker";

    public static string? Resolve()
    {
        var beside = Path.Combine(AppContext.BaseDirectory, ExecutableName);
        if (File.Exists(beside))
            return beside;

        var dev = TryDevOutputPath();
        return dev is not null && File.Exists(dev) ? dev : null;
    }

    public static bool IsAvailable => Resolve() is not null;

    // Dev only: …/src/FinishReplay/bin/<cfg>/net9.0/  →  …/src/FinishReplay.MediaWorker/bin/<cfg>/net9.0/
    private static string? TryDevOutputPath()
    {
        var baseDir = AppContext.BaseDirectory.Replace('\\', '/').TrimEnd('/');
        const string marker = "/FinishReplay/bin/";
        var idx = baseDir.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return null;

        var tail = baseDir[(idx + marker.Length)..];
        var candidate = baseDir[..idx] + "/FinishReplay.MediaWorker/bin/" + tail + "/" + ExecutableName;
        return Path.GetFullPath(candidate);
    }
}
