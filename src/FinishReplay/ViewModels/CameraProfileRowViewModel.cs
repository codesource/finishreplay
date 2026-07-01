using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using FinishReplay.Models;

namespace FinishReplay.ViewModels;

/// <summary>
/// Row in the camera list: wraps a <see cref="CameraProfile"/> for editing (selection, manual
/// offset) and shows calibration status. Edits are written back to the underlying profile so the
/// sync calculator sees them.
/// </summary>
public partial class CameraProfileRowViewModel : ObservableObject
{
    public CameraProfileRowViewModel(CameraProfile profile)
    {
        Profile = profile;
        _displayName = profile.DisplayName;
        _calibratedLatencyMs = profile.CalibratedLatencyMs;
        _manualOffsetMs = profile.ManualOffsetMs ?? 0;
        _calibrationStatus = profile.CalibratedLatencyMs is null ? "Not calibrated" : "Calibrated";
    }

    public CameraProfile Profile { get; }

    public string Id => Profile.Id;
    public string SourceType => Profile.SourceType;
    public string SourceUrl => Profile.SourceUrl;

    /// <summary>Whether this camera participates in recording/calibration.</summary>
    [ObservableProperty]
    private bool _isSelected = true;

    [ObservableProperty]
    private string _displayName;

    [ObservableProperty]
    private double? _calibratedLatencyMs;

    [ObservableProperty]
    private double _manualOffsetMs;

    [ObservableProperty]
    private string _calibrationStatus;

    [ObservableProperty]
    private double? _confidence;

    [ObservableProperty]
    private double _syncOffsetMs;

    /// <summary>Latest decoded preview frame, or null before any frame arrives.</summary>
    [ObservableProperty]
    private Bitmap? _preview;

    /// <summary>Capture error for this camera (e.g. ffmpeg missing / device busy), or null.</summary>
    [ObservableProperty]
    private string? _errorText;

    private long _lastPreviewTicks;

    /// <summary>Show a capture error in the preview tile (marshals to the UI thread).</summary>
    public void SetError(string message) =>
        Dispatcher.UIThread.Post(() => ErrorText = message);

    /// <summary>
    /// Decode a JPEG frame (off the UI thread) and publish it as the preview, throttled to ~15 fps
    /// to keep the UI light. Safe to call from the capture thread.
    /// </summary>
    public void SubmitJpeg(byte[] jpeg)
    {
        var now = Environment.TickCount64;
        if (now - _lastPreviewTicks < 66)
            return;
        _lastPreviewTicks = now;

        Bitmap bitmap;
        try
        {
            using var ms = new MemoryStream(jpeg);
            bitmap = new Bitmap(ms);
        }
        catch
        {
            return; // ignore undecodable frames
        }

        Dispatcher.UIThread.Post(() =>
        {
            var old = Preview;
            Preview = bitmap;
            old?.Dispose();
            ErrorText = null; // a frame arrived — clear any prior error
        });
    }

    partial void OnDisplayNameChanged(string value) => Profile.DisplayName = value;
    partial void OnManualOffsetMsChanged(double value) => Profile.ManualOffsetMs = value;
    partial void OnCalibratedLatencyMsChanged(double? value) => Profile.CalibratedLatencyMs = value;

    /// <summary>Apply a calibration outcome to both this row and the underlying profile.</summary>
    public void ApplyCalibration(double? latencyMs, double? confidence, bool success, string? error)
    {
        CalibratedLatencyMs = latencyMs;
        Confidence = confidence;
        CalibrationStatus = success
            ? $"Calibrated: {latencyMs:0.#} ms"
            : $"Failed: {error}";
    }
}
