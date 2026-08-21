using System.Collections.Immutable;
using MediaLock.Core.Media;

namespace MediaLock.Core.Routing;

public enum RoutingMode
{
    WindowsAuto,
    AppLock,
    SessionLock,
}

public enum RouteDecisionKind
{
    None,
    Routed,
    Skipped,
    Failed,
}

public enum RouterStatus
{
    Ready,
    Locked,
    Recovering,
    Fallback,
    Unavailable,
}

public enum FallbackPolicy
{
    Wait,
    SameApplication,
    WindowsCurrentSession,
    DisableRouting,
}

public sealed record RouterOptions(FallbackPolicy FallbackPolicy)
{
    public static RouterOptions Default { get; } = new(FallbackPolicy.Wait);
}

public enum RouteReason
{
    StateUpdated,
    WindowsCurrentSession,
    LockedSession,
    LockedApplication,
    FallbackWindowsCurrentSession,
    FallbackSameApplication,
    LockedTargetRecovering,
    LockedTargetUnavailable,
    RoutingDisabled,
    NoWindowsCurrentSession,
    UnsupportedCommand,
    ControlRejected,
    ControlFailed,
}

public sealed record RouteDecision(
    RouteDecisionKind Kind,
    RouteReason Reason,
    MediaCommand? Command = null,
    SessionKey? Target = null,
    MediaControlResult? ControlResult = null)
{
    public static RouteDecision StateUpdated { get; } = new(
        RouteDecisionKind.None,
        RouteReason.StateUpdated);
}

public sealed record RouterState(
    RoutingMode Mode,
    RouterStatus Status,
    ImmutableArray<MediaSessionSnapshot> Sessions,
    SessionKey? WindowsCurrentSession,
    LockedTarget? LockedTarget,
    FallbackPolicy? ActiveFallback,
    long Revision)
{
    public static RouterState Initial { get; } = new(
        RoutingMode.WindowsAuto,
        RouterStatus.Ready,
        [],
        null,
        null,
        null,
        0);
}

public sealed record LockedTarget(
    SessionFingerprint Fingerprint,
    SessionKey? ResolvedSession);

public sealed record RouterResult(RouterState State, RouteDecision Decision);

public interface IMediaRouter : IAsyncDisposable
{
    ValueTask<RouterResult> DispatchAsync(
        RouterIntent intent,
        CancellationToken cancellationToken);
}

public abstract record RouterIntent
{
    private RouterIntent()
    {
    }

    public sealed record CatalogUpdated(
        IReadOnlyList<MediaSessionSnapshot> Sessions,
        SessionKey? WindowsCurrentSession) : RouterIntent;

    public sealed record Route(MediaCommand Command) : RouterIntent;

    public sealed record LockSession(SessionKey Session) : RouterIntent;

    public sealed record LockApplication(string SourceAppUserModelId) : RouterIntent;

    public sealed record RecoveryTimedOut(long RecoveryRevision) : RouterIntent;

    public sealed record UseWindowsAuto : RouterIntent;
}
