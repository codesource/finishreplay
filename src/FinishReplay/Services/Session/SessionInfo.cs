namespace FinishReplay.Services.Session;

/// <summary>Lightweight summary of a saved session for the "recent sessions" list.</summary>
public sealed record SessionInfo(string SessionId, string MetadataFilePath, DateTimeOffset RecordedAt)
{
    public override string ToString() => SessionId;
}
