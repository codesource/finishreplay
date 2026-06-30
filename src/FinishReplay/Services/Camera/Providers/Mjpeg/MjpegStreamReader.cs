using System.Runtime.CompilerServices;

namespace FinishReplay.Services.Camera.Providers.Mjpeg;

/// <summary>
/// Splits a raw MJPEG byte stream (e.g. an HTTP <c>multipart/x-mixed-replace</c> body) into individual
/// JPEG frames by scanning for the JPEG Start-Of-Image (<c>FF D8</c>) and End-Of-Image (<c>FF D9</c>)
/// markers. Multipart boundary/header lines between frames are skipped naturally because they fall
/// outside an SOI…EOI pair.
/// </summary>
public static class MjpegStreamReader
{
    private const byte Marker = 0xFF;
    private const byte Soi = 0xD8; // Start Of Image
    private const byte Eoi = 0xD9; // End Of Image

    /// <summary>Yield complete JPEG frames from <paramref name="stream"/> until it ends or is cancelled.</summary>
    public static async IAsyncEnumerable<byte[]> ReadFramesAsync(
        Stream stream,
        int readBufferSize = 16 * 1024,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var read = new byte[readBufferSize];
        var acc = new GrowBuffer();

        while (!cancellationToken.IsCancellationRequested)
        {
            var n = await stream.ReadAsync(read.AsMemory(0, read.Length), cancellationToken).ConfigureAwait(false);
            if (n <= 0)
                break;

            acc.Append(read, n);

            // Extract every complete frame currently buffered.
            while (true)
            {
                var start = IndexOfMarker(acc.Data, acc.Count, Soi, 0);
                if (start < 0)
                {
                    // No SOI yet; drop everything but the last byte, which may be a leading 0xFF
                    // whose 0xD8 partner arrives in the next read. Keeps junk bounded too.
                    acc.TrimStart(acc.Count - 1);
                    break;
                }

                var end = IndexOfMarker(acc.Data, acc.Count, Eoi, start + 2);
                if (end < 0)
                {
                    // Incomplete frame; keep from SOI onward and wait for more bytes.
                    acc.TrimStart(start);
                    break;
                }

                var frameEnd = end + 2; // include the EOI marker
                yield return acc.Slice(start, frameEnd - start);
                acc.TrimStart(frameEnd);
            }
        }
    }

    /// <summary>Index of a two-byte JPEG marker (<c>FF xx</c>) at or after <paramref name="from"/>, or -1.</summary>
    private static int IndexOfMarker(byte[] data, int count, byte second, int from)
    {
        for (var i = Math.Max(from, 0); i < count - 1; i++)
        {
            if (data[i] == Marker && data[i + 1] == second)
                return i;
        }
        return -1;
    }

    /// <summary>Minimal growable byte buffer with front-trim compaction.</summary>
    private sealed class GrowBuffer
    {
        private byte[] _data = new byte[64 * 1024];

        public byte[] Data => _data;
        public int Count { get; private set; }

        public void Append(byte[] src, int length)
        {
            EnsureCapacity(Count + length);
            Array.Copy(src, 0, _data, Count, length);
            Count += length;
        }

        public byte[] Slice(int offset, int length)
        {
            var result = new byte[length];
            Array.Copy(_data, offset, result, 0, length);
            return result;
        }

        public void TrimStart(int count)
        {
            if (count <= 0) return;
            var remaining = Count - count;
            if (remaining > 0)
                Array.Copy(_data, count, _data, 0, remaining);
            Count = Math.Max(0, remaining);
        }

        public void Clear() => Count = 0;

        private void EnsureCapacity(int needed)
        {
            if (needed <= _data.Length) return;
            var size = _data.Length * 2;
            while (size < needed) size *= 2;
            Array.Resize(ref _data, size);
        }
    }
}
