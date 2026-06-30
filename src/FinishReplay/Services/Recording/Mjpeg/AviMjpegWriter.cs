using System.Text;

namespace FinishReplay.Services.Recording.Mjpeg;

/// <summary>
/// Writes JPEG frames into a Motion-JPEG AVI file (RIFF/AVI with the <c>MJPG</c> codec). Because each
/// frame is stored verbatim as a JPEG, no encoder/native dependency is needed and the result plays in
/// VLC and most players. The output stream must be seekable (header sizes are patched on
/// <see cref="Finish"/>). Dimensions are taken from the first frame when not supplied.
/// </summary>
public sealed class AviMjpegWriter : IDisposable
{
    private readonly Stream _s;
    private readonly BinaryWriter _w;
    private readonly double _fps;

    private bool _headerWritten;
    private int _width;
    private int _height;
    private int _frameCount;

    private long _riffSizePos;
    private long _avihTotalFramesPos;
    private long _strhLengthPos;
    private long _moviSizePos;
    private long _moviListPos;     // position of the 'movi' FOURCC (index offsets are relative to this)
    private readonly List<(uint offset, uint size)> _index = new();

    public AviMjpegWriter(Stream output, double fps = 30, int width = 0, int height = 0)
    {
        if (!output.CanSeek)
            throw new ArgumentException("AVI output stream must be seekable.", nameof(output));
        _s = output;
        _w = new BinaryWriter(_s, Encoding.ASCII, leaveOpen: true);
        _fps = fps <= 0 ? 30 : fps;
        _width = width;
        _height = height;
    }

    public void AddFrame(byte[] jpeg)
    {
        if (!_headerWritten)
        {
            if ((_width <= 0 || _height <= 0) && JpegInfo.TryGetDimensions(jpeg, out var w, out var h))
            {
                _width = w;
                _height = h;
            }
            if (_width <= 0) _width = 640;
            if (_height <= 0) _height = 480;
            WriteHeader();
        }

        // '00dc' = stream 0, compressed video data.
        var chunkStart = _s.Position;
        WriteFourCc("00dc");
        _w.Write((uint)jpeg.Length);
        _w.Write(jpeg);
        if ((jpeg.Length & 1) == 1)
            _w.Write((byte)0); // chunks are word-aligned

        _index.Add(((uint)(chunkStart - _moviListPos), (uint)jpeg.Length));
        _frameCount++;
    }

    /// <summary>Write the idx1 index and patch all the size/count fields. Call once when done.</summary>
    public void Finish()
    {
        if (!_headerWritten)
            WriteHeader(); // empty file still produces a valid (0-frame) AVI

        // Patch the 'movi' LIST size (covers 'movi' + all chunks).
        var moviEnd = _s.Position;
        var moviSize = (uint)(moviEnd - (_moviSizePos + 4));
        PatchUInt32(_moviSizePos, moviSize);

        // idx1
        WriteFourCc("idx1");
        _w.Write((uint)(_index.Count * 16));
        foreach (var (offset, size) in _index)
        {
            WriteFourCc("00dc");
            _w.Write((uint)0x10); // AVIIF_KEYFRAME
            _w.Write(offset);
            _w.Write(size);
        }

        // Patch RIFF size (everything after 'RIFF'+size), total frame counts.
        var end = _s.Position;
        PatchUInt32(_riffSizePos, (uint)(end - 8));
        PatchUInt32(_avihTotalFramesPos, (uint)_frameCount);
        PatchUInt32(_strhLengthPos, (uint)_frameCount);

        _s.Position = end;
        _w.Flush();
    }

    private void WriteHeader()
    {
        var microSecPerFrame = (uint)Math.Round(1_000_000 / _fps);

        WriteFourCc("RIFF");
        _riffSizePos = _s.Position;
        _w.Write((uint)0); // RIFF size (patched)
        WriteFourCc("AVI ");

        // LIST hdrl
        WriteFourCc("LIST");
        _w.Write((uint)192); // hdrl size (fixed: see layout below)
        WriteFourCc("hdrl");

        // avih
        WriteFourCc("avih");
        _w.Write((uint)56);
        _w.Write(microSecPerFrame);
        _w.Write((uint)0);            // dwMaxBytesPerSec
        _w.Write((uint)0);            // dwPaddingGranularity
        _w.Write((uint)0x10);         // dwFlags = AVIF_HASINDEX
        _avihTotalFramesPos = _s.Position;
        _w.Write((uint)0);            // dwTotalFrames (patched)
        _w.Write((uint)0);            // dwInitialFrames
        _w.Write((uint)1);            // dwStreams
        _w.Write((uint)0);            // dwSuggestedBufferSize
        _w.Write((uint)_width);
        _w.Write((uint)_height);
        for (var k = 0; k < 4; k++) _w.Write((uint)0); // dwReserved[4]

        // LIST strl
        WriteFourCc("LIST");
        _w.Write((uint)116); // strl size = 4 ('strl') + 64 (strh) + 48 (strf)
        WriteFourCc("strl");

        // strh
        WriteFourCc("strh");
        _w.Write((uint)56);
        WriteFourCc("vids");
        WriteFourCc("MJPG");
        _w.Write((uint)0);            // dwFlags
        _w.Write((ushort)0);          // wPriority
        _w.Write((ushort)0);          // wLanguage
        _w.Write((uint)0);            // dwInitialFrames
        _w.Write((uint)1);            // dwScale
        _w.Write((uint)Math.Round(_fps)); // dwRate (rate/scale = fps)
        _w.Write((uint)0);            // dwStart
        _strhLengthPos = _s.Position;
        _w.Write((uint)0);            // dwLength (patched)
        _w.Write((uint)0);            // dwSuggestedBufferSize
        _w.Write((uint)0xFFFFFFFF);   // dwQuality
        _w.Write((uint)0);            // dwSampleSize
        _w.Write((short)0);           // rcFrame.left
        _w.Write((short)0);           // rcFrame.top
        _w.Write((short)_width);      // rcFrame.right
        _w.Write((short)_height);     // rcFrame.bottom

        // strf (BITMAPINFOHEADER)
        WriteFourCc("strf");
        _w.Write((uint)40);
        _w.Write((uint)40);           // biSize
        _w.Write(_width);             // biWidth
        _w.Write(_height);            // biHeight
        _w.Write((ushort)1);          // biPlanes
        _w.Write((ushort)24);         // biBitCount
        WriteFourCc("MJPG");          // biCompression
        _w.Write((uint)(_width * _height * 3)); // biSizeImage
        _w.Write(0);                  // biXPelsPerMeter
        _w.Write(0);                  // biYPelsPerMeter
        _w.Write((uint)0);            // biClrUsed
        _w.Write((uint)0);            // biClrImportant

        // LIST movi
        WriteFourCc("LIST");
        _moviSizePos = _s.Position;
        _w.Write((uint)0);            // movi size (patched)
        _moviListPos = _s.Position;   // index offsets are relative to the 'movi' FOURCC
        WriteFourCc("movi");

        _headerWritten = true;
    }

    private void WriteFourCc(string fourCc) => _w.Write(Encoding.ASCII.GetBytes(fourCc));

    private void PatchUInt32(long position, uint value)
    {
        var cur = _s.Position;
        _s.Position = position;
        _w.Write(value);
        _s.Position = cur;
    }

    public void Dispose()
    {
        _w.Dispose();
    }
}
