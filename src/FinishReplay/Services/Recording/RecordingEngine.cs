using FinishReplay.Models;

namespace FinishReplay.Services.Recording;

/// <summary>
/// Default <see cref="IRecordingEngine"/>. Owns state transitions and the pre/post-record
/// timing; defers actual capture to the injected <see cref="IVideoBackend"/>.
/// </summary>
public sealed class RecordingEngine : IRecordingEngine
{
    private readonly IVideoBackend _backend;
    private CameraDevice? _camera;
    private RecordingState _state = RecordingState.Idle;

    public RecordingEngine(IVideoBackend backend)
    {
        _backend = backend;
    }

    public RecordingState State
    {
        get => _state;
        private set
        {
            if (_state == value) return;
            _state = value;
            StateChanged?.Invoke(this, value);
        }
    }

    public TimeSpan PreRecord { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan PostRecord { get; set; } = TimeSpan.FromSeconds(3);

    public event EventHandler<RecordingState>? StateChanged;

    public async Task StartPreviewAsync(CameraDevice camera)
    {
        _camera = camera;
        await _backend.StartPreviewAsync(camera).ConfigureAwait(false);
        State = RecordingState.Previewing;
    }

    public Task StartRecordingAsync()
    {
        if (_camera is null)
            throw new InvalidOperationException("Start preview before recording.");

        // TODO: mark the rolling buffer's start point so PreRecord seconds are retained.
        State = RecordingState.Recording;
        return Task.CompletedTask;
    }

    public async Task<string?> StopRecordingAsync(string outputPath)
    {
        if (State != RecordingState.Recording)
            return null;

        State = RecordingState.Stopping;

        // TODO: continue capturing for PostRecord before flushing.
        var written = await _backend
            .SaveClipAsync(outputPath, PreRecord, PostRecord)
            .ConfigureAwait(false);

        State = _camera is not null ? RecordingState.Previewing : RecordingState.Idle;
        return written;
    }
}
