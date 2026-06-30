using System.Text.Json;
using System.Text.Json.Serialization;

namespace FinishReplay.Models;

/// <summary>
/// Metadata persisted per session as <c>&lt;sessionId&gt;.timing.json</c> next to the video files.
/// A session may contain several cameras (different protocols and latencies); each is stored with
/// its source, file, calibrated latency, manual offset and computed sync offset so synchronized
/// replay can line the cameras up. Loaded/saved by
/// <see cref="FinishReplay.Services.Session.ISessionManager"/>.
/// </summary>
public sealed class SessionMetadata
{
    public string SessionId { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Name of the timing provider that produced the markers (e.g. "ALGE TimY3", "Manual").</summary>
    public string TimingProvider { get; set; } = "";

    public double PreRecordSeconds { get; set; }
    public double PostRecordSeconds { get; set; }

    public List<SessionCamera> Cameras { get; set; } = new();

    public List<TimingTrigger> TimingMarkers { get; set; } = new();

    /// <summary>Shared JSON options: indented, camelCase, enums as names, tolerant reader.</summary>
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };
}
