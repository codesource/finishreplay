using FinishReplay.Services.Camera.Providers.Usb;
using Xunit;

namespace FinishReplay.Tests;

public class NativeUsbEnumeratorTests
{
    [Fact]
    public void Reports_support_per_platform()
    {
        var enumerator = new NativeUsbCameraEnumerator();
        var expected = OperatingSystem.IsWindows() || OperatingSystem.IsLinux();
        Assert.Equal(expected, enumerator.IsSupported);
    }

    [Fact]
    public void Enumerate_never_throws_and_returns_usb_devices()
    {
        // Exercises the DirectShow (Windows) / V4L2 (Linux) path; may legitimately be empty on CI.
        var enumerator = new NativeUsbCameraEnumerator();
        var devices = enumerator.Enumerate();

        Assert.NotNull(devices);
        Assert.All(devices, d =>
        {
            Assert.Equal("USB", d.SourceType);
            Assert.False(string.IsNullOrWhiteSpace(d.Id));
        });
    }
}
