using System.Net;
using System.Net.Sockets;

namespace FinishReplay.Services.Camera;

/// <summary>
/// Lightweight reachability checks used to show whether a configured camera is currently online:
/// an HTTP request for MJPEG sources, a TCP connect for RTSP, and (for USB) presence in the current
/// device list. All checks are best-effort and swallow errors, returning false on any failure.
/// </summary>
public static class CameraReachability
{
    /// <summary>Reachable if an HTTP GET to <paramref name="url"/> returns a success status in time.</summary>
    public static async Task<bool> CheckHttpAsync(string url, HttpClient http, TimeSpan timeout, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Reachable if a TCP connection to <paramref name="host"/>:<paramref name="port"/> succeeds in time.</summary>
    public static async Task<bool> CheckTcpAsync(string host, int port, TimeSpan timeout, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(host) || port <= 0)
            return false;
        try
        {
            // Resolve to an IPv4 address first, on its own time budget, so a slow/flaky name lookup (or
            // an IPv6 address that would be tried first) can't eat the whole timeout and cancel the
            // connect — which is what made a working camera show "offline".
            var target = await ResolveIPv4Async(host, timeout, ct).ConfigureAwait(false) ?? host;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout); // fresh budget for the connect itself
            using var client = new TcpClient();
            await client.ConnectAsync(target, port, cts.Token).ConfigureAwait(false);
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Resolve a host to an IPv4 address (mDNS for <c>.local</c>, the OS resolver otherwise).</summary>
    private static async Task<string?> ResolveIPv4Async(string host, TimeSpan timeout, CancellationToken ct)
    {
        if (IPAddress.TryParse(host, out var literal))
            return literal.AddressFamily == AddressFamily.InterNetwork ? host : null;

        try
        {
            if (MdnsResolver.IsMdnsHost(host))
                return (await MdnsResolver.ResolveAsync(host, timeout, ct).ConfigureAwait(false))?.ToString();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            var addresses = await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, cts.Token).ConfigureAwait(false);
            return addresses.FirstOrDefault()?.ToString();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Extract host and port from an RTSP URL (default port 554).</summary>
    public static (string Host, int Port) ParseRtspEndpoint(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
            return (uri.Host, uri.Port > 0 ? uri.Port : 554);
        return ("", 554);
    }

    /// <summary>
    /// Extract host and port from any camera URL (http/https/rtsp), filling in the scheme's default
    /// port. Used for a TCP reachability check that doesn't depend on HTTP status/auth/path — a
    /// working camera has its port open, which is what we want to report.
    /// </summary>
    public static (string Host, int Port) ParseEndpoint(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
        {
            var port = uri.Port;
            if (port <= 0)
                port = uri.Scheme.ToLowerInvariant() switch { "https" => 443, "rtsp" => 554, _ => 80 };
            return (uri.Host, port);
        }
        return ("", 0);
    }
}
