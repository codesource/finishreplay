using System.Diagnostics;
using System.Text;

namespace FinishReplay.Services.Recording.Ffmpeg;

/// <summary>
/// Records an archival clip by letting ffmpeg copy the source's encoded stream straight to disk
/// (e.g. RTSP/H.264 → MP4 with <c>-c:v copy</c>), no re-encode. Stop sends "q" to ffmpeg's stdin so
/// it finalizes the container gracefully; the fragmented-MP4 flags keep the file playable even if a
/// kill is needed.
/// </summary>
public sealed class FfmpegPassthroughRecorder : IDisposable
{
    private readonly Process _process;
    private readonly StringBuilder _stderr = new();

    public FfmpegPassthroughRecorder(string executablePath, IReadOnlyList<string> arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = executablePath,
            RedirectStandardInput = true,   // used to send "q" for a clean stop
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in arguments)
            psi.ArgumentList.Add(arg);

        _process = new Process { StartInfo = psi };
        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (_stderr)
            {
                if (_stderr.Length < 8000)
                    _stderr.AppendLine(e.Data);
            }
        };
        _process.Start();
        _process.BeginErrorReadLine();
    }

    public string StandardErrorTail
    {
        get { lock (_stderr) return _stderr.ToString(); }
    }

    /// <summary>Finalize the recording: ask ffmpeg to quit, then force-kill if it doesn't.</summary>
    public void Stop(int timeoutMs = 4000)
    {
        try
        {
            if (_process.HasExited)
                return;

            _process.StandardInput.Write('q');
            _process.StandardInput.Flush();

            if (!_process.WaitForExit(timeoutMs))
                _process.Kill(entireProcessTree: true);
        }
        catch
        {
            try { _process.Kill(entireProcessTree: true); } catch { /* ignore */ }
        }
    }

    public void Dispose()
    {
        try
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
        }
        catch
        {
            // best-effort
        }
        _process.Dispose();
    }
}
