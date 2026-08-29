using System.Collections.Immutable;

namespace MediaLock.Core.Media;

public enum MediaSessionCatalogStatus
{
    Available,
    Suspended,
    Reacquiring,
    Unavailable,
}

public sealed record MediaSourceGroupHint
{
    public MediaSourceGroupHint(string key, string displayName)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("A media source group key is required.", nameof(key));
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException(
                "A media source group display name is required.",
                nameof(displayName));
        }

        Key = key;
        DisplayName = displayName;
    }

    public string Key { get; }

    public string DisplayName { get; }
}

public sealed record MediaTargetPresentation
{
    public MediaTargetPresentation(
        string SourceDisplayName,
        PlaybackStatus PlaybackStatus,
        MediaCommandCapabilities Capabilities,
        DateTimeOffset ObservedAt,
        MediaMetadata? Metadata = null,
        MediaTimeline? Timeline = null,
        MediaPlaybackType PlaybackType = MediaPlaybackType.Unknown,
        MediaArtwork? Artwork = null,
        double? ReportedPlaybackRate = null,
        MediaSourceGroupHint? SourceGroup = null)
    {
        this.SourceDisplayName = SourceDisplayName;
        this.PlaybackStatus = PlaybackStatus;
        this.Capabilities = Capabilities;
        this.ObservedAt = ObservedAt;
        this.Metadata = Metadata;
        this.Timeline = Timeline;
        this.PlaybackType = PlaybackType;
        this.Artwork = Artwork;
        this.ReportedPlaybackRate = PlaybackRateResolution.NormalizeReported(ReportedPlaybackRate);
        this.SourceGroup = SourceGroup;
        PlaybackRate = PlaybackRateResolution.FromReported(this.ReportedPlaybackRate);
    }

    public string SourceDisplayName { get; init; }

    public PlaybackStatus PlaybackStatus { get; init; }

    public MediaCommandCapabilities Capabilities { get; init; }

    public DateTimeOffset ObservedAt { get; init; }

    public MediaMetadata? Metadata { get; init; }

    public MediaTimeline? Timeline { get; init; }

    public MediaPlaybackType PlaybackType { get; init; }

    public MediaArtwork? Artwork { get; init; }

    public double? ReportedPlaybackRate { get; }

    public MediaSourceGroupHint? SourceGroup { get; init; }

    public PlaybackRateResolution PlaybackRate { get; private init; }

    public MonotonicTimestamp? MonotonicObservedAt { get; private init; }

    public MediaTargetPresentation WithPlaybackRateProjection(
        PlaybackRateResolution playbackRate,
        MonotonicTimestamp? monotonicObservedAt) => this with
        {
            PlaybackRate = PlaybackRateResolution.Validate(playbackRate),
            MonotonicObservedAt = monotonicObservedAt,
        };
}

public sealed record MediaTargetSnapshot
{
    private MediaTargetSnapshot(
        MediaTargetId id,
        MediaTargetPresentation presentation,
        MediaSessionSnapshot? gsmtcSession)
    {
        Id = id;
        Presentation = presentation;
        GsmtcSession = gsmtcSession;
    }

    public MediaTargetId Id { get; }

    public MediaTargetPresentation Presentation { get; }

    public MediaSessionSnapshot? GsmtcSession { get; }

    public MediaTargetSnapshot WithPresentation(MediaTargetPresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        return new MediaTargetSnapshot(Id, presentation, GsmtcSession);
    }

    public static MediaTargetSnapshot FromGsmtc(MediaSessionSnapshot session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return new MediaTargetSnapshot(
            MediaTargetId.FromGsmtc(session.Key),
            new MediaTargetPresentation(
            session.SourceAppUserModelId,
            session.PlaybackStatus,
            session.Capabilities,
            session.ObservedAt,
            session.Metadata,
            session.Timeline,
            session.PlaybackType,
            session.Artwork,
            ReportedPlaybackRate: session.ReportedPlaybackRate),
            session);
    }

