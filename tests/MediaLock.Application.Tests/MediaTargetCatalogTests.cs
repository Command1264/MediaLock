using System.Collections.Immutable;
using System.Threading.Channels;
using MediaLock.Core.Configuration;
using MediaLock.Core.Media;
using MediaLock.Core.Playback;
using MediaLock.Core.Routing;
using Xunit;

namespace MediaLock.Application.Tests;

public sealed class MediaTargetCatalogTests
{
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    [Fact]
    public async Task SameTitleBrowserTargetsAndUncorrelatedBraveGsmtcRemainDistinct()
    {
        var braveGsmtc = MediaTargetSnapshot.FromGsmtc(Session("brave-gsmtc", "Brave"));
        var first = BrowserTarget("first-page", "Same title");
        var second = BrowserTarget("second-page", "Same title");
        await using var application = await StartAsync(new MediaTargetCatalogSnapshot(
            [braveGsmtc, first, second],
            braveGsmtc.Id,
            []));

        Assert.Equal(3, application.State.Targets.Length);
        Assert.Contains(application.State.Targets, target => target.Id == braveGsmtc.Id);
        Assert.Contains(application.State.Targets, target => target.Id == first.Id);
        Assert.Contains(application.State.Targets, target => target.Id == second.Id);
    }

    [Fact]
    public async Task AuthoritativeCorrelationSuppressesOnlyItsExactGsmtcDuplicate()
    {
        var duplicate = MediaTargetSnapshot.FromGsmtc(Session("duplicate", "Brave"));
        var fallback = MediaTargetSnapshot.FromGsmtc(Session("fallback", "Brave"));
        var direct = BrowserTarget("direct-page", "Same title");
        await using var application = await StartAsync(new MediaTargetCatalogSnapshot(
            [duplicate, fallback, direct],
            fallback.Id,
            [new AuthoritativeMediaTargetCorrelation(direct.Id, duplicate.Id)]));

        Assert.DoesNotContain(application.State.Targets, target => target.Id == duplicate.Id);
        Assert.Contains(application.State.Targets, target => target.Id == fallback.Id);
        Assert.Contains(application.State.Targets, target => target.Id == direct.Id);
    }

    [Fact]
    public async Task ReconciledTargetsRemainAvailableAcrossApplicationDispatches()
    {
        var braveSession = Session("brave-gsmtc", "Brave");
        var braveGsmtc = MediaTargetSnapshot.FromGsmtc(braveSession);
        var direct = BrowserTarget("brave-page", "Same title");
        var catalog = new SingleSnapshotTargetCatalog(new MediaTargetCatalogSnapshot(
            [braveGsmtc, direct],
            braveGsmtc.Id,
            [new AuthoritativeMediaTargetCorrelation(direct.Id, braveGsmtc.Id)]));
        await using var application = new MediaLockApplication(
            catalog,
            new MediaRouter(new SuccessfulTargetController()));
        await application.StartAsync(CancellationToken.None);

        await application.DispatchAsync(
            new ApplicationIntent.UseWindowsAuto(),
            CancellationToken.None);

        var visible = Assert.Single(application.State.Targets);
        Assert.Equal(direct.Id, visible.Id);
    }

    [Fact]
    public async Task PlaybackStateLockCapturesProviderQualifiedActiveTarget()
    {
        var session = Session("playing", "Music");
        var target = MediaTargetSnapshot.FromGsmtc(session);
        await using var application = await StartAsync(new MediaTargetCatalogSnapshot(
            [target],
            target.Id,
            []));

        await application.DispatchAsync(
            new ApplicationIntent.SetPlaybackStateLock(PlaybackStateLockMode.KeepPlaying),
            CancellationToken.None);

        Assert.Equal(target.Id, application.State.PlaybackStateLock.ArmedTarget);
    }

