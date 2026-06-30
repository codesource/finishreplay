using FinishReplay.Services.Camera.Providers.Ffmpeg;
using FinishReplay.Services.Camera.Providers.Usb;
using Xunit;

namespace FinishReplay.Tests;

public class UsbCaptureTests
{
    // Captured from `ffmpeg -list_devices true -f dshow -i dummy` (modern ffmpeg).
    private const string DshowOutput = """
        [dshow @ 000001] DirectShow video devices (some may be both video and audio devices)
        [dshow @ 000001]  "Integrated Webcam" (video)
        [dshow @ 000001]     Alternative name "@device_pnp_\\?\usb#vid_1234"
        [dshow @ 000001]  "Logitech HD Webcam C920" (video)
        [dshow @ 000001]     Alternative name "@device_pnp_\\?\usb#vid_046d"
        [dshow @ 000001] DirectShow audio devices
        [dshow @ 000001]  "Microphone (Realtek Audio)" (audio)
        """;

    // Captured from `ffmpeg -list_devices true -f avfoundation -i ""`.
    private const string AvfoundationOutput = """
        [AVFoundation indev @ 0x7f8] AVFoundation video devices:
        [AVFoundation indev @ 0x7f8] [0] FaceTime HD Camera
        [AVFoundation indev @ 0x7f8] [1] USB Capture HDMI
        [AVFoundation indev @ 0x7f8] AVFoundation audio devices:
        [AVFoundation indev @ 0x7f8] [0] MacBook Pro Microphone
        """;

    [Fact]
    public void Parses_dshow_video_devices_only()
    {
        var devices = UsbDeviceParser.ParseDShow(DshowOutput);

        Assert.Equal(2, devices.Count);
        Assert.Equal("Integrated Webcam", devices[0].Name);
        Assert.Equal("Integrated Webcam", devices[0].Id); // dshow opens by name
        Assert.Equal("Logitech HD Webcam C920", devices[1].Name);
        Assert.DoesNotContain(devices, d => d.Name.Contains("Microphone"));
    }

    [Fact]
    public void Parses_avfoundation_video_devices_with_index_ids()
    {
        var devices = UsbDeviceParser.ParseAvFoundation(AvfoundationOutput);

        Assert.Equal(2, devices.Count);
        Assert.Equal("0", devices[0].Id);
        Assert.Equal("FaceTime HD Camera", devices[0].Name);
        Assert.Equal("1", devices[1].Id);
        Assert.DoesNotContain(devices, d => d.Name.Contains("Microphone"));
    }

    [Fact]
    public void Windows_usb_args_use_dshow_video_input_to_mjpeg()
    {
        var args = FfmpegArguments.ForUsbToMjpeg(UsbPlatform.Windows, "Integrated Webcam", fps: 30).ToList();

        Assert.Equal("dshow", args[args.IndexOf("-f") + 1]);
        Assert.Equal("video=Integrated Webcam", args[args.IndexOf("-i") + 1]);
        Assert.Equal("mjpeg", args[args.LastIndexOf("-f") + 1]);
        Assert.Equal("pipe:1", args[^1]);
        Assert.Equal("30", args[args.IndexOf("-framerate") + 1]);
    }

    [Fact]
    public void Macos_usb_args_use_avfoundation_input()
    {
        var args = FfmpegArguments.ForUsbToMjpeg(UsbPlatform.MacOS, "0:none").ToList();
        Assert.Equal("avfoundation", args[args.IndexOf("-f") + 1]);
        Assert.Equal("0:none", args[args.IndexOf("-i") + 1]);
        Assert.Equal("pipe:1", args[^1]);
    }

    [Fact]
    public void Linux_usb_args_use_v4l2_device_path()
    {
        var args = FfmpegArguments.ForUsbToMjpeg(UsbPlatform.Linux, "/dev/video0").ToList();
        Assert.Equal("v4l2", args[args.IndexOf("-f") + 1]);
        Assert.Equal("/dev/video0", args[args.IndexOf("-i") + 1]);
    }
}
