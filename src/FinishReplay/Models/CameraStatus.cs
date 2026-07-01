namespace FinishReplay.Models;

/// <summary>Reachability of a configured camera, refreshed periodically.</summary>
public enum CameraStatus
{
    Unknown,
    Checking,
    Reachable,
    Unreachable,
}