    [Fact]
    public async Task KeepPlayingCorrectsAnExternalPauseOnTheExactBrowserTarget()
    {
        var playing = BrowserTarget("keep-playing-page", "Browser media");
        var catalog = new PublishingTargetCatalog(new MediaTargetCatalogSnapshot(
            [playing],
            null,
            []));
        var controller = new RecordingTargetController();
        await using var application = new MediaLockApplication(
            catalog,
            new MediaRouter(controller));
        await application.StartAsync(CancellationToken.None);
        await application.DispatchAsync(
            new ApplicationIntent.LockTarget(playing.Id),
            CancellationToken.None);

        await application.DispatchAsync(
            new ApplicationIntent.SetPlaybackStateLock(PlaybackStateLockMode.KeepPlaying),
            CancellationToken.None);
        var paused = MediaTargetSnapshot.FromProvider(
            playing.Id,
            playing.Presentation with { PlaybackStatus = PlaybackStatus.Paused });
        await catalog.PublishAsync(new MediaTargetCatalogSnapshot(
            [paused],
            null,
            []));
        await controller.WaitForCommandCountAsync(1);

        Assert.Equal(playing.Id, application.State.PlaybackStateLock.ArmedTarget);
        Assert.Equal([(playing.Id, MediaCommand.Play)], controller.Commands);
    }

    [Fact]
    public async Task MissingArmedBrowserTargetSuspendsWithoutCorrectingAReplacementOrCompetitor()
    {
        var armed = BrowserTarget("armed-page", "Browser media");
        var competitor = MediaTargetSnapshot.FromGsmtc(Session("youtube-music", "YouTube Music"));
        var catalog = new PublishingTargetCatalog(new MediaTargetCatalogSnapshot(
            [armed, competitor],
            competitor.Id,
            []));
        var controller = new RecordingTargetController();
        await using var application = new MediaLockApplication(
            catalog,
            new MediaRouter(controller));
        await application.StartAsync(CancellationToken.None);
        await application.DispatchAsync(
            new ApplicationIntent.LockTarget(armed.Id),
            CancellationToken.None);
        await application.DispatchAsync(
            new ApplicationIntent.SetPlaybackStateLock(PlaybackStateLockMode.KeepPlaying),
            CancellationToken.None);
        var replacement = BrowserTarget("replacement-page", "Browser media");
        await catalog.PublishAsync(new MediaTargetCatalogSnapshot(
            [
                MediaTargetSnapshot.FromProvider(
                    replacement.Id,
                    replacement.Presentation with { PlaybackStatus = PlaybackStatus.Paused }),
                competitor,
            ],
            competitor.Id,
            []));

        await WaitUntilAsync(() =>
            application.State.PlaybackStateLock.Status == PlaybackStateLockStatus.Suspended);

        Assert.Equal(armed.Id, application.State.PlaybackStateLock.ArmedTarget);
        Assert.Null(application.State.Router.ActiveTarget);
        Assert.Empty(controller.Commands);
    }

    [Fact]
    public async Task ThirdDistinctBrowserPauseReleasesKeepPlayingWithoutAThirdCorrection()
    {
        var playing = BrowserTarget("repeated-pause-page", "Browser media");
        var paused = MediaTargetSnapshot.FromProvider(
            playing.Id,
            playing.Presentation with { PlaybackStatus = PlaybackStatus.Paused });
        var catalog = new PublishingTargetCatalog(new MediaTargetCatalogSnapshot(
            [playing],
            null,
            []));
        var controller = new RecordingTargetController();
        await using var application = new MediaLockApplication(
            catalog,
            new MediaRouter(controller));
        await application.StartAsync(CancellationToken.None);
        await application.DispatchAsync(
            new ApplicationIntent.LockTarget(playing.Id),
            CancellationToken.None);
        await application.DispatchAsync(
            new ApplicationIntent.SetPlaybackStateLock(PlaybackStateLockMode.KeepPlaying),
            CancellationToken.None);

        for (var pauseNumber = 1; pauseNumber <= 2; pauseNumber++)
        {
            await catalog.PublishAsync(new MediaTargetCatalogSnapshot([paused], null, []));
            await controller.WaitForCommandCountAsync(pauseNumber);
            await catalog.PublishAsync(new MediaTargetCatalogSnapshot([playing], null, []));
            await WaitUntilAsync(() =>
                application.State.PlaybackStateLock.Status == PlaybackStateLockStatus.Ready);
        }

        await catalog.PublishAsync(new MediaTargetCatalogSnapshot([paused], null, []));
        await WaitUntilAsync(() =>
            application.State.PlaybackStateLock.Status == PlaybackStateLockStatus.Released);

        Assert.Equal(PlaybackStateLockMode.Off, application.State.PlaybackStateLock.Mode);
        Assert.Equal(2, controller.Commands.Count);
        Assert.All(controller.Commands, command =>
        {
            Assert.Equal(playing.Id, command.Target);
            Assert.Equal(MediaCommand.Play, command.Command);
        });
    }

