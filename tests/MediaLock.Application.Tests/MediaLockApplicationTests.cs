using System.Collections.Immutable;
using System.Threading.Channels;
using MediaLock.Application;
using MediaLock.Core.Media;
using MediaLock.Core.Routing;
using Xunit;

namespace MediaLock.Application.Tests;

public sealed class MediaLockApplicationTests
{
    [Fact]
    public async Task CatalogSnapshotBecomesObservableApplicationState()
    {
        var session = Session("music", "Brave");
        var catalog = new InMemoryCatalog(
            new MediaSessionCatalogSnapshot([session], session.Key));
        var router = new MediaRouter(new SuccessfulController());
        await using var application = new MediaLockApplication(catalog, router);
        var observed = new List<MediaLockApplicationState>();
        application.StateChanged += (_, args) => observed.Add(args.State);

        await application.StartAsync(CancellationToken.None);

        Assert.Equal(session, Assert.Single(application.State.Router.Sessions));
        Assert.Equal(session.Key, application.State.Router.WindowsCurrentSession);
        Assert.Contains(application.State, observed);
    }

    [Fact]
    public async Task UiIntentLocksAndRoutesThroughTheApplicationSeam()
    {
        var session = Session("music", "Brave");
        var catalog = new InMemoryCatalog(
            new MediaSessionCatalogSnapshot([session], session.Key));
        var controller = new RecordingController();
        var router = new MediaRouter(controller);
        await using var application = new MediaLockApplication(catalog, router);
        await application.StartAsync(CancellationToken.None);

        var locked = await application.DispatchAsync(
            new ApplicationIntent.LockSession(session.Key),
            CancellationToken.None);
        var routed = await application.DispatchAsync(
            new ApplicationIntent.Route(MediaCommand.Next),
            CancellationToken.None);

        Assert.Equal(RoutingMode.SessionLock, locked.State.Router.Mode);
        Assert.Equal(RouterStatus.Locked, locked.State.Router.Status);
        Assert.Equal(RouteDecisionKind.Routed, routed.Decision.Kind);
        Assert.Equal([(session.Key, MediaCommand.Next)], controller.Commands);
    }

    [Fact]
    public async Task RecoveryDeadlineEffectAppliesFallbackWithoutUiCoordination()
    {
        var locked = Session("music", "Brave");
        var current = Session("video", "Chrome");
        var catalog = new InMemoryCatalog(
            new MediaSessionCatalogSnapshot([locked, current], locked.Key));
        var router = new MediaRouter(
            new SuccessfulController(),
            new RouterOptions(
                FallbackPolicy.WindowsCurrentSession,
                TimeSpan.Zero));
        await using var application = new MediaLockApplication(catalog, router);
        await application.StartAsync(CancellationToken.None);
        await application.DispatchAsync(
            new ApplicationIntent.LockSession(locked.Key),
            CancellationToken.None);
        var fallbackObserved = new TaskCompletionSource<MediaLockApplicationState>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        application.StateChanged += (_, args) =>
        {
            if (args.State.Router.Status == RouterStatus.Fallback)
            {
                fallbackObserved.TrySetResult(args.State);
            }
        };

        await catalog.PublishAsync(
            new MediaSessionCatalogSnapshot([current], current.Key));
        var fallback = await fallbackObserved.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(FallbackPolicy.WindowsCurrentSession, fallback.Router.ActiveFallback);
    }

    [Fact]
    public async Task UnexpectedCatalogCompletionBecomesObservableErrorState()
    {
        var session = Session("music", "Brave");
        var catalog = new InMemoryCatalog(
            new MediaSessionCatalogSnapshot([session], session.Key));
        var controller = new RecordingController();
        var router = new MediaRouter(controller);
        await using var application = new MediaLockApplication(catalog, router);
        await application.StartAsync(CancellationToken.None);
        await application.DispatchAsync(
            new ApplicationIntent.LockSession(session.Key),
            CancellationToken.None);
        var errorObserved = new TaskCompletionSource<MediaLockApplicationState>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        application.StateChanged += (_, args) =>
        {
            if (args.State.ErrorMessage is not null)
            {
                errorObserved.TrySetResult(args.State);
            }
        };

        catalog.Complete();
        var failed = await errorObserved.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal("Media Session catalog stopped unexpectedly.", failed.ErrorMessage);
        Assert.Empty(failed.Router.Sessions);
        Assert.Equal(RouterStatus.Recovering, failed.Router.Status);
        var routed = await application.DispatchAsync(
            new ApplicationIntent.Route(MediaCommand.Next),
            CancellationToken.None);
        Assert.Equal(RouteReason.LockedTargetRecovering, routed.Decision.Reason);
        Assert.Empty(controller.Commands);
    }

    [Fact]
    public async Task ConcurrentApplicationIntentsPublishRouterResultsInOrder()
    {
        var session = Session("music", "Brave");
        var catalog = new InMemoryCatalog(
            new MediaSessionCatalogSnapshot([session], session.Key));
        var router = new ControllableRouter();
        await using var application = new MediaLockApplication(catalog, router);
        await application.StartAsync(CancellationToken.None);

        var first = application.DispatchAsync(
            new ApplicationIntent.UseWindowsAuto(),
            CancellationToken.None).AsTask();
        await router.WaitForCallCountAsync(2);
        var second = application.DispatchAsync(
            new ApplicationIntent.UseWindowsAuto(),
            CancellationToken.None).AsTask();

        if (await router.TryWaitForCallCountAsync(3, TimeSpan.FromMilliseconds(100)))
        {
            router.CompleteCall(2, revision: 3);
            await second;
            router.CompleteCall(1, revision: 2);
        }
        else
        {
            router.CompleteCall(1, revision: 2);
            await router.WaitForCallCountAsync(3);
            router.CompleteCall(2, revision: 3);
        }

        await Task.WhenAll(first, second);

        Assert.Equal(3, application.State.Router.Revision);
    }

