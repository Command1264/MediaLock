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

    internal SessionMatchScore? Score(MediaSessionSnapshot candidate)
    {
        if (!CanRepresent(candidate))
        {
            return null;
        }

        var observationGap = (candidate.ObservedAt - ObservedAt).Duration();
        var proximity = observationGap <= TimeSpan.FromMinutes(1)
            ? ObservationProximity.WithinOneMinute
            : observationGap <= TimeSpan.FromMinutes(5)
                ? ObservationProximity.WithinFiveMinutes
                : observationGap <= TimeSpan.FromMinutes(15)
                    ? ObservationProximity.WithinFifteenMinutes
                    : ObservationProximity.Distant;
        var titleMatches = !string.IsNullOrEmpty(Title) &&
            string.Equals(Title, candidate.Metadata?.Title, StringComparison.Ordinal);
        var artistMatches = !string.IsNullOrEmpty(Artist) &&
            string.Equals(Artist, candidate.Metadata?.Artist, StringComparison.Ordinal);
        var hasMetadataEvidence = !string.IsNullOrEmpty(Title) || !string.IsNullOrEmpty(Artist);
        var allAvailableMetadataMatches =
            (string.IsNullOrEmpty(Title) || titleMatches) &&
            (string.IsNullOrEmpty(Artist) || artistMatches);
        var confidence = Descriptor.SessionInstanceHint is not null
            ? SessionMatchConfidence.StableDescriptor
            : hasMetadataEvidence &&
                allAvailableMetadataMatches &&
                proximity != ObservationProximity.Distant
                ? SessionMatchConfidence.ObservedCharacteristics
                : SessionMatchConfidence.Unacceptable;

        return new SessionMatchScore(
            confidence,
            Convert.ToInt32(titleMatches) + Convert.ToInt32(artistMatches),
            PlaybackStatus == candidate.PlaybackStatus,
            proximity);
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

internal enum SessionMatchConfidence
{
    Unacceptable,
    ObservedCharacteristics,
    StableDescriptor,
}

internal enum ObservationProximity
{
    Distant,
    WithinFifteenMinutes,
    WithinFiveMinutes,
    WithinOneMinute,
}

internal readonly record struct SessionMatchScore(
    SessionMatchConfidence Confidence,
    int MetadataMatches,
    bool PlaybackStatusMatches,
    ObservationProximity ObservationProximity) : IComparable<SessionMatchScore>
{
    public bool IsAcceptable => Confidence != SessionMatchConfidence.Unacceptable;

    public int CompareTo(SessionMatchScore other)
    {
        var comparison = Confidence.CompareTo(other.Confidence);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = MetadataMatches.CompareTo(other.MetadataMatches);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = PlaybackStatusMatches.CompareTo(other.PlaybackStatusMatches);
        return comparison != 0
            ? comparison
            : ObservationProximity.CompareTo(other.ObservationProximity);
    }
}
