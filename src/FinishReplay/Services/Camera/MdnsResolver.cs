using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace FinishReplay.Services.Camera;

/// <summary>
/// Resolves <c>*.local</c> multicast-DNS (Bonjour/Avahi) hostnames to an IPv4 address. Windows'
/// standard resolver (which <see cref="Dns"/> and libav both use) does not resolve <c>.local</c> names,
/// so a camera addressed as <c>camera-box.local</c> only works by raw IP unless we do the mDNS query
/// ourselves. This sends a multicast A-record query to 224.0.0.251:5353 and reads the answer.
///
/// Best-effort: returns null on timeout / any failure, and results are briefly cached. Non-<c>.local</c>
/// hosts are left untouched for the normal resolver.
/// </summary>
public static class MdnsResolver
{
    private static readonly IPAddress MulticastAddress = IPAddress.Parse("224.0.0.251");
    private const int MdnsPort = 5353;

    private static readonly ConcurrentDictionary<string, (IPAddress Ip, long ExpiresTicks)> Cache = new();
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    /// <summary>True for hostnames in the mDNS <c>.local</c> zone.</summary>
    public static bool IsMdnsHost(string host) =>
        !string.IsNullOrEmpty(host) &&
        (host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
         host.EndsWith(".local.", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Return <paramref name="url"/> with a <c>.local</c> host replaced by its resolved IPv4 address,
    /// so HTTP/RTSP clients that can't do mDNS still connect. Non-<c>.local</c> hosts (and failures)
    /// return the URL unchanged.
    /// </summary>
    public static async Task<string> ResolveUrlAsync(string url, TimeSpan timeout, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || !IsMdnsHost(uri.Host))
            return url;

        var ip = await ResolveAsync(uri.Host, timeout, ct).ConfigureAwait(false);
        if (ip is null)
            return url;

        return new UriBuilder(uri) { Host = ip.ToString() }.Uri.ToString();
    }

    /// <summary>Resolve a <c>.local</c> hostname to an IPv4 address, or null if it can't be resolved.</summary>
    public static async Task<IPAddress?> ResolveAsync(string host, TimeSpan timeout, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(host))
            return null;

        var name = host.TrimEnd('.');
        if (Cache.TryGetValue(name, out var hit) && hit.ExpiresTicks > DateTime.UtcNow.Ticks)
            return hit.Ip;

        var ip = await ResolveViaOsAsync(name, ct).ConfigureAwait(false)
              ?? await SafeQueryAsync(name, timeout, ct).ConfigureAwait(false);

        if (ip is not null)
            Cache[name] = (ip, DateTime.UtcNow.Add(CacheTtl).Ticks);
        return ip;
    }

    /// <summary>
    /// The OS resolver first — Windows resolves <c>.local</c> via its DNS client, and Linux/macOS via
    /// nss/avahi. This is what Chrome and the browser use, so it picks the same address and the same
    /// interface; our own multicast query is only a fallback for when the OS can't do mDNS.
    /// </summary>
    private static async Task<IPAddress?> ResolveViaOsAsync(string name, CancellationToken ct)
    {
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(name, AddressFamily.InterNetwork, ct).ConfigureAwait(false);
            return addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<IPAddress?> SafeQueryAsync(string name, TimeSpan timeout, CancellationToken ct)
    {
        try { return await QueryAsync(name, timeout, ct).ConfigureAwait(false); }
        catch { return null; }
    }

    private static async Task<IPAddress?> QueryAsync(string name, TimeSpan timeout, CancellationToken ct)
    {
        var query = BuildQuery(name);

        // Bind an EPHEMERAL port (not 5353 — the Windows mDNS service owns that, and sharing it for
        // receive is unreliable). We set the unicast-response (QU) bit in the query, so responders reply
        // directly to this socket. We send the query out of every IPv4 interface so a machine with a VPN
        // and several adapters still reaches the camera's subnet.
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
        var target = new IPEndPoint(MulticastAddress, MdnsPort);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        foreach (var localIp in MulticastInterfaces())
        {
            try
            {
                udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface,
                    localIp.GetAddressBytes());
                await udp.SendAsync(query, query.Length, target).ConfigureAwait(false); // UDP is lossy
                await udp.SendAsync(query, query.Length, target).ConfigureAwait(false);
            }
            catch { /* interface can't multicast; try the next */ }
        }

        try
        {
            while (!cts.IsCancellationRequested)
            {
                var result = await udp.ReceiveAsync(cts.Token).ConfigureAwait(false);
                var ip = ParseAnswer(result.Buffer, name);
                if (ip is not null)
                    return ip;
            }
        }
        catch (OperationCanceledException)
        {
            // timed out
        }

        return null;
    }

