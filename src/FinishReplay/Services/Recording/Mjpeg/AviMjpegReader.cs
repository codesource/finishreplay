using System.Text;

namespace FinishReplay.Services.Recording.Mjpeg;

/// <summary>
/// Reads the JPEG frames back out of a Motion-JPEG AVI written by <see cref="AviMjpegWriter"/>
/// (or any standard MJPG AVI). Walks the RIFF tree to the <c>movi</c> list and returns each
/// <c>00dc</c> video chunk's payload, ignoring the index.
/// </summary>
public static class AviMjpegReader
{
    public static IReadOnlyList<byte[]> ReadFrames(Stream stream)
    {
        var frames = new List<byte[]>();
        using var r = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);

        if (ReadFourCc(r) != "RIFF") return frames;
        r.ReadUInt32();                 // RIFF size
        if (ReadFourCc(r) != "AVI ") return frames;

        while (stream.Position + 8 <= stream.Length)
        {
            var id = ReadFourCc(r);
            var size = r.ReadUInt32();
            var dataStart = stream.Position;

            if (id == "LIST")
            {
                var listType = ReadFourCc(r);
                if (listType == "movi")
                    ReadMovi(r, stream, dataStart + size, frames);
                else
                    stream.Position = dataStart + size; // skip other lists (hdrl, ...)
            }
            else
            {
                stream.Position = dataStart + size; // skip idx1, JUNK, ...
            }

            if ((size & 1) == 1 && stream.Position < stream.Length)
                stream.Position++; // word-alignment padding
        }

        return frames;
    }

    private static void ReadMovi(BinaryReader r, Stream stream, long end, List<byte[]> frames)
    {
        while (stream.Position + 8 <= end)
        {
            var id = ReadFourCc(r);
            var size = r.ReadUInt32();

            if (id.EndsWith("dc", StringComparison.Ordinal) || id.EndsWith("db", StringComparison.Ordinal))
                frames.Add(r.ReadBytes((int)size));
            else
                stream.Position += size;

            if ((size & 1) == 1 && stream.Position < end)
                stream.Position++;
        }
    }

    private static string ReadFourCc(BinaryReader r) => Encoding.ASCII.GetString(r.ReadBytes(4));
}
