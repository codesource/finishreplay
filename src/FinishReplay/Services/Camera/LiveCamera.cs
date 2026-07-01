using FinishReplay.Models;
using FinishReplay.Services.Camera.Providers;
using FinishReplay.Services.Camera.Providers.Ffmpeg;
using FinishReplay.Services.Recording;
using FinishReplay.Services.Recording.Ffmpeg;
using FinishReplay.Services.Recording.Mjpeg;

namespace FinishReplay.Services.Camera;

/// <summary>
/// Drives a single live camera: opens its stream once and runs a read loop that raises
/// <see cref="FrameReady"/> for every JPEG frame (for preview), maintains a rolling pre-record buffer,
/// and — while recording — writes the clip. Transcode recording prepends the buffered pre-roll and
/// keeps writing for the post-roll after stop; passthrough (RTSP) lets ffmpeg copy to MP4 and simply
/// delays the stop for the post-roll. One read loop feeds preview, buffer and recording.
/// </summary>
public sealed class LiveCamera : IAsyncDisposable
{
    private enum RecState { Idle, Recording, PostRoll }

    private readonly CameraProviderRegistry _registry;
    private readonly CameraProfile _profile;
    private readonly Func<string> _ffmpegPath;
    private readonly object _gate = new();
    private readonly FrameRingBuffer _buffer = new();

    private CancellationTokenSource? _cts;
    private Task? _loop;
    private AviMjpegWriter? _writer;
    private FileStream? _file;
    private FfmpegPassthroughRecorder? _passthrough;
    private RecState _state = RecState.Idle;
    private double _postRecordSeconds;
    private int _recordedFrames;

    public LiveCamera(CameraProviderRegistry registry, CameraProfile profile, Func<string>? ffmpegPath = null)
    {
        _registry = registry;
        _profile = profile;
        _ffmpegPath = ffmpegPath ?? (() => "ffmpeg");
    }

    public string CameraId => _profile.Id;

    /// <summary>Raised on the capture thread for each JPEG frame; subscribers must marshal to the UI.</summary>
    public event Action<byte[]>? FrameReady;

    /// <summary>Raised when capture fails (bad URL/device, ffmpeg missing, decoder error). UI must marshal.</summary>
    public event Action<string>? Error;

    /// <summary>Length of the rolling pre-record buffer kept ahead of a start.</summary>
    public double PreRecordSeconds
    {
        get { lock (_gate) return _buffer.Window.TotalSeconds; }
        set { lock (_gate) _buffer.Window = TimeSpan.FromSeconds(Math.Max(0, value)); }
    }

    public bool IsRecording
    {
        get { lock (_gate) return _state != RecState.Idle || _passthrough is not null; }
    }

    /// <summary>Frames currently held in the pre-record buffer (for diagnostics/tests).</summary>
    public int BufferedFrameCount
    {
        get { lock (_gate) return _buffer.Count; }
    }

    /// <summary>True when this source records archival H.264 (RTSP) under passthrough mode.</summary>
    public bool SupportsPassthrough => _profile.SourceType == RtspCameraProvider.Type;

    public void Start()
    {
        if (_loop is not null) return;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    public void StartRecording(string filePath, double fps, RecordingMode mode, double postRecordSeconds)
    {
        lock (_gate)
        {
            if (_state != RecState.Idle || _passthrough is not null) return;
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            _postRecordSeconds = Math.Max(0, postRecordSeconds);

            // Passthrough (archival H.264 copy to MP4) for RTSP — ffmpeg writes the file directly.
            // No pre-roll (ffmpeg can't include already-past frames); post-roll is a delayed stop.
            if (mode == RecordingMode.Passthrough && SupportsPassthrough &&
                FfmpegLocator.Resolve(_ffmpegPath()) is { } exe)
            {
                var args = FfmpegArguments.ForRtspPassthroughMp4(_profile.SourceUrl, filePath);
                _passthrough = new FfmpegPassthroughRecorder(exe, args);
                return;
            }

            // Transcode: prepend the buffered pre-roll, then keep appending live frames.
            _file = File.Create(filePath);
            _writer = new AviMjpegWriter(_file, fps);
            _recordedFrames = 0;
            foreach (var jpeg in _buffer.Snapshot())
            {
                _writer.AddFrame(jpeg);
                _recordedFrames++;
            }
            _state = RecState.Recording;
        }
    }

    /// <summary>
    /// Stop recording, honouring the post-roll: keeps capturing for the configured post-record
    /// seconds, then finalizes. Returns frames written (0 for passthrough).
    /// </summary>
    public async Task<int> StopRecordingAsync()
    {
        double post;
        bool passthrough;
        lock (_gate)
        {
            if (_state == RecState.Idle && _passthrough is null) return 0;
            post = _postRecordSeconds;
            passthrough = _passthrough is not null;
            if (!passthrough)
                _state = RecState.PostRoll; // keep appending live frames during the tail
        }

        if (post > 0)
            await Task.Delay(TimeSpan.FromSeconds(post)).ConfigureAwait(false);

        lock (_gate)
        {
            return FinalizeLocked();
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            var settings = new CameraSettings
            {
                Width = _profile.Width,
                Height = _profile.Height,
                FrameRate = _profile.FrameRate,
                PixelFormat = _profile.PixelFormat,
            };
            await using var stream = await _registry.OpenAsync(_profile.ToDevice(), settings, ct).ConfigureAwait(false);
            await foreach (var frame in stream.ReadFramesAsync(ct).ConfigureAwait(false))
            {
                if (frame.Format != VideoFrameFormat.Jpeg)
                    continue;

                FrameReady?.Invoke(frame.Data);

                lock (_gate)
                {
                    _buffer.Add(frame.Timestamp, frame.Data);
                    if (_writer is not null && _state != RecState.Idle)
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
        catch (Exception ex)
        {
            // Surface stream errors (bad URL/device, ffmpeg missing, decoder failure) to the UI.
            Error?.Invoke(ex.Message);
        }
    }

    /// <summary>Finalize whatever recorder is active. Caller holds <see cref="_gate"/>.</summary>
    private int FinalizeLocked()
    {
        if (_passthrough is not null)
        {
            _passthrough.Stop();
            _passthrough.Dispose();
            _passthrough = null;
            return 0;
        }

        if (_writer is null)
            return 0;

        var count = _recordedFrames;
        _writer.Finish();
        _writer.Dispose();
        _file?.Dispose();
        _writer = null;
        _file = null;
        _state = RecState.Idle;
        return count;
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        if (_loop is not null)
        {
            try { await _loop.ConfigureAwait(false); }
            catch { /* ignore */ }
        }
        lock (_gate)
        {
            FinalizeLocked(); // finalize immediately, skipping any remaining post-roll
        }
        _cts?.Dispose();
    }
}
