using MediaLock.Core.Media;
using MediaLock.Windows.Gsmtc;
using Xunit;

namespace MediaLock.Windows.Tests;

public sealed class GsmtcMediaAdapterTests
{
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

    private sealed class FakeManager(
        IGsmtcSession session,
        IGsmtcSession? currentSession = null) : IGsmtcSessionManager
    {
        public event EventHandler? SessionsChanged;

        public IReadOnlyList<IGsmtcSession> GetSessions() => [session];

        public IGsmtcSession? GetCurrentSession() => currentSession ?? session;

        public void RaiseSessionsChanged() => SessionsChanged?.Invoke(this, EventArgs.Empty);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeSession(
        string source,
        MediaControlResult controlResult) : IGsmtcSession
    {
        private TaskCompletionSource? blockedReadStarted;
        private TaskCompletionSource? releaseBlockedRead;

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
            if (releaseBlockedRead is not null)
            {
                blockedReadStarted!.TrySetResult();
                await releaseBlockedRead.Task.WaitAsync(cancellationToken);
                releaseBlockedRead = null;
                blockedReadStarted = null;
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

        public void BlockNextRead()
        {
            blockedReadStarted = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            releaseBlockedRead = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public void ReleaseBlockedRead() => releaseBlockedRead!.TrySetResult();
    }
}
