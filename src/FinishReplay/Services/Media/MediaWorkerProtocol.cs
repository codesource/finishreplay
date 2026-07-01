using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace FinishReplay.Services.Media;

public enum MediaMessageType
{
    Frame,   // JPEG-encoded video frame
    Log,     // diagnostic text
    Error,   // fatal error text (worker will exit)
    Eos,     // end of stream
    Ready,   // worker started, source opened
    Unknown,
}

public readonly record struct MediaMessage(MediaMessageType Type, byte[] Payload);

/// <summary>
/// Tiny framed message protocol between the main app and the isolated media-worker process.
/// Each message is: 1 type byte + 4-byte little-endian length + payload. The worker writes frames
/// to its stdout; the host reads them here. Kept deliberately trivial so a worker crash simply ends
/// the stream (the host treats a truncated read as end-of-stream, never throwing into the app).
///
/// NOTE: the worker process has its own copy of the writer side — keep the byte format in sync.
/// </summary>
public static class MediaWorkerProtocol
{
    public const byte TypeFrame = (byte)'F';
    public const byte TypeLog = (byte)'L';
    public const byte TypeError = (byte)'E';
    public const byte TypeEos = (byte)'X';
    public const byte TypeReady = (byte)'R';

    public static async IAsyncEnumerable<MediaMessage> ReadAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var header = new byte[5];
        while (!cancellationToken.IsCancellationRequested)
        {
            if (!await ReadExactAsync(stream, header, 5, cancellationToken).ConfigureAwait(false))
                yield break; // stream closed / worker died mid-frame → clean end, no throw

            var type = header[0];
            var length = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(1, 4));

            var payload = length == 0 ? Array.Empty<byte>() : new byte[length];
            if (length > 0 && !await ReadExactAsync(stream, payload, (int)length, cancellationToken).ConfigureAwait(false))
                yield break;

            yield return new MediaMessage(Map(type), payload);

            if (type == TypeEos)
                yield break;
        }
    }

    public static async Task WriteAsync(Stream stream, byte type, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        var header = new byte[5];
        header[0] = type;
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(1, 4), (uint)payload.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        if (!payload.IsEmpty)
            await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static Task WriteFrameAsync(Stream stream, ReadOnlyMemory<byte> jpeg, CancellationToken ct = default)
        => WriteAsync(stream, TypeFrame, jpeg, ct);

    private static MediaMessageType Map(byte type) => type switch
    {
        TypeFrame => MediaMessageType.Frame,
        TypeLog => MediaMessageType.Log,
        TypeError => MediaMessageType.Error,
        TypeEos => MediaMessageType.Eos,
        TypeReady => MediaMessageType.Ready,
        _ => MediaMessageType.Unknown,
    };

    /// <summary>Read exactly <paramref name="count"/> bytes; false if the stream ended first.</summary>
    private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, int count, CancellationToken ct)
    {
        var offset = 0;
        while (offset < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset), ct).ConfigureAwait(false);
            if (read <= 0)
                return false;
            offset += read;
        }
        return true;
    }
}
