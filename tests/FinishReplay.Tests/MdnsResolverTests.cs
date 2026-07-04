using System.Collections.Generic;
using System.Net;
using FinishReplay.Services.Camera;
using Xunit;

namespace FinishReplay.Tests;

public class MdnsResolverTests
{
    [Theory]
    [InlineData("camera-box.local", true)]
    [InlineData("camera.LOCAL", true)]
    [InlineData("camera.local.", true)]
    [InlineData("192.168.1.50", false)]
    [InlineData("camera.lan", false)]
    [InlineData("", false)]
    public void IsMdnsHost_detects_local_zone(string host, bool expected) =>
        Assert.Equal(expected, MdnsResolver.IsMdnsHost(host));

    [Fact]
    public void ParseAnswer_reads_the_A_record_matching_the_queried_name()
    {
        var response = BuildResponse("camera-box.local", new byte[] { 192, 168, 1, 42 });
        var ip = MdnsResolver.ParseAnswer(response, "camera-box.local");
        Assert.Equal(IPAddress.Parse("192.168.1.42"), ip);
    }

    [Fact]
    public void ParseAnswer_uses_name_compression_pointer()
    {
        // Answer name is a compression pointer back to the question's QNAME (the common mDNS layout).
        var response = BuildResponse("cam.local", new byte[] { 10, 0, 0, 7 }, compressAnswerName: true);
        var ip = MdnsResolver.ParseAnswer(response, "cam.local");
        Assert.Equal(IPAddress.Parse("10.0.0.7"), ip);
    }

    [Fact]
    public void ParseAnswer_ignores_records_for_a_different_host()
    {
        var response = BuildResponse("other.local", new byte[] { 1, 2, 3, 4 });
        Assert.Null(MdnsResolver.ParseAnswer(response, "camera-box.local"));
    }

    [Fact]
    public void ParseAnswer_returns_null_for_truncated_data() =>
        Assert.Null(MdnsResolver.ParseAnswer(new byte[] { 0, 0, 0 }, "cam.local"));

    /// <summary>Craft a minimal mDNS response: one question echoed back, one A answer.</summary>
    private static byte[] BuildResponse(string host, byte[] ipv4, bool compressAnswerName = false)
    {
        var msg = new List<byte>
        {
            0x00, 0x00,             // id
            0x84, 0x00,             // flags: response, authoritative
            0x00, 0x01,             // QDCOUNT = 1
            0x00, 0x01,             // ANCOUNT = 1
            0x00, 0x00,             // NSCOUNT
            0x00, 0x00,             // ARCOUNT
        };

        const int qnameOffset = 12;
        AppendName(msg, host);
        msg.AddRange(new byte[] { 0x00, 0x01, 0x00, 0x01 }); // QTYPE=A, QCLASS=IN

        // Answer name: either a compression pointer to the question, or the full name again.
        if (compressAnswerName)
        {
            msg.Add(0xC0);
            msg.Add(qnameOffset);
        }
        else
        {
            AppendName(msg, host);
        }

        msg.AddRange(new byte[] { 0x00, 0x01 });             // TYPE = A
        msg.AddRange(new byte[] { 0x00, 0x01 });             // CLASS = IN
        msg.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x78 }); // TTL
        msg.AddRange(new byte[] { 0x00, 0x04 });             // RDLENGTH = 4
        msg.AddRange(ipv4);                                  // RDATA
        return msg.ToArray();
    }

    private static void AppendName(List<byte> msg, string host)
    {
        foreach (var label in host.TrimEnd('.').Split('.'))
        {
            msg.Add((byte)label.Length);
            foreach (var c in label)
                msg.Add((byte)c);
        }
        msg.Add(0x00);
    }
}
