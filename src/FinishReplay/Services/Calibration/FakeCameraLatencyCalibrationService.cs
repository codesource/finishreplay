using FinishReplay.Models;
using FinishReplay.Services.Camera;
using FinishReplay.Services.Camera.Providers;

namespace FinishReplay.Services.Calibration;

/// <summary>
/// Stand-in calibration service used until the real capture + flash detection pipeline exists.
/// It returns deterministic, plausible latencies (derived from the camera id and source type)
/// so the calibration UI and synchronization math can be exercised end-to-end.
///
/// The real flow this stands in for:
///   1. open each camera stream,
///   2. for each of <see cref="CalibrationSettings.FlashCount"/> flashes: record the trigger
///      time (<see cref="ITriggerOutput.FireAsync"/>) then detect the first flashed frame
///      (<see cref="IFlashDetector"/>),
///   3. latency = frameArrivalTime - flashTriggerTime, averaged across flashes.
///
/// TODO: implement the real pipeline with <see cref="CameraProviderRegistry"/>,
///       <see cref="ITriggerOutput"/> and <see cref="IFlashDetector"/>.
/// </summary>
public sealed class FakeCameraLatencyCalibrationService : ICameraLatencyCalibrationService
{
    public Task<CameraLatencyCalibrationResult> CalibrateAsync(
        IReadOnlyList<CameraProfile> cameras,
        CalibrationSettings settings,
        CancellationToken cancellationToken = default)
    {
        var results = new List<CameraLatencyResult>(cameras.Count);

        foreach (var cam in cameras)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Deterministic "measurement": base latency by transport + a stable per-camera jitter.
            var baseMs = cam.SourceType switch
            {
                MjpegCameraProvider.Type => 70.0,   // MJPEG: low, stable
                UsbCameraProvider.Type => 90.0,     // USB: moderate
                RtspCameraProvider.Type => 120.0,   // RTSP/H.264: higher, less stable
                _ => 100.0,
            };

            var jitter = (StableHash(cam.Id) % 25); // 0..24 ms, stable per camera
            var samples = new List<double>(Math.Max(1, settings.FlashCount));
            for (var i = 0; i < Math.Max(1, settings.FlashCount); i++)
                samples.Add(baseMs + jitter + (i % 3)); // tiny per-flash variation

            var latency = samples.Average();

            results.Add(new CameraLatencyResult
            {
                CameraId = cam.Id,
                Success = true,
                LatencyMs = Math.Round(latency, 1),
                Confidence = cam.SourceType == MjpegCameraProvider.Type ? 0.95 : 0.8,
                RawSamplesMs = samples,
            });
        }

        var result = new CameraLatencyCalibrationResult
        {
            StartedAt = DateTimeOffset.Now,
            CameraResults = results,
        };

        return Task.FromResult(result);
    }

    /// <summary>Small stable hash so results are reproducible across runs (no RNG).</summary>
    private static int StableHash(string s)
    {
        unchecked
        {
            var hash = 17;
            foreach (var c in s)
                hash = (hash * 31) + c;
            return Math.Abs(hash);
        }
    }
}
