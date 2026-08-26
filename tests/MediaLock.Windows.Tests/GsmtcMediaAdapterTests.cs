using MediaLock.Core.Media;
using MediaLock.Core.Lifecycle;
using MediaLock.Windows.Gsmtc;
using Xunit;

namespace MediaLock.Windows.Tests;

public sealed class GsmtcMediaAdapterTests
{
    [Fact]
    public async Task ResumeReleasesOldManagerAndPublishesReacquiredCatalog()
    {
        var oldSession = new FakeSession("Brave", MediaControlResult.Succeeded);
        var newSession = new FakeSession("Chrome", MediaControlResult.Succeeded);
        var oldManager = new FakeManager(oldSession);
        var newManager = new FakeManager(newSession);
        var lifecycle = new FakeSystemLifecycle();
        await using var adapter = new GsmtcMediaAdapter(
            new QueueManagerFactory(oldManager, newManager),
            TimeProvider.System,
            lifecycle);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using var snapshots = adapter.WatchAsync(cancellation.Token).GetAsyncEnumerator();
        Assert.True(await snapshots.MoveNextAsync());

        lifecycle.Suspend();

        Assert.True(await snapshots.MoveNextAsync());
        Assert.Equal(MediaSessionCatalogStatus.Suspended, snapshots.Current.Status);
        Assert.Empty(snapshots.Current.Sessions);
        Assert.True(oldManager.Disposed);

        lifecycle.Resume();

        Assert.True(await snapshots.MoveNextAsync());
        Assert.Equal(MediaSessionCatalogStatus.Reacquiring, snapshots.Current.Status);
        Assert.True(await snapshots.MoveNextAsync());
        Assert.Equal(MediaSessionCatalogStatus.Available, snapshots.Current.Status);
        Assert.Equal("Chrome", Assert.Single(snapshots.Current.Sessions).SourceAppUserModelId);
        Assert.Equal(0, oldManager.SubscriberCount);
        Assert.Equal(1, newManager.SubscriberCount);
    }

    [Fact]
    public async Task WorkstationUnlockQueuesAFreshCatalogSnapshot()
    {
        var session = new FakeSession("Brave", MediaControlResult.Succeeded);
        var lifecycle = new FakeSystemLifecycle();
        await using var adapter = new GsmtcMediaAdapter(
            new FakeManagerFactory(new FakeManager(session)),
            TimeProvider.System,
            lifecycle);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using var snapshots = adapter.WatchAsync(cancellation.Token).GetAsyncEnumerator();
        Assert.True(await snapshots.MoveNextAsync());
        Assert.Equal(1, session.ReadCount);

        lifecycle.Lock();
        lifecycle.Unlock();

        Assert.True(await snapshots.MoveNextAsync());
        Assert.Equal(MediaSessionCatalogStatus.Available, snapshots.Current.Status);
        Assert.True(session.ReadCount >= 2);
    }

    [Fact]
    public async Task FailedResumeUsesThreeAttemptsAndASecondResumeCanRecover()
    {
        var initialManager = new FakeManager(
            new FakeSession("Brave", MediaControlResult.Succeeded));
        var recoveredManager = new FakeManager(
            new FakeSession("Chrome", MediaControlResult.Succeeded));
        var factory = new ResumeRetryManagerFactory(initialManager, recoveredManager);
        var lifecycle = new FakeSystemLifecycle();
        await using var adapter = new GsmtcMediaAdapter(
            factory,
            TimeProvider.System,
            lifecycle,
            [TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero]);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using var snapshots = adapter.WatchAsync(cancellation.Token).GetAsyncEnumerator();
        Assert.True(await snapshots.MoveNextAsync());

        lifecycle.Resume();

        Assert.True(await snapshots.MoveNextAsync());
        Assert.Equal(MediaSessionCatalogStatus.Reacquiring, snapshots.Current.Status);
        Assert.True(await snapshots.MoveNextAsync());
        Assert.Equal(MediaSessionCatalogStatus.Unavailable, snapshots.Current.Status);
        Assert.Contains("3 attempts", snapshots.Current.StatusMessage, StringComparison.Ordinal);
        Assert.Equal(4, factory.CallCount);

        lifecycle.Resume();

        Assert.True(await snapshots.MoveNextAsync());
        Assert.Equal(MediaSessionCatalogStatus.Reacquiring, snapshots.Current.Status);
        Assert.True(await snapshots.MoveNextAsync());
        Assert.Equal(MediaSessionCatalogStatus.Available, snapshots.Current.Status);
        Assert.Equal("Chrome", Assert.Single(snapshots.Current.Sessions).SourceAppUserModelId);
        Assert.Equal(5, factory.CallCount);
    }

