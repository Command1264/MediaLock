using System.Collections.Immutable;

namespace MediaLock.Core.Media;

public enum MediaSessionCatalogStatus
{
    Available,
    Suspended,
    Reacquiring,
    Unavailable,
}

public sealed record MediaTargetPresentation(
    string SourceDisplayName,
    PlaybackStatus PlaybackStatus,
    MediaCommandCapabilities Capabilities,
    DateTimeOffset ObservedAt,
    MediaMetadata? Metadata = null,
    MediaTimeline? Timeline = null,
    MediaPlaybackType PlaybackType = MediaPlaybackType.Unknown,
    MediaArtwork? Artwork = null);

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
                session.Artwork),
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
