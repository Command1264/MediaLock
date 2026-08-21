using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using MediaLock.Core.Media;

namespace MediaLock.Windows.Gsmtc;

public sealed class GsmtcMediaAdapter : IMediaSessionCatalog, IMediaController
{
    private readonly IGsmtcSessionManagerFactory managerFactory;
    private readonly TimeProvider timeProvider;
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
    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly Lock sessionsSync = new();
    private readonly Dictionary<IGsmtcSession, SessionKey> keys =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<SessionKey, IGsmtcSession> liveSessions = [];
    private IGsmtcSessionManager? manager;
    private Task? refreshWorker;
    private long nextKey;
    private int watching;
    private bool disposed;

    public GsmtcMediaAdapter()
        : this(new GsmtcSessionManagerFactory(), TimeProvider.System)
    {
    }

    internal GsmtcMediaAdapter(
        IGsmtcSessionManagerFactory managerFactory,
        TimeProvider timeProvider)
    {
        this.managerFactory = managerFactory;
        this.timeProvider = timeProvider;
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
        manager = await managerFactory.CreateAsync(startupCancellation.Token);
        manager.SessionsChanged += OnSessionsChanged;
        await RefreshAsync(startupCancellation.Token);
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
        await lifetime.CancelAsync();
        refreshRequests.Writer.TryComplete();
        snapshots.Writer.TryComplete();
        if (refreshWorker is not null)
        {
            await refreshWorker;
        }

        await refreshGate.WaitAsync();
        try
        {
            if (manager is not null)
            {
                manager.SessionsChanged -= OnSessionsChanged;
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

            if (manager is not null)
            {
                await manager.DisposeAsync();
                manager = null;
            }
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
            ObjectDisposedException.ThrowIf(disposed, this);
            var currentManager = manager ?? throw new InvalidOperationException("GSMTC manager is not initialized.");
            var sessions = currentManager.GetSessions();
            ReconcileSessions(sessions);
            var observedAt = timeProvider.GetUtcNow();
            var builder = ImmutableArray.CreateBuilder<MediaSessionSnapshot>(sessions.Count);
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
            catch (Exception exception)
            {
                snapshots.Writer.TryComplete(exception);
                return;
            }
        }
    }
}
