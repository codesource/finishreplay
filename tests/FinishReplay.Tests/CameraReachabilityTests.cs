using System.Net;
using System.Net.Sockets;
using FinishReplay.Services.Camera;
using Xunit;

namespace FinishReplay.Tests;

public class CameraReachabilityTests
{
    [Theory]
    [InlineData("rtsp://cam/live", "cam", 554)]
    [InlineData("rtsp://cam:8554/live", "cam", 8554)]
    [InlineData("rtsp://user:pass@10.0.0.5:10554/h264", "10.0.0.5", 10554)]
    public void Parses_rtsp_endpoint(string url, string host, int port)
    {
        var (h, p) = CameraReachability.ParseRtspEndpoint(url);
        Assert.Equal(host, h);
        Assert.Equal(port, p);
    }

    [Theory]
    [InlineData("http://cam/video", "cam", 80)]
    [InlineData("http://cam:8081/stream", "cam", 8081)]
    [InlineData("https://cam/video", "cam", 443)]
    [InlineData("rtsp://cam/live", "cam", 554)]
    [InlineData("rtsp://cam:10554/live", "cam", 10554)]
    public void Parses_endpoint_with_default_ports(string url, string host, int port)
    {
        var (h, p) = CameraReachability.ParseEndpoint(url);
        Assert.Equal(host, h);
        Assert.Equal(port, p);
    }

    [Fact]
    public async Task Tcp_check_is_true_for_a_listening_port()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        try
        {
            var ok = await CameraReachability.CheckTcpAsync("127.0.0.1", port, TimeSpan.FromSeconds(2));
            Assert.True(ok);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task Tcp_check_is_false_for_a_closed_port()
    {
        // Reserve then release a port so nothing is listening on it.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var ok = await CameraReachability.CheckTcpAsync("127.0.0.1", port, TimeSpan.FromSeconds(2));
        Assert.False(ok);
    }

    [Fact]
    public async Task Http_check_is_false_for_an_unroutable_url()
    {
        using var http = new HttpClient();
        var ok = await CameraReachability.CheckHttpAsync("http://127.0.0.1:1/video", http, TimeSpan.FromSeconds(2));
        Assert.False(ok);
    }
}
