using FinishReplay.Models;
using FinishReplay.Services.Recording.Mjpeg;

namespace FinishReplay.Services.Camera;

/// <summary>
/// Drives a single live camera: opens its stream once and runs a read loop that raises
/// <see cref="FrameReady"/> for every JPEG frame (for preview) and, while recording, tees the same
/// frames into a Motion-JPEG AVI file. One read loop feeds both preview and recording so the camera
/// is only opened once.
/// </summary>
public sealed class LiveCamera : IAsyncDisposable
{
    private readonly CameraProviderRegistry _registry;
    private readonly CameraProfile _profile;
    private readonly object _gate = new();

    private CancellationTokenSource? _cts;
    private Task? _loop;
    private AviMjpegWriter? _writer;
    private FileStream? _file;
    private int _recordedFrames;

    public LiveCamera(CameraProviderRegistry registry, CameraProfile profile)
    {
        _registry = registry;
        _profile = profile;
    }

    public string CameraId => _profile.Id;

    /// <summary>Raised on the capture thread for each JPEG frame; subscribers must marshal to the UI.</summary>
    public event Action<byte[]>? FrameReady;

    public bool IsRecording
    {
        get { lock (_gate) return _writer is not null; }
    }

    /// <summary>Open the stream and begin the read loop (idempotent).</summary>
    public void Start()
    {
        if (_loop is not null) return;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    public void StartRecording(string filePath, double fps)
    {
        lock (_gate)
        {
            if (_writer is not null) return;
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            _file = File.Create(filePath);
            _writer = new AviMjpegWriter(_file, fps);
            _recordedFrames = 0;
        }
    }

    /// <summary>Finalize the AVI and return how many frames were written.</summary>
    public int StopRecording()
    {
        lock (_gate)
        {
            if (_writer is null) return 0;
            _writer.Finish();
            _writer.Dispose();
            _file?.Dispose();
            _writer = null;
            _file = null;
            return _recordedFrames;
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            await using var stream = await _registry.OpenAsync(_profile.ToDevice(), CameraSettings.Default, ct).ConfigureAwait(false);
            await foreach (var frame in stream.ReadFramesAsync(ct).ConfigureAwait(false))
            {
                if (frame.Format != VideoFrameFormat.Jpeg)
                    continue;

                FrameReady?.Invoke(frame.Data);

                lock (_gate)
                {
                    if (_writer is not null)
                    {
                        _writer.AddFrame(frame.Data);
                        _recordedFrames++;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        catch
        {
            // TODO: surface stream errors (bad URL, disconnect) to the UI/log.
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        if (_loop is not null)
        {
            try { await _loop.ConfigureAwait(false); }
            catch { /* ignore */ }
        }
        StopRecording();
        _cts?.Dispose();
    }
}