    /// <summary>Up, multicast-capable IPv4 interface addresses, so the query reaches every subnet.</summary>
    private static IEnumerable<IPAddress> MulticastInterfaces()
    {
        NetworkInterface[] nics;
        try { nics = NetworkInterface.GetAllNetworkInterfaces(); }
        catch { yield break; }

        foreach (var nic in nics)
        {
            if (nic.OperationalStatus != OperationalStatus.Up || !nic.SupportsMulticast)
                continue;

            IPInterfaceProperties props;
            try { props = nic.GetIPProperties(); }
            catch { continue; }

            foreach (var addr in props.UnicastAddresses)
            {
                if (addr.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(addr.Address))
                    yield return addr.Address;
            }
        }
    }

    private static byte[] BuildQuery(string name)
    {
        var msg = new List<byte>(name.Length + 20)
        {
            0x00, 0x00,             // transaction id (0 for mDNS)
            0x00, 0x00,             // flags: standard query
            0x00, 0x01,             // QDCOUNT = 1
            0x00, 0x00,             // ANCOUNT
            0x00, 0x00,             // NSCOUNT
            0x00, 0x00,             // ARCOUNT
        };

        foreach (var label in name.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            msg.Add((byte)bytes.Length);
            msg.AddRange(bytes);
        }
        msg.Add(0x00);             // end of QNAME

        msg.Add(0x00); msg.Add(0x01);   // QTYPE = A
        msg.Add(0x80); msg.Add(0x01);   // QCLASS = IN with QU bit (prefer a unicast response)
        return msg.ToArray();
    }

    internal static IPAddress? ParseAnswer(byte[] msg, string host)
    {
        if (msg.Length < 12)
            return null;

        int qd = (msg[4] << 8) | msg[5];
        int an = (msg[6] << 8) | msg[7];
        var pos = 12;

        for (var i = 0; i < qd && pos < msg.Length; i++)
            pos = SkipName(msg, pos) + 4; // + QTYPE(2) + QCLASS(2)

        for (var i = 0; i < an; i++)
        {
            var (name, next) = ReadName(msg, pos);
            pos = next;
            if (pos + 10 > msg.Length)
                return null;

            int type = (msg[pos] << 8) | msg[pos + 1];
            int rdlen = (msg[pos + 8] << 8) | msg[pos + 9];
            pos += 10;
            if (pos + rdlen > msg.Length)
                return null;

            if (type == 1 && rdlen == 4 && name.Equals(host, StringComparison.OrdinalIgnoreCase))
                return new IPAddress(new[] { msg[pos], msg[pos + 1], msg[pos + 2], msg[pos + 3] });

            pos += rdlen;
        }

        return null;
    }

    private static int SkipName(byte[] msg, int pos) => ReadName(msg, pos).Next;

    /// <summary>Read a (possibly compressed) DNS name; returns the dotted name and the offset after it.</summary>
    private static (string Name, int Next) ReadName(byte[] msg, int pos)
    {
        var sb = new StringBuilder();
        var jumped = false;
        var afterPointer = -1;
        var guard = 0;

        while (pos < msg.Length && guard++ < 128)
        {
            int len = msg[pos];
            if ((len & 0xC0) == 0xC0)
            {
                if (pos + 1 >= msg.Length)
                    break;
                if (!jumped)
                    afterPointer = pos + 2;
                pos = ((len & 0x3F) << 8) | msg[pos + 1];
                jumped = true;
                continue;
            }
            if (len == 0)
            {
                pos += 1;
                break;
            }
            pos += 1;
            if (pos + len > msg.Length)
                break;
            if (sb.Length > 0)
                sb.Append('.');
            sb.Append(Encoding.ASCII.GetString(msg, pos, len));
            pos += len;
        }

        return (sb.ToString(), jumped ? afterPointer : pos);
    }
}