    [Fact]
    public async Task RefreshFailureReleasesOldManagerAndReacquiresWithoutEndingTheStream()
    {
        var oldSession = new FakeSession("Brave", MediaControlResult.Succeeded);
        var newSession = new FakeSession("Chrome", MediaControlResult.Succeeded);
        var oldManager = new FakeManager(oldSession);
        var newManager = new FakeManager(newSession);
        await using var adapter = new GsmtcMediaAdapter(
            new QueueManagerFactory(oldManager, newManager),
            TimeProvider.System,
            systemLifecycle: null,
            [TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero]);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using var snapshots = adapter.WatchAsync(cancellation.Token).GetAsyncEnumerator();
        Assert.True(await snapshots.MoveNextAsync());

        oldSession.FailNextRead();
        oldSession.RaiseChanged();

        Assert.True(await snapshots.MoveNextAsync());
        Assert.Equal(MediaSessionCatalogStatus.Unavailable, snapshots.Current.Status);
        Assert.True(await snapshots.MoveNextAsync());
        Assert.Equal(MediaSessionCatalogStatus.Reacquiring, snapshots.Current.Status);
        Assert.True(await snapshots.MoveNextAsync());
        Assert.Equal(MediaSessionCatalogStatus.Available, snapshots.Current.Status);
        Assert.Equal("Chrome", Assert.Single(snapshots.Current.Sessions).SourceAppUserModelId);
        Assert.True(oldManager.Disposed);
        Assert.Equal(0, oldManager.SubscriberCount);
    }

    [Fact]
    public async Task RefreshFailureQueuedAfterSuspendDoesNotReacquireWhileSuspended()
    {
        var oldSession = new FakeSession("Brave", MediaControlResult.Succeeded);
        var oldManager = new FakeManager(oldSession);
        var factory = new QueueManagerFactory(
            oldManager,
            new FakeManager(new FakeSession("Chrome", MediaControlResult.Succeeded)));
        var lifecycle = new FakeSystemLifecycle();
        await using var adapter = new GsmtcMediaAdapter(
            factory,
            TimeProvider.System,
            lifecycle,
            [TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero]);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using var snapshots = adapter.WatchAsync(cancellation.Token).GetAsyncEnumerator();
        Assert.True(await snapshots.MoveNextAsync());
        oldSession.BlockNextRead(failAfterRelease: true);
        oldSession.RaiseChanged();
        await oldSession.BlockedReadStarted.WaitAsync(cancellation.Token);

        lifecycle.Suspend();
        oldSession.ReleaseBlockedRead();

        Assert.True(await snapshots.MoveNextAsync());
        Assert.Equal(MediaSessionCatalogStatus.Suspended, snapshots.Current.Status);
        await Task.Delay(100, cancellation.Token);
        Assert.Equal(1, factory.CallCount);
        Assert.True(oldManager.Disposed);
    }

    [Fact]
    public async Task InitialCatalogAndControlUseTheSameEphemeralSessionKey()
    {
        var session = new FakeSession("Brave", MediaControlResult.Succeeded);
        var manager = new FakeManager(session);
        await using var adapter = new GsmtcMediaAdapter(
            new FakeManagerFactory(manager),
            TimeProvider.System);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using var snapshots = adapter.WatchAsync(cancellation.Token).GetAsyncEnumerator();

        Assert.True(await snapshots.MoveNextAsync());
        var snapshot = snapshots.Current;
        var observed = Assert.Single(snapshot.Sessions);
        var result = await adapter.TryExecuteAsync(
            observed.Key,
            MediaCommand.Next,
            cancellation.Token);

        Assert.Equal("Brave", observed.SourceAppUserModelId);
        Assert.Equal(observed.Key, snapshot.WindowsCurrentSession);
        Assert.Equal(MediaControlResult.Succeeded, result);
        Assert.Equal([MediaCommand.Next], session.Commands);
    }

    [Fact]
    public async Task ExcludedOwnedSessionCannotEnterCatalogOrBecomeCurrent()
    {
        var owned = new FakeSession("MediaLock.Phase11BMirrorProbe.exe", MediaControlResult.Succeeded);
        var target = new FakeSession("Brave", MediaControlResult.Succeeded);
        var manager = new FakeListManager([owned, target], owned);
        await using var adapter = new GsmtcMediaAdapter(
            new FakeManagerFactory(manager),
            TimeProvider.System,
            excludedSourceApplicationIds: ["MediaLock.Phase11BMirrorProbe.exe"]);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using var snapshots = adapter.WatchAsync(cancellation.Token).GetAsyncEnumerator();

        Assert.True(await snapshots.MoveNextAsync());
        var snapshot = snapshots.Current;

        Assert.Equal("Brave", Assert.Single(snapshot.Sessions).SourceAppUserModelId);
        Assert.Null(snapshot.WindowsCurrentSession);
        Assert.Equal(0, owned.ReadCount);
    }

