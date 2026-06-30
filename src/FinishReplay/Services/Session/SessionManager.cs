using System.Text.Json;
using FinishReplay.Models;

namespace FinishReplay.Services.Session;

/// <summary>
/// File-based <see cref="ISessionManager"/>. One metadata file per session
/// (<c>&lt;sessionId&gt;.timing.json</c>) sits alongside the per-camera video files in the
/// session folder.
/// </summary>
public sealed class SessionManager : ISessionManager
{
    private const string MetadataExtension = ".timing.json";

    public SessionMetadata CreateSession(string sessionId, TimeSpan preRecord, TimeSpan postRecord, string timingProvider) => new()
    {
        SessionId = sessionId,
        CreatedAt = DateTimeOffset.Now,
        TimingProvider = timingProvider,
        PreRecordSeconds = preRecord.TotalSeconds,
        PostRecordSeconds = postRecord.TotalSeconds,
        Cameras = new List<SessionCamera>(),
        TimingMarkers = new List<TimingTrigger>(),
    };

    public string GetMetadataPath(string directory, string sessionId)
        => Path.Combine(directory, sessionId + MetadataExtension);

    public async Task SaveAsync(string directory, SessionMetadata session)
    {
        Directory.CreateDirectory(directory);
        var path = GetMetadataPath(directory, session.SessionId);
        var json = JsonSerializer.Serialize(session, SessionMetadata.JsonOptions);
        await File.WriteAllTextAsync(path, json).ConfigureAwait(false);
    }

    public async Task<SessionMetadata?> LoadAsync(string metadataFilePath)
    {
        if (!File.Exists(metadataFilePath))
            return null;

        await using var stream = File.OpenRead(metadataFilePath);
        return await JsonSerializer
            .DeserializeAsync<SessionMetadata>(stream, SessionMetadata.JsonOptions)
            .ConfigureAwait(false);
    }

    public IReadOnlyList<SessionInfo> GetRecentSessions(string directory)
    {
        if (!Directory.Exists(directory))
            return Array.Empty<SessionInfo>();

        var sessions = new List<SessionInfo>();
        foreach (var metaPath in Directory.EnumerateFiles(directory, "*" + MetadataExtension))
        {
            var fileName = Path.GetFileName(metaPath);
            var sessionId = fileName[..^MetadataExtension.Length];
            sessions.Add(new SessionInfo(
                SessionId: sessionId,
                MetadataFilePath: metaPath,
                RecordedAt: File.GetLastWriteTime(metaPath)));
        }

        return sessions
            .OrderByDescending(s => s.RecordedAt)
            .ToList();
    }
}
