namespace FinishReplay.Services.Camera.Providers.Ffmpeg;

/// <summary>
/// Resolves the ffmpeg executable so end users rarely need to configure it. Searches, in order: an
/// explicit configured path, the app's own folder and a bundled <c>ffmpeg/</c> subfolder (the
/// packaging drop-point), the app-data ffmpeg folder, the PATH, and the platform's common install
/// locations (winget / chocolatey / scoop / Program Files on Windows; Homebrew / /usr/local on
/// macOS/Linux). Returns null when ffmpeg genuinely can't be found.
/// </summary>
public static class FfmpegLocator
{
    public static string ExecutableName => OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";

    /// <summary>Folder next to the app where a bundled ffmpeg can be placed by packaging.</summary>
    public static string BundledDirectory => Path.Combine(AppContext.BaseDirectory, "ffmpeg");

    /// <summary>App-data folder where a downloaded ffmpeg would live.</summary>
    public static string AppDataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FinishReplay", "ffmpeg");

    public static string? Resolve(string? configured) =>
        ResolveCore(configured, CandidateDirectories(), File.Exists, OperatingSystem.IsWindows());

    public static bool IsAvailable(string? configured) => Resolve(configured) is not null;

    /// <summary>
    /// Pure resolution core (no environment access) so the search can be unit-tested.
    /// Precedence: explicit existing path → each directory in <paramref name="directories"/> (in order),
    /// trying the configured bare name first, then the platform executable name.
    /// </summary>
    public static string? ResolveCore(string? configured, IEnumerable<string> directories, Func<string, bool> exists, bool windows)
    {
        var exe = windows ? "ffmpeg.exe" : "ffmpeg";

        // 1. An explicit path (contains a separator) is honoured when it exists.
        if (!string.IsNullOrWhiteSpace(configured) && HasSeparator(configured) && exists(configured))
            return configured;

        // Names to try in each directory: a configured bare name (e.g. "ffmpeg7"), then the default.
        var names = new List<string>();
        if (!string.IsNullOrWhiteSpace(configured) && !HasSeparator(configured))
        {
            names.Add(configured);
            if (windows && !configured.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                names.Add(configured + ".exe");
        }
        names.Add(exe);

        foreach (var dir in directories)
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            foreach (var name in names)
            {
                var candidate = Path.Combine(dir, name);
                if (exists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    private static IReadOnlyList<string> CandidateDirectories()
    {
        var dirs = new List<string>
        {
            // App / bundle locations first so a shipped ffmpeg wins.
            AppContext.BaseDirectory,
            BundledDirectory,
            Path.Combine(BundledDirectory, "bin"),
            Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg"),
            AppDataDirectory,
            Path.Combine(AppDataDirectory, "bin"),
        };

        // PATH.
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        dirs.AddRange(path.Split(Path.PathSeparator));

        // Common per-platform install locations not always on PATH.
        if (OperatingSystem.IsWindows())
        {
            AddEnv(dirs, "ProgramFiles", "ffmpeg", "bin");
            AddEnv(dirs, "ProgramW6432", "ffmpeg", "bin");
            AddEnv(dirs, "ProgramFiles(x86)", "ffmpeg", "bin");
            AddEnv(dirs, "LOCALAPPDATA", "Microsoft", "WinGet", "Links");
            AddEnv(dirs, "ProgramData", "chocolatey", "bin");
            AddEnv(dirs, "USERPROFILE", "scoop", "shims");
            dirs.Add(@"C:\ffmpeg\bin");
        }
        else if (OperatingSystem.IsMacOS())
        {
            dirs.Add("/opt/homebrew/bin");
            dirs.Add("/usr/local/bin");
            dirs.Add("/usr/bin");
            dirs.Add("/opt/local/bin");
        }
        else
        {
            dirs.Add("/usr/bin");
            dirs.Add("/usr/local/bin");
            dirs.Add("/snap/bin");
        }

        return dirs;
    }

    private static void AddEnv(List<string> dirs, string envVar, params string[] sub)
    {
        var root = Environment.GetEnvironmentVariable(envVar);
        if (!string.IsNullOrWhiteSpace(root))
            dirs.Add(Path.Combine(new[] { root }.Concat(sub).ToArray()));
    }

    private static bool HasSeparator(string s) =>
        s.Contains(Path.DirectorySeparatorChar) || s.Contains(Path.AltDirectorySeparatorChar);
}
