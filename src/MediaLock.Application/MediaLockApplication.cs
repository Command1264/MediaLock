using MediaLock.Core.Media;
using MediaLock.Core.Routing;

namespace MediaLock.Application;

public sealed class MediaLockApplication : IMediaLockApplication
{
    private readonly IMediaSessionCatalog catalog;
    private readonly IMediaRouter router;
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim dispatchGate = new(1, 1);
    private readonly Lock recoverySync = new();
    private readonly Dictionary<long, RecoveryDeadline> recoveryDeadlines = [];
    private Task? catalogWorker;
    private bool disposed;

    public MediaLockApplication(
        IMediaSessionCatalog catalog,
        IMediaRouter router)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(router);
        this.catalog = catalog;
        this.router = router;
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

        RouterIntent routerIntent = intent switch
        {
            ApplicationIntent.LockSession lockSession =>
                new RouterIntent.LockSession(lockSession.Session),
            ApplicationIntent.UseWindowsAuto => new RouterIntent.UseWindowsAuto(),
            ApplicationIntent.Route route => new RouterIntent.Route(route.Command),
            _ => throw new ArgumentOutOfRangeException(nameof(intent)),
        };
        var dispatch = await DispatchRouterAsync(routerIntent, cancellationToken);
        return new ApplicationResult(dispatch.State, dispatch.Result.Decision);
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
            await foreach (var snapshot in catalog.WatchAsync(cancellationToken))
            {
                await DispatchRouterAsync(
                    new RouterIntent.CatalogUpdated(
                        snapshot.Sessions,
                        snapshot.WindowsCurrentSession),
                    cancellationToken);
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
        CancellationToken cancellationToken)
    {
        using var dispatchCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            lifetime.Token);
        await dispatchGate.WaitAsync(dispatchCancellation.Token);
        try
        {
            var result = await router.DispatchAsync(intent, dispatchCancellation.Token);
            Apply(result);
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
                var result = await router.DispatchAsync(
                    new RouterIntent.CatalogUpdated([], null),
                    cancellationToken);
                Apply(result);
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
        State = new MediaLockApplicationState(routerState);
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

    private sealed class RecoveryDeadline(CancellationTokenSource cancellation)
    {
        public CancellationTokenSource Cancellation { get; } = cancellation;

        public Task Task { get; set; } = Task.CompletedTask;
    }
}
