using FinishReplay.Models;

namespace FinishReplay.Services.Calibration;

/// <summary>
/// Simple brightness-step flash detector: walks frames computing mean brightness inside the ROI
/// and flags the first frame whose brightness jumps well above the running baseline.
///
/// This is the intentionally-simple first implementation described in the design.
/// TODO: replace/augment with OpenCV for robust detection (adaptive threshold, denoise,
///       sub-frame interpolation) and a better confidence estimate.
/// </summary>
public sealed class BrightnessFlashDetector : IFlashDetector
{
    /// <summary>Fractional brightness jump (0..1) over baseline that counts as a flash.</summary>
    private readonly double _threshold;

    public BrightnessFlashDetector(double threshold = 0.25)
    {
        _threshold = threshold;
    }

    public FlashDetectionResult Detect(IReadOnlyList<VideoFrame> frames, RegionOfInterest region)
    {
        if (frames.Count == 0)
            return new FlashDetectionResult { Detected = false };

        double baseline = MeanBrightness(frames[0], region);

        for (var i = 1; i < frames.Count; i++)
        {
            var brightness = MeanBrightness(frames[i], region);
            var jump = brightness - baseline;

            if (jump >= _threshold)
            {
                return new FlashDetectionResult
                {
                    Detected = true,
                    FrameArrivalTime = frames[i].Timestamp,
                    Confidence = Math.Clamp(jump, 0, 1),
                };
            }

            // Slowly track the baseline so gradual lighting changes don't trip detection.
            baseline = (baseline * 0.8) + (brightness * 0.2);
        }

        return new FlashDetectionResult { Detected = false };
    }

    /// <summary>
    /// Mean brightness (0..1) of the ROI.
    /// TODO: honour the ROI, and decode JPEG frames before sampling (today only raw BGRA frames are
    /// averaged; JPEG frames return 0 until a decode step is added here).
    /// </summary>
    private static double MeanBrightness(VideoFrame frame, RegionOfInterest region)
    {
        if (frame.Data.Length == 0 || frame.Format != VideoFrameFormat.Bgra32)
            return 0;

        long sum = 0;
        foreach (var b in frame.Data)
            sum += b;
        return sum / (double)frame.Data.Length / 255.0;
    }
}
