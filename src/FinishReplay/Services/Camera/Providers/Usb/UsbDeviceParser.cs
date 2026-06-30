using System.Text.RegularExpressions;

namespace FinishReplay.Services.Camera.Providers.Usb;

/// <summary>
/// Parses the device lists ffmpeg prints to stderr for <c>-list_devices true</c>, for the DirectShow
/// (Windows) and AVFoundation (macOS) input formats.
/// </summary>
public static class UsbDeviceParser
{
    private static readonly Regex DShowVideo = new("\"([^\"]+)\"\\s*\\(video\\)", RegexOptions.Compiled);
    private static readonly Regex AvIndexed = new("\\[(\\d+)\\]\\s+(.+?)\\s*$", RegexOptions.Compiled);

    /// <summary>DirectShow: video devices are opened by name, so id == name.</summary>
    public static IReadOnlyList<(string Id, string Name)> ParseDShow(string stderr)
    {
        var devices = new List<(string, string)>();
        foreach (var line in SplitLines(stderr))
        {
            var m = DShowVideo.Match(line);
            if (m.Success)
                devices.Add((m.Groups[1].Value, m.Groups[1].Value));
        }
        return devices;
    }

    /// <summary>AVFoundation: video devices are opened by index, listed under "video devices:".</summary>
    public static IReadOnlyList<(string Id, string Name)> ParseAvFoundation(string stderr)
    {
        var devices = new List<(string, string)>();
        var inVideoSection = false;

        foreach (var line in SplitLines(stderr))
        {
            if (line.Contains("video devices:", StringComparison.OrdinalIgnoreCase)) { inVideoSection = true; continue; }
            if (line.Contains("audio devices:", StringComparison.OrdinalIgnoreCase)) { inVideoSection = false; continue; }
            if (!inVideoSection) continue;

            var m = AvIndexed.Match(line);
            if (m.Success)
                devices.Add((m.Groups[1].Value, m.Groups[2].Value.Trim()));
        }
        return devices;
    }

    private static IEnumerable<string> SplitLines(string s) =>
        s.Replace("\r\n", "\n").Split('\n');
}
