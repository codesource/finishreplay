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
