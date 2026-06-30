using System.Runtime.CompilerServices;
using FinishReplay.Models;

namespace FinishReplay.Services.Camera.Providers;

/// <summary>
/// Shared no-op stream returned by the placeholder providers so the contract is fully wired
/// before real capture exists. Yields no frames.
///
/// TODO: replace per-provider with a real stream that decodes frames from the transport.
/// </summary>
internal sealed class PlaceholderCameraStream : ICameraStream
{
    public PlaceholderCameraStream(CameraDevice device, CameraStreamInfo info)
    {
        Device = device;
        StreamInfo = info;
    }

    public CameraDevice Device { get; }
    public CameraStreamInfo StreamInfo { get; }

#pragma warning disable CS1998 // async iterator with no awaits yet — real capture will await I/O.
    public async IAsyncEnumerable<VideoFrame> ReadFramesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // TODO: yield decoded frames here. Intentionally empty for the placeholder.
        yield break;
    }
#pragma warning restore CS1998

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
