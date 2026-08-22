using MediaLock.Core.Configuration;
using MediaLock.Core.Diagnostics;
using MediaLock.Core.Media;
using MediaLock.Core.Routing;

namespace MediaLock.Application;

public sealed class MediaLockApplication : IMediaLockApplication
{
    private readonly IMediaSessionCatalog catalog;
    private readonly IMediaRouter router;
    private readonly ISettingsRepository? settingsRepository;
    private readonly ILoginStartupManager? loginStartupManager;
    private readonly IRuntimeStateRepository? runtimeStateRepository;
    private readonly IDiagnosticLog? diagnosticLog;
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim dispatchGate = new(1, 1);
    private readonly Lock recoverySync = new();
    private readonly Dictionary<long, RecoveryDeadline> recoveryDeadlines = [];
    private Task? catalogWorker;
    private bool disposed;
    private MediaLockSettings settings = MediaLockSettings.Default;
    private string? settingsLoadWarning;
    private string? runtimeStateLoadWarning;
    private SessionFingerprint? startupRestoreFingerprint;
    private MediaSessionCatalogStatus catalogStatus = MediaSessionCatalogStatus.Available;
    private string? catalogStatusMessage;

    public MediaLockApplication(
        IMediaSessionCatalog catalog,
        IMediaRouter router)
        : this(
            catalog,
            router,
            settingsRepository: null,
            loginStartupManager: null,
            runtimeStateRepository: null,
            diagnosticLog: null)
    {
    }

    public MediaLockApplication(
        IMediaSessionCatalog catalog,
        IMediaRouter router,
        ISettingsRepository? settingsRepository,
        ILoginStartupManager? loginStartupManager,
        IRuntimeStateRepository? runtimeStateRepository = null,
        IDiagnosticLog? diagnosticLog = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(router);
        this.catalog = catalog;
        this.router = router;
        this.settingsRepository = settingsRepository;
        this.loginStartupManager = loginStartupManager;
        this.runtimeStateRepository = runtimeStateRepository;
        this.diagnosticLog = diagnosticLog;
    }

    public event EventHandler<MediaLockApplicationStateChangedEventArgs>? StateChanged;

