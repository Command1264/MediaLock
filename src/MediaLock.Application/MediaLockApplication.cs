using System.Collections.Immutable;
using MediaLock.Core.Configuration;
using MediaLock.Core.Diagnostics;
using MediaLock.Core.Lifecycle;
using MediaLock.Core.Media;
using MediaLock.Core.Playback;
using MediaLock.Core.Routing;

namespace MediaLock.Application;

public sealed class MediaLockApplication : IMediaLockApplication
{
    private const int MaximumPlaybackStateCorrectionAttempts = 2;
    private readonly IMediaTargetCatalog catalog;
    private readonly IMediaRouter router;
    private readonly ISettingsRepository? settingsRepository;
    private readonly ILoginStartupManager? loginStartupManager;
    private readonly IRuntimeStateRepository? runtimeStateRepository;
    private readonly IDiagnosticLog? diagnosticLog;
    private readonly IWorkstationLockState? workstationLockState;
    private readonly IMediaTargetAuthorizationController? mediaTargetAuthorizationController;
    private readonly TimeProvider timeProvider;
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim dispatchGate = new(1, 1);
    private readonly Lock recoverySync = new();
    private readonly Dictionary<long, RecoveryDeadline> recoveryDeadlines = [];
    private Task? catalogWorker;
    private Task? loginStartupWorker;
    private MediaLockApplicationState state = MediaLockApplicationState.Initial;
    private bool disposed;
    private MediaLockSettings settings = MediaLockSettings.Default;
    private string? settingsLoadWarning;
    private string? runtimeStateLoadWarning;
    private RuntimeStateDocument? persistedRuntimeState;
    private bool runtimeStatePersistenceSuppressed;
    private RouterIntent? startupRestoreIntent;
    private MediaSessionCatalogStatus catalogStatus = MediaSessionCatalogStatus.Available;
    private string? catalogStatusMessage;
    private ImmutableArray<MediaTargetSnapshot> visibleTargets = [];
    private SessionFingerprint? playbackStateLockRecoveryFingerprint;
    private int playbackStateCorrectionAttempts;
    private int workstationLocked;
    private int workstationObservationPending;
    private int repeatedPauseResetPending;
    private readonly Queue<DateTimeOffset> repeatedPauseObservations = [];
    private bool pausedEpisodeObserved;
    private PlaybackStatus? lastArmedPlaybackStatus;

    public MediaLockApplication(
        IMediaTargetCatalog catalog,
        IMediaRouter router,
        IMediaTargetAuthorizationController? mediaTargetAuthorizationController = null)
        : this(
            catalog,
            router,
            settingsRepository: null,
            loginStartupManager: null,
            runtimeStateRepository: null,
            diagnosticLog: null,
            workstationLockState: null,
            timeProvider: null,
            mediaTargetAuthorizationController: mediaTargetAuthorizationController)
    {
    }

    public MediaLockApplication(
        IMediaTargetCatalog catalog,
        IMediaRouter router,
        ISettingsRepository? settingsRepository,
        ILoginStartupManager? loginStartupManager,
        IRuntimeStateRepository? runtimeStateRepository = null,
        IDiagnosticLog? diagnosticLog = null,
        IWorkstationLockState? workstationLockState = null,
        TimeProvider? timeProvider = null,
        IMediaTargetAuthorizationController? mediaTargetAuthorizationController = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(router);
        this.catalog = catalog;
        this.router = router;
        this.settingsRepository = settingsRepository;
        this.loginStartupManager = loginStartupManager;
        this.runtimeStateRepository = runtimeStateRepository;
        this.diagnosticLog = diagnosticLog;
        this.workstationLockState = workstationLockState;
        this.mediaTargetAuthorizationController = mediaTargetAuthorizationController;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        if (workstationLockState is not null)
        {
            workstationLocked = workstationLockState.IsLocked ? 1 : 0;
            workstationObservationPending = workstationLocked;
            workstationLockState.Locked += OnWorkstationLocked;
            workstationLockState.Unlocked += OnWorkstationUnlocked;
        }
    }

    public event EventHandler<MediaLockApplicationStateChangedEventArgs>? StateChanged;

    public MediaLockApplicationState State
    {
        get => Volatile.Read(ref state);
        private set => Volatile.Write(ref state, value);
    }

    public async ValueTask StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (catalogWorker is not null)
        {
            throw new InvalidOperationException("Media Lock application has already started.");
        }

        if (settingsRepository is not null)
        {
            var loaded = await settingsRepository.LoadAsync(cancellationToken);
            settings = loaded.Value;
            settingsLoadWarning = loaded.Issues.Length == 0
                ? null
                : string.Join(" ", loaded.Issues.Select(issue => issue.Message));
            State = new MediaLockApplicationState(
                State.Router,
                PersistenceWarnings,
                settings,
                catalogStatus,
                catalogStatusMessage)
            {
                PlaybackStateLock = State.PlaybackStateLock,
                Targets = visibleTargets,
            };
            if (loginStartupManager is not null &&
                await loginStartupManager.IsEnabledAsync(cancellationToken) !=
                settings.Desktop!.StartWithWindows)
            {
                await loginStartupManager.SetEnabledAsync(
                    settings.Desktop.StartWithWindows,
                    cancellationToken);
            }

            if (loginStartupManager is ILoginStartupChangeSource changeSource)
            {
                loginStartupWorker = WatchLoginStartupAsync(
                    changeSource,
                    lifetime.Token);
            }
        }