    [Fact]
    public async Task AbsoluteSeekUsesTheSameLiveSessionControlSeam()
    {
        var session = new FakeSession("Brave", MediaControlResult.Succeeded);
        var manager = new FakeManager(session);
        await using var adapter = new GsmtcMediaAdapter(
            new FakeManagerFactory(manager),
            TimeProvider.System);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using var snapshots = adapter.WatchAsync(cancellation.Token).GetAsyncEnumerator();
        Assert.True(await snapshots.MoveNextAsync());
        var target = Assert.Single(snapshots.Current.Sessions).Key;
        var command = MediaCommand.SeekAbsolute(TimeSpan.FromSeconds(75));

        var result = await adapter.TryExecuteAsync(target, command, cancellation.Token);

        Assert.Equal(MediaControlResult.Succeeded, result);
        Assert.Equal([command], session.Commands);
    }

    [Fact]
    public async Task SessionChangePublishesUpdatedSnapshotWithoutChangingLiveKey()
    {
        var session = new FakeSession("Brave", MediaControlResult.Succeeded);
        var manager = new FakeManager(session);
        await using var adapter = new GsmtcMediaAdapter(
            new FakeManagerFactory(manager),
            TimeProvider.System);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using var snapshots = adapter.WatchAsync(cancellation.Token).GetAsyncEnumerator();
        Assert.True(await snapshots.MoveNextAsync());
        var original = Assert.Single(snapshots.Current.Sessions);

        session.PlaybackStatus = PlaybackStatus.Paused;
        session.RaiseChanged();

        Assert.True(await snapshots.MoveNextAsync());
        var updated = Assert.Single(snapshots.Current.Sessions);
        Assert.Equal(original.Key, updated.Key);
        Assert.Equal(PlaybackStatus.Paused, updated.PlaybackStatus);
    }

    [Fact]
    public async Task DistinctCurrentWrapperResolvesUniqueSourceInCatalog()
    {
        var listed = new FakeSession("Brave", MediaControlResult.Succeeded);
        var currentWrapper = new FakeSession("Brave", MediaControlResult.Succeeded);
        var manager = new FakeManager(listed, currentWrapper);
        await using var adapter = new GsmtcMediaAdapter(
            new FakeManagerFactory(manager),
            TimeProvider.System);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using var snapshots = adapter.WatchAsync(cancellation.Token).GetAsyncEnumerator();

        Assert.True(await snapshots.MoveNextAsync());
        var snapshot = snapshots.Current;

        Assert.Equal(Assert.Single(snapshot.Sessions).Key, snapshot.WindowsCurrentSession);
    }

    [Fact]
    public async Task BurstSessionEventsCoalesceIntoABoundedRefreshBacklog()
    {
        var session = new FakeSession("Brave", MediaControlResult.Succeeded);
        var manager = new FakeManager(session);
        await using var adapter = new GsmtcMediaAdapter(
            new FakeManagerFactory(manager),
            TimeProvider.System);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using var snapshots = adapter.WatchAsync(cancellation.Token).GetAsyncEnumerator();
        Assert.True(await snapshots.MoveNextAsync());
        session.BlockNextRead();

        session.RaiseChanged();
        await session.BlockedReadStarted.WaitAsync(cancellation.Token);
        for (var index = 0; index < 100; index++)
        {
            session.RaiseChanged();
        }

        session.ReleaseBlockedRead();
        Assert.True(await snapshots.MoveNextAsync());
        Assert.True(await snapshots.MoveNextAsync());
        await Task.Delay(100, cancellation.Token);

        Assert.Equal(3, session.ReadCount);
    }

    [Fact]
    public async Task DisposalCancelsABlockedEventRefresh()
    {
        var session = new FakeSession("Brave", MediaControlResult.Succeeded);
        var manager = new FakeManager(session);
        var adapter = new GsmtcMediaAdapter(
            new FakeManagerFactory(manager),
            TimeProvider.System);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using var snapshots = adapter.WatchAsync(cancellation.Token).GetAsyncEnumerator();
        Assert.True(await snapshots.MoveNextAsync());
        session.BlockNextRead();
        session.RaiseChanged();
        await session.BlockedReadStarted.WaitAsync(cancellation.Token);

        var disposal = adapter.DisposeAsync().AsTask();
        var completed = await Task.WhenAny(disposal, Task.Delay(100, cancellation.Token));
        if (completed != disposal)
        {
            session.ReleaseBlockedRead();
        }

        await disposal.WaitAsync(cancellation.Token);
        Assert.Same(disposal, completed);
    }

    private sealed class FakeManagerFactory(IGsmtcSessionManager manager) : IGsmtcSessionManagerFactory
    {
        public ValueTask<IGsmtcSessionManager> CreateAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(manager);
    }

