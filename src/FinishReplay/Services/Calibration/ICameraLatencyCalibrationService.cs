using FinishReplay.Models;

namespace FinishReplay.Services.Calibration;

/// <summary>
/// Measures end-to-end latency per camera by firing a trigger flash and detecting the first
/// frame in which it appears: <c>latency = frameArrivalTime - flashTriggerTime</c>.
/// </summary>
public interface ICameraLatencyCalibrationService
{
    Task<CameraLatencyCalibrationResult> CalibrateAsync(
        IReadOnlyList<CameraProfile> cameras,
        CalibrationSettings settings,
        CancellationToken cancellationToken = default);
}
