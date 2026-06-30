namespace FinishReplay.Models;

/// <summary>Inputs to a latency calibration run.</summary>
public sealed class CalibrationSettings
{
    /// <summary>Number of flashes to average for a more stable result.</summary>
    public int FlashCount { get; init; } = 3;

    /// <summary>Default region of interest for flash detection when a camera has none configured.</summary>
    public RegionOfInterest Region { get; init; } = RegionOfInterest.Full;

    /// <summary>Id of the trigger output device that fires the LED/relay (null = first available).</summary>
    public string? TriggerOutputId { get; init; }
}

/// <summary>Result of calibrating one camera.</summary>
public sealed class CameraLatencyResult
{
    public string CameraId { get; init; } = "";
    public bool Success { get; init; }

    /// <summary>Measured end-to-end latency in milliseconds, when <see cref="Success"/> is true.</summary>
    public double? LatencyMs { get; init; }

    /// <summary>Detector confidence 0..1 (higher = clearer flash edge).</summary>
    public double? Confidence { get; init; }

    public string? ErrorMessage { get; init; }

    /// <summary>Per-flash raw measurements (ms), kept for debugging/manual review.</summary>
    public IReadOnlyList<double> RawSamplesMs { get; init; } = Array.Empty<double>();
}

/// <summary>Result of a full calibration run across the selected cameras.</summary>
public sealed class CameraLatencyCalibrationResult
{
    public DateTimeOffset StartedAt { get; init; }
    public IReadOnlyList<CameraLatencyResult> CameraResults { get; init; } = Array.Empty<CameraLatencyResult>();
}

/// <summary>Outcome of analysing a frame sequence for a flash within a region of interest.</summary>
public sealed class FlashDetectionResult
{
    public bool Detected { get; init; }

    /// <summary>Arrival timestamp of the first frame in which the flash is visible.</summary>
    public TimeSpan FrameArrivalTime { get; init; }

    /// <summary>Detector confidence 0..1.</summary>
    public double Confidence { get; init; }
}