    [Fact]
    public async Task DisposalCancelsAnInFlightUiRouteBeforeDisposingTheRouter()
    {
        var session = Session("music", "Brave");
        var catalog = new InMemoryCatalog(
            new MediaSessionCatalogSnapshot([session], session.Key));
        var controller = new BlockingController();
        var application = new MediaLockApplication(catalog, new MediaRouter(controller));
        await application.StartAsync(CancellationToken.None);
        var route = application.DispatchAsync(
            new ApplicationIntent.Route(MediaCommand.Next),
            CancellationToken.None).AsTask();
        await controller.Started.WaitAsync(TimeSpan.FromSeconds(1));

        await application.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => route);
        Assert.True(controller.CancellationObserved);
    }

    private static MediaSessionSnapshot Session(string key, string source) => new(
        new SessionKey(key),
        source,
        PlaybackStatus.Playing,
        MediaCommandCapabilities.All,
        DateTimeOffset.Parse("2026-08-22T00:00:00Z"));

    private sealed class InMemoryCatalog(MediaSessionCatalogSnapshot initial) : IMediaSessionCatalog
    {
        private readonly Channel<MediaSessionCatalogSnapshot> snapshots =
            Channel.CreateUnbounded<MediaSessionCatalogSnapshot>();

        public async IAsyncEnumerable<MediaSessionCatalogSnapshot> WatchAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return initial;
            await foreach (var snapshot in snapshots.Reader.ReadAllAsync(cancellationToken))
            {
                yield return snapshot;
            }
        }

        public ValueTask PublishAsync(MediaSessionCatalogSnapshot snapshot) =>
            snapshots.Writer.WriteAsync(snapshot);

        public void Complete() => snapshots.Writer.TryComplete();

        public ValueTask DisposeAsync()
        {
            snapshots.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SuccessfulController : IMediaController
    {
        public ValueTask<MediaControlResult> TryExecuteAsync(
            SessionKey target,
            MediaCommand command,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(MediaControlResult.Succeeded);
    }

    private sealed class RecordingController : IMediaController
    {
        public List<(SessionKey Target, MediaCommand Command)> Commands { get; } = [];

        public ValueTask<MediaControlResult> TryExecuteAsync(
            SessionKey target,
            MediaCommand command,
            CancellationToken cancellationToken)
        {
            Commands.Add((target, command));
            return ValueTask.FromResult(MediaControlResult.Succeeded);
        }
    }

    private sealed class ControllableRouter : IMediaRouter
    {
        private readonly List<TaskCompletionSource<RouterResult>> calls = [];
        private readonly object sync = new();
        private TaskCompletionSource callCountChanged = NewSignal();

        public ValueTask<RouterResult> DispatchAsync(
            RouterIntent intent,
            CancellationToken cancellationToken)
        {
            lock (sync)
            {
                if (calls.Count == 0)
                {
                    calls.Add(new TaskCompletionSource<RouterResult>());
                    return ValueTask.FromResult(Result(revision: 1));
                }

                var completion = new TaskCompletionSource<RouterResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                calls.Add(completion);
                callCountChanged.TrySetResult();
                callCountChanged = NewSignal();
                return new ValueTask<RouterResult>(completion.Task.WaitAsync(cancellationToken));
            }
        }

        public async Task WaitForCallCountAsync(int expected)
        {
            while (true)
            {
                Task signal;
                lock (sync)
                {
                    if (calls.Count >= expected)
                    {
                        return;
                    }

                    signal = callCountChanged.Task;
                }

                await signal.WaitAsync(TimeSpan.FromSeconds(1));
            }
        }

        public async Task<bool> TryWaitForCallCountAsync(int expected, TimeSpan timeout)
        {
            try
            {
                await WaitForCallCountAsync(expected).WaitAsync(timeout);
                return true;
            }
            catch (TimeoutException)
            {
                return false;
            }
        }

        public void CompleteCall(int controlledCallIndex, long revision)
        {
            TaskCompletionSource<RouterResult> completion;
            lock (sync)
            {
                completion = calls[controlledCallIndex];
            }

            completion.SetResult(Result(revision));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private static RouterResult Result(long revision) => new(
            RouterState.Initial with { Revision = revision },
            RouteDecision.StateUpdated);

        private static TaskCompletionSource NewSignal() => new(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class BlockingController : IMediaController
    {
        private readonly TaskCompletionSource started = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => started.Task;

        public bool CancellationObserved { get; private set; }

        public async ValueTask<MediaControlResult> TryExecuteAsync(
            SessionKey target,
            MediaCommand command,
            CancellationToken cancellationToken)
        {
            started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return MediaControlResult.Succeeded;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CancellationObserved = true;
                throw;
            }
        }
    }
}
