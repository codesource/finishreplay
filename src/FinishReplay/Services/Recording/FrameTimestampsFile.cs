using System.Text.Json;

namespace FinishReplay.Services.Recording;

/// <summary>
/// Per-frame capture timestamps stored alongside a recorded clip as <c>&lt;clip&gt;.ftime.json</c> — a
/// plain JSON array of integer milliseconds from the first frame. These record the frames' *actual*
/// arrival times (jitter and variable network latency included), so replay can place each camera on the
/// master timeline by real time instead of assuming a constant frame rate. Optional: clips without the
/// sidecar (older recordings, ffmpeg passthrough) fall back to nominal-fps timing on replay.
/// </summary>
public static class FrameTimestampsFile
{
    public static string PathFor(string videoPath) => videoPath + ".ftime.json";

    public static void Write(string videoPath, IReadOnlyList<double> millis)
    {
        var ms = millis.Select(m => (int)Math.Round(m)).ToArray();
        File.WriteAllText(PathFor(videoPath), JsonSerializer.Serialize(ms));
    }

    /// <summary>Read the per-frame timestamps for a clip, or null if there's no (valid) sidecar.</summary>
    public static IReadOnlyList<double>? Read(string videoPath)
    {
        var path = PathFor(videoPath);
        try
        {
            if (!File.Exists(path))
                return null;
            var ms = JsonSerializer.Deserialize<int[]>(File.ReadAllText(path));
            return ms is { Length: > 0 } ? ms.Select(v => (double)v).ToList() : null;
        }
        catch
        {
            return null;
        }
    }
}
