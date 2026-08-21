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
    MediaTimeline? Timeline = null)
{
    public SessionDescriptor Descriptor => new(
        SourceAppUserModelId,
        SessionInstanceHint);
}

public sealed record SessionDescriptor(
    string SourceAppUserModelId,
    string? SessionInstanceHint);

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
    SessionDescriptor Descriptor,
    PlaybackStatus PlaybackStatus,
    DateTimeOffset ObservedAt,
    string? Title,
    string? Artist)
{
    public static SessionFingerprint From(MediaSessionSnapshot session) => new(
        session.Descriptor,
        session.PlaybackStatus,
        session.ObservedAt,
        session.Metadata?.Title,
        session.Metadata?.Artist);

    internal long? Score(MediaSessionSnapshot candidate)
    {
        if (!CanRepresent(candidate))
        {
            return null;
        }

        var score = Descriptor.SessionInstanceHint is null ? 0L : 1_000_000L;
        if (!string.IsNullOrEmpty(Title) &&
            string.Equals(Title, candidate.Metadata?.Title, StringComparison.Ordinal))
        {
            score += 10_000;
        }

        if (!string.IsNullOrEmpty(Artist) &&
            string.Equals(Artist, candidate.Metadata?.Artist, StringComparison.Ordinal))
        {
            score += 5_000;
        }

        if (PlaybackStatus == candidate.PlaybackStatus)
        {
            score += 1_000;
        }

        var observationGap = (candidate.ObservedAt - ObservedAt).Duration();
        score += observationGap <= TimeSpan.FromMinutes(1)
            ? 100
            : observationGap <= TimeSpan.FromMinutes(5)
                ? 50
                : observationGap <= TimeSpan.FromMinutes(15)
                    ? 10
                    : 0;

        return score;
    }

    internal bool CanRepresent(MediaSessionSnapshot candidate) =>
        string.Equals(
            Descriptor.SourceAppUserModelId,
            candidate.SourceAppUserModelId,
            StringComparison.Ordinal) &&
        (Descriptor.SessionInstanceHint is null ||
            string.Equals(
                Descriptor.SessionInstanceHint,
                candidate.SessionInstanceHint,
                StringComparison.Ordinal));
}