    [Fact]
    public async Task ApplicationLocksAndRoutesAnExactBrowserMediaTarget()
    {
        var browser = BrowserTarget("locked-page", "Browser media");
        var controller = new RecordingTargetController();
        await using var application = new MediaLockApplication(
            new SingleSnapshotTargetCatalog(new MediaTargetCatalogSnapshot(
                [browser],
                null,
                [])),
            new MediaRouter(controller));
        await application.StartAsync(CancellationToken.None);

        await application.DispatchAsync(
            new ApplicationIntent.LockTarget(browser.Id),
            CancellationToken.None);
        var routed = await application.DispatchAsync(
            new ApplicationIntent.Route(MediaCommand.Pause),
            CancellationToken.None);

        Assert.Equal(browser.Id, routed.State.Router.ActiveTarget);
        Assert.Equal(browser.Id, routed.Decision.Target);
        Assert.Equal([(browser.Id, MediaCommand.Pause)], controller.Commands);
    }

    [Fact]
    public async Task RuntimeOnlyBrowserLockNeverPersistsAnInvalidSessionLockDocument()
    {
        var browser = BrowserTarget("runtime-only-page", "Browser media");
        var runtimeState = new RecordingRuntimeStateRepository();
        await using var application = new MediaLockApplication(
            new SingleSnapshotTargetCatalog(new MediaTargetCatalogSnapshot(
                [browser],
                null,
                [])),
            new MediaRouter(new SuccessfulTargetController()),
            settingsRepository: null,
            loginStartupManager: null,
            runtimeStateRepository: runtimeState);
        await application.StartAsync(CancellationToken.None);
        var saveCountAfterStartup = runtimeState.Saved.Count;

        await application.DispatchAsync(
            new ApplicationIntent.LockTarget(browser.Id),
            CancellationToken.None);
        await application.DispatchAsync(
            new ApplicationIntent.Route(MediaCommand.Pause),
            CancellationToken.None);

        Assert.Equal(saveCountAfterStartup, runtimeState.Saved.Count);
        Assert.Null(application.State.Problem);
        Assert.Equal(browser.Id, application.State.Router.ActiveTarget);
    }

    [Fact]
    public async Task ApplicationRevokesOnlyTheExactBrowserTargetAuthorization()
    {
        var browser = BrowserTarget("revoked-page", "Browser media");
        var authorization = new RecordingAuthorizationController();
        await using var application = new MediaLockApplication(
            new SingleSnapshotTargetCatalog(new MediaTargetCatalogSnapshot(
                [browser],
                null,
                [])),
            new MediaRouter(new SuccessfulTargetController()),
            mediaTargetAuthorizationController: authorization);
        await application.StartAsync(CancellationToken.None);

        await application.DispatchAsync(
            new ApplicationIntent.RevokeTargetAuthorization(browser.Id),
            CancellationToken.None);

        Assert.Equal([browser.Id], authorization.RevokedTargets);
    }

    private static MediaSessionSnapshot Session(string key, string source) => new(
        new SessionKey(key),
        source,
        PlaybackStatus.Playing,
        MediaCommandCapabilities.All,
        DateTimeOffset.Parse("2026-08-27T00:00:00Z"));