        if (runtimeStateRepository is not null)
        {
            var loadedRuntimeState = await runtimeStateRepository.LoadAsync(cancellationToken);
            if (loadedRuntimeState.Issues.Length == 0)
            {
                persistedRuntimeState = loadedRuntimeState.Value;
            }

            if (loadedRuntimeState.Issues.Length > 0)
            {
                runtimeStateLoadWarning = string.Join(
                    " ",
                    loadedRuntimeState.Issues.Select(issue => issue.Message));
                State = State with
                {
                    ErrorMessage = PersistenceWarnings,
                };
            }
            else if (settings.DefaultRoutingMode is RoutingMode.SessionLock or RoutingMode.AppLock)
            {
                if (loadedRuntimeState.Value is
                    {
                        Mode: var persistedMode,
                        LockedTarget.Fingerprint: { } persistedFingerprint,
                    } && persistedMode == settings.DefaultRoutingMode)
                {
                    startupRestoreIntent = persistedMode == RoutingMode.SessionLock
                        ? new RouterIntent.RestoreSessionLock(ToSessionFingerprint(persistedFingerprint))
                        : new RouterIntent.LockApplication(persistedFingerprint.SourceAppUserModelId);
                }
                else
                {
                    runtimeStateLoadWarning = settings.DefaultRoutingMode == RoutingMode.SessionLock
                        ? "Default Session Lock requires a valid persisted Session Lock target; Windows Auto is active."
                        : "Default App Lock requires a valid persisted App Lock target; Windows Auto is active.";
                }
            }
        }
        else if (settings.DefaultRoutingMode is RoutingMode.SessionLock or RoutingMode.AppLock)
        {
            runtimeStateLoadWarning = settings.DefaultRoutingMode == RoutingMode.SessionLock
                ? "Default Session Lock requires runtime-state persistence; Windows Auto is active."
                : "Default App Lock requires runtime-state persistence; Windows Auto is active.";
        }

        if (settings.DefaultRoutingMode == RoutingMode.PriorityRules)
        {
            startupRestoreIntent = new RouterIntent.UsePriorityRules();
        }

        if (settingsRepository is not null)
        {
            await DispatchRouterAsync(
                new RouterIntent.UpdateOptions(ToRouterOptions(settings)),
                cancellationToken,
                persistRuntimeState: startupRestoreIntent is null);
        }

