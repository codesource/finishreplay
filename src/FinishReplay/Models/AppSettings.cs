using System.Text.Json;
using System.Text.Json.Serialization;

namespace FinishReplay.Models;

/// <summary>
/// User-configurable application settings, persisted by
/// <see cref="FinishReplay.Services.Settings.ISettingsService"/>. Holds recording buffers,
/// where files are written, the configured cameras (with per-camera filename suffix) and the
/// clip filename template.
/// </summary>
public sealed class AppSettings
{
    public double PreRecordSeconds { get; set; } = 5;
    public double PostRecordSeconds { get; set; } = 3;

    /// <summary>Folder where session clips and metadata are written.</summary>
    public string StorageDirectory { get; set; } = DefaultStorageDirectory;

    /// <summary>
    /// Clip filename template. Supported tokens (case-insensitive):
    /// <c>{date}</c>, <c>{category}</c>, <c>{discipline}</c>, <c>{serie}</c>, <c>{camera}</c>.
    /// </summary>
    public string FilenameFormat { get; set; } = "{date}-{category}-{discipline}-{serie}-{camera}";

    /// <summary>Path to the ffmpeg executable (used for RTSP/H.264 capture). Bare name = search PATH.</summary>
    public string FfmpegPath { get; set; } = "ffmpeg";

    /// <summary>How clips are recorded: re-encode to MJPEG AVI, or copy H.264 to MP4 (archival).</summary>
    public RecordingMode RecordingMode { get; set; } = RecordingMode.Transcode;

    /// <summary>External ffmpeg process (default) or embedded, crash-isolated worker for RTSP/USB.</summary>
    public VideoBackend VideoBackend { get; set; } = VideoBackend.ExternalProcess;

    /// <summary>Which timing provider supplies trigger markers.</summary>
    public TimingSource TimingSource { get; set; } = TimingSource.Manual;

    /// <summary>Serial/USB-serial port for the ALGE TimY3 (e.g. "COM3").</summary>
    public string TimingSerialPort { get; set; } = "";

    public int TimingBaudRate { get; set; } = 9600;

    /// <summary>How often (seconds) to re-check camera reachability in Settings.</summary>
    public int CameraStatusIntervalSeconds { get; set; } = 5;

    // Current event context used when building filenames.
    public string Category { get; set; } = "";
    public string Discipline { get; set; } = "";
    public int SeriesNumber { get; set; } = 1;

    public List<CameraProfile> Cameras { get; set; } = new();

    public static string DefaultStorageDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "FinishReplay");

    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };
}