    private static MediaTargetSnapshot BrowserTarget(string bindingId, string title) =>
        MediaTargetSnapshot.FromBrowserPageBinding(
            bindingId,
            new MediaTargetPresentation(
                "Brave",
                PlaybackStatus.Playing,
                MediaCommandCapabilities.All,
                DateTimeOffset.Parse("2026-08-27T00:00:00Z"),
                new MediaMetadata(title, null, null, null)));

    private static async Task<MediaLockApplication> StartAsync(MediaTargetCatalogSnapshot snapshot)
    {
        var application = new MediaLockApplication(
            new SingleSnapshotTargetCatalog(snapshot),
            new MediaRouter(new SuccessfulTargetController()));
        await application.StartAsync(CancellationToken.None);
        return application;
    }

    private sealed class SingleSnapshotTargetCatalog(MediaTargetCatalogSnapshot snapshot)
        : IMediaTargetCatalog
    {
        public async IAsyncEnumerable<MediaTargetCatalogSnapshot> WatchAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return snapshot;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class PublishingTargetCatalog(MediaTargetCatalogSnapshot initial)
        : IMediaTargetCatalog
    {
        private readonly Channel<MediaTargetCatalogSnapshot> snapshots =
            Channel.CreateUnbounded<MediaTargetCatalogSnapshot>();

        public async IAsyncEnumerable<MediaTargetCatalogSnapshot> WatchAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return initial;
            await foreach (var snapshot in snapshots.Reader.ReadAllAsync(cancellationToken))
            {
                yield return snapshot;
            }
        }

        public ValueTask PublishAsync(MediaTargetCatalogSnapshot snapshot) =>
            snapshots.Writer.WriteAsync(snapshot);

        public ValueTask DisposeAsync()
        {
            snapshots.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SuccessfulTargetController : IMediaTargetController
    {
        public ValueTask<MediaCommandOutcome> TryExecuteAsync(
            MediaTargetId target,
            MediaCommand command,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(MediaCommandOutcome.Succeeded);
    }

    private sealed class RecordingTargetController : IMediaTargetController
    {
        private TaskCompletionSource changed = NewSignal();

        public List<(MediaTargetId Target, MediaCommand Command)> Commands { get; } = [];

        public ValueTask<MediaCommandOutcome> TryExecuteAsync(
            MediaTargetId target,
            MediaCommand command,
            CancellationToken cancellationToken)
        {
            Commands.Add((target, command));
            changed.TrySetResult();
            changed = NewSignal();
            return ValueTask.FromResult(MediaCommandOutcome.Succeeded);
        }

        public async Task WaitForCommandCountAsync(int expected)
        {
            while (Commands.Count < expected)
            {
                await changed.Task.WaitAsync(TimeSpan.FromSeconds(1));
            }
        }

        private static TaskCompletionSource NewSignal() => new(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class RecordingAuthorizationController : IMediaTargetAuthorizationController
    {
        public List<MediaTargetId> RevokedTargets { get; } = [];

        public ValueTask<bool> RevokeAsync(
            MediaTargetId target,
            CancellationToken cancellationToken)
        {
            RevokedTargets.Add(target);
            return ValueTask.FromResult(true);
        }
    }

    private sealed class RecordingRuntimeStateRepository : IRuntimeStateRepository
    {
        public List<RuntimeStateDocument> Saved { get; } = [];

        public ValueTask<ConfigurationLoadResult<RuntimeStateDocument>> LoadAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new ConfigurationLoadResult<RuntimeStateDocument>(
                new RuntimeStateDocument(
                    RuntimeStateDocument.CurrentSchemaVersion,
                    RoutingMode.WindowsAuto,
                    LockedTarget: null),
                UsedDefaults: true,
                Issues: []));

        public ValueTask SaveAsync(
            RuntimeStateDocument state,
            CancellationToken cancellationToken)
        {
            Saved.Add(state);
            return ValueTask.CompletedTask;
        }
    }
}