        var initialized = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        catalogWorker = WatchCatalogAsync(initialized, lifetime.Token);
        await initialized.Task.WaitAsync(cancellationToken);
    }

    public async ValueTask<ApplicationResult> DispatchAsync(
        ApplicationIntent intent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ObjectDisposedException.ThrowIf(disposed, this);
        if (catalogWorker is null)
        {
            throw new InvalidOperationException("Media Lock application must be started before dispatching intents.");
        }

        if (intent is ApplicationIntent.UpdateSettings updateSettings)
        {
            return await UpdateSettingsAsync(updateSettings.Settings, cancellationToken);
        }

        if (intent is ApplicationIntent.SetPlaybackStateLock setPlaybackStateLock)
        {
            return await SetPlaybackStateLockAsync(
                setPlaybackStateLock.Mode,
                cancellationToken);
        }

        if (intent is ApplicationIntent.RevokeTargetAuthorization revokeTargetAuthorization)
        {
            return await RevokeTargetAuthorizationAsync(
                revokeTargetAuthorization.Target,
                cancellationToken);
        }

        (RouterIntent routerIntent, RoutingMode? startupRoutingMode, bool persistRuntimeState) =
            intent switch
            {
                ApplicationIntent.LockSession lockSession =>
                    ((RouterIntent)new RouterIntent.LockSession(lockSession.Session), (RoutingMode?)RoutingMode.SessionLock, true),
                ApplicationIntent.LockTarget lockTarget =>
                    ((RouterIntent)new RouterIntent.LockTarget(lockTarget.Target), (RoutingMode?)null, false),
                ApplicationIntent.LockApplication lockApplication =>
                    ((RouterIntent)new RouterIntent.LockApplication(lockApplication.SourceAppUserModelId), (RoutingMode?)RoutingMode.AppLock, true),
                ApplicationIntent.UsePriorityRules =>
                    ((RouterIntent)new RouterIntent.UsePriorityRules(), (RoutingMode?)RoutingMode.PriorityRules, true),
                ApplicationIntent.UseWindowsAuto =>
                    ((RouterIntent)new RouterIntent.UseWindowsAuto(), (RoutingMode?)RoutingMode.WindowsAuto, true),
                ApplicationIntent.UseWindowsAutoForCurrentRun =>
                    ((RouterIntent)new RouterIntent.UseWindowsAuto(), (RoutingMode?)null, false),
                ApplicationIntent.Route route =>
                    ((RouterIntent)new RouterIntent.Route(route.Command, route.ExpectedTarget), (RoutingMode?)null, true),
                _ => throw new ArgumentOutOfRangeException(nameof(intent)),
            };
        var dispatch = await DispatchRouterAsync(
            routerIntent,
            cancellationToken,
            persistRuntimeState: persistRuntimeState,
            startupRoutingMode: startupRoutingMode,
            suppressRuntimeStatePersistence: intent is ApplicationIntent.UseWindowsAutoForCurrentRun,
            resumeRuntimeStatePersistence: startupRoutingMode is not null,
            clearPlaybackStateLock: intent is ApplicationIntent.Route
            {
                Command.Kind: MediaCommandKind.Pause or
                    MediaCommandKind.TogglePlayPause or
                    MediaCommandKind.Stop,
            });
        return new ApplicationResult(State, dispatch.Result.Decision);
    }

    private async ValueTask<ApplicationResult> SetPlaybackStateLockAsync(
        PlaybackStateLockMode mode,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown Playback State Lock mode.");
        }

        using var updateCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            lifetime.Token);
        await dispatchGate.WaitAsync(updateCancellation.Token);
        try
        {
            if (mode == PlaybackStateLockMode.Off)
            {
                ClearPlaybackStateLock();
                return new ApplicationResult(State, RouteDecision.StateUpdated);
            }

            var activeTarget = State.Router.ActiveTarget;
            var activeSession = activeTarget is { } target
                ? State.Router.Sessions.FirstOrDefault(session => session.Key == target)
                : null;
            if (activeSession is null ||
                !PlaybackStateLockRules.CanArm(activeSession.PlaybackStatus))
            {
                throw new InvalidOperationException(
                    "Keep Playing can only be enabled while the current target is playing.");
            }

            playbackStateLockRecoveryFingerprint =
                State.Router.LockedTarget is { } lockedTarget &&
                lockedTarget.ResolvedSession == activeSession.Key
                    ? lockedTarget.Fingerprint
                    : SessionFingerprint.From(activeSession);
            playbackStateCorrectionAttempts = 0;
            ResetRepeatedPauseObservations();
            lastArmedPlaybackStatus = activeSession.PlaybackStatus;
            Volatile.Write(
                ref workstationObservationPending,
                Volatile.Read(ref workstationLocked));
            PublishPlaybackStateLock(new PlaybackStateLockState(
                PlaybackStateLockMode.KeepPlaying,
                PlaybackStateLockStatus.Ready,
                activeSession.Key));
            return new ApplicationResult(State, RouteDecision.StateUpdated);
        }
        finally
        {
            dispatchGate.Release();
        }
    }

    private async ValueTask<ApplicationResult> RevokeTargetAuthorizationAsync(
        MediaTargetId target,
        CancellationToken cancellationToken)
    {
        if (!target.IsValid || target.Provider == MediaTargetProviderId.Gsmtc)
        {
            throw new ArgumentException(
                "Only a valid direct Media Target authorization can be revoked.",
                nameof(target));
        }
        if (mediaTargetAuthorizationController is null)
        {
            throw new InvalidOperationException("Media Target authorization control is unavailable.");
        }

        using var revokeCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            lifetime.Token);
        await dispatchGate.WaitAsync(revokeCancellation.Token);
        try
        {
            if (!await mediaTargetAuthorizationController.RevokeAsync(
                    target,
                    revokeCancellation.Token))
            {
                throw new InvalidOperationException(
                    "The Media Target authorization could not be revoked.");
            }

            return new ApplicationResult(State, RouteDecision.StateUpdated);
        }
        finally
        {
            dispatchGate.Release();
        }
    }

    private void PublishPlaybackStateLock(PlaybackStateLockState playbackStateLock)
    {
        State = State with { PlaybackStateLock = playbackStateLock };
        StateChanged?.Invoke(this, new MediaLockApplicationStateChangedEventArgs(State));
    }

    private void ClearPlaybackStateLock()
    {
        PublishPlaybackStateLock(ResetPlaybackStateLock());
    }

    private PlaybackStateLockState ResetPlaybackStateLock()
    {
        playbackStateLockRecoveryFingerprint = null;
        playbackStateCorrectionAttempts = 0;
        ResetRepeatedPauseObservations();
        return PlaybackStateLockState.Off;
    }

    private void ReleasePlaybackStateLock()
    {
        playbackStateLockRecoveryFingerprint = null;
        playbackStateCorrectionAttempts = 0;
        ResetRepeatedPauseObservations();
        PublishPlaybackStateLock(new PlaybackStateLockState(
            PlaybackStateLockMode.Off,
            PlaybackStateLockStatus.Released,
            ArmedTarget: null));
    }

    private void ResetRepeatedPauseObservations()
    {
        repeatedPauseObservations.Clear();
        pausedEpisodeObserved = false;
        lastArmedPlaybackStatus = null;
    }

    private void OnWorkstationLocked()
    {
        Volatile.Write(ref repeatedPauseResetPending, 1);
        Volatile.Write(ref workstationLocked, 1);
        Volatile.Write(ref workstationObservationPending, 1);
    }

    private void OnWorkstationUnlocked() =>
        Volatile.Write(ref workstationLocked, 0);

    private async ValueTask<ApplicationResult> UpdateSettingsAsync(
        MediaLockSettings updated,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(updated);
        var issues = updated.Validate();
        if (issues.Length > 0)
        {
            throw new ArgumentException(
                string.Join(" ", issues.Select(issue => $"{issue.Path}: {issue.Message}")),
                nameof(updated));
        }

        using var updateCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            lifetime.Token);
        await dispatchGate.WaitAsync(updateCancellation.Token);
        try
        {
            var previousSettings = settings;
            var startupChanged =
                loginStartupManager is not null &&
                previousSettings.Desktop!.StartWithWindows != updated.Desktop!.StartWithWindows;
            if (settingsRepository is not null)
            {
                await settingsRepository.SaveAsync(updated, updateCancellation.Token);
            }

            if (startupChanged)
            {
                try
                {
                    await loginStartupManager!.SetEnabledAsync(
                        updated.Desktop!.StartWithWindows,
                        updateCancellation.Token);
                }
                catch (Exception exception)
                {
                    var failures = new List<Exception> { exception };
                    failures.AddRange(await RollbackSettingsSideEffectsAsync(
                        previousSettings,
                        rollbackStartup: true));

                    if (exception is OperationCanceledException && failures.Count == 1)
                    {
                        throw;
                    }

                    throw new AggregateException(
                        "Login startup could not be updated; rollback was attempted.",
                        failures);
                }
            }

            RouterResult routerResult;
            var previousRevision = State.Router.Revision;
            try
            {
                routerResult = await router.DispatchAsync(
                    new RouterIntent.UpdateOptions(ToRouterOptions(updated)),
                    updateCancellation.Token);
            }
            catch (Exception exception)
            {
                var failures = new List<Exception> { exception };
                failures.AddRange(await RollbackSettingsSideEffectsAsync(
                    previousSettings,
                    rollbackStartup: startupChanged));

                if (exception is OperationCanceledException && failures.Count == 1)
                {
                    throw;
                }

                throw new AggregateException(
                    "Runtime settings could not be applied; rollback was attempted.",
                    failures);
            }

            settings = updated;
            ResetRepeatedPauseObservations();
            settingsLoadWarning = null;
            if (State.PlaybackStateLock is
                {
                    Mode: PlaybackStateLockMode.KeepPlaying,
                    ArmedTarget: { } armedTarget,
                } && routerResult.State.ActiveTarget != armedTarget)
            {
                State = State with
                {
                    PlaybackStateLock = ResetPlaybackStateLock(),
                };
            }
            Apply(routerResult);
            await RecordStateTransitionAsync(
                previousRevision,
                routerResult.State,
                CancellationToken.None);
            await TryWriteDiagnosticAsync(
                new DiagnosticEvent("settings.saved"),
                CancellationToken.None);
            return new ApplicationResult(State, RouteDecision.StateUpdated);
        }
        finally
        {
            dispatchGate.Release();
        }
    }

    private static RouterOptions ToRouterOptions(MediaLockSettings source) => new(
        source.Recovery!.FallbackPolicy,
        source.Recovery.Timeout,
        source.PriorityRules);

    private async ValueTask<List<Exception>> RollbackSettingsSideEffectsAsync(
        MediaLockSettings previousSettings,
        bool rollbackStartup)
    {
        var failures = new List<Exception>();
        if (rollbackStartup && loginStartupManager is not null)
        {
            try
            {
                await loginStartupManager.SetEnabledAsync(
                    previousSettings.Desktop!.StartWithWindows,
                    CancellationToken.None);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        if (settingsRepository is not null)
        {
            try
            {
                await settingsRepository.SaveAsync(
                    previousSettings,
                    CancellationToken.None);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        return failures;
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        if (workstationLockState is not null)
        {
            workstationLockState.Locked -= OnWorkstationLocked;
            workstationLockState.Unlocked -= OnWorkstationUnlocked;
        }
        await lifetime.CancelAsync();
        Task[] deadlines;
        lock (recoverySync)
        {
            foreach (var deadline in recoveryDeadlines.Values)
            {
                deadline.Cancellation.Cancel();
            }

            deadlines = recoveryDeadlines.Values
                .Select(deadline => deadline.Task)
                .ToArray();
        }

        try
        {
            await Task.WhenAll(deadlines);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }

        if (catalogWorker is not null)
        {
            try
            {
                await catalogWorker;
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
            {
            }
        }

        if (loginStartupWorker is not null)
        {
            try
            {
                await loginStartupWorker;
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
            {
            }
        }

        await dispatchGate.WaitAsync();
        try
        {
            await catalog.DisposeAsync();
            await router.DisposeAsync();
            lifetime.Dispose();
        }
        finally
        {
            dispatchGate.Release();
            dispatchGate.Dispose();
        }
    }

    private async Task WatchLoginStartupAsync(
        ILoginStartupChangeSource changeSource,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var enabled in changeSource.WatchEnabledAsync(cancellationToken))
            {
                await dispatchGate.WaitAsync(cancellationToken);
                try
                {
                    var desired = settings.Desktop!.StartWithWindows;
                    if (enabled != desired &&
                        await loginStartupManager!.IsEnabledAsync(cancellationToken) != desired)
                    {
                        await loginStartupManager.SetEnabledAsync(desired, cancellationToken);
                        await TryWriteDiagnosticAsync(
                            new DiagnosticEvent("startup.registration.repaired"),
                            cancellationToken);
                    }
                }
                finally
                {
                    dispatchGate.Release();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            PublishError($"Login startup monitoring is unavailable: {exception.Message}");
        }
    }

    private async Task WatchCatalogAsync(
        TaskCompletionSource initialized,
        CancellationToken cancellationToken)
    {
        try
        {
            var firstSnapshot = true;
            await foreach (var snapshot in catalog.WatchAsync(cancellationToken))
            {
                var nextVisibleTargets = snapshot.Targets;
                var visibleWindowsCurrentTarget = snapshot.WindowsCurrentTarget is { } currentTarget &&
                    nextVisibleTargets.Any(target => target.Id == currentTarget)
                        ? currentTarget
                        : (MediaTargetId?)null;
                var containsOnlyGsmtcTargets = nextVisibleTargets.All(
                    target => target.GsmtcSession is not null);
                RouterIntent catalogIntent = containsOnlyGsmtcTargets
                    ? new RouterIntent.CatalogUpdated(
                        nextVisibleTargets
                            .Select(target => target.GsmtcSession!)
                            .ToImmutableArray(),
                        visibleWindowsCurrentTarget is { Value: var currentValue }
                            ? new SessionKey(currentValue)
                            : null)
                    : new RouterIntent.MediaTargetsUpdated(
                        nextVisibleTargets,
                        visibleWindowsCurrentTarget);
                await DispatchRouterAsync(
                    catalogIntent,
                    cancellationToken,
                    persistRuntimeState: !firstSnapshot || startupRestoreIntent is null,
                    nextCatalogStatus: snapshot.Status,
                    nextCatalogStatusMessage: snapshot.StatusMessage,
                    nextVisibleTargets: nextVisibleTargets);
                if (firstSnapshot && startupRestoreIntent is { } restoreIntent)
                {
                    await DispatchRouterAsync(
                        restoreIntent,
                        cancellationToken);
                    startupRestoreIntent = null;
                }

                firstSnapshot = false;
                initialized.TrySetResult();
            }

            if (!initialized.TrySetException(new InvalidOperationException(
                "Media Session catalog completed before publishing an initial snapshot.")))
            {
                await TransitionCatalogUnavailableAsync(
                    "Media Session catalog stopped unexpectedly.",
                    cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            if (initialized.TrySetException(exception))
            {
                throw;
            }

            await TransitionCatalogUnavailableAsync(
                $"GSMTC catalog became unavailable: {exception.Message}",
                cancellationToken);
        }
    }

    private async ValueTask<(RouterResult Result, MediaLockApplicationState State)> DispatchRouterAsync(
        RouterIntent intent,
        CancellationToken cancellationToken,
        bool persistRuntimeState = true,
        MediaSessionCatalogStatus? nextCatalogStatus = null,
        string? nextCatalogStatusMessage = null,
        RoutingMode? startupRoutingMode = null,
        bool suppressRuntimeStatePersistence = false,
        bool resumeRuntimeStatePersistence = false,
        bool clearPlaybackStateLock = false,
        ImmutableArray<MediaTargetSnapshot>? nextVisibleTargets = null)
    {
        using var dispatchCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            lifetime.Token);
        await dispatchGate.WaitAsync(dispatchCancellation.Token);
        try
        {
            var previousRevision = State.Router.Revision;
            var previousCatalogStatus = catalogStatus;
            if (nextCatalogStatus is { } status)
            {
                catalogStatus = status;
                catalogStatusMessage = nextCatalogStatusMessage;
            }

            if (clearPlaybackStateLock &&
                State.PlaybackStateLock.Mode != PlaybackStateLockMode.Off)
            {
                ClearPlaybackStateLock();
            }
            else if (intent is RouterIntent.Route)
            {
                ResetRepeatedPauseObservations();
            }

            var result = await router.DispatchAsync(intent, dispatchCancellation.Token);
            if (nextVisibleTargets is { } targets)
            {
                visibleTargets = targets;
            }

            Apply(result);
            if (intent is RouterIntent.CatalogUpdated or RouterIntent.MediaTargetsUpdated)
            {
                result = await CorrectPlaybackStateAsync(result, dispatchCancellation.Token);
            }
            else if (State.PlaybackStateLock is
            {
                Mode: PlaybackStateLockMode.KeepPlaying,
                ArmedTarget: { } armedTarget,
            } && result.State.ActiveTarget != armedTarget)
            {
                ClearPlaybackStateLock();
            }
            if (suppressRuntimeStatePersistence)
            {
                runtimeStatePersistenceSuppressed = true;
            }

            var previousPersistedRuntimeState = persistedRuntimeState;
            var shouldPersistRuntimeState =
                persistRuntimeState &&
                (!runtimeStatePersistenceSuppressed || resumeRuntimeStatePersistence);
            var runtimeStatePersisted = !shouldPersistRuntimeState ||
                await PersistRuntimeStateAsync(result.State, dispatchCancellation.Token);
            if (startupRoutingMode is { } mode)
            {
                var requiresPersistedTarget = mode is RoutingMode.AppLock or RoutingMode.SessionLock;
                if (!requiresPersistedTarget || runtimeStatePersisted)
                {
                    await PersistStartupRoutingModeAsync(
                        mode,
                        preserveCurrentError: !runtimeStatePersisted,
                        previousPersistedRuntimeState: previousPersistedRuntimeState,
                        runtimeStateWasPersisted: shouldPersistRuntimeState && runtimeStatePersisted,
                        cancellationToken: dispatchCancellation.Token);
                    runtimeStatePersistenceSuppressed = false;
                }
                else
                {
                    runtimeStatePersistenceSuppressed = true;
                    if (runtimeStateRepository is null)
                    {
                        PublishError(
                            "Runtime state persistence is unavailable; startup routing mode was not changed.");
                    }
                }
            }
            await RecordStateTransitionAsync(
                previousRevision,
                result.State,
                dispatchCancellation.Token);
            if (nextCatalogStatus is not null && catalogStatus != previousCatalogStatus)
            {
                await TryWriteDiagnosticAsync(
                    new DiagnosticEvent(
                        "catalog.status",
                        new Dictionary<string, string>
                        {
                            ["status"] = catalogStatus.ToString(),
                        }),
                    dispatchCancellation.Token);
            }
            if (intent is RouterIntent.Route routedCommand)
            {
                await TryWriteDiagnosticAsync(
                    new DiagnosticEvent(
                        "route.completed",
                        new Dictionary<string, string>
                        {
                            ["command"] = routedCommand.Command.ToString(),
                            ["decision"] = result.Decision.Kind.ToString(),
                            ["reason"] = result.Decision.Reason.ToString(),
                            ["target"] = result.Decision.Target?.Value ?? string.Empty,
                        }),
                    dispatchCancellation.Token);
            }

            return (result, State);
        }
        finally
        {
            dispatchGate.Release();
        }
    }

    private async ValueTask<RouterResult> CorrectPlaybackStateAsync(
        RouterResult catalogResult,
        CancellationToken cancellationToken)
    {
        var playbackStateLock = State.PlaybackStateLock;
        if (Interlocked.Exchange(ref repeatedPauseResetPending, 0) != 0)
        {
            ResetRepeatedPauseObservations();
        }

        if (playbackStateLock.Mode != PlaybackStateLockMode.KeepPlaying ||
            playbackStateLock.ArmedTarget is not { } armedTarget)
        {
            return catalogResult;
        }

        if (catalogStatus == MediaSessionCatalogStatus.Suspended)
        {
            ClearPlaybackStateLock();
            return catalogResult;
        }

        if (catalogStatus is MediaSessionCatalogStatus.Reacquiring or
            MediaSessionCatalogStatus.Unavailable)
        {
            playbackStateCorrectionAttempts = 0;
            ResetRepeatedPauseObservations();
            if (playbackStateLock.Status != PlaybackStateLockStatus.Suspended ||
                playbackStateLock.Message is not null)
            {
                PublishPlaybackStateLock(playbackStateLock with
                {
                    Status = PlaybackStateLockStatus.Suspended,
                    Message = null,
                });
            }

            return catalogResult;
        }

        if (catalogResult.State.LockedTarget is
            {
                ResolvedSession: { } resolvedSession,
                Fingerprint: { } refreshedFingerprint,
            } && resolvedSession == armedTarget)
        {
            playbackStateLockRecoveryFingerprint = refreshedFingerprint;
        }

        var armedSession = catalogResult.State.Sessions.FirstOrDefault(
            session => session.Key == armedTarget);
        if (armedSession is not null &&
            catalogResult.State.Mode is RoutingMode.WindowsAuto or RoutingMode.PriorityRules &&
            catalogResult.State.ActiveTarget == armedTarget)
        {
            playbackStateLockRecoveryFingerprint = SessionFingerprint.From(armedSession);
        }

        var armedSessionIsMissing = armedSession is null;
        if (armedSessionIsMissing &&
            catalogResult.State.Mode is RoutingMode.WindowsAuto or RoutingMode.PriorityRules &&
            catalogResult.State.ActiveTarget == armedTarget)
        {
            playbackStateCorrectionAttempts = 0;
            ResetRepeatedPauseObservations();
            PublishPlaybackStateLock(playbackStateLock with
            {
                Status = PlaybackStateLockStatus.Suspended,
                Message = null,
            });
            return catalogResult;
        }

        if (catalogResult.State.ActiveTarget != armedTarget)
        {
            if (Volatile.Read(ref workstationObservationPending) != 0)
            {
                playbackStateCorrectionAttempts = 0;
                PublishPlaybackStateLock(playbackStateLock with
                {
                    Status = PlaybackStateLockStatus.Suspended,
                    Message = null,
                });
                return catalogResult;
            }

            var isRecoveringSameLockedTarget =
                catalogResult.State.Status == RouterStatus.Recovering &&
                playbackStateLockRecoveryFingerprint is { } fingerprint &&
                catalogResult.State.LockedTarget?.Fingerprint == fingerprint &&
                catalogResult.State.LockedTarget.ResolvedSession is null;
            if (isRecoveringSameLockedTarget)
            {
                playbackStateCorrectionAttempts = 0;
                PublishPlaybackStateLock(playbackStateLock with
                {
                    Status = PlaybackStateLockStatus.Suspended,
                    Message = null,
                });
                return catalogResult;
            }

            var acceptedSuccessor = catalogResult.State.ActiveTarget is { } successor &&
                playbackStateLock.Status == PlaybackStateLockStatus.Suspended &&
                playbackStateLockRecoveryFingerprint is not null &&
                catalogResult.State.Status == RouterStatus.Locked &&
                catalogResult.State.LockedTarget?.ResolvedSession == successor;
            var acceptedAutomaticSuccessor =
                catalogResult.State.Mode is RoutingMode.WindowsAuto or RoutingMode.PriorityRules &&
                catalogResult.State.ActiveTarget is { } automaticSuccessor &&
                playbackStateLock.Status == PlaybackStateLockStatus.Suspended &&
                playbackStateLockRecoveryFingerprint is { } automaticFingerprint &&
                ResolveUniquePlaybackStateSuccessor(
                    automaticFingerprint,
                    catalogResult.State.Sessions) == automaticSuccessor;
            if (acceptedSuccessor || acceptedAutomaticSuccessor)
            {
                armedTarget = catalogResult.State.ActiveTarget!.Value;
                playbackStateCorrectionAttempts = 0;
                ResetRepeatedPauseObservations();
                playbackStateLock = playbackStateLock with
                {
                    Status = PlaybackStateLockStatus.Ready,
                    ArmedTarget = armedTarget,
                    Message = null,
                };
                PublishPlaybackStateLock(playbackStateLock);
            }
            else if (
                catalogResult.State.Mode is RoutingMode.WindowsAuto or RoutingMode.PriorityRules &&
                armedSessionIsMissing)
            {
                playbackStateCorrectionAttempts = 0;
                ResetRepeatedPauseObservations();
                PublishPlaybackStateLock(playbackStateLock with
                {
                    Status = PlaybackStateLockStatus.Suspended,
                    Message = null,
                });
                return catalogResult;
            }
            else
            {
                ClearPlaybackStateLock();
                return catalogResult;
            }
        }

        var observed = catalogResult.State.Sessions.FirstOrDefault(
            session => session.Key == armedTarget);
        if (observed is null)
        {
            return catalogResult;
        }

        if (Volatile.Read(ref workstationObservationPending) != 0)
        {
            if (observed.PlaybackStatus == PlaybackStatus.Playing)
            {
                if (Volatile.Read(ref workstationLocked) == 0)
                {
                    Volatile.Write(ref workstationObservationPending, 0);
                }
            }
            else if (observed.PlaybackStatus is PlaybackStatus.Paused or
                PlaybackStatus.Stopped or PlaybackStatus.Closed)
            {
                Volatile.Write(ref workstationObservationPending, 0);
                ClearPlaybackStateLock();
                return catalogResult;
            }
            else
            {
                playbackStateCorrectionAttempts = 0;
                PublishPlaybackStateLock(playbackStateLock with
                {
                    Status = PlaybackStateLockStatus.Suspended,
                    Message = null,
                });
                return catalogResult;
            }
        }

        if (playbackStateLock.Status == PlaybackStateLockStatus.Suspended)
        {
            playbackStateCorrectionAttempts = 0;
            playbackStateLock = playbackStateLock with
            {
                Status = PlaybackStateLockStatus.Ready,
                Message = null,
            };
            PublishPlaybackStateLock(playbackStateLock);
        }

        if (observed.PlaybackStatus == PlaybackStatus.Playing)
        {
            playbackStateCorrectionAttempts = 0;
            pausedEpisodeObserved = false;
            lastArmedPlaybackStatus = PlaybackStatus.Playing;
            if (playbackStateLock.Status != PlaybackStateLockStatus.Ready ||
                playbackStateLock.Message is not null)
            {
                PublishPlaybackStateLock(playbackStateLock with
                {
                    Status = PlaybackStateLockStatus.Ready,
                    Message = null,
                });
            }

            return catalogResult;
        }

        if (observed.PlaybackStatus != PlaybackStatus.Paused)
        {
            lastArmedPlaybackStatus = observed.PlaybackStatus;
        }

        if (PlaybackStateLockRules.Decide(
                playbackStateLock.Mode,
                observed.PlaybackStatus) != PlaybackStateCorrection.Play ||
            playbackStateLock.Status == PlaybackStateLockStatus.Failed)
        {
            return catalogResult;
        }

        if (observed.PlaybackStatus == PlaybackStatus.Paused)
        {
            var shouldRelease = ShouldReleaseForRepeatedPause();
            lastArmedPlaybackStatus = PlaybackStatus.Paused;
            if (shouldRelease)
            {
                ReleasePlaybackStateLock();
                return catalogResult;
            }
        }
        if (playbackStateCorrectionAttempts >= MaximumPlaybackStateCorrectionAttempts)
        {
            PublishPlaybackStateLock(playbackStateLock with
            {
                Status = PlaybackStateLockStatus.Failed,
                Message = "Keep Playing could not be confirmed after two correction attempts.",
            });
            return catalogResult;
        }

        playbackStateCorrectionAttempts++;

        var correction = await router.DispatchAsync(
            new RouterIntent.Route(MediaCommand.Play, armedTarget),
            cancellationToken);
        Apply(correction);
        return correction;
    }

    private static SessionKey? ResolveUniquePlaybackStateSuccessor(
        SessionFingerprint fingerprint,
        IReadOnlyList<MediaSessionSnapshot> sessions)
    {
        SessionKey? successor = null;
        foreach (var session in sessions)
        {
            if (!fingerprint.IsAcceptableSuccessor(session))
            {
                continue;
            }

            if (successor is not null)
            {
                return null;
            }

            successor = session.Key;
        }

        return successor;
    }

    private bool ShouldReleaseForRepeatedPause()
    {
        var overrideSettings = settings.PlaybackStateLock!;
        if (!overrideSettings.RepeatedPauseOverrideEnabled || pausedEpisodeObserved ||
            lastArmedPlaybackStatus != PlaybackStatus.Playing)
        {
            return false;
        }

        pausedEpisodeObserved = true;
        var now = timeProvider.GetUtcNow();
        var earliest = now - overrideSettings.RepeatedPauseWindow;
        while (repeatedPauseObservations.TryPeek(out var observedAt) &&
               observedAt < earliest)
        {
            repeatedPauseObservations.Dequeue();
        }

        repeatedPauseObservations.Enqueue(now);
        return repeatedPauseObservations.Count >= overrideSettings.RepeatedPauseCount;
    }

    private async ValueTask PersistStartupRoutingModeAsync(
        RoutingMode mode,
        bool preserveCurrentError,
        RuntimeStateDocument? previousPersistedRuntimeState,
        bool runtimeStateWasPersisted,
        CancellationToken cancellationToken)
    {
        var currentError = preserveCurrentError ? State.ErrorMessage : null;
        var updated = settings with { DefaultRoutingMode = mode };
        if (settingsRepository is not null)
        {
            try
            {
                await settingsRepository.SaveAsync(updated, cancellationToken);
            }
            catch (Exception exception)
            {
                var failures = new List<Exception> { exception };
                var runtimeRollbackSucceeded = false;
                if (runtimeStateWasPersisted &&
                    previousPersistedRuntimeState is not null &&
                    runtimeStateRepository is not null)
                {
                    try
                    {
                        await runtimeStateRepository.SaveAsync(
                            previousPersistedRuntimeState,
                            CancellationToken.None);
                        persistedRuntimeState = previousPersistedRuntimeState;
                        runtimeRollbackSucceeded = true;
                    }
                    catch (Exception rollbackException)
                    {
                        failures.Add(rollbackException);
                    }
                }

                var rollbackStatus = runtimeRollbackSucceeded
                    ? " The previous runtime state was restored."
                    : runtimeStateWasPersisted
                        ? " The previous runtime state could not be restored."
                        : string.Empty;
                var message =
                    $"Routing mode changed for this run, but the startup mode could not be saved: {exception.Message}{rollbackStatus}";
                runtimeStatePersistenceSuppressed = true;
                PublishError(message);
                throw new InvalidOperationException(
                    message,
                    failures.Count == 1 ? exception : new AggregateException(failures));
            }
        }

        settings = updated;
        settingsLoadWarning = null;
        runtimeStateLoadWarning = null;
        State = new MediaLockApplicationState(
            State.Router,
            currentError ?? PersistenceWarnings,
            settings,
            catalogStatus,
            catalogStatusMessage)
        {
            PlaybackStateLock = State.PlaybackStateLock,
            Targets = visibleTargets,
        };
        StateChanged?.Invoke(this, new MediaLockApplicationStateChangedEventArgs(State));
        await TryWriteDiagnosticAsync(
            new DiagnosticEvent("settings.saved"),
            cancellationToken);
    }

    private async Task TransitionCatalogUnavailableAsync(
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            await DispatchRouterAsync(
                new RouterIntent.CatalogUpdated([], null),
                cancellationToken,
                nextCatalogStatus: MediaSessionCatalogStatus.Unavailable,
                nextCatalogStatusMessage: message,
                nextVisibleTargets: []);
            PublishError(message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            PublishError($"{message} State transition failed: {exception.Message}");
        }
    }

    private void Apply(RouterResult result)
    {
        foreach (var effect in result.Effects)
        {
            switch (effect)
            {
                case RouterEffect.ScheduleRecoveryTimeout schedule:
                    ScheduleRecoveryTimeout(schedule);
                    break;
                case RouterEffect.CancelRecoveryTimeout cancel:
                    CancelRecoveryTimeout(cancel.RecoveryEpoch);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(result));
            }
        }

        Publish(result.State);
    }

    private void ScheduleRecoveryTimeout(RouterEffect.ScheduleRecoveryTimeout effect)
    {
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var deadline = new RecoveryDeadline(cancellation);
        lock (recoverySync)
        {
            if (!recoveryDeadlines.TryAdd(effect.RecoveryEpoch, deadline))
            {
                cancellation.Dispose();
                return;
            }

            deadline.Task = RunRecoveryTimeoutAsync(effect, deadline);
        }
    }

    private void CancelRecoveryTimeout(long recoveryEpoch)
    {
        lock (recoverySync)
        {
            if (recoveryDeadlines.TryGetValue(recoveryEpoch, out var deadline))
            {
                deadline.Cancellation.Cancel();
            }
        }
    }

    private async Task RunRecoveryTimeoutAsync(
        RouterEffect.ScheduleRecoveryTimeout effect,
        RecoveryDeadline deadline)
    {
        try
        {
            await Task.Delay(effect.Delay, deadline.Cancellation.Token);
            await DispatchRouterAsync(
                new RouterIntent.RecoveryTimedOut(effect.RecoveryEpoch),
                deadline.Cancellation.Token);
        }
        catch (OperationCanceledException) when (deadline.Cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            lock (recoverySync)
            {
                recoveryDeadlines.Remove(effect.RecoveryEpoch);
            }

            deadline.Cancellation.Dispose();
        }
    }

    private void Publish(RouterState routerState)
    {
        var playbackStateLock = State.PlaybackStateLock;
        State = new MediaLockApplicationState(
            routerState,
            PersistenceWarnings,
            settings,
            catalogStatus,
            catalogStatusMessage)
        {
            PlaybackStateLock = playbackStateLock,
            Targets = visibleTargets,
        };
        StateChanged?.Invoke(
            this,
            new MediaLockApplicationStateChangedEventArgs(State));
    }

    private void PublishError(string message)
    {
        State = State with { ErrorMessage = message };
        StateChanged?.Invoke(
            this,
            new MediaLockApplicationStateChangedEventArgs(State));
    }

    private async ValueTask<bool> PersistRuntimeStateAsync(
        RouterState routerState,
        CancellationToken cancellationToken)
    {
        if (runtimeStateRepository is null)
        {
            return false;
        }

        var fingerprint = routerState.LockedTarget?.Fingerprint;
        var persisted = new RuntimeStateDocument(
            RuntimeStateDocument.CurrentSchemaVersion,
            routerState.Mode,
            fingerprint is null
                ? null
                : new PersistedLockedTarget(new PersistedSessionFingerprint(
                    fingerprint.Descriptor.SourceAppUserModelId,
                    fingerprint.Descriptor.SessionInstanceHint,
                    fingerprint.PlaybackStatus,
                    fingerprint.ObservedAt,
                    fingerprint.PlaybackType,
                    fingerprint.Title,
                    fingerprint.Artist)));
        try
        {
            await runtimeStateRepository.SaveAsync(persisted, cancellationToken);
            persistedRuntimeState = persisted;
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            PublishError($"Runtime state could not be saved: {exception.Message}");
            return false;
        }
    }

    private static SessionFingerprint ToSessionFingerprint(
        PersistedSessionFingerprint persisted) => new(
        new SessionDescriptor(
            persisted.SourceAppUserModelId,
            persisted.SessionInstanceHint),
        persisted.PlaybackStatus,
        persisted.ObservedAt,
        persisted.PlaybackType,
        persisted.Title,
        persisted.Artist);

    private string? PersistenceWarnings =>
        string.Join(
            " ",
            new[] { settingsLoadWarning, runtimeStateLoadWarning }
                .Where(message => !string.IsNullOrWhiteSpace(message))) is { Length: > 0 } warnings
            ? warnings
            : null;

    private async ValueTask TryWriteDiagnosticAsync(
        DiagnosticEvent diagnosticEvent,
        CancellationToken cancellationToken)
    {
        if (diagnosticLog is null)
        {
            return;
        }

        try
        {
            await diagnosticLog.WriteAsync(diagnosticEvent, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            PublishError($"Diagnostic logging is unavailable: {exception.Message}");
        }
    }

    private ValueTask RecordStateTransitionAsync(
        long previousRevision,
        RouterState routerState,
        CancellationToken cancellationToken) =>
        routerState.Revision == previousRevision
            ? ValueTask.CompletedTask
            : TryWriteDiagnosticAsync(
                new DiagnosticEvent(
                    "state.changed",
                    new Dictionary<string, string>
                    {
                        ["mode"] = routerState.Mode.ToString(),
                        ["status"] = routerState.Status.ToString(),
                        ["revision"] = routerState.Revision.ToString(
                            System.Globalization.CultureInfo.InvariantCulture),
                    }),
                cancellationToken);

    private sealed class RecoveryDeadline(CancellationTokenSource cancellation)
    {
        public CancellationTokenSource Cancellation { get; } = cancellation;

        public Task Task { get; set; } = Task.CompletedTask;
    }
}
