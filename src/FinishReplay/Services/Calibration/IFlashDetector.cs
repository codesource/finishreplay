using FinishReplay.Models;

namespace FinishReplay.Services.Calibration;

/// <summary>
/// Finds the first frame in which a calibration flash becomes visible within a region of interest.
/// </summary>
public interface IFlashDetector
{
    /// <summary>
    /// Scan <paramref name="frames"/> (in arrival order) for a sudden brightness increase inside
    /// <paramref name="region"/> and report the first frame that contains the flash.
    /// </summary>
    FlashDetectionResult Detect(IReadOnlyList<VideoFrame> frames, RegionOfInterest region);
}
