namespace FinishReplay.Services.Camera.Providers.Ffmpeg;

/// <summary>
/// Resolves the ffmpeg executable: an explicit path if it exists, otherwise a search of the PATH
/// directories. Returns null when ffmpeg can't be found so callers can surface a clear message.
/// </summary>
public static class FfmpegLocator
{
    public static string? Resolve(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            // An explicit path (with a separator) is used verbatim when it exists.
            if (HasSeparator(configured) && File.Exists(configured))
                return configured;

            var fromPath = SearchPath(configured);
            if (fromPath is not null)
                return fromPath;
        }

        return SearchPath("ffmpeg");
    }

    public static bool IsAvailable(string? configured) => Resolve(configured) is not null;

    private static string? SearchPath(string name)
    {
        var exts = OperatingSystem.IsWindows() ? new[] { ".exe", "" } : new[] { "" };
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator);

        foreach (var dir in dirs)
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            foreach (var ext in exts)
            {
                var candidate = Path.Combine(dir, name + ext);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    private static bool HasSeparator(string s) =>
        s.Contains(Path.DirectorySeparatorChar) || s.Contains(Path.AltDirectorySeparatorChar);
}
