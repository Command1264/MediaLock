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
    SameApplicationThenWindowsCurrentSession,
    DisableRouting,
}

public sealed record RouterOptions(
    FallbackPolicy FallbackPolicy,
    TimeSpan RecoveryTimeout)
{
    public RouterOptions(FallbackPolicy fallbackPolicy)
        : this(fallbackPolicy, TimeSpan.FromSeconds(15))
    {
    }

    public static RouterOptions Default { get; } = new(
        FallbackPolicy.SameApplicationThenWindowsCurrentSession,
        TimeSpan.FromSeconds(15));
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
    MediaControlResult? ControlResult = null,
    string? Error = null)
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
    long? RecoveryEpoch,
    long Revision)
{
    public SessionKey? ActiveTarget => Status == RouterStatus.Fallback &&
        ActiveFallback == FallbackPolicy.WindowsCurrentSession
            ? WindowsCurrentSession
            : Mode is RoutingMode.SessionLock or RoutingMode.AppLock
                ? LockedTarget?.ResolvedSession
                : WindowsCurrentSession;

    public static RouterState Initial { get; } = new(
        RoutingMode.WindowsAuto,
        RouterStatus.Ready,
        [],
        null,
        null,
        null,
        null,
        0);
}

public sealed record LockedTarget(
    SessionFingerprint Fingerprint,
    SessionKey? ResolvedSession);

public abstract record RouterEffect
{
    private RouterEffect()
    {
    }

    public sealed record ScheduleRecoveryTimeout(
        long RecoveryEpoch,
        TimeSpan Delay) : RouterEffect;

    public sealed record CancelRecoveryTimeout(long RecoveryEpoch) : RouterEffect;
}

public sealed record RouterResult(
    RouterState State,
    RouteDecision Decision,
    ImmutableArray<RouterEffect> Effects)
{
    public RouterResult(RouterState state, RouteDecision decision)
        : this(state, decision, [])
    {
    }
}

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
        ImmutableArray<MediaSessionSnapshot> Sessions,
        SessionKey? WindowsCurrentSession) : RouterIntent;

    public sealed record Route(MediaCommand Command) : RouterIntent;

    public sealed record LockSession(SessionKey Session) : RouterIntent;

    public sealed record RestoreSessionLock(SessionFingerprint Fingerprint) : RouterIntent;

    public sealed record LockApplication(string SourceAppUserModelId) : RouterIntent;

    public sealed record RecoveryTimedOut(long RecoveryEpoch) : RouterIntent;

    public sealed record UpdateOptions(RouterOptions Options) : RouterIntent;

    public sealed record UseWindowsAuto : RouterIntent;
}
