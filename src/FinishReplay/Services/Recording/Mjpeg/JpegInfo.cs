namespace FinishReplay.Services.Recording.Mjpeg;

/// <summary>Reads basic properties (dimensions) from a JPEG byte buffer without fully decoding it.</summary>
public static class JpegInfo
{
    /// <summary>Try to read the image dimensions from the JPEG's Start-Of-Frame segment.</summary>
    public static bool TryGetDimensions(byte[] jpeg, out int width, out int height)
    {
        width = 0;
        height = 0;
        var i = 2; // skip SOI (FF D8)

        while (i + 1 < jpeg.Length)
        {
            if (jpeg[i] != 0xFF) { i++; continue; }

            var marker = jpeg[i + 1];

            // Standalone markers (no length): padding, SOI/EOI, restart markers.
            if (marker == 0xFF) { i++; continue; }
            if (marker is 0x01 or 0xD8 or 0xD9 || (marker >= 0xD0 && marker <= 0xD7)) { i += 2; continue; }

            if (i + 3 >= jpeg.Length) break;
            var segLength = (jpeg[i + 2] << 8) | jpeg[i + 3];

            // SOF markers carry the frame size (exclude DHT/JPG/DAC: C4, C8, CC).
            var isSof = marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC);
            if (isSof && i + 8 < jpeg.Length)
            {
                height = (jpeg[i + 5] << 8) | jpeg[i + 6];
                width = (jpeg[i + 7] << 8) | jpeg[i + 8];
                return true;
            }

            i += 2 + segLength; // advance past this segment
        }

        return false;
    }
}
