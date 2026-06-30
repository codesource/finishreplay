using FinishReplay.Models;

namespace FinishReplay.Services.Recording;

/// <summary>
/// Default <see cref="IRecordingEngine"/>. Owns state transitions and the pre/post-record timing;
/// starts/stops every selected camera together. Actual capture is deferred to the injected
/// <see cref="IVideoBackend"/>.
/// </summary>
public sealed class RecordingEngine : IRecordingEngine
{
    private readonly IVideoBackend _backend;
    private IReadOnlyList<CameraDevice> _cameras = Array.Empty<CameraDevice>();
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

    public async Task StartAsync(IReadOnlyList<CameraDevice> cameras)
    {
        if (cameras.Count == 0)
            throw new InvalidOperationException("Select at least one camera before recording.");

        _cameras = cameras;

        // TODO: use one IVideoBackend instance per camera for true parallel capture, and mark
        // each rolling buffer's start so PreRecord seconds are retained.
        foreach (var cam in cameras)
            await _backend.StartPreviewAsync(cam).ConfigureAwait(false);

        State = RecordingState.Recording;
    }

    public async Task StopAsync()
    {
        if (State != RecordingState.Recording)
            return;

        State = RecordingState.Stopping;

        // TODO: keep capturing for PostRecord, then flush each camera's clip via SaveClipAsync.
        await _backend.StopPreviewAsync().ConfigureAwait(false);

        _cameras = Array.Empty<CameraDevice>();
        State = RecordingState.Idle;
    }
}
