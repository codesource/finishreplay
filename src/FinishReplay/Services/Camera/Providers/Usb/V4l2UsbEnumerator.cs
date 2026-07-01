using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using FinishReplay.Models;

namespace FinishReplay.Services.Camera.Providers.Usb;

/// <summary>
/// Enumerates V4L2 capture devices on Linux. Lists <c>/dev/video*</c> and queries each node with
/// <c>VIDIOC_QUERYCAP</c> to keep only capture-capable nodes (a camera often exposes extra
/// metadata/output nodes) and to read the driver "card" name. Falls back to including a node (with
/// its sysfs name) if the query can't be performed. Pure syscalls, no ffmpeg. The device Id is the
/// <c>/dev/videoN</c> path the capture backend opens (<c>-i /dev/videoN</c>).
/// </summary>
[SupportedOSPlatform("linux")]
internal static class V4l2UsbEnumerator
{
    // struct v4l2_capability is 104 bytes: driver[16] card[32] bus_info[32] version(4)
    // capabilities(4) device_caps(4) reserved[3](12). Field offsets:
    private const int CapabilityStructSize = 104;
    private const int CardOffset = 16;
    private const int CapabilitiesOffset = 84;
    private const int DeviceCapsOffset = 88;

    private const uint V4L2_CAP_VIDEO_CAPTURE = 0x00000001;
    private const uint V4L2_CAP_DEVICE_CAPS = 0x80000000;

    // VIDIOC_QUERYCAP = _IOR('V', 0, struct v4l2_capability)
    private const ulong VIDIOC_QUERYCAP = 0x80685600;

    private const int O_RDWR = 2;
    private const int O_NONBLOCK = 0x800;

    public static IReadOnlyList<CameraDevice> Enumerate()
    {
        if (!Directory.Exists("/dev"))
            return Array.Empty<CameraDevice>();

        var devices = new List<CameraDevice>();
        foreach (var path in Directory.GetFiles("/dev", "video*").OrderBy(p => p, StringComparer.Ordinal))
        {
            var (canCapture, card) = QueryCapture(path);
            if (canCapture == false)
                continue; // definitely not a capture node — skip

            var name = !string.IsNullOrWhiteSpace(card) ? card! : ReadSysfsName(path) ?? path;
            devices.Add(new CameraDevice(path, $"{name} ({path})")
            {
                ProviderName = UsbCameraProvider.Type,
                SourceType = UsbCameraProvider.Type,
            });
        }

        return devices;
    }

    /// <summary>
    /// Query capture capability. Returns (true, card) for a capture node, (false, _) for a definite
    /// non-capture node, and (null, _) when it couldn't be determined (kept, to avoid hiding a camera).
    /// </summary>
    private static (bool? CanCapture, string? Card) QueryCapture(string path)
    {
        var fd = open(path, O_RDWR | O_NONBLOCK);
        if (fd < 0)
            return (null, null); // busy/permission — can't tell, keep it

        var buffer = Marshal.AllocHGlobal(CapabilityStructSize);
        try
        {
            for (var i = 0; i < CapabilityStructSize; i++)
                Marshal.WriteByte(buffer, i, 0);

            if (ioctl(fd, VIDIOC_QUERYCAP, buffer) < 0)
                return (null, null); // query failed — keep it

            var bytes = new byte[CapabilityStructSize];
            Marshal.Copy(buffer, bytes, 0, CapabilityStructSize);

            var capabilities = BitConverter.ToUInt32(bytes, CapabilitiesOffset);
            var deviceCaps = BitConverter.ToUInt32(bytes, DeviceCapsOffset);
            var effective = (capabilities & V4L2_CAP_DEVICE_CAPS) != 0 ? deviceCaps : capabilities;

            var card = Encoding.ASCII.GetString(bytes, CardOffset, 32).TrimEnd('\0').Trim();
            return ((effective & V4L2_CAP_VIDEO_CAPTURE) != 0, card);
        }
        catch
        {
            return (null, null);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
            close(fd);
        }
    }

    private static string? ReadSysfsName(string path)
    {
        try
        {
            var nameFile = $"/sys/class/video4linux/{Path.GetFileName(path)}/name";
            return File.Exists(nameFile) ? File.ReadAllText(nameFile).Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);

    [DllImport("libc", SetLastError = true)]
    private static extern int ioctl(int fd, ulong request, IntPtr argp);
}
