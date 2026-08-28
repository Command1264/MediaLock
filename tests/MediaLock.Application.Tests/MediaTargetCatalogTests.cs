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
    public async Task CatalogProjectionEstimatesAMissingRateFromMonotonicObservations()
    {
        var target = BrowserTimelineTarget("estimated-page", positionSeconds: 10);
        var catalog = new PublishingTargetCatalog(new MediaTargetCatalogSnapshot(
            [target],
            null,
            []));
        var clock = new ManualTimeProvider();
        await using var application = new MediaLockApplication(
            catalog,
            new MediaRouter(new SuccessfulTargetController()),
            settingsRepository: null,
            loginStartupManager: null,
            timeProvider: clock);
        await application.StartAsync(CancellationToken.None);

        clock.Advance(TimeSpan.FromSeconds(2));
        await catalog.PublishAsync(new MediaTargetCatalogSnapshot(
            [BrowserTimelineTarget("estimated-page", positionSeconds: 14)],
            null,
            []));
        await WaitUntilAsync(() => Assert.Single(application.State.Targets)
            .Presentation.Timeline?.Position == TimeSpan.FromSeconds(14));
        clock.Advance(TimeSpan.FromSeconds(2));
        await catalog.PublishAsync(new MediaTargetCatalogSnapshot(
            [BrowserTimelineTarget("estimated-page", positionSeconds: 18)],
            null,
            []));

        await WaitUntilAsync(() => Assert.Single(application.State.Targets)
            .Presentation.PlaybackRate.Source == PlaybackRateResolutionSource.Estimated);
        var projected = Assert.Single(application.State.Targets).Presentation.PlaybackRate;
        Assert.Equal(2d, projected.Rate, precision: 6);
    }

    [Fact]
    public async Task SameTitleGsmtcAndBrowserRatesAreEstimatedIndependently()
    {
        var catalog = new PublishingTargetCatalog(new MediaTargetCatalogSnapshot(
            [
                GsmtcTimelineTarget("same-title", positionSeconds: 10),
                BrowserTimelineTarget("same-title", positionSeconds: 50),
            ],
            null,
            []));
        var clock = new ManualTimeProvider();
        await using var application = new MediaLockApplication(
            catalog,
            new MediaRouter(new SuccessfulTargetController()),
            settingsRepository: null,
            loginStartupManager: null,
            timeProvider: clock);
        await application.StartAsync(CancellationToken.None);

        clock.Advance(TimeSpan.FromSeconds(2));
        await catalog.PublishAsync(new MediaTargetCatalogSnapshot(
            [
                GsmtcTimelineTarget("same-title", positionSeconds: 12),
                BrowserTimelineTarget("same-title", positionSeconds: 54),
            ],
            null,
            []));
        await WaitUntilAsync(() => application.State.Targets.All(target =>
            target.Presentation.Timeline?.Position is var position &&
            position is not null &&
            position != TimeSpan.FromSeconds(10) &&
            position != TimeSpan.FromSeconds(50)));
        clock.Advance(TimeSpan.FromSeconds(2));
        await catalog.PublishAsync(new MediaTargetCatalogSnapshot(
            [
                GsmtcTimelineTarget("same-title", positionSeconds: 14),
                BrowserTimelineTarget("same-title", positionSeconds: 58),
            ],
            null,
            []));

        await WaitUntilAsync(() => application.State.Targets.All(target =>
            target.Presentation.PlaybackRate.Source == PlaybackRateResolutionSource.Estimated));
        var gsmtc = Assert.Single(application.State.Targets, target =>
            target.Id.Provider == MediaTargetProviderId.Gsmtc);
        var browser = Assert.Single(application.State.Targets, target =>
            target.Id.Provider == MediaTargetProviderId.Browser);
        Assert.Equal(1d, gsmtc.Presentation.PlaybackRate.Rate, precision: 6);
        Assert.Equal(2d, browser.Presentation.PlaybackRate.Rate, precision: 6);
    }

    [Fact]
    public async Task CachedProviderTargetIsNotResampledWhenOnlyAnotherProviderChanges()
    {
        var cachedGsmtc = GsmtcTimelineTarget("cached", positionSeconds: 10);
        var changingBrowser = BrowserTimelineTarget("changing", positionSeconds: 50);
        var catalog = new PublishingTargetCatalog(new MediaTargetCatalogSnapshot(
            [cachedGsmtc, changingBrowser],
            cachedGsmtc.Id,
            []));
        var clock = new ManualTimeProvider();
        await using var application = new MediaLockApplication(
            catalog,
            new MediaRouter(new SuccessfulTargetController()),
            settingsRepository: null,
            loginStartupManager: null,
            timeProvider: clock);
        await application.StartAsync(CancellationToken.None);
        var originalAnchor = Assert.Single(
            application.State.Targets,
            target => target.Id == cachedGsmtc.Id).Presentation.MonotonicObservedAt;

        clock.Advance(TimeSpan.FromSeconds(2));
        await catalog.PublishAsync(new MediaTargetCatalogSnapshot(
            [cachedGsmtc, BrowserTimelineTarget("changing", positionSeconds: 54)],
            cachedGsmtc.Id,
            []));
        await WaitUntilAsync(() => Assert.Single(
            application.State.Targets,
            target => target.Id == changingBrowser.Id).Presentation.Timeline?.Position ==
            TimeSpan.FromSeconds(54));

        var cachedProjection = Assert.Single(
            application.State.Targets,
            target => target.Id == cachedGsmtc.Id).Presentation;
        Assert.Equal(originalAnchor, cachedProjection.MonotonicObservedAt);
    }

    [Fact]
    public async Task SilentProviderEstimateExpiresToFallbackWithoutMovingPresentationBackward()
    {
        var estimated = BrowserTimelineTarget("silent", positionSeconds: 10);
        var competitor = GsmtcTimelineTarget("competitor", positionSeconds: 50);
        var catalog = new PublishingTargetCatalog(new MediaTargetCatalogSnapshot(
            [estimated, competitor], competitor.Id, []));
        var clock = new ManualTimeProvider();
        await using var application = new MediaLockApplication(
            catalog,
            new MediaRouter(new SuccessfulTargetController()),
            settingsRepository: null,
            loginStartupManager: null,
            timeProvider: clock);
        await application.StartAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(2));
        estimated = BrowserTimelineTarget("silent", positionSeconds: 14);
        await catalog.PublishAsync(new MediaTargetCatalogSnapshot(
            [estimated, competitor], competitor.Id, []));
        await WaitUntilAsync(() => Assert.Single(application.State.Targets, target =>
            target.Id == estimated.Id).Presentation.Timeline?.Position == TimeSpan.FromSeconds(14));
        clock.Advance(TimeSpan.FromSeconds(2));
        estimated = BrowserTimelineTarget("silent", positionSeconds: 18);
        await catalog.PublishAsync(new MediaTargetCatalogSnapshot(
            [estimated, competitor], competitor.Id, []));
        await WaitUntilAsync(() => Assert.Single(application.State.Targets, target =>
            target.Id == estimated.Id).Presentation.PlaybackRate.Source ==
            PlaybackRateResolutionSource.Estimated);

        clock.Advance(TimeSpan.FromSeconds(6));
        await WaitUntilAsync(() => Assert.Single(application.State.Targets, target =>
            target.Id == estimated.Id).Presentation.PlaybackRate.Source ==
            PlaybackRateResolutionSource.Fallback);
        var expired = Assert.Single(application.State.Targets, target =>
            target.Id == estimated.Id).Presentation;
        Assert.Equal(TimeSpan.FromSeconds(30), expired.Timeline?.Position);
        Assert.NotNull(expired.MonotonicObservedAt);

        clock.Advance(TimeSpan.FromSeconds(1));
        estimated = BrowserTimelineTarget("silent", positionSeconds: 20);
        await catalog.PublishAsync(new MediaTargetCatalogSnapshot(
            [estimated, GsmtcTimelineTarget("competitor", positionSeconds: 57)],
            competitor.Id,
            []));
        await WaitUntilAsync(() => Assert.Single(application.State.Targets, target =>
            target.Id == estimated.Id).Presentation.Timeline?.Position >=
            TimeSpan.FromSeconds(31));

        var resumed = Assert.Single(application.State.Targets, target =>
            target.Id == estimated.Id).Presentation;
        Assert.Equal(PlaybackRateResolutionSource.Fallback, resumed.PlaybackRate.Source);
        Assert.True(resumed.Timeline?.Position >= TimeSpan.FromSeconds(31));
    }

    [Fact]
    public async Task ConfidenceWorkerFailureIsReportedAndDoesNotBreakDisposal()
    {
        var catalog = new PublishingTargetCatalog(new MediaTargetCatalogSnapshot(
            [BrowserTimelineTarget("worker-failure", positionSeconds: 10)], null, []));
        var clock = new ManualTimeProvider();
        var application = new MediaLockApplication(
            catalog,
            new FailingAfterDispatchRouter(successfulDispatchCount: 3),
            settingsRepository: null,
            loginStartupManager: null,
            timeProvider: clock);
        await application.StartAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(2));
        await catalog.PublishAsync(new MediaTargetCatalogSnapshot(
            [BrowserTimelineTarget("worker-failure", positionSeconds: 14)], null, []));
        await WaitUntilAsync(() => Assert.Single(application.State.Targets)
            .Presentation.Timeline?.Position == TimeSpan.FromSeconds(14));
        clock.Advance(TimeSpan.FromSeconds(2));
        await catalog.PublishAsync(new MediaTargetCatalogSnapshot(
            [BrowserTimelineTarget("worker-failure", positionSeconds: 18)], null, []));
        await WaitUntilAsync(() => Assert.Single(application.State.Targets)
            .Presentation.PlaybackRate.Source == PlaybackRateResolutionSource.Estimated);

        clock.Advance(TimeSpan.FromSeconds(6));

        await WaitUntilAsync(() => application.State.Problem?.Id ==
            MediaLockProblemId.ApplicationOperationFailed);
        await application.DisposeAsync();
    }

    [Fact]
    public async Task PureGsmtcCatalogKeepsProjectedPlaybackRateInRouterState()
    {
        var catalog = new PublishingTargetCatalog(new MediaTargetCatalogSnapshot(
            [GsmtcTimelineTarget("gsmtc-only", positionSeconds: 10)],
            MediaTargetId.FromGsmtc(new SessionKey("gsmtc-only")),
            []));
        var clock = new ManualTimeProvider();
        await using var application = new MediaLockApplication(
            catalog,
            new MediaRouter(new SuccessfulTargetController()),
            settingsRepository: null,
            loginStartupManager: null,
            timeProvider: clock);
        await application.StartAsync(CancellationToken.None);

        clock.Advance(TimeSpan.FromSeconds(2));
        await catalog.PublishAsync(new MediaTargetCatalogSnapshot(
            [GsmtcTimelineTarget("gsmtc-only", positionSeconds: 14)],
            MediaTargetId.FromGsmtc(new SessionKey("gsmtc-only")),
            []));
        await WaitUntilAsync(() => Assert.Single(application.State.Targets)
            .Presentation.Timeline?.Position == TimeSpan.FromSeconds(14));
        clock.Advance(TimeSpan.FromSeconds(2));
        await catalog.PublishAsync(new MediaTargetCatalogSnapshot(
            [GsmtcTimelineTarget("gsmtc-only", positionSeconds: 18)],
            MediaTargetId.FromGsmtc(new SessionKey("gsmtc-only")),
            []));

        await WaitUntilAsync(() => Assert.Single(application.State.Router.Targets)
            .Presentation.PlaybackRate.Source == PlaybackRateResolutionSource.Estimated);
        var presentation = Assert.Single(application.State.Router.Targets).Presentation;
        Assert.Equal(2d, presentation.PlaybackRate.Rate, precision: 6);
        Assert.NotNull(presentation.MonotonicObservedAt);
    }

    [Fact]
    public async Task RoutedSeekDiscardsThePreviousPlaybackRateEstimate()
    {
        var target = BrowserTimelineTarget("seek-reset", positionSeconds: 10);
        var catalog = new PublishingTargetCatalog(new MediaTargetCatalogSnapshot([target], null, []));
        var clock = new ManualTimeProvider();
        await using var application = new MediaLockApplication(
            catalog,
            new MediaRouter(new SuccessfulTargetController()),
            settingsRepository: null,
            loginStartupManager: null,
            timeProvider: clock);
        await application.StartAsync(CancellationToken.None);
        await application.DispatchAsync(new ApplicationIntent.LockTarget(target.Id), CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(2));
        await catalog.PublishAsync(new MediaTargetCatalogSnapshot(
            [BrowserTimelineTarget("seek-reset", positionSeconds: 14)], null, []));
        await WaitUntilAsync(() => Assert.Single(application.State.Targets)
            .Presentation.Timeline?.Position == TimeSpan.FromSeconds(14));
        clock.Advance(TimeSpan.FromSeconds(2));
        await catalog.PublishAsync(new MediaTargetCatalogSnapshot(
            [BrowserTimelineTarget("seek-reset", positionSeconds: 18)], null, []));
        await WaitUntilAsync(() => Assert.Single(application.State.Targets)
            .Presentation.PlaybackRate.Source == PlaybackRateResolutionSource.Estimated);

        await application.DispatchAsync(
            new ApplicationIntent.Route(
                MediaCommand.SeekAbsolute(TimeSpan.FromSeconds(30)),
                target.Id),
            CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(1));
        await catalog.PublishAsync(new MediaTargetCatalogSnapshot(
            [BrowserTimelineTarget("seek-reset", positionSeconds: 30)], null, []));

        await WaitUntilAsync(() => Assert.Single(application.State.Targets)
            .Presentation.Timeline?.Position == TimeSpan.FromSeconds(30));
        Assert.Equal(
            PlaybackRateResolutionSource.Fallback,
            Assert.Single(application.State.Targets).Presentation.PlaybackRate.Source);
    }

    [Fact]
    public async Task RecoveryAndReconnectionRequireANewObservationWindow()
    {
        var catalog = new PublishingTargetCatalog(new MediaTargetCatalogSnapshot(
            [BrowserTimelineTarget("reconnect", positionSeconds: 10)], null, []));
        var clock = new ManualTimeProvider();
        await using var application = new MediaLockApplication(
            catalog,
            new MediaRouter(new SuccessfulTargetController()),
            settingsRepository: null,
            loginStartupManager: null,
            timeProvider: clock);
        await application.StartAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(2));
        await catalog.PublishAsync(new MediaTargetCatalogSnapshot(
            [BrowserTimelineTarget("reconnect", positionSeconds: 14)], null, []));
        await WaitUntilAsync(() => Assert.Single(application.State.Targets)
            .Presentation.Timeline?.Position == TimeSpan.FromSeconds(14));
        clock.Advance(TimeSpan.FromSeconds(2));
        await catalog.PublishAsync(new MediaTargetCatalogSnapshot(
            [BrowserTimelineTarget("reconnect", positionSeconds: 18)], null, []));
        await WaitUntilAsync(() => Assert.Single(application.State.Targets)
            .Presentation.PlaybackRate.Source == PlaybackRateResolutionSource.Estimated);

        await catalog.PublishAsync(new MediaTargetCatalogSnapshot(
            [BrowserTimelineTarget("reconnect", positionSeconds: 18)],
            null,
            [],
            MediaSessionCatalogStatus.Reacquiring));
        await WaitUntilAsync(() => application.State.CatalogStatus ==
            MediaSessionCatalogStatus.Reacquiring);
        clock.Advance(TimeSpan.FromSeconds(1));
        await catalog.PublishAsync(new MediaTargetCatalogSnapshot(
            [BrowserTimelineTarget("reconnect", positionSeconds: 20)], null, []));

        await WaitUntilAsync(() => application.State.CatalogStatus ==
            MediaSessionCatalogStatus.Available);
        Assert.Equal(
            PlaybackRateResolutionSource.Fallback,
            Assert.Single(application.State.Targets).Presentation.PlaybackRate.Source);
    }

    [Fact]
    public async Task DocumentIdentityChangeRequiresANewObservationWindow()
    {
        var initial = BrowserTimelineTarget("document", positionSeconds: 10);
        var catalog = new PublishingTargetCatalog(new MediaTargetCatalogSnapshot([initial], null, []));
        var clock = new ManualTimeProvider();
        await using var application = new MediaLockApplication(
            catalog,
            new MediaRouter(new SuccessfulTargetController()),
            settingsRepository: null,
            loginStartupManager: null,
            timeProvider: clock);
        await application.StartAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(2));
        await catalog.PublishAsync(new MediaTargetCatalogSnapshot(
            [BrowserTimelineTarget("document", positionSeconds: 14)], null, []));
        await WaitUntilAsync(() => Assert.Single(application.State.Targets)
            .Presentation.Timeline?.Position == TimeSpan.FromSeconds(14));
        clock.Advance(TimeSpan.FromSeconds(2));
        await catalog.PublishAsync(new MediaTargetCatalogSnapshot(
            [BrowserTimelineTarget("document", positionSeconds: 18)], null, []));
        await WaitUntilAsync(() => Assert.Single(application.State.Targets)
            .Presentation.PlaybackRate.Source == PlaybackRateResolutionSource.Estimated);

        var replacement = MediaTargetSnapshot.FromProvider(
            initial.Id,
            BrowserTimelineTarget("document", positionSeconds: 20).Presentation with
            {
                Metadata = new MediaMetadata("Replacement document", null, null, null),
            });
        clock.Advance(TimeSpan.FromSeconds(1));
        await catalog.PublishAsync(new MediaTargetCatalogSnapshot([replacement], null, []));

        await WaitUntilAsync(() => Assert.Single(application.State.Targets)
            .Presentation.Metadata?.Title == "Replacement document");
        Assert.Equal(
            PlaybackRateResolutionSource.Fallback,
            Assert.Single(application.State.Targets).Presentation.PlaybackRate.Source);
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

    private static MediaTargetSnapshot BrowserTimelineTarget(
        string bindingId,
        double positionSeconds) => MediaTargetSnapshot.FromBrowserPageBinding(
            bindingId,
            new MediaTargetPresentation(
                "Brave",
                PlaybackStatus.Playing,
                MediaCommandCapabilities.All,
                DateTimeOffset.Parse("2026-08-28T00:00:00Z"),
                Metadata: new MediaMetadata("Same title", null, null, null),
                Timeline: new MediaTimeline(
                    TimeSpan.Zero,
                    TimeSpan.FromMinutes(10),
                    TimeSpan.FromSeconds(positionSeconds),
                    DateTimeOffset.Parse("2026-08-28T00:00:00Z"))));

    private static MediaTargetSnapshot GsmtcTimelineTarget(
        string key,
        double positionSeconds) => MediaTargetSnapshot.FromGsmtc(new MediaSessionSnapshot(
            new SessionKey(key),
            "Brave",
            PlaybackStatus.Playing,
            MediaCommandCapabilities.All,
            DateTimeOffset.Parse("2026-08-28T00:00:00Z"),
            Metadata: new MediaMetadata("Same title", null, null, null),
            Timeline: new MediaTimeline(
                TimeSpan.Zero,
                TimeSpan.FromMinutes(10),
                TimeSpan.FromSeconds(positionSeconds),
                DateTimeOffset.Parse("2026-08-28T00:00:00Z"))));

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

    private sealed class FailingAfterDispatchRouter(int successfulDispatchCount) : IMediaRouter
    {
        private readonly MediaRouter inner = new(new SuccessfulTargetController());
        private int dispatchCount;

        public ValueTask<RouterResult> DispatchAsync(
            RouterIntent intent,
            CancellationToken cancellationToken) =>
            Interlocked.Increment(ref dispatchCount) > successfulDispatchCount
                ? ValueTask.FromException<RouterResult>(
                    new InvalidOperationException("Injected confidence worker failure."))
                : inner.DispatchAsync(intent, cancellationToken);

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long timestamp;
        private readonly List<ManualTimer> timers = [];

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => timestamp;

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state, dueTime, period);
            timers.Add(timer);
            return timer;
        }

        public void Advance(TimeSpan amount)
        {
            timestamp += amount.Ticks;
            foreach (var timer in timers.ToArray())
            {
                timer.FireDue(timestamp);
            }
        }

        private sealed class ManualTimer : ITimer
        {
            private readonly ManualTimeProvider owner;
            private readonly TimerCallback callback;
            private readonly object? state;
            private long dueTimestamp;
            private long periodTicks;
            private bool disposed;

            public ManualTimer(
                ManualTimeProvider owner,
                TimerCallback callback,
                object? state,
                TimeSpan dueTime,
                TimeSpan period)
            {
                this.owner = owner;
                this.callback = callback;
                this.state = state;
                Change(dueTime, period);
            }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (disposed)
                {
                    return false;
                }

                dueTimestamp = dueTime == Timeout.InfiniteTimeSpan
                    ? long.MaxValue
                    : owner.timestamp + dueTime.Ticks;
                periodTicks = period == Timeout.InfiniteTimeSpan ? 0 : period.Ticks;
                return true;
            }

            public void Dispose() => disposed = true;

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }

            public void FireDue(long now)
            {
                if (disposed || now < dueTimestamp)
                {
                    return;
                }

                dueTimestamp = periodTicks > 0 ? now + periodTicks : long.MaxValue;
                callback(state);
            }
        }
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