    private sealed class QueueManagerFactory(params IGsmtcSessionManager[] managers)
        : IGsmtcSessionManagerFactory
    {
        private readonly Queue<IGsmtcSessionManager> remaining = new(managers);

        public int CallCount { get; private set; }

        public ValueTask<IGsmtcSessionManager> CreateAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            return ValueTask.FromResult(remaining.Dequeue());
        }
    }

    private sealed class ResumeRetryManagerFactory(
        IGsmtcSessionManager initial,
        IGsmtcSessionManager recovered) : IGsmtcSessionManagerFactory
    {
        public int CallCount { get; private set; }

        public ValueTask<IGsmtcSessionManager> CreateAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            return CallCount switch
            {
                1 => ValueTask.FromResult(initial),
                2 or 3 or 4 => ValueTask.FromException<IGsmtcSessionManager>(
                    new InvalidOperationException("manager unavailable")),
                5 => ValueTask.FromResult(recovered),
                _ => throw new InvalidOperationException("Unexpected manager acquisition."),
            };
        }
    }

    private sealed class FakeSystemLifecycle : ISystemLifecycle, IWorkstationLockState
    {
        public bool IsLocked { get; private set; }

        public event Action? Suspending;

        public event Action? Resumed;

        public event Action? Locked;

        public event Action? Unlocked;

        public void Suspend() => Suspending?.Invoke();

        public void Resume() => Resumed?.Invoke();

        public void Lock()
        {
            IsLocked = true;
            Locked?.Invoke();
        }

        public void Unlock()
        {
            IsLocked = false;
            Unlocked?.Invoke();
        }
    }

    private sealed class FakeManager(
        IGsmtcSession session,
        IGsmtcSession? currentSession = null) : IGsmtcSessionManager
    {
        private EventHandler? sessionsChanged;

        public event EventHandler? SessionsChanged
        {
            add => sessionsChanged += value;
            remove => sessionsChanged -= value;
        }

        public bool Disposed { get; private set; }

        public int SubscriberCount => sessionsChanged?.GetInvocationList().Length ?? 0;

        public IReadOnlyList<IGsmtcSession> GetSessions() => [session];

        public IGsmtcSession? GetCurrentSession() => currentSession ?? session;

        public void RaiseSessionsChanged() => sessionsChanged?.Invoke(this, EventArgs.Empty);

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeListManager(
        IReadOnlyList<IGsmtcSession> sessions,
        IGsmtcSession? currentSession) : IGsmtcSessionManager
    {
        public event EventHandler? SessionsChanged
        {
            add { }
            remove { }
        }

        public IReadOnlyList<IGsmtcSession> GetSessions() => sessions;

        public IGsmtcSession? GetCurrentSession() => currentSession;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeSession(
        string source,
        MediaControlResult controlResult) : IGsmtcSession
    {
        private TaskCompletionSource? blockedReadStarted;
        private TaskCompletionSource? releaseBlockedRead;
        private bool failNextRead;
        private bool failAfterBlockedRead;

        public event EventHandler? Changed;

        public List<MediaCommand> Commands { get; } = [];

        public string SourceAppUserModelId => source;

        public PlaybackStatus PlaybackStatus { get; set; } = PlaybackStatus.Playing;

        public int ReadCount { get; private set; }

        public Task BlockedReadStarted => blockedReadStarted?.Task ?? Task.CompletedTask;

        public async ValueTask<MediaSessionSnapshot> ReadAsync(
            SessionKey key,
            DateTimeOffset observedAt,
            CancellationToken cancellationToken)
        {
            ReadCount++;
            if (failNextRead)
            {
                failNextRead = false;
                throw new InvalidOperationException("session read failed");
            }

            if (releaseBlockedRead is not null)
            {
                blockedReadStarted!.TrySetResult();
                await releaseBlockedRead.Task.WaitAsync(cancellationToken);
                releaseBlockedRead = null;
                blockedReadStarted = null;
                if (failAfterBlockedRead)
                {
                    failAfterBlockedRead = false;
                    throw new InvalidOperationException("blocked session read failed");
                }
            }

            return new MediaSessionSnapshot(
                key,
                source,
                PlaybackStatus,
                MediaCommandCapabilities.All,
                observedAt);
        }

        public ValueTask<MediaControlResult> TryExecuteAsync(
            MediaCommand command,
            CancellationToken cancellationToken)
        {
            Commands.Add(command);
            return ValueTask.FromResult(controlResult);
        }

        public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

        public void BlockNextRead(bool failAfterRelease = false)
        {
            blockedReadStarted = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            releaseBlockedRead = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            failAfterBlockedRead = failAfterRelease;
        }

        public void ReleaseBlockedRead() => releaseBlockedRead!.TrySetResult();

        public void FailNextRead() => failNextRead = true;
    }
}
