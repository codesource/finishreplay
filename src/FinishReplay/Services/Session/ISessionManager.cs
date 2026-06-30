using FinishReplay.Models;

namespace FinishReplay.Services.Session;

/// <summary>
/// Creates sessions and persists their metadata as <c>&lt;sessionId&gt;.timing.json</c> in the
/// session folder, and enumerates previously recorded sessions.
/// </summary>
public interface ISessionManager
{
    /// <summary>Build a new (unsaved) metadata object for a session about to be recorded.</summary>
    SessionMetadata CreateSession(string sessionId, TimeSpan preRecord, TimeSpan postRecord, string timingProvider);

    /// <summary>Persist the session's metadata JSON into <paramref name="directory"/>.</summary>
    Task SaveAsync(string directory, SessionMetadata session);

    /// <summary>Load a session from its <c>.timing.json</c> path.</summary>
    Task<SessionMetadata?> LoadAsync(string metadataFilePath);

    /// <summary>Enumerate saved sessions (newest first) found under <paramref name="directory"/>.</summary>
    IReadOnlyList<SessionInfo> GetRecentSessions(string directory);

    /// <summary>The conventional metadata path for a session id within a directory.</summary>
    string GetMetadataPath(string directory, string sessionId);
}
