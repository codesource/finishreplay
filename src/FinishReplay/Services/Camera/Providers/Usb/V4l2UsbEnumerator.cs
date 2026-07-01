using System.Runtime.Versioning;
using FinishReplay.Models;

namespace FinishReplay.Services.Camera.Providers.Usb;

/// <summary>
/// Enumerates V4L2 capture devices on Linux by listing <c>/dev/video*</c> and reading each node's
/// friendly name from <c>/sys/class/video4linux/&lt;node&gt;/name</c>. Pure file reads, no ffmpeg.
/// The device Id is the <c>/dev/videoN</c> path the capture backend opens (<c>-i /dev/videoN</c>).
///
/// TODO: a camera can expose several nodes (capture + metadata); filter to capture-capable nodes via
/// a V4L2 capabilities ioctl to avoid listing non-capture nodes.
/// </summary>
[SupportedOSPlatform("linux")]
internal static class V4l2UsbEnumerator
{
    public static IReadOnlyList<CameraDevice> Enumerate()
    {
        if (!Directory.Exists("/dev"))
            return Array.Empty<CameraDevice>();

        var devices = new List<CameraDevice>();
        foreach (var path in Directory.GetFiles("/dev", "video*").OrderBy(p => p, StringComparer.Ordinal))
        {
            var node = Path.GetFileName(path);
            var label = path;
            try
            {
                var nameFile = $"/sys/class/video4linux/{node}/name";
                if (File.Exists(nameFile))
                {
                    var friendly = File.ReadAllText(nameFile).Trim();
                    if (friendly.Length > 0)
                        label = $"{friendly} ({path})";
                }
            }
            catch
            {
                // fall back to the raw path as the label
            }

            devices.Add(new CameraDevice(path, label)
            {
                ProviderName = UsbCameraProvider.Type,
                SourceType = UsbCameraProvider.Type,
            });
        }

        return devices;
    }
}