    public MediaLockApplicationState State { get; private set; } = MediaLockApplicationState.Initial;

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
                catalogStatusMessage);
            if (loginStartupManager is not null &&
                await loginStartupManager.IsEnabledAsync(cancellationToken) !=
                settings.Desktop!.StartWithWindows)
            {
                await loginStartupManager.SetEnabledAsync(
                    settings.Desktop.StartWithWindows,
                    cancellationToken);
            }
        }

        if (runtimeStateRepository is not null)
        {
            var loadedRuntimeState = await runtimeStateRepository.LoadAsync(cancellationToken);
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
            else if (settings.DefaultRoutingMode == RoutingMode.SessionLock)
            {
                if (loadedRuntimeState.Value is
                    {
                        Mode: RoutingMode.SessionLock,
                        LockedTarget.Fingerprint: { } persistedFingerprint,
                    })
                {
                    startupRestoreFingerprint = ToSessionFingerprint(persistedFingerprint);
                }
                else
                {
                    runtimeStateLoadWarning =
                        "Default Session Lock requires a valid persisted Session Lock target; Windows Auto is active.";
                }
            }
        }
        else if (settings.DefaultRoutingMode == RoutingMode.SessionLock)
        {
            runtimeStateLoadWarning =
                "Default Session Lock requires runtime-state persistence; Windows Auto is active.";
        }

        if (settingsRepository is not null)
        {
            await DispatchRouterAsync(
                new RouterIntent.UpdateOptions(new RouterOptions(
                    settings.Recovery!.FallbackPolicy,
                    settings.Recovery.Timeout)),
                cancellationToken,
                persistRuntimeState: startupRestoreFingerprint is null);
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

        RouterIntent routerIntent = intent switch
        {
            ApplicationIntent.LockSession lockSession =>
                new RouterIntent.LockSession(lockSession.Session),
            ApplicationIntent.UseWindowsAuto => new RouterIntent.UseWindowsAuto(),
            ApplicationIntent.Route route => new RouterIntent.Route(route.Command),
            _ => throw new ArgumentOutOfRangeException(nameof(intent)),
        };
        var dispatch = await DispatchRouterAsync(routerIntent, cancellationToken);
        return new ApplicationResult(State, dispatch.Result.Decision);
    }

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
            if (settingsRepository is not null)
            {
                await settingsRepository.SaveAsync(updated, updateCancellation.Token);
            }

            if (loginStartupManager is not null &&
                settings.Desktop!.StartWithWindows != updated.Desktop!.StartWithWindows)
            {
                try
                {
                    await loginStartupManager.SetEnabledAsync(
                        updated.Desktop.StartWithWindows,
                        updateCancellation.Token);
                }
                catch (Exception exception)
                {
                    var failures = new List<Exception> { exception };
                    try
                    {
                        await loginStartupManager.SetEnabledAsync(
                            settings.Desktop.StartWithWindows,
                            CancellationToken.None);
                    }
                    catch (Exception rollbackException)
                    {
                        failures.Add(rollbackException);
                    }

                    if (settingsRepository is not null)
                    {
                        try
                        {
                            await settingsRepository.SaveAsync(settings, CancellationToken.None);
                        }
                        catch (Exception rollbackException)
                        {
                            failures.Add(rollbackException);
                        }
                    }

                    if (exception is OperationCanceledException && failures.Count == 1)
                    {
                        throw;
                    }

                    throw new AggregateException(
                        "Login startup could not be updated; rollback was attempted.",
                        failures);
                }
            }

            settings = updated;
            settingsLoadWarning = null;
            State = new MediaLockApplicationState(
                State.Router,
                PersistenceWarnings,
                settings,
                catalogStatus,
                catalogStatusMessage);
            StateChanged?.Invoke(this, new MediaLockApplicationStateChangedEventArgs(State));
            await TryWriteDiagnosticAsync(
                new DiagnosticEvent("settings.saved"),
                updateCancellation.Token);
            return new ApplicationResult(State, RouteDecision.StateUpdated);
        }
        finally
        {
            dispatchGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
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

    private async Task WatchCatalogAsync(
        TaskCompletionSource initialized,
        CancellationToken cancellationToken)
    {
        try
        {
            var firstSnapshot = true;
            await foreach (var snapshot in catalog.WatchAsync(cancellationToken))
            {
                await DispatchRouterAsync(
                    new RouterIntent.CatalogUpdated(
                        snapshot.Sessions,
                        snapshot.WindowsCurrentSession),
                    cancellationToken,
                    persistRuntimeState: !firstSnapshot || startupRestoreFingerprint is null,
                    nextCatalogStatus: snapshot.Status,
                    nextCatalogStatusMessage: snapshot.StatusMessage);
                if (firstSnapshot && startupRestoreFingerprint is { } fingerprint)
                {
                    await DispatchRouterAsync(
                        new RouterIntent.RestoreSessionLock(fingerprint),
                        cancellationToken);
                    startupRestoreFingerprint = null;
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
        string? nextCatalogStatusMessage = null)
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

            var result = await router.DispatchAsync(intent, dispatchCancellation.Token);
            Apply(result);
            if (persistRuntimeState)
            {
                await PersistRuntimeStateAsync(result.State, dispatchCancellation.Token);
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

    private async Task TransitionCatalogUnavailableAsync(
        string message,
        CancellationToken cancellationToken)
    {
        await dispatchGate.WaitAsync(cancellationToken);
        try
        {
            try
            {
                var previousRevision = State.Router.Revision;
                var result = await router.DispatchAsync(
                    new RouterIntent.CatalogUpdated([], null),
                    cancellationToken);
                Apply(result);
                await PersistRuntimeStateAsync(result.State, cancellationToken);
                await RecordStateTransitionAsync(
                    previousRevision,
                    result.State,
                    cancellationToken);
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
        finally
        {
            dispatchGate.Release();
        }
    }

    private void Apply(RouterResult result)
    {
        Publish(result.State);
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
        State = new MediaLockApplicationState(
            routerState,
            PersistenceWarnings,
            settings,
            catalogStatus,
            catalogStatusMessage);
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

    private async ValueTask PersistRuntimeStateAsync(
        RouterState routerState,
        CancellationToken cancellationToken)
    {
        if (runtimeStateRepository is null)
        {
            return;
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
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            PublishError($"Runtime state could not be saved: {exception.Message}");
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
