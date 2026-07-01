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
            using var client = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            await client.ConnectAsync(host, port, cts.Token).ConfigureAwait(false);
            return client.Connected;
        }
        catch
        {
            return false;
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
