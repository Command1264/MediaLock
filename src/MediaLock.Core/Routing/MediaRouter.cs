using System.Collections.Immutable;
using System.Threading.Channels;
using MediaLock.Core.Configuration;
using MediaLock.Core.Media;

namespace MediaLock.Core.Routing;

public sealed class MediaRouter : IMediaRouter
{
    private readonly IMediaTargetController controller;
    private RouterOptions options;
    private readonly Channel<PendingIntent> intents;
    private readonly CancellationTokenSource shutdown = new();
    private readonly Task worker;
    private RouterState state = RouterState.Initial;

    public MediaRouter(IMediaTargetController controller, RouterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(controller);
        this.controller = controller;
        this.options = options ?? RouterOptions.Default;
        ValidateOptions(this.options);
        intents = Channel.CreateUnbounded<PendingIntent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
        worker = ProcessQueueAsync();
    }

    private static void ValidateOptions(RouterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!Enum.IsDefined(options.FallbackPolicy))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.FallbackPolicy,
                "Unknown fallback policy.");
        }

        if (options.RecoveryTimeout < TimeSpan.FromSeconds(RecoverySettings.MinimumTimeoutSeconds) ||
            options.RecoveryTimeout > TimeSpan.FromSeconds(RecoverySettings.MaximumTimeoutSeconds))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.RecoveryTimeout,
                "Recovery timeout must be between 0 seconds and 5 minutes.");
        }

        if (options.PriorityRules.IsDefault)
        {
            throw new ArgumentException("Priority Rules must be an initialized immutable array.", nameof(options));
        }

        var sourceApplications = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in options.PriorityRules)
        {
            if (rule is null || string.IsNullOrWhiteSpace(rule.SourceAppUserModelId))
            {
                throw new ArgumentException(
                    "Priority Rule source application IDs must not be blank.",
                    nameof(options));
            }

            if (!sourceApplications.Add(rule.SourceAppUserModelId))
            {
                throw new ArgumentException(
                    $"Priority Rule source application ID '{rule.SourceAppUserModelId}' is duplicated.",
                    nameof(options));
            }
        }
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
            RouterIntent.MediaTargetsUpdated catalog => UpdateCatalog(catalog),
            RouterIntent.LockSession lockSession => LockSession(lockSession.Session),
            RouterIntent.LockTarget lockTarget => LockTarget(lockTarget.Target),
            RouterIntent.RestoreSessionLock restore => RestoreSessionLock(restore.Fingerprint),
            RouterIntent.LockApplication lockApplication => LockApplication(lockApplication.SourceAppUserModelId),
            RouterIntent.RecoveryTimedOut timeout => ApplyRecoveryTimeout(timeout.RecoveryEpoch),
            RouterIntent.UpdateOptions update => UpdateOptions(update.Options),
            RouterIntent.UsePriorityRules => UsePriorityRules(),
            RouterIntent.UseWindowsAuto => UseWindowsAuto(),
            RouterIntent.Route route => await RouteAsync(
                route.Command,
                route.ExpectedTarget,
                cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(intent)),
        };
    }

    private RouterResult UpdateOptions(RouterOptions updated)
    {
        ValidateOptions(updated);
        var previousOptions = options;
        options = updated;
        if (state.Status == RouterStatus.Recovering &&
            previousOptions.RecoveryTimeout != updated.RecoveryTimeout)
        {
            var previous = state;
            var nextRevision = state.Revision + 1;
            state = state with
            {
                RecoveryEpoch = nextRevision,
                Revision = nextRevision,
            };
            return StateUpdated(previous);
        }

        if (state.Mode == RoutingMode.PriorityRules)
        {
            var nextTarget = SelectPriorityCandidate(state.Sessions, updated.PriorityRules)?.Key;
            if (state.PriorityTarget != nextTarget)
            {
                var previous = state;
                state = state with
                {
                    PriorityTarget = nextTarget,
                    Revision = state.Revision + 1,
                };
                return StateUpdated(previous);
            }
        }

        return new RouterResult(state, RouteDecision.StateUpdated);
    }

    private RouterResult UsePriorityRules()
    {
        var previous = state;
        state = state with
        {
            Mode = RoutingMode.PriorityRules,
            Status = RouterStatus.Ready,
            LockedTarget = null,
            LockedMediaTarget = null,
            PriorityTarget = SelectPriorityCandidate(state.Sessions, options.PriorityRules)?.Key,
            ActiveFallback = null,
            RecoveryEpoch = null,
            Revision = state.Revision + 1,
        };

        return StateUpdated(previous);
    }

    private RouterResult UseWindowsAuto()
    {
        var previous = state;
        state = state with
        {
            Mode = RoutingMode.WindowsAuto,
            Status = RouterStatus.Ready,
            LockedTarget = null,
            LockedMediaTarget = null,
            PriorityTarget = null,
            ActiveFallback = null,
            RecoveryEpoch = null,
            Revision = state.Revision + 1,
        };

        return StateUpdated(previous);
    }

    private RouterResult ApplyRecoveryTimeout(long recoveryEpoch)
    {
        if (state.Status != RouterStatus.Recovering || state.RecoveryEpoch != recoveryEpoch)
        {
            return new RouterResult(state, RouteDecision.StateUpdated);
        }

        var previous = state;
        var (status, target, activeFallback) = state.LockedMediaTarget is not null
            ? (RouterStatus.Unavailable, state.LockedTarget, (FallbackPolicy?)FallbackPolicy.Wait)
            : options.FallbackPolicy switch
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

        return StateUpdated(previous, recoveryEpoch);
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

        var previous = state;
        var candidate = SelectApplicationCandidate(state.Sessions, sourceAppUserModelId);
        var fingerprint = new SessionFingerprint(
            new SessionDescriptor(sourceAppUserModelId, null),
            candidate?.PlaybackStatus ?? PlaybackStatus.Unknown,
            candidate?.ObservedAt ?? DateTimeOffset.MinValue,
            candidate?.PlaybackType ?? MediaPlaybackType.Unknown,
            candidate?.Metadata?.Title,
            candidate?.Metadata?.Artist);
        state = state with
        {
            Mode = RoutingMode.AppLock,
            Status = candidate is null ? RouterStatus.Recovering : RouterStatus.Locked,
            LockedTarget = new LockedTarget(fingerprint, candidate?.Key),
            LockedMediaTarget = null,
            ActiveFallback = null,
            RecoveryEpoch = candidate is null ? state.Revision + 1 : null,
            Revision = state.Revision + 1,
        };

        return StateUpdated(previous);
    }

    private RouterResult LockSession(SessionKey sessionKey)
    {
        var session = state.Sessions.FirstOrDefault(candidate => candidate.Key == sessionKey);
        if (session is null)
        {
            throw new ArgumentException("The Session to lock is not present in the current catalog.", nameof(sessionKey));
        }

        var previous = state;
        state = state with
        {
            Mode = RoutingMode.SessionLock,
            Status = RouterStatus.Locked,
            LockedTarget = new LockedTarget(SessionFingerprint.From(session), session.Key),
            LockedMediaTarget = null,
            ActiveFallback = null,
            RecoveryEpoch = null,
            Revision = state.Revision + 1,
        };

        return StateUpdated(previous);
    }

    private RouterResult LockTarget(MediaTargetId targetId)
    {
        if (!targetId.IsValid)
        {
            throw new ArgumentException("The Media Target to lock must be valid.", nameof(targetId));
        }

        if (targetId.Provider == MediaTargetProviderId.Gsmtc)
        {
            return LockSession(new SessionKey(targetId.Value));
        }

        if (!state.Targets.Any(target => target.Id == targetId))
        {
            throw new ArgumentException(
                "The Media Target to lock is not present in the current catalog.",
                nameof(targetId));
        }

        var previous = state;
        state = state with
        {
            Mode = RoutingMode.SessionLock,
            Status = RouterStatus.Locked,
            LockedTarget = null,
            LockedMediaTarget = targetId,
            ActiveFallback = null,
            RecoveryEpoch = null,
            Revision = state.Revision + 1,
        };

        return StateUpdated(previous);
    }

    private RouterResult RestoreSessionLock(SessionFingerprint fingerprint)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);

        var previous = state;
        state = state with
        {
            Mode = RoutingMode.SessionLock,
            Status = RouterStatus.Recovering,
            LockedTarget = new LockedTarget(fingerprint, null),
            LockedMediaTarget = null,
            ActiveFallback = null,
            RecoveryEpoch = null,
            Revision = state.Revision + 1,
        };
        var (status, lockedTarget, activeFallback) = ResolveLockedTarget(
            state.Targets,
            state.Sessions);
        state = state with
        {
            Status = status,
            LockedTarget = lockedTarget,
            ActiveFallback = activeFallback,
            RecoveryEpoch = status == RouterStatus.Recovering ? state.Revision : null,
        };

        return StateUpdated(previous);
    }

    private RouterResult UpdateCatalog(RouterIntent.MediaTargetsUpdated catalog)
    {
        ValidateCatalog(catalog);

        if (state.WindowsCurrentTarget == catalog.WindowsCurrentTarget &&
            state.Targets.SequenceEqual(catalog.Targets))
        {
            return new RouterResult(state, RouteDecision.StateUpdated);
        }

        var previous = state;
        var targets = catalog.Targets;
        var sessions = targets
            .Where(target => target.GsmtcSession is not null)
            .Select(target => target.GsmtcSession!)
            .ToImmutableArray();
        var currentSession = catalog.WindowsCurrentTarget is
        { Provider: var provider, Value: var value } &&
            provider == MediaTargetProviderId.Gsmtc
                ? new SessionKey(value)
                : (SessionKey?)null;
        var (status, lockedTarget, activeFallback) = ResolveLockedTarget(targets, sessions);
        var nextRevision = state.Revision + 1;
        long? recoveryEpoch = status == RouterStatus.Recovering
            ? state.Status == RouterStatus.Recovering
                ? state.RecoveryEpoch ?? nextRevision
                : nextRevision
            : null;
        state = state with
        {
            Targets = targets,
            WindowsCurrentTarget = catalog.WindowsCurrentTarget,
            Sessions = sessions,
            WindowsCurrentSession = currentSession,
            Status = status,
            LockedTarget = lockedTarget,
            ActiveFallback = activeFallback,
            RecoveryEpoch = recoveryEpoch,
            PriorityTarget = state.Mode == RoutingMode.PriorityRules
                ? SelectPriorityCandidate(sessions, options.PriorityRules)?.Key
                : null,
            Revision = nextRevision,
        };

        return StateUpdated(previous);
    }

    private RouterResult UpdateCatalog(RouterIntent.CatalogUpdated catalog)
    {
        ValidateCatalog(catalog);
        return UpdateCatalog(new RouterIntent.MediaTargetsUpdated(
            catalog.Sessions.Select(MediaTargetSnapshot.FromGsmtc).ToImmutableArray(),
            catalog.WindowsCurrentSession is { } current
                ? MediaTargetId.FromGsmtc(current)
                : null));
    }

    private static void ValidateCatalog(RouterIntent.CatalogUpdated catalog)
    {
        if (catalog.Sessions.IsDefault)
        {
            throw new ArgumentException(
                "Catalog Sessions must be an initialized immutable array.",
                nameof(catalog));
        }

        var keys = new HashSet<SessionKey>();
        foreach (var session in catalog.Sessions)
        {
            if (session is null)
            {
                throw new ArgumentException(
                    "Catalog Sessions must not contain a null Session.",
                    nameof(catalog));
            }

            if (string.IsNullOrWhiteSpace(session.Key.Value))
            {
                throw new ArgumentException(
                    "Every catalog Session key must be non-blank.",
                    nameof(catalog));
            }

            if (string.IsNullOrWhiteSpace(session.SourceAppUserModelId))
            {
                throw new ArgumentException(
                    "Every catalog Session source application ID must be non-blank.",
                    nameof(catalog));
            }

            if (session.SessionInstanceHint is not null &&
                string.IsNullOrWhiteSpace(session.SessionInstanceHint))
            {
                throw new ArgumentException(
                    "Every catalog Session instance hint must be null or non-blank.",
                    nameof(catalog));
            }

            if (!Enum.IsDefined(session.PlaybackStatus))
            {
                throw new ArgumentException(
                    "Every catalog Session playback status must be defined.",
                    nameof(catalog));
            }

            if (!Enum.IsDefined(session.PlaybackType))
            {
                throw new ArgumentException(
                    "Every catalog Session playback type must be defined.",
                    nameof(catalog));
            }

            if ((session.Capabilities & ~MediaCommandCapabilities.All) != 0)
            {
                throw new ArgumentException(
                    "Every catalog Session capability value must contain only known flags.",
                    nameof(catalog));
            }

            if (!keys.Add(session.Key))
            {
                throw new ArgumentException(
                    $"Catalog Session key '{session.Key}' is duplicated.",
                    nameof(catalog));
            }
        }

        if (catalog.WindowsCurrentSession is { } current && !keys.Contains(current))
        {
            throw new ArgumentException(
                "Windows Current Session must identify a Session in the catalog.",
                nameof(catalog));
        }
    }

    private static void ValidateCatalog(RouterIntent.MediaTargetsUpdated catalog)
    {
        if (catalog.Targets.IsDefault)
        {
            throw new ArgumentException(
                "Catalog Media Targets must be an initialized immutable array.",
                nameof(catalog));
        }

        var targetIds = new HashSet<MediaTargetId>();
        foreach (var target in catalog.Targets)
        {
            if (target is null)
            {
                throw new ArgumentException(
                    "Catalog Media Targets must not contain a null target.",
                    nameof(catalog));
            }

            if (!target.Id.IsValid || !targetIds.Add(target.Id))
            {
                throw new ArgumentException(
                    "Every catalog Media Target must have a unique valid identity.",
                    nameof(catalog));
            }

            var presentation = target.Presentation;
            if (string.IsNullOrWhiteSpace(presentation.SourceDisplayName) ||
                !Enum.IsDefined(presentation.PlaybackStatus) ||
                (presentation.Capabilities & ~MediaCommandCapabilities.All) != 0)
            {
                throw new ArgumentException(
                    "Every catalog Media Target must have a valid presentation.",
                    nameof(catalog));
            }

            if (target.GsmtcSession is not { } session)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(session.Key.Value))
            {
                throw new ArgumentException(
                    "Every catalog Session key must be non-blank.",
                    nameof(catalog));
            }

            if (string.IsNullOrWhiteSpace(session.SourceAppUserModelId))
            {
                throw new ArgumentException(
                    "Every catalog Session source application ID must be non-blank.",
                    nameof(catalog));
            }

            if (session.SessionInstanceHint is not null &&
                string.IsNullOrWhiteSpace(session.SessionInstanceHint))
            {
                throw new ArgumentException(
                    "Every catalog Session instance hint must be null or non-blank.",
                    nameof(catalog));
            }

            if (!Enum.IsDefined(session.PlaybackStatus))
            {
                throw new ArgumentException(
                    "Every catalog Session playback status must be defined.",
                    nameof(catalog));
            }

            if (!Enum.IsDefined(session.PlaybackType))
            {
                throw new ArgumentException(
                    "Every catalog Session playback type must be defined.",
                    nameof(catalog));
            }

            if ((session.Capabilities & ~MediaCommandCapabilities.All) != 0)
            {
                throw new ArgumentException(
                    "Every catalog Session capability value must contain only known flags.",
                    nameof(catalog));
            }

            if (target.Id != MediaTargetId.FromGsmtc(session.Key))
            {
                throw new ArgumentException(
                    "A GSMTC Media Target identity must match its Session key.",
                    nameof(catalog));
            }
        }

        if (catalog.WindowsCurrentTarget is { } current &&
            (current.Provider != MediaTargetProviderId.Gsmtc || !targetIds.Contains(current)))
        {
            throw new ArgumentException(
                "Windows Current Target must identify a GSMTC target in the catalog.",
                nameof(catalog));
        }
    }

    private (RouterStatus Status, LockedTarget? Target, FallbackPolicy? ActiveFallback) ResolveLockedTarget(
        ImmutableArray<MediaTargetSnapshot> targets,
        ImmutableArray<MediaSessionSnapshot> sessions)
    {
        if (state.Mode == RoutingMode.SessionLock && state.LockedMediaTarget is { } exactTarget)
        {
            if (targets.Any(target => target.Id == exactTarget))
            {
                return (RouterStatus.Locked, null, null);
            }

            return state.Status == RouterStatus.Unavailable
                ? (RouterStatus.Unavailable, null, FallbackPolicy.Wait)
                : (RouterStatus.Recovering, null, null);
        }

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

        var resolvedSession = lockedTarget.ResolvedSession is { } resolved
            ? sessions.FirstOrDefault(session =>
                session.Key == resolved && lockedTarget.Fingerprint.CanRepresent(session))
            : null;
        if (resolvedSession is not null)
        {
            return (
                RouterStatus.Locked,
                lockedTarget with { Fingerprint = SessionFingerprint.From(resolvedSession) },
                null);
        }

        var rankedCandidates = sessions
            .Select(session => new
            {
                Session = session,
                Score = lockedTarget.Fingerprint.Score(session),
            })
            .Where(candidate => candidate.Score is { IsAcceptable: true })
            .OrderByDescending(candidate => candidate.Score)
            .Take(2)
            .ToArray();

        if (rankedCandidates.Length >= 1 &&
            (rankedCandidates.Length == 1 ||
                rankedCandidates[0].Score!.Value.CompareTo(rankedCandidates[1].Score!.Value) > 0))
        {
            return (
                RouterStatus.Locked,
                new LockedTarget(
                    SessionFingerprint.From(rankedCandidates[0].Session),
                    rankedCandidates[0].Session.Key),
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

        return (RouterStatus.Recovering, lockedTarget with { ResolvedSession = null }, null);
    }

    private RouterResult StateUpdated(
        RouterState previous,
        long? consumedRecoveryEpoch = null)
    {
        var effects = ImmutableArray.CreateBuilder<RouterEffect>();
        if (previous.RecoveryEpoch is { } previousEpoch &&
            previousEpoch != state.RecoveryEpoch &&
            previousEpoch != consumedRecoveryEpoch)
        {
            effects.Add(new RouterEffect.CancelRecoveryTimeout(previousEpoch));
        }

        if (state.RecoveryEpoch is { } nextEpoch && nextEpoch != previous.RecoveryEpoch)
        {
            effects.Add(new RouterEffect.ScheduleRecoveryTimeout(
                nextEpoch,
                options.RecoveryTimeout));
        }

        return new RouterResult(state, RouteDecision.StateUpdated, effects.ToImmutable());
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

    private static MediaSessionSnapshot? SelectPriorityCandidate(
        ImmutableArray<MediaSessionSnapshot> sessions,
        ImmutableArray<PriorityRule> priorityRules)
    {
        foreach (var rule in priorityRules)
        {
            if (!rule.IsEnabled)
            {
                continue;
            }

            var candidate = SelectApplicationCandidate(sessions, rule.SourceAppUserModelId);
            if (candidate is not null)
            {
                return candidate;
            }
        }

        return null;
    }

    private async ValueTask<RouterResult> RouteAsync(
        MediaCommand command,
        MediaTargetId? expectedTarget,
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

        var targetId = state.ActiveTarget;
        if (expectedTarget is not null && targetId != expectedTarget)
        {
            return Skipped(command, RouteReason.InputTargetChanged, targetId);
        }

        if (targetId is null)
        {
            return Skipped(command, RouteReason.NoWindowsCurrentSession);
        }

        var target = state.Targets.FirstOrDefault(candidate => candidate.Id == targetId.Value);
        if (target is null)
        {
            return Skipped(command, RouteReason.NoWindowsCurrentSession);
        }

        if (!target.Presentation.Capabilities.Supports(command))
        {
            return Skipped(command, RouteReason.UnsupportedCommand, target.Id);
        }

        if (command.Kind == MediaCommandKind.SeekAbsolute)
        {
            if (target.Presentation.Timeline is not { } timeline || timeline.End <= timeline.Start)
            {
                return Skipped(command, RouteReason.SeekTimelineUnavailable, target.Id);
            }

            var position = command.AbsolutePosition!.Value;
            if (position < timeline.Start || position > timeline.End)
            {
                return Skipped(command, RouteReason.SeekOutOfRange, target.Id);
            }
        }

        MediaCommandOutcome controlOutcome;
        try
        {
            controlOutcome = await controller.TryExecuteAsync(target.Id, command, cancellationToken);
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
                    target.Id,
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
            RoutingMode.PriorityRules when state.PriorityTarget is not null => RouteReason.PriorityRule,
            RoutingMode.PriorityRules => RouteReason.PriorityRulesWindowsCurrentSession,
            _ => RouteReason.WindowsCurrentSession,
        };
        var (kind, reason) = controlOutcome switch
        {
            MediaCommandOutcome.Succeeded => (RouteDecisionKind.Routed, successfulReason),
            MediaCommandOutcome.Unsupported => (RouteDecisionKind.Skipped, RouteReason.UnsupportedCommand),
            MediaCommandOutcome.Rejected => (RouteDecisionKind.Skipped, RouteReason.ControlRejected),
            MediaCommandOutcome.Failed => (RouteDecisionKind.Failed, RouteReason.ControlFailed),
            MediaCommandOutcome.OutcomeUnknown => (RouteDecisionKind.Failed, RouteReason.ControlOutcomeUnknown),
            _ => throw new ArgumentOutOfRangeException(nameof(controlOutcome)),
        };
        return new RouterResult(
            state,
            new RouteDecision(
                kind,
                reason,
                command,
                target.Id,
                controlOutcome));
    }

    private RouterResult Skipped(
        MediaCommand command,
        RouteReason reason,
        MediaTargetId? target = null) => new(
            state,
            new RouteDecision(RouteDecisionKind.Skipped, reason, command, target));

    private sealed record PendingIntent(
        RouterIntent Intent,
        CancellationToken CancellationToken,
        CancellationTokenRegistration CancellationRegistration,
        TaskCompletionSource<RouterResult> Completion);
}
