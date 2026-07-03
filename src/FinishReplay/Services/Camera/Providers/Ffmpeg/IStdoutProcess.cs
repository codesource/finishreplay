using System.Diagnostics;
using System.Text;

namespace FinishReplay.Services.Camera.Providers.Ffmpeg;

/// <summary>
/// A running child process exposing its standard output as a stream. This is the seam that lets the
/// ffmpeg stream be unit-tested without actually spawning ffmpeg (tests supply a fake over a
/// MemoryStream).
/// </summary>
public interface IStdoutProcess : IDisposable
{
    Stream StandardOutput { get; }

    /// <summary>Captured stderr tail, for diagnostics when the stream fails.</summary>
    string StandardErrorTail { get; }
}

/// <summary>
/// Real <see cref="IStdoutProcess"/> backed by an ffmpeg process. stdout is the binary MJPEG stream;
/// stderr is drained in the background (and kept as a bounded tail) so the process can't deadlock.
/// </summary>
public sealed class FfmpegProcess : IStdoutProcess
{
    private readonly Process _process;
    private readonly StringBuilder _stderr = new();

    public FfmpegProcess(string executablePath, IReadOnlyList<string> arguments)
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

    public Stream StandardOutput => _process.StandardOutput.BaseStream;

    public string StandardErrorTail
    {
        get { lock (_stderr) return _stderr.ToString(); }
    }

    public void Dispose()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                // Kill only *requests* termination — wait for the OS to actually tear the process
                // down so it releases any exclusive device handle (e.g. a USB webcam). Without this
                // a freshly reopened capture races the dying worker and fails "device in use".
                _process.WaitForExit(3000);
            }
        }
        catch
        {
            // best-effort teardown
        }
        _process.Dispose();
    }
}
