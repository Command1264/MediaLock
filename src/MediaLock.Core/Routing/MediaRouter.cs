using System.Collections.Immutable;
using System.Threading.Channels;
using MediaLock.Core.Media;

namespace MediaLock.Core.Routing;

public sealed class MediaRouter : IMediaRouter
{
    private readonly IMediaController controller;
    private readonly RouterOptions options;
    private readonly Channel<PendingIntent> intents;
    private readonly CancellationTokenSource shutdown = new();
    private readonly Task worker;
    private RouterState state = RouterState.Initial;

    public MediaRouter(IMediaController controller, RouterOptions? options = null)
    {
        this.controller = controller;
        this.options = options ?? RouterOptions.Default;
        if (!Enum.IsDefined(this.options.FallbackPolicy))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                this.options.FallbackPolicy,
                "Unknown fallback policy.");
        }

        intents = Channel.CreateUnbounded<PendingIntent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
        worker = ProcessQueueAsync();
    }

    public ValueTask<RouterResult> DispatchAsync(
        RouterIntent intent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(intent);
        cancellationToken.ThrowIfCancellationRequested();

        var completion = new TaskCompletionSource<RouterResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationRegistration = cancellationToken.Register(
            () => completion.TrySetCanceled(cancellationToken));
        var pending = new PendingIntent(
            intent,
            cancellationToken,
            cancellationRegistration,
            completion);
        if (!intents.Writer.TryWrite(pending))
        {
            cancellationRegistration.Dispose();
            throw new ObjectDisposedException(nameof(MediaRouter));
        }

        return new ValueTask<RouterResult>(completion.Task);
    }

    public async ValueTask DisposeAsync()
    {
        if (intents.Writer.TryComplete())
        {
            await shutdown.CancelAsync();
        }

        await worker;
        shutdown.Dispose();
    }

    private async Task ProcessQueueAsync()
    {
        await foreach (var pending in intents.Reader.ReadAllAsync())
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                pending.CancellationToken,
                shutdown.Token);

            try
            {
                var result = await ProcessAsync(pending.Intent, cancellation.Token);
                pending.Completion.TrySetResult(result);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                pending.Completion.TrySetCanceled(cancellation.Token);
            }
            catch (Exception exception)
            {
                pending.Completion.TrySetException(exception);
            }
            finally
            {
                pending.CancellationRegistration.Dispose();
            }
        }
    }

    private async ValueTask<RouterResult> ProcessAsync(
        RouterIntent intent,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return intent switch
        {
            RouterIntent.CatalogUpdated catalog => UpdateCatalog(catalog),
            RouterIntent.LockSession lockSession => LockSession(lockSession.Session),
            RouterIntent.LockApplication lockApplication => LockApplication(lockApplication.SourceAppUserModelId),
            RouterIntent.RecoveryTimedOut timeout => ApplyRecoveryTimeout(timeout.RecoveryEpoch),
            RouterIntent.UseWindowsAuto => UseWindowsAuto(),
            RouterIntent.Route route => await RouteAsync(route.Command, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(intent)),
        };
    }

    private RouterResult UseWindowsAuto()
    {
        state = state with
        {
            Mode = RoutingMode.WindowsAuto,
            Status = RouterStatus.Ready,
            LockedTarget = null,
            ActiveFallback = null,
            RecoveryEpoch = null,
            Revision = state.Revision + 1,
        };

        return new RouterResult(state, RouteDecision.StateUpdated);
    }

    private RouterResult ApplyRecoveryTimeout(long recoveryEpoch)
    {
        if (state.Status != RouterStatus.Recovering || state.RecoveryEpoch != recoveryEpoch)
        {
            return new RouterResult(state, RouteDecision.StateUpdated);
        }

        var (status, target, activeFallback) = options.FallbackPolicy switch
        {
            FallbackPolicy.SameApplication => ResolveSameApplicationFallback(),
            FallbackPolicy.WindowsCurrentSession => (
                RouterStatus.Fallback,
                state.LockedTarget,
                FallbackPolicy.WindowsCurrentSession),
            FallbackPolicy.SameApplicationThenWindowsCurrentSession =>
                ResolveDefaultFallback(),
            FallbackPolicy.Wait => (
                RouterStatus.Unavailable,
                state.LockedTarget,
                FallbackPolicy.Wait),
            FallbackPolicy.DisableRouting => (
                RouterStatus.Unavailable,
                state.LockedTarget,
                FallbackPolicy.DisableRouting),
            _ => throw new ArgumentOutOfRangeException(nameof(options.FallbackPolicy)),
        };

        state = state with
        {
            Status = status,
            LockedTarget = target,
            ActiveFallback = activeFallback,
            RecoveryEpoch = null,
            Revision = state.Revision + 1,
        };

        return new RouterResult(state, RouteDecision.StateUpdated);
    }

    private (RouterStatus Status, LockedTarget? Target, FallbackPolicy ActiveFallback)
        ResolveDefaultFallback()
    {
        var sameApplication = ResolveSameApplicationFallback();
        return sameApplication.Status == RouterStatus.Fallback
            ? sameApplication
            : (
                RouterStatus.Fallback,
                state.LockedTarget,
                FallbackPolicy.WindowsCurrentSession);
    }

    private (RouterStatus Status, LockedTarget? Target, FallbackPolicy ActiveFallback)
        ResolveSameApplicationFallback()
    {
        if (state.LockedTarget is null)
        {
            return (RouterStatus.Unavailable, null, FallbackPolicy.SameApplication);
        }

        var candidate = SelectApplicationCandidate(
            state.Sessions,
            state.LockedTarget.Fingerprint.Descriptor.SourceAppUserModelId);
        return candidate is null
            ? (RouterStatus.Unavailable, state.LockedTarget, FallbackPolicy.SameApplication)
            : (
                RouterStatus.Fallback,
                state.LockedTarget with { ResolvedSession = candidate.Key },
                FallbackPolicy.SameApplication);
    }

    private RouterResult LockApplication(string sourceAppUserModelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceAppUserModelId);

        var candidate = SelectApplicationCandidate(state.Sessions, sourceAppUserModelId);
        var fingerprint = new SessionFingerprint(
            new SessionDescriptor(sourceAppUserModelId, null),
            candidate?.PlaybackStatus ?? PlaybackStatus.Unknown,
            candidate?.ObservedAt ?? DateTimeOffset.MinValue,
            candidate?.Metadata?.Title,
            candidate?.Metadata?.Artist);
        state = state with
        {
            Mode = RoutingMode.AppLock,
            Status = candidate is null ? RouterStatus.Recovering : RouterStatus.Locked,
            LockedTarget = new LockedTarget(fingerprint, candidate?.Key),
            ActiveFallback = null,
            RecoveryEpoch = candidate is null ? state.Revision + 1 : null,
            Revision = state.Revision + 1,
        };

        return new RouterResult(state, RouteDecision.StateUpdated);
    }

    private RouterResult LockSession(SessionKey sessionKey)
    {
        var session = state.Sessions.FirstOrDefault(candidate => candidate.Key == sessionKey);
        if (session is null)
        {
            throw new ArgumentException("The Session to lock is not present in the current catalog.", nameof(sessionKey));
        }

        state = state with
        {
            Mode = RoutingMode.SessionLock,
            Status = RouterStatus.Locked,
            LockedTarget = new LockedTarget(SessionFingerprint.From(session), session.Key),
            ActiveFallback = null,
            RecoveryEpoch = null,
            Revision = state.Revision + 1,
        };

        return new RouterResult(state, RouteDecision.StateUpdated);
    }

    private RouterResult UpdateCatalog(RouterIntent.CatalogUpdated catalog)
    {
        if (catalog.Sessions.IsDefault)
        {
            throw new ArgumentException("Catalog Sessions must be an initialized immutable array.", nameof(catalog));
        }

        if (state.WindowsCurrentSession == catalog.WindowsCurrentSession &&
            state.Sessions.SequenceEqual(catalog.Sessions))
        {
            return new RouterResult(state, RouteDecision.StateUpdated);
        }

        var sessions = catalog.Sessions;
        var (status, lockedTarget, activeFallback) = ResolveLockedTarget(sessions);
        var nextRevision = state.Revision + 1;
        long? recoveryEpoch = status == RouterStatus.Recovering
            ? state.Status == RouterStatus.Recovering
                ? state.RecoveryEpoch ?? nextRevision
                : nextRevision
            : null;
        state = state with
        {
            Sessions = sessions,
            WindowsCurrentSession = catalog.WindowsCurrentSession,
            Status = status,
            LockedTarget = lockedTarget,
            ActiveFallback = activeFallback,
            RecoveryEpoch = recoveryEpoch,
            Revision = nextRevision,
        };

        return new RouterResult(state, RouteDecision.StateUpdated);
    }

    private (RouterStatus Status, LockedTarget? Target, FallbackPolicy? ActiveFallback) ResolveLockedTarget(
        ImmutableArray<MediaSessionSnapshot> sessions)
    {
        if (state.LockedTarget is null)
        {
            return (state.Status, state.LockedTarget, state.ActiveFallback);
        }

        var lockedTarget = state.LockedTarget;
        if (state.Mode == RoutingMode.AppLock)
        {
            var appCandidate = SelectApplicationCandidate(
                sessions,
                lockedTarget.Fingerprint.Descriptor.SourceAppUserModelId);
            if (appCandidate is not null)
            {
                return (RouterStatus.Locked, lockedTarget with { ResolvedSession = appCandidate.Key }, null);
            }

            return state.Status is RouterStatus.Fallback or RouterStatus.Unavailable &&
                state.ActiveFallback is not null
                    ? (state.Status, lockedTarget with { ResolvedSession = null }, state.ActiveFallback)
                    : (RouterStatus.Recovering, lockedTarget with { ResolvedSession = null }, null);
        }

        if (state.Mode != RoutingMode.SessionLock)
        {
            return (state.Status, state.LockedTarget, state.ActiveFallback);
        }

        var rankedCandidates = sessions
            .Select(session => new
            {
                Session = session,
                Score = lockedTarget.Fingerprint.Score(session),
            })
            .Where(candidate => candidate.Score is not null)
            .OrderByDescending(candidate => candidate.Score)
            .Take(2)
            .ToArray();

        if (rankedCandidates.Length >= 1 &&
            (rankedCandidates.Length == 1 || rankedCandidates[0].Score > rankedCandidates[1].Score))
        {
            return (
                RouterStatus.Locked,
                lockedTarget with { ResolvedSession = rankedCandidates[0].Session.Key },
                null);
        }

        if (state.Status == RouterStatus.Fallback &&
            state.ActiveFallback == FallbackPolicy.WindowsCurrentSession)
        {
            return (RouterStatus.Fallback, lockedTarget with { ResolvedSession = null }, state.ActiveFallback);
        }

        if (state.Status == RouterStatus.Fallback &&
            state.ActiveFallback == FallbackPolicy.SameApplication)
        {
            var fallbackCandidate = SelectApplicationCandidate(
                sessions,
                lockedTarget.Fingerprint.Descriptor.SourceAppUserModelId);
            return fallbackCandidate is null
                ? (RouterStatus.Unavailable, lockedTarget with { ResolvedSession = null }, state.ActiveFallback)
                : (
                    RouterStatus.Fallback,
                    lockedTarget with { ResolvedSession = fallbackCandidate.Key },
                    state.ActiveFallback);
        }

        if (lockedTarget.ResolvedSession is { } resolved &&
            sessions.Any(session =>
                session.Key == resolved && lockedTarget.Fingerprint.CanRepresent(session)))
        {
            return (RouterStatus.Locked, lockedTarget, null);
        }

        return (RouterStatus.Recovering, lockedTarget with { ResolvedSession = null }, null);
    }

    private static MediaSessionSnapshot? SelectApplicationCandidate(
        IEnumerable<MediaSessionSnapshot> sessions,
        string sourceAppUserModelId) => sessions
        .Where(session => string.Equals(
            session.SourceAppUserModelId,
            sourceAppUserModelId,
            StringComparison.Ordinal))
        .OrderByDescending(session => session.PlaybackStatus == PlaybackStatus.Playing)
        .ThenByDescending(session => session.ObservedAt)
        .ThenBy(session => session.Key.Value, StringComparer.Ordinal)
        .FirstOrDefault();

    private async ValueTask<RouterResult> RouteAsync(
        MediaCommand command,
        CancellationToken cancellationToken)
    {
        if (state.Mode is RoutingMode.SessionLock or RoutingMode.AppLock)
        {
            if (state.Status == RouterStatus.Recovering)
            {
                return Skipped(command, RouteReason.LockedTargetRecovering);
            }

            if (state.Status == RouterStatus.Unavailable)
            {
                var unavailableReason = options.FallbackPolicy == FallbackPolicy.DisableRouting
                    ? RouteReason.RoutingDisabled
                    : RouteReason.LockedTargetUnavailable;
                return Skipped(command, unavailableReason);
            }
        }

        var targetKey = state.Status == RouterStatus.Fallback &&
            state.ActiveFallback == FallbackPolicy.WindowsCurrentSession
                ? state.WindowsCurrentSession
                : state.Mode is RoutingMode.SessionLock or RoutingMode.AppLock
                    ? state.LockedTarget?.ResolvedSession
                    : state.WindowsCurrentSession;
        if (targetKey is null)
        {
            return Skipped(command, RouteReason.NoWindowsCurrentSession);
        }

        var target = state.Sessions.FirstOrDefault(session => session.Key == targetKey.Value);
        if (target is null)
        {
            return Skipped(command, RouteReason.NoWindowsCurrentSession);
        }

        if (!target.Capabilities.Supports(command))
        {
            return Skipped(command, RouteReason.UnsupportedCommand, target.Key);
        }

        MediaControlResult controlResult;
        try
        {
            controlResult = await controller.TryExecuteAsync(target.Key, command, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new RouterResult(
                state,
                new RouteDecision(
                    RouteDecisionKind.Failed,
                    RouteReason.ControlFailed,
                    command,
                    target.Key,
                    Error: exception.Message));
        }

        var successfulReason = state.Mode switch
        {
            _ when state.ActiveFallback == FallbackPolicy.WindowsCurrentSession =>
                RouteReason.FallbackWindowsCurrentSession,
            _ when state.ActiveFallback == FallbackPolicy.SameApplication =>
                RouteReason.FallbackSameApplication,
            RoutingMode.SessionLock => RouteReason.LockedSession,
            RoutingMode.AppLock => RouteReason.LockedApplication,
            _ => RouteReason.WindowsCurrentSession,
        };
        var (kind, reason) = controlResult switch
        {
            MediaControlResult.Succeeded => (RouteDecisionKind.Routed, successfulReason),
            MediaControlResult.Rejected => (RouteDecisionKind.Skipped, RouteReason.ControlRejected),
            MediaControlResult.Failed => (RouteDecisionKind.Failed, RouteReason.ControlFailed),
            _ => throw new ArgumentOutOfRangeException(nameof(controlResult)),
        };

        return new RouterResult(
            state,
            new RouteDecision(kind, reason, command, target.Key, controlResult));
    }

    private RouterResult Skipped(
        MediaCommand command,
        RouteReason reason,
        SessionKey? target = null) => new(
            state,
            new RouteDecision(RouteDecisionKind.Skipped, reason, command, target));

    private sealed record PendingIntent(
        RouterIntent Intent,
        CancellationToken CancellationToken,
        CancellationTokenRegistration CancellationRegistration,
        TaskCompletionSource<RouterResult> Completion);
}
