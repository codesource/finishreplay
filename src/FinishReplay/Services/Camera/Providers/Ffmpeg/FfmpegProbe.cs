using System.Diagnostics;

namespace FinishReplay.Services.Camera.Providers.Ffmpeg;

/// <summary>
/// Runs ffmpeg once and returns its stderr — used for device enumeration (ffmpeg prints the device
/// list to stderr and exits). Stdout is drained to avoid blocking, and a timeout guards hangs.
/// </summary>
public static class FfmpegProbe
{
    public static async Task<string> GetStderrAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default,
        int timeoutMs = 5000)
    {
        var psi = new ProcessStartInfo
        {
            FileName = executablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in arguments)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start ffmpeg.");

        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken); // drain to avoid deadlock

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(timeoutMs);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
        }

        var stderr = await stderrTask.ConfigureAwait(false);
        _ = await stdoutTask.ConfigureAwait(false);
        return stderr;
    }
}
