namespace MediaLock.Core.Media;

public readonly record struct SessionKey(string Value)
{
    public override string ToString() => Value;
}

public enum PlaybackStatus
{
    Unknown,
    Closed,
    Opened,
    Changing,
    Stopped,
    Playing,
    Paused,
}

public sealed record MediaSessionSnapshot(
    SessionKey Key,
    string SourceAppUserModelId,
    PlaybackStatus PlaybackStatus,
    MediaCommandCapabilities Capabilities,
    DateTimeOffset ObservedAt,
    string? SessionInstanceHint = null,
    MediaMetadata? Metadata = null,
    MediaTimeline? Timeline = null);

public sealed record MediaMetadata(
    string? Title,
    string? Artist,
    string? AlbumTitle,
    int? TrackNumber);

public sealed record MediaTimeline(
    TimeSpan Start,
    TimeSpan End,
    TimeSpan Position,
    DateTimeOffset LastUpdatedAt);

public sealed record SessionFingerprint(
    string SourceAppUserModelId,
    string? SessionInstanceHint)
{
    public static SessionFingerprint From(MediaSessionSnapshot session) => new(
        session.SourceAppUserModelId,
        session.SessionInstanceHint);
}
