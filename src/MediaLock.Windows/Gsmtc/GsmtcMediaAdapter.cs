using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using MediaLock.Core.Lifecycle;
using MediaLock.Core.Media;

namespace MediaLock.Windows.Gsmtc;

public sealed class GsmtcMediaAdapter : IMediaSessionCatalog, IMediaController
{
    private readonly IGsmtcSessionManagerFactory managerFactory;
    private readonly TimeProvider timeProvider;
    private readonly ISystemLifecycle? systemLifecycle;
    private readonly IWorkstationLockState? workstationLockState;
    private readonly IReadOnlyList<TimeSpan> reacquisitionDelays;
    private readonly HashSet<string> excludedSourceApplicationIds;
    private readonly Channel<MediaSessionCatalogSnapshot> snapshots =
        Channel.CreateUnbounded<MediaSessionCatalogSnapshot>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
    private readonly Channel<bool> refreshRequests =
        Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.DropWrite,
        });
    private readonly Channel<AdapterTransition> lifecycleTransitions =
        Channel.CreateUnbounded<AdapterTransition>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly Lock sessionsSync = new();
    private readonly Dictionary<IGsmtcSession, SessionKey> keys =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<SessionKey, IGsmtcSession> liveSessions = [];
    private IGsmtcSessionManager? manager;
    private Task? refreshWorker;
    private Task? lifecycleWorker;
    private long nextKey;
    private int watching;
    private bool disposed;

    public GsmtcMediaAdapter()
        : this(new GsmtcSessionManagerFactory(), TimeProvider.System, systemLifecycle: null)
    {
    }

    public GsmtcMediaAdapter(ISystemLifecycle systemLifecycle)
        : this(
            new GsmtcSessionManagerFactory(),
            TimeProvider.System,
            systemLifecycle ?? throw new ArgumentNullException(nameof(systemLifecycle)))
    {
    }

    public GsmtcMediaAdapter(
        ISystemLifecycle systemLifecycle,
        IEnumerable<string> excludedSourceApplicationIds)
        : this(
            new GsmtcSessionManagerFactory(),
            TimeProvider.System,
            systemLifecycle ?? throw new ArgumentNullException(nameof(systemLifecycle)),
            excludedSourceApplicationIds: excludedSourceApplicationIds)
    {
    }

    internal GsmtcMediaAdapter(
        IGsmtcSessionManagerFactory managerFactory,
        TimeProvider timeProvider,
        ISystemLifecycle? systemLifecycle = null,
        IReadOnlyList<TimeSpan>? reacquisitionDelays = null,
        IEnumerable<string>? excludedSourceApplicationIds = null)
    {
        this.managerFactory = managerFactory;
        this.timeProvider = timeProvider;
        this.systemLifecycle = systemLifecycle;
        workstationLockState = systemLifecycle as IWorkstationLockState;
        this.excludedSourceApplicationIds = new HashSet<string>(
            excludedSourceApplicationIds ?? [],
            StringComparer.OrdinalIgnoreCase);
        if (this.excludedSourceApplicationIds.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "Excluded source application IDs must not be blank.",
                nameof(excludedSourceApplicationIds));
        }
        this.reacquisitionDelays = reacquisitionDelays ??
            [TimeSpan.Zero, TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(2)];
        if (this.reacquisitionDelays.Count != 3 ||
            this.reacquisitionDelays.Any(delay => delay < TimeSpan.Zero))
        {
            throw new ArgumentException(
                "GSMTC reacquisition requires exactly three non-negative attempt delays.",
                nameof(reacquisitionDelays));
        }
    }

    public async IAsyncEnumerable<MediaSessionCatalogSnapshot> WatchAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (Interlocked.Exchange(ref watching, 1) != 0)
        {
            throw new InvalidOperationException("GSMTC catalog supports one active watcher.");
        }

        using var startupCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            lifetime.Token);
        lifecycleWorker = ProcessLifecycleTransitionsAsync();
        if (systemLifecycle is not null)
        {
            systemLifecycle.Suspending += OnSuspending;
            systemLifecycle.Resumed += OnResumed;
        }
        if (workstationLockState is not null)
        {
            workstationLockState.Unlocked += OnWorkstationUnlocked;
        }

        await AcquireManagerAndPublishAsync(startupCancellation.Token);
        refreshWorker = ProcessRefreshRequestsAsync();

        await foreach (var snapshot in snapshots.Reader.ReadAllAsync(cancellationToken))
        {
            yield return snapshot;
        }
    }

    public ValueTask<MediaControlResult> TryExecuteAsync(
        SessionKey target,
        MediaCommand command,
        CancellationToken cancellationToken)
    {
        IGsmtcSession? session;
        lock (sessionsSync)
        {
            liveSessions.TryGetValue(target, out session);
        }

        return session is null
            ? ValueTask.FromResult(MediaControlResult.Failed)
            : session.TryExecuteAsync(command, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        if (systemLifecycle is not null)
        {
            systemLifecycle.Suspending -= OnSuspending;
            systemLifecycle.Resumed -= OnResumed;
        }
        if (workstationLockState is not null)
        {
            workstationLockState.Unlocked -= OnWorkstationUnlocked;
        }

        await lifetime.CancelAsync();
        refreshRequests.Writer.TryComplete();
        lifecycleTransitions.Writer.TryComplete();
        snapshots.Writer.TryComplete();
        if (refreshWorker is not null)
        {
            await refreshWorker;
        }

        if (lifecycleWorker is not null)
        {
            await lifecycleWorker;
        }

        await refreshGate.WaitAsync();
        try
        {
            await ReleaseManagerAsync();
        }
        finally
        {
            refreshGate.Release();
            refreshGate.Dispose();
            lifetime.Dispose();
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await refreshGate.WaitAsync(cancellationToken);
        try
        {
            await PublishCurrentCatalogAsync(cancellationToken);
        }
        finally
        {
            refreshGate.Release();
        }
    }

    private SessionKey? ResolveCurrentSessionKey(
        IGsmtcSession? current,
        IReadOnlyList<IGsmtcSession> sessions)
    {
        if (current is null)
        {
            return null;
        }

        lock (sessionsSync)
        {
            if (keys.TryGetValue(current, out var exact))
            {
                return exact;
            }

            var sameSource = sessions
                .Where(session => string.Equals(
                    session.SourceAppUserModelId,
                    current.SourceAppUserModelId,
                    StringComparison.Ordinal))
                .Take(2)
                .ToArray();
            return sameSource.Length == 1 ? keys[sameSource[0]] : null;
        }
    }

    private void ReconcileSessions(IReadOnlyList<IGsmtcSession> sessions)
    {
        lock (sessionsSync)
        {
            var present = sessions.ToHashSet(ReferenceEqualityComparer.Instance);
            foreach (var removed in keys.Keys.Where(session => !present.Contains(session)).ToArray())
            {
                removed.Changed -= OnSessionChanged;
                liveSessions.Remove(keys[removed]);
                keys.Remove(removed);
            }

            foreach (var session in sessions)
            {
                if (keys.ContainsKey(session))
                {
                    continue;
                }

                var key = new SessionKey($"gsmtc-{Interlocked.Increment(ref nextKey)}");
                keys.Add(session, key);
                liveSessions.Add(key, session);
                session.Changed += OnSessionChanged;
            }
        }
    }

    private SessionKey GetKey(IGsmtcSession session)
    {
        lock (sessionsSync)
        {
            return keys[session];
        }
    }

    private void OnSessionsChanged(object? sender, EventArgs args) => QueueRefresh();

    private void OnSessionChanged(object? sender, EventArgs args) => QueueRefresh();

    private void OnSuspending() =>
        lifecycleTransitions.Writer.TryWrite(new AdapterTransition(
            AdapterTransitionKind.Suspend));

    private void OnResumed() =>
        lifecycleTransitions.Writer.TryWrite(new AdapterTransition(
            AdapterTransitionKind.Resume));

    private void OnWorkstationUnlocked() => QueueRefresh();

    private void QueueRefresh()
    {
        if (!disposed)
        {
            refreshRequests.Writer.TryWrite(true);
        }
    }

    private async Task ProcessRefreshRequestsAsync()
    {
        await foreach (var _ in refreshRequests.Reader.ReadAllAsync())
        {
            try
            {
                await RefreshAsync(lifetime.Token);
            }
            catch (ObjectDisposedException) when (disposed)
            {
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
            {
                return;
            }
            catch (InvalidOperationException) when (manager is null)
            {
                // A refresh queued immediately before suspend is obsolete once its manager is released.
            }
            catch (Exception exception)
            {
                lifecycleTransitions.Writer.TryWrite(new AdapterTransition(
                    AdapterTransitionKind.AdapterFailure,
                    exception));
            }
        }
    }

    private async Task RecoverFromAdapterFailureAsync(
        Exception refreshFailure,
        CancellationToken cancellationToken)
    {
        Exception? releaseFailure = null;
        await refreshGate.WaitAsync(cancellationToken);
        try
        {
            try
            {
                await ReleaseManagerAsync();
            }
            catch (Exception exception)
            {
                releaseFailure = exception;
            }

            var message = releaseFailure is null
                ? $"GSMTC catalog refresh failed: {refreshFailure.Message}"
                : $"GSMTC catalog refresh failed: {refreshFailure.Message} Manager release also failed: {releaseFailure.Message}";
            await PublishStatusAsync(
                MediaSessionCatalogStatus.Unavailable,
                message,
                cancellationToken);
        }
        finally
        {
            refreshGate.Release();
        }

        await ResumeAsync(cancellationToken);
    }

    private async Task ProcessLifecycleTransitionsAsync()
    {
        var suspended = false;
        try
        {
            await foreach (var transition in lifecycleTransitions.Reader.ReadAllAsync(lifetime.Token))
            {
                try
                {
                    switch (transition.Kind)
                    {
                        case AdapterTransitionKind.Suspend:
                            suspended = true;
                            await SuspendAsync(lifetime.Token);
                            break;
                        case AdapterTransitionKind.Resume:
                            suspended = false;
                            await ResumeAsync(lifetime.Token);
                            break;
                        case AdapterTransitionKind.AdapterFailure when !suspended:
                            await RecoverFromAdapterFailureAsync(
                                transition.Failure ?? new InvalidOperationException(
                                    "GSMTC adapter failed without an error."),
                                lifetime.Token);
                            break;
                        case AdapterTransitionKind.AdapterFailure:
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(transition));
                    }
                }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    await PublishStatusAsync(
                        MediaSessionCatalogStatus.Unavailable,
                        $"GSMTC lifecycle transition failed: {exception.Message}",
                        lifetime.Token);
                }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
    }

    private async Task SuspendAsync(CancellationToken cancellationToken)
    {
        await refreshGate.WaitAsync(cancellationToken);
        try
        {
            await ReleaseManagerAsync();
            await PublishStatusAsync(
                MediaSessionCatalogStatus.Suspended,
                "GSMTC is suspended while Windows sleeps.",
                cancellationToken);
        }
        finally
        {
            refreshGate.Release();
        }
    }

    private async Task ResumeAsync(CancellationToken cancellationToken)
    {
        await PublishStatusAsync(
            MediaSessionCatalogStatus.Reacquiring,
            "Reacquiring GSMTC after Windows resumed.",
            cancellationToken);
        Exception? lastFailure = null;
        foreach (var delay in reacquisitionDelays)
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, timeProvider, cancellationToken);
            }

            try
            {
                await AcquireManagerAndPublishAsync(cancellationToken);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                lastFailure = exception;
            }
        }

        await PublishStatusAsync(
            MediaSessionCatalogStatus.Unavailable,
            $"GSMTC could not be reacquired after 3 attempts: {lastFailure?.Message}",
            cancellationToken);
    }

    private async Task AcquireManagerAndPublishAsync(CancellationToken cancellationToken)
    {
        await refreshGate.WaitAsync(cancellationToken);
        try
        {
            await ReleaseManagerAsync();
            try
            {
                manager = await managerFactory.CreateAsync(cancellationToken);
                manager.SessionsChanged += OnSessionsChanged;
                await PublishCurrentCatalogAsync(cancellationToken);
            }
            catch
            {
                await ReleaseManagerAsync();
                throw;
            }
        }
        finally
        {
            refreshGate.Release();
        }
    }

    private async Task PublishCurrentCatalogAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var currentManager = manager ?? throw new InvalidOperationException("GSMTC manager is not initialized.");
        var sessions = currentManager
            .GetSessions()
            .Where(session => !excludedSourceApplicationIds.Contains(
                session.SourceAppUserModelId))
            .ToArray();
        ReconcileSessions(sessions);
        var observedAt = timeProvider.GetUtcNow();
        var builder = ImmutableArray.CreateBuilder<MediaSessionSnapshot>(sessions.Length);
        foreach (var session in sessions)
        {
            builder.Add(await session.ReadAsync(GetKey(session), observedAt, cancellationToken));
        }

        var current = currentManager.GetCurrentSession();
        var currentKey = ResolveCurrentSessionKey(current, sessions);
        await snapshots.Writer.WriteAsync(
            new MediaSessionCatalogSnapshot(builder.MoveToImmutable(), currentKey),
            cancellationToken);
    }

    private ValueTask PublishStatusAsync(
        MediaSessionCatalogStatus status,
        string message,
        CancellationToken cancellationToken) => snapshots.Writer.WriteAsync(
            new MediaSessionCatalogSnapshot([], null, status, message),
            cancellationToken);

    private async ValueTask ReleaseManagerAsync()
    {
        var managerToDispose = manager;
        manager = null;
        if (managerToDispose is not null)
        {
            managerToDispose.SessionsChanged -= OnSessionsChanged;
        }

        lock (sessionsSync)
        {
            foreach (var session in keys.Keys)
            {
                session.Changed -= OnSessionChanged;
            }

            keys.Clear();
            liveSessions.Clear();
        }

        if (managerToDispose is not null)
        {
            await managerToDispose.DisposeAsync();
        }
    }

    private sealed record AdapterTransition(
        AdapterTransitionKind Kind,
        Exception? Failure = null);

    private enum AdapterTransitionKind
    {
        Suspend,
        Resume,
        AdapterFailure,
    }
}
