using System.Collections.Immutable;

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

public enum MediaPlaybackType
{
    Unknown,
    Music,
    Video,
    Image,
}

public sealed record MediaSessionSnapshot(
    SessionKey Key,
    string SourceAppUserModelId,
    PlaybackStatus PlaybackStatus,
    MediaCommandCapabilities Capabilities,
    DateTimeOffset ObservedAt,
    string? SessionInstanceHint = null,
    MediaMetadata? Metadata = null,
    MediaTimeline? Timeline = null,
    MediaPlaybackType PlaybackType = MediaPlaybackType.Unknown,
    MediaArtwork? Artwork = null)
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

public sealed record MediaArtwork
{
    public const int MaximumEncodedByteCount = 5 * 1024 * 1024;

    private static readonly byte[] PngSignature =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private MediaArtwork(ImmutableArray<byte> bytes, string contentType)
    {
        Bytes = bytes;
        ContentType = contentType;
    }

    public ImmutableArray<byte> Bytes { get; }

    public string ContentType { get; }

    public static bool TryCreate(ReadOnlySpan<byte> encoded, out MediaArtwork? artwork)
    {
        artwork = null;
        if (encoded.IsEmpty || encoded.Length > MaximumEncodedByteCount)
        {
            return false;
        }

        var contentType = encoded.StartsWith(PngSignature)
            ? "image/png"
            : encoded.Length >= 3 &&
                encoded[0] == 0xFF &&
                encoded[1] == 0xD8 &&
                encoded[2] == 0xFF
                ? "image/jpeg"
                : null;
        if (contentType is null)
        {
            return false;
        }

        artwork = new MediaArtwork(ImmutableArray.CreateRange(encoded.ToArray()), contentType);
        return true;
    }
}

public sealed record SessionFingerprint(
    SessionDescriptor Descriptor,
    PlaybackStatus PlaybackStatus,
    DateTimeOffset ObservedAt,
    MediaPlaybackType PlaybackType,
    string? Title,
    string? Artist)
{
    public static SessionFingerprint From(MediaSessionSnapshot session) => new(
        session.Descriptor,
        session.PlaybackStatus,
        session.ObservedAt,
        session.PlaybackType,
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
        var playbackTypeIsCompatible =
            PlaybackType == MediaPlaybackType.Unknown ||
            candidate.PlaybackType == MediaPlaybackType.Unknown ||
            PlaybackType == candidate.PlaybackType;
        var confidence = Descriptor.SessionInstanceHint is not null &&
            proximity != ObservationProximity.Distant
            ? SessionMatchConfidence.StableDescriptor
            : playbackTypeIsCompatible &&
                hasMetadataEvidence &&
                allAvailableMetadataMatches &&
                proximity != ObservationProximity.Distant
                ? SessionMatchConfidence.ObservedCharacteristics
                : playbackTypeIsCompatible && proximity != ObservationProximity.Distant
                    ? SessionMatchConfidence.RecentSameSource
                    : SessionMatchConfidence.Unacceptable;

        return new SessionMatchScore(
            confidence,
            Convert.ToInt32(titleMatches) + Convert.ToInt32(artistMatches),
            PlaybackStatus == candidate.PlaybackStatus,
            PlaybackType == candidate.PlaybackType,
            proximity);
    }

    public bool IsAcceptableSuccessor(MediaSessionSnapshot candidate) =>
        Score(candidate) is { IsAcceptable: true };

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
    RecentSameSource,
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
    bool PlaybackTypeMatches,
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
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = PlaybackTypeMatches.CompareTo(other.PlaybackTypeMatches);
        return comparison != 0
            ? comparison
            : ObservationProximity.CompareTo(other.ObservationProximity);
    }
}