    public static MediaTargetSnapshot FromBrowserPageBinding(
        string bindingId,
        MediaTargetPresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        return new MediaTargetSnapshot(
            MediaTargetId.FromBrowserPageBinding(bindingId),
            presentation,
            null);
    }

    public static MediaTargetSnapshot FromProvider(
        MediaTargetId id,
        MediaTargetPresentation presentation)
    {
        if (!id.IsValid || id.Provider == MediaTargetProviderId.Gsmtc)
        {
            throw new ArgumentException(
                "A valid non-GSMTC media target identity is required.",
                nameof(id));
        }

        ArgumentNullException.ThrowIfNull(presentation);
        return new MediaTargetSnapshot(id, presentation, null);
    }
}

public sealed record AuthoritativeMediaTargetCorrelation
{
    public AuthoritativeMediaTargetCorrelation(
        MediaTargetId directTarget,
        MediaTargetId duplicateGsmtcTarget)
    {
        if (!directTarget.IsValid || directTarget.Provider == MediaTargetProviderId.Gsmtc)
        {
            throw new ArgumentException("The direct target must not be a GSMTC target.", nameof(directTarget));
        }

        if (!duplicateGsmtcTarget.IsValid ||
            duplicateGsmtcTarget.Provider != MediaTargetProviderId.Gsmtc)
        {
            throw new ArgumentException("The duplicate target must be a GSMTC target.", nameof(duplicateGsmtcTarget));
        }

        DirectTarget = directTarget;
        DuplicateGsmtcTarget = duplicateGsmtcTarget;
    }

    public MediaTargetId DirectTarget { get; }

    public MediaTargetId DuplicateGsmtcTarget { get; }
}

public sealed record MediaTargetCatalogSnapshot(
    ImmutableArray<MediaTargetSnapshot> ObservedTargets,
    MediaTargetId? WindowsCurrentTarget,
    ImmutableArray<AuthoritativeMediaTargetCorrelation> AuthoritativeCorrelations,
    MediaSessionCatalogStatus Status = MediaSessionCatalogStatus.Available,
    string? StatusMessage = null)
{
    public ImmutableArray<MediaTargetSnapshot> Targets =>
        MediaTargetReconciler.Reconcile(ObservedTargets, AuthoritativeCorrelations);

    public ImmutableArray<MediaSessionSnapshot> Sessions => Targets
        .Where(target => target.GsmtcSession is not null)
        .Select(target => target.GsmtcSession!)
        .ToImmutableArray();

    public SessionKey? WindowsCurrentSession => WindowsCurrentTarget is
    { Provider: var provider, Value: var value } &&
        provider == MediaTargetProviderId.Gsmtc &&
        Targets.Any(target => target.Id == WindowsCurrentTarget)
            ? new SessionKey(value)
            : null;
}

internal static class MediaTargetReconciler
{
    public static ImmutableArray<MediaTargetSnapshot> Reconcile(
        ImmutableArray<MediaTargetSnapshot> observedTargets,
        ImmutableArray<AuthoritativeMediaTargetCorrelation> authoritativeCorrelations)
    {
        if (observedTargets.IsDefault)
        {
            throw new ArgumentException("Observed targets must be initialized.", nameof(observedTargets));
        }

        if (authoritativeCorrelations.IsDefault)
        {
            throw new ArgumentException(
                "Authoritative correlations must be initialized.",
                nameof(authoritativeCorrelations));
        }

        var observedIds = observedTargets.Select(target => target.Id).ToHashSet();
        var suppressedGsmtcIds = authoritativeCorrelations
            .Where(correlation =>
                observedIds.Contains(correlation.DirectTarget) &&
                observedIds.Contains(correlation.DuplicateGsmtcTarget))
            .Select(correlation => correlation.DuplicateGsmtcTarget)
            .ToHashSet();
        return observedTargets
            .Where(target => !suppressedGsmtcIds.Contains(target.Id))
            .ToImmutableArray();
    }
}

public interface IMediaTargetCatalog : IAsyncDisposable
{
    IAsyncEnumerable<MediaTargetCatalogSnapshot> WatchAsync(
        CancellationToken cancellationToken);
}
