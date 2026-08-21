using System.Collections.Immutable;
using MediaLock.Core.Media;
using MediaLock.Core.Routing;

namespace MediaLock.Core.Tests;

public sealed class MediaRouterTests
{
    [Fact]
    public async Task WindowsAutoRoutesToCurrentSessionAtCommandTime()
    {
        var controller = new RecordingMediaController(MediaControlResult.Succeeded);
        await using var router = new MediaRouter(controller);
        var current = Session("current", "browser");
        var other = Session("other", "music");

        await router.DispatchAsync(
            new RouterIntent.CatalogUpdated([other, current], current.Key),
            CancellationToken.None);

        var result = await router.DispatchAsync(
            new RouterIntent.Route(MediaCommand.TogglePlayPause),
            CancellationToken.None);

        Assert.Equal(RouteDecisionKind.Routed, result.Decision.Kind);
        Assert.Equal(current.Key, result.Decision.Target);
        Assert.Equal(RouteReason.WindowsCurrentSession, result.Decision.Reason);
        Assert.Equal(MediaControlResult.Succeeded, result.Decision.ControlResult);
    }

    [Fact]
    public async Task SessionLockRoutesToLockedSessionWhenWindowsCurrentChanges()
    {
        var controller = new RecordingMediaController(MediaControlResult.Succeeded);
        await using var router = new MediaRouter(controller);
        var locked = Session("locked", "music");
        var competing = Session("competing", "browser");

        await router.DispatchAsync(
            new RouterIntent.CatalogUpdated([locked, competing], locked.Key),
            CancellationToken.None);
        await router.DispatchAsync(new RouterIntent.LockSession(locked.Key), CancellationToken.None);
        await router.DispatchAsync(
            new RouterIntent.CatalogUpdated([locked, competing], competing.Key),
            CancellationToken.None);

        var result = await router.DispatchAsync(
            new RouterIntent.Route(MediaCommand.Next),
            CancellationToken.None);

        Assert.Equal(RoutingMode.SessionLock, result.State.Mode);
        Assert.Equal(RouterStatus.Locked, result.State.Status);
        Assert.Equal(locked.Key, result.Decision.Target);
        Assert.Equal(RouteReason.LockedSession, result.Decision.Reason);
    }

    [Fact]
    public async Task SessionLockRecoversAUniqueFingerprintSuccessor()
    {
        var controller = new RecordingMediaController(MediaControlResult.Succeeded);
        await using var router = new MediaRouter(controller);
        var original = Session("original", "music", "pwa");
        var replacement = Session("replacement", "music", "pwa");

        await router.DispatchAsync(
            new RouterIntent.CatalogUpdated([original], original.Key),
            CancellationToken.None);
        await router.DispatchAsync(new RouterIntent.LockSession(original.Key), CancellationToken.None);

        var lost = await router.DispatchAsync(
            new RouterIntent.CatalogUpdated([], null),
            CancellationToken.None);
        var recovered = await router.DispatchAsync(
            new RouterIntent.CatalogUpdated([replacement], replacement.Key),
            CancellationToken.None);
        var routed = await router.DispatchAsync(
            new RouterIntent.Route(MediaCommand.Play),
            CancellationToken.None);

        Assert.Equal(RouterStatus.Recovering, lost.State.Status);
        Assert.Null(lost.State.LockedTarget!.ResolvedSession);
        Assert.Equal(RouterStatus.Locked, recovered.State.Status);
        Assert.Equal(replacement.Key, recovered.State.LockedTarget!.ResolvedSession);
        Assert.Equal(replacement.Key, routed.Decision.Target);
    }

    [Fact]
    public async Task AppLockUsesDeterministicCandidatePolicy()
    {
        var controller = new RecordingMediaController(MediaControlResult.Succeeded);
        await using var router = new MediaRouter(controller);
        var paused = Session(
            "paused",
            "browser",
            observedAt: DateTimeOffset.Parse("2026-08-22T00:05:00Z"));
        var playing = Session(
            "playing",
            "browser",
            playbackStatus: PlaybackStatus.Playing,
            observedAt: DateTimeOffset.Parse("2026-08-22T00:00:00Z"));
        var competing = Session("competing", "other");

        await router.DispatchAsync(
            new RouterIntent.CatalogUpdated([paused, competing, playing], competing.Key),
            CancellationToken.None);
        await router.DispatchAsync(
            new RouterIntent.LockApplication("browser"),
            CancellationToken.None);

        var result = await router.DispatchAsync(
            new RouterIntent.Route(MediaCommand.TogglePlayPause),
            CancellationToken.None);

        Assert.Equal(RoutingMode.AppLock, result.State.Mode);
        Assert.Equal(playing.Key, result.Decision.Target);
        Assert.Equal(RouteReason.LockedApplication, result.Decision.Reason);
    }

    [Fact]
    public async Task RecoveryTimeoutCanFallBackToWindowsCurrentSession()
    {
        var controller = new RecordingMediaController(MediaControlResult.Succeeded);
        await using var router = new MediaRouter(
            controller,
            new RouterOptions(FallbackPolicy.WindowsCurrentSession));
        var locked = Session("locked", "music", "pwa");
        var current = Session("current", "browser");

        await router.DispatchAsync(
            new RouterIntent.CatalogUpdated([locked, current], locked.Key),
            CancellationToken.None);
        await router.DispatchAsync(new RouterIntent.LockSession(locked.Key), CancellationToken.None);
        var lost = await router.DispatchAsync(
            new RouterIntent.CatalogUpdated([current], current.Key),
            CancellationToken.None);

        var fallback = await router.DispatchAsync(
            new RouterIntent.RecoveryTimedOut(lost.State.RecoveryEpoch!.Value),
            CancellationToken.None);
        var routed = await router.DispatchAsync(
            new RouterIntent.Route(MediaCommand.Play),
            CancellationToken.None);

        Assert.Equal(RouterStatus.Fallback, fallback.State.Status);
        Assert.Equal(current.Key, routed.Decision.Target);
        Assert.Equal(RouteReason.FallbackWindowsCurrentSession, routed.Decision.Reason);
    }

    [Fact]
    public async Task UseWindowsAutoForgetsLockedTargetAndRoutesCurrentSession()
    {
        var controller = new RecordingMediaController(MediaControlResult.Succeeded);
        await using var router = new MediaRouter(controller);
        var locked = Session("locked", "music");
        var current = Session("current", "browser");

        await router.DispatchAsync(
            new RouterIntent.CatalogUpdated([locked, current], current.Key),
            CancellationToken.None);
        await router.DispatchAsync(new RouterIntent.LockSession(locked.Key), CancellationToken.None);

        var unlocked = await router.DispatchAsync(
            new RouterIntent.UseWindowsAuto(),
            CancellationToken.None);
        var routed = await router.DispatchAsync(
            new RouterIntent.Route(MediaCommand.Stop),
            CancellationToken.None);

        Assert.Equal(RoutingMode.WindowsAuto, unlocked.State.Mode);
        Assert.Equal(RouterStatus.Ready, unlocked.State.Status);
        Assert.Null(unlocked.State.LockedTarget);
        Assert.Equal(current.Key, routed.Decision.Target);
        Assert.Equal(RouteReason.WindowsCurrentSession, routed.Decision.Reason);
    }

    [Fact]
    public async Task ConcurrentIntentsExecuteWithoutOverlap()
    {
        var controller = new BlockingMediaController();
        await using var router = new MediaRouter(controller);
        var current = Session("current", "browser");
        await router.DispatchAsync(
            new RouterIntent.CatalogUpdated([current], current.Key),
            CancellationToken.None);

        var first = router.DispatchAsync(
            new RouterIntent.Route(MediaCommand.Play),
            CancellationToken.None).AsTask();
        await controller.FirstEntered;
        var second = router.DispatchAsync(
            new RouterIntent.Route(MediaCommand.Pause),
            CancellationToken.None).AsTask();

        controller.ReleaseFirst();
        var results = await Task.WhenAll(first, second);

        Assert.All(results, result => Assert.Equal(RouteDecisionKind.Routed, result.Decision.Kind));
        Assert.Equal(1, controller.MaximumConcurrency);
        Assert.Equal([MediaCommand.Play, MediaCommand.Pause], controller.Commands);
    }

    [Fact]
    public async Task QueuedCancellationCompletesPromptlyWithoutStoppingLaterIntents()
    {
        var controller = new BlockingMediaController();
        await using var router = new MediaRouter(controller);
        var current = Session("current", "browser");
        await router.DispatchAsync(
            new RouterIntent.CatalogUpdated([current], current.Key),
            CancellationToken.None);

        var first = router.DispatchAsync(
            new RouterIntent.Route(MediaCommand.Play),
            CancellationToken.None).AsTask();
        await controller.FirstEntered;
        using var cancellation = new CancellationTokenSource();
        var canceled = router.DispatchAsync(
            new RouterIntent.Route(MediaCommand.Pause),
            cancellation.Token).AsTask();

        cancellation.Cancel();

        Assert.True(canceled.IsCanceled);
        controller.ReleaseFirst();
        await first;
        var later = await router.DispatchAsync(
            new RouterIntent.Route(MediaCommand.Stop),
            CancellationToken.None);
        Assert.Equal(RouteDecisionKind.Routed, later.Decision.Kind);
    }

    [Fact]
    public async Task ControlFailureIsObservableAsFailedDecision()
    {
        var controller = new RecordingMediaController(MediaControlResult.Failed);
        await using var router = new MediaRouter(controller);
        var current = Session("current", "browser");
        await router.DispatchAsync(
            new RouterIntent.CatalogUpdated([current], current.Key),
            CancellationToken.None);

        var result = await router.DispatchAsync(
            new RouterIntent.Route(MediaCommand.Play),
            CancellationToken.None);

        Assert.Equal(RouteDecisionKind.Failed, result.Decision.Kind);
        Assert.Equal(RouteReason.ControlFailed, result.Decision.Reason);
        Assert.Equal(MediaControlResult.Failed, result.Decision.ControlResult);
    }

    [Fact]
    public async Task RecoveringLockSkipsWithExplicitReason()
    {
        var controller = new RecordingMediaController(MediaControlResult.Succeeded);
        await using var router = new MediaRouter(controller);
        var locked = Session("locked", "music", "pwa");
        await router.DispatchAsync(
            new RouterIntent.CatalogUpdated([locked], locked.Key),
            CancellationToken.None);
        await router.DispatchAsync(new RouterIntent.LockSession(locked.Key), CancellationToken.None);
        await router.DispatchAsync(
            new RouterIntent.CatalogUpdated([], null),
            CancellationToken.None);

        var result = await router.DispatchAsync(
            new RouterIntent.Route(MediaCommand.Next),
            CancellationToken.None);

        Assert.Equal(RouteDecisionKind.Skipped, result.Decision.Kind);
        Assert.Equal(RouteReason.LockedTargetRecovering, result.Decision.Reason);
        Assert.Null(result.Decision.Target);
    }

    [Fact]
    public async Task RecoveryTimeoutCanUseDeterministicSameApplicationFallback()
    {
        var controller = new RecordingMediaController(MediaControlResult.Succeeded);
        await using var router = new MediaRouter(
            controller,
            new RouterOptions(FallbackPolicy.SameApplication));
        var locked = Session("locked", "browser", "music-tab");
        var alternative = Session("alternative", "browser", "video-tab");
        await router.DispatchAsync(
            new RouterIntent.CatalogUpdated([locked], locked.Key),
            CancellationToken.None);
        await router.DispatchAsync(new RouterIntent.LockSession(locked.Key), CancellationToken.None);
        var lost = await router.DispatchAsync(
            new RouterIntent.CatalogUpdated([alternative], alternative.Key),
            CancellationToken.None);

        var fallback = await router.DispatchAsync(
            new RouterIntent.RecoveryTimedOut(lost.State.RecoveryEpoch!.Value),
            CancellationToken.None);
        var routed = await router.DispatchAsync(
            new RouterIntent.Route(MediaCommand.Play),
            CancellationToken.None);

        Assert.Equal(RouterStatus.Fallback, fallback.State.Status);
        Assert.Equal(alternative.Key, routed.Decision.Target);
        Assert.Equal(RouteReason.FallbackSameApplication, routed.Decision.Reason);
    }

    [Fact]
    public void UnknownFallbackPolicyIsRejectedAtComposition()
    {
        var controller = new RecordingMediaController(MediaControlResult.Succeeded);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MediaRouter(controller, new RouterOptions((FallbackPolicy)99)));

        Assert.Equal("options", exception.ParamName);
    }

    [Fact]
    public void NullControllerIsRejectedAtComposition()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new MediaRouter(null!));

        Assert.Equal("controller", exception.ParamName);
    }

    [Fact]
    public async Task CatalogRejectsNullSessionEntry()
    {
        await using var router = new MediaRouter(
            new RecordingMediaController(MediaControlResult.Succeeded));
        var sessions = ImmutableArray<MediaSessionSnapshot>.Empty.Add(null!);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            router.DispatchAsync(
                new RouterIntent.CatalogUpdated(sessions, null),
                CancellationToken.None).AsTask());

        Assert.Equal("catalog", exception.ParamName);
        Assert.Contains("null Session", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CatalogRejectsBlankSessionIdentity()
    {
        await using var router = new MediaRouter(
            new RecordingMediaController(MediaControlResult.Succeeded));
        var invalid = Session("duplicate", " ");

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            router.DispatchAsync(
                new RouterIntent.CatalogUpdated([invalid], invalid.Key),
                CancellationToken.None).AsTask());

        Assert.Equal("catalog", exception.ParamName);
        Assert.Contains("source application ID", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CatalogRejectsDuplicateSessionKey()
    {
        await using var router = new MediaRouter(
            new RecordingMediaController(MediaControlResult.Succeeded));
        var first = Session("duplicate", "browser");
        var second = Session("duplicate", "music");

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            router.DispatchAsync(
                new RouterIntent.CatalogUpdated([first, second], first.Key),
                CancellationToken.None).AsTask());

        Assert.Equal("catalog", exception.ParamName);
        Assert.Contains("duplicated", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CatalogRejectsWindowsCurrentSessionOutsideCatalog()
    {
        await using var router = new MediaRouter(
            new RecordingMediaController(MediaControlResult.Succeeded));
        var session = Session("known", "browser");

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            router.DispatchAsync(
                new RouterIntent.CatalogUpdated([session], new SessionKey("missing")),
                CancellationToken.None).AsTask());

        Assert.Equal("catalog", exception.ParamName);
        Assert.Contains("Windows Current Session", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReusedSessionKeyDoesNotBypassFingerprintMatching()
    {
        var controller = new RecordingMediaController(MediaControlResult.Succeeded);
        await using var router = new MediaRouter(controller);
        var original = Session("reused", "music", "pwa");
        var unrelated = Session("reused", "browser", "video");
        await router.DispatchAsync(
            new RouterIntent.CatalogUpdated([original], original.Key),
            CancellationToken.None);
        await router.DispatchAsync(new RouterIntent.LockSession(original.Key), CancellationToken.None);

        var result = await router.DispatchAsync(
            new RouterIntent.CatalogUpdated([unrelated], unrelated.Key),
            CancellationToken.None);

        Assert.Equal(RouterStatus.Recovering, result.State.Status);
        Assert.Null(result.State.LockedTarget!.ResolvedSession);
    }

    [Fact]
    public async Task AmbiguousFingerprintSuccessorsRemainRecovering()
    {
        var controller = new RecordingMediaController(MediaControlResult.Succeeded);
        await using var router = new MediaRouter(controller);
        var original = Session("original", "music", "pwa");
        var first = Session("first", "music", "pwa");
        var second = Session("second", "music", "pwa");
        await router.DispatchAsync(
            new RouterIntent.CatalogUpdated([original], original.Key),
            CancellationToken.None);
        await router.DispatchAsync(new RouterIntent.LockSession(original.Key), CancellationToken.None);

        var result = await router.DispatchAsync(
            new RouterIntent.CatalogUpdated([first, second], first.Key),
            CancellationToken.None);

        Assert.Equal(RouterStatus.Recovering, result.State.Status);
        Assert.Null(result.State.LockedTarget!.ResolvedSession);
    }

    [Theory]
    [InlineData(FallbackPolicy.Wait, RouteReason.LockedTargetUnavailable)]
    [InlineData(FallbackPolicy.DisableRouting, RouteReason.RoutingDisabled)]
    public async Task NonRoutingFallbacksReturnExplicitReason(
        FallbackPolicy fallbackPolicy,
        RouteReason expectedReason)
    {
        var controller = new RecordingMediaController(MediaControlResult.Succeeded);
        await using var router = new MediaRouter(controller, new RouterOptions(fallbackPolicy));
        var locked = Session("locked", "music");
        await router.DispatchAsync(
            new RouterIntent.CatalogUpdated([locked], locked.Key),
            CancellationToken.None);
        await router.DispatchAsync(new RouterIntent.LockSession(locked.Key), CancellationToken.None);
        var lost = await router.DispatchAsync(
            new RouterIntent.CatalogUpdated([], null),
            CancellationToken.None);
        await router.DispatchAsync(
            new RouterIntent.RecoveryTimedOut(lost.State.RecoveryEpoch!.Value),
            CancellationToken.None);

        var routed = await router.DispatchAsync(
            new RouterIntent.Route(MediaCommand.Play),
            CancellationToken.None);

        Assert.Equal(RouteDecisionKind.Skipped, routed.Decision.Kind);
        Assert.Equal(expectedReason, routed.Decision.Reason);
    }

    [Fact]
    public async Task StaleRecoveryTimeoutDoesNotOverrideRecoveredTarget()
    {
        var controller = new RecordingMediaController(MediaControlResult.Succeeded);
        await using var router = new MediaRouter(
            controller,
            new RouterOptions(FallbackPolicy.WindowsCurrentSession));
        var original = Session("original", "music", "pwa");
        var replacement = Session("replacement", "music", "pwa");
        await router.DispatchAsync(
            new RouterIntent.CatalogUpdated([original], original.Key),
            CancellationToken.None);
        await router.DispatchAsync(new RouterIntent.LockSession(original.Key), CancellationToken.None);
        var lost = await router.DispatchAsync(
            new RouterIntent.CatalogUpdated([], null),
            CancellationToken.None);
        await router.DispatchAsync(
            new RouterIntent.CatalogUpdated([replacement], replacement.Key),
            CancellationToken.None);

        var staleTimeout = await router.DispatchAsync(
            new RouterIntent.RecoveryTimedOut(lost.State.RecoveryEpoch!.Value),
            CancellationToken.None);

        Assert.Equal(RouterStatus.Locked, staleTimeout.State.Status);
        Assert.Null(staleTimeout.State.ActiveFallback);
        Assert.Equal(replacement.Key, staleTimeout.State.LockedTarget!.ResolvedSession);
    }

    [Fact]
    public async Task CatalogRefreshDuringRecoveryDoesNotInvalidateDeadline()
    {
        var controller = new RecordingMediaController(MediaControlResult.Succeeded);
        await using var router = new MediaRouter(controller);
        var locked = Session("locked", "music", "pwa");
        var unrelated = Session("unrelated", "browser");
        await router.DispatchAsync(
            new RouterIntent.CatalogUpdated([locked], locked.Key),
            CancellationToken.None);
        await router.DispatchAsync(new RouterIntent.LockSession(locked.Key), CancellationToken.None);
        var lost = await router.DispatchAsync(
            new RouterIntent.CatalogUpdated([], null),
            CancellationToken.None);
        await router.DispatchAsync(
            new RouterIntent.CatalogUpdated([unrelated], unrelated.Key),
            CancellationToken.None);

        var timedOut = await router.DispatchAsync(
            new RouterIntent.RecoveryTimedOut(lost.State.RecoveryEpoch!.Value),
            CancellationToken.None);

        Assert.Equal(RouterStatus.Fallback, timedOut.State.Status);
        Assert.Null(timedOut.State.RecoveryEpoch);
    }

    [Fact]
    public async Task AppLockFallbackSurvivesUnrelatedCatalogRefresh()
    {
        var controller = new RecordingMediaController(MediaControlResult.Succeeded);
        await using var router = new MediaRouter(
            controller,
            new RouterOptions(FallbackPolicy.WindowsCurrentSession));
        var locked = Session("locked", "music");
        var current = Session("current", "browser");
        await router.DispatchAsync(
            new RouterIntent.CatalogUpdated([locked, current], locked.Key),
            CancellationToken.None);
        await router.DispatchAsync(
            new RouterIntent.LockApplication("music"),
            CancellationToken.None);
        var lost = await router.DispatchAsync(
            new RouterIntent.CatalogUpdated([current], current.Key),
            CancellationToken.None);
        await router.DispatchAsync(
            new RouterIntent.RecoveryTimedOut(lost.State.RecoveryEpoch!.Value),
            CancellationToken.None);
        var refreshedCurrent = current with
        {
            ObservedAt = current.ObservedAt.AddSeconds(1),
        };

        var refreshed = await router.DispatchAsync(
            new RouterIntent.CatalogUpdated([refreshedCurrent], refreshedCurrent.Key),
            CancellationToken.None);

        Assert.Equal(RouterStatus.Fallback, refreshed.State.Status);
        Assert.Equal(FallbackPolicy.WindowsCurrentSession, refreshed.State.ActiveFallback);
    }

    [Fact]
    public async Task RecoveryRanksCandidatesUsingObservedCharacteristicsAndTime()
    {
        var controller = new RecordingMediaController(MediaControlResult.Succeeded);
        await using var router = new MediaRouter(controller);
        var observedAt = DateTimeOffset.Parse("2026-08-22T00:00:00Z");
        var original = Session(
            "original",
            "browser",
            observedAt: observedAt,
            metadata: new MediaMetadata("title", "artist", null, null));
        var lessLikely = Session(
            "less-likely",
            "browser",
            observedAt: observedAt.AddMinutes(10),
            metadata: new MediaMetadata("different", "artist", null, null));
        var likely = Session(
            "likely",
            "browser",
            observedAt: observedAt.AddSeconds(30),
            metadata: new MediaMetadata("title", "artist", null, null));
        await router.DispatchAsync(
            new RouterIntent.CatalogUpdated([original], original.Key),
            CancellationToken.None);
        await router.DispatchAsync(new RouterIntent.LockSession(original.Key), CancellationToken.None);

        var recovered = await router.DispatchAsync(
            new RouterIntent.CatalogUpdated([lessLikely, likely], likely.Key),
            CancellationToken.None);

        Assert.Equal(RouterStatus.Locked, recovered.State.Status);
        Assert.Equal(likely.Key, recovered.State.LockedTarget!.ResolvedSession);
    }

    [Fact]
    public async Task SameSourceWithoutAcceptableConfidenceWaitsForFallback()
    {
        var controller = new RecordingMediaController(MediaControlResult.Succeeded);
        await using var router = new MediaRouter(controller);
        var original = Session("original", "browser");
        var unrelatedSameSource = Session(
            "other-tab",
            "browser",
            observedAt: original.ObservedAt.AddHours(1));
        await router.DispatchAsync(
            new RouterIntent.CatalogUpdated([original], original.Key),
            CancellationToken.None);
        await router.DispatchAsync(new RouterIntent.LockSession(original.Key), CancellationToken.None);

        var result = await router.DispatchAsync(
            new RouterIntent.CatalogUpdated([unrelatedSameSource], unrelatedSameSource.Key),
            CancellationToken.None);

        Assert.Equal(RouterStatus.Recovering, result.State.Status);
        Assert.Null(result.State.LockedTarget!.ResolvedSession);
    }

    [Fact]
    public async Task UnsupportedCommandIsSkippedBeforeTheControllerSeam()
    {
        var controller = new ThrowingMediaController();
        await using var router = new MediaRouter(controller);
        var current = Session(
            "current",
            "browser",
            capabilities: MediaCommandCapabilities.Play);
        await router.DispatchAsync(
            new RouterIntent.CatalogUpdated([current], current.Key),
            CancellationToken.None);

        var result = await router.DispatchAsync(
            new RouterIntent.Route(MediaCommand.Stop),
            CancellationToken.None);

        Assert.Equal(RouteDecisionKind.Skipped, result.Decision.Kind);
        Assert.Equal(RouteReason.UnsupportedCommand, result.Decision.Reason);
    }

    [Fact]
    public async Task ControllerExceptionBecomesFailedDecisionAndQueueContinues()
    {
        var controller = new FailOnceMediaController();
        await using var router = new MediaRouter(controller);
        var current = Session("current", "browser");
        await router.DispatchAsync(
            new RouterIntent.CatalogUpdated([current], current.Key),
            CancellationToken.None);

        var failed = await router.DispatchAsync(
            new RouterIntent.Route(MediaCommand.Play),
            CancellationToken.None);
        var later = await router.DispatchAsync(
            new RouterIntent.Route(MediaCommand.Pause),
            CancellationToken.None);

        Assert.Equal(RouteDecisionKind.Failed, failed.Decision.Kind);
        Assert.Equal(RouteReason.ControlFailed, failed.Decision.Reason);
        Assert.Equal("controller unavailable", failed.Decision.Error);
        Assert.Equal(RouteDecisionKind.Routed, later.Decision.Kind);
    }

    [Fact]
    public async Task IdenticalCatalogRefreshIsIdempotent()
    {
        var controller = new RecordingMediaController(MediaControlResult.Succeeded);
        await using var router = new MediaRouter(controller);
        var current = Session("current", "browser");
        var first = await router.DispatchAsync(
            new RouterIntent.CatalogUpdated([current], current.Key),
            CancellationToken.None);

        var second = await router.DispatchAsync(
            new RouterIntent.CatalogUpdated([current], current.Key),
            CancellationToken.None);

        Assert.Equal(first.State, second.State);
        Assert.Equal(first.State.Revision, second.State.Revision);
    }

    [Fact]
    public async Task DefaultFallbackPrefersSameApplicationBeforeWindowsCurrent()
    {
        var controller = new RecordingMediaController(MediaControlResult.Succeeded);
        await using var router = new MediaRouter(controller);
        var locked = Session("locked", "music", "primary");
        var sameApplication = Session("same-app", "music", "secondary");
        var current = Session("current", "browser");
        await router.DispatchAsync(
            new RouterIntent.CatalogUpdated([locked, current], locked.Key),
            CancellationToken.None);
        await router.DispatchAsync(new RouterIntent.LockSession(locked.Key), CancellationToken.None);
        var lost = await router.DispatchAsync(
            new RouterIntent.CatalogUpdated([current, sameApplication], current.Key),
            CancellationToken.None);

        await router.DispatchAsync(
            new RouterIntent.RecoveryTimedOut(lost.State.RecoveryEpoch!.Value),
            CancellationToken.None);
        var routed = await router.DispatchAsync(
            new RouterIntent.Route(MediaCommand.Play),
            CancellationToken.None);

        Assert.Equal(sameApplication.Key, routed.Decision.Target);
        Assert.Equal(RouteReason.FallbackSameApplication, routed.Decision.Reason);
    }

    [Fact]
    public async Task DefaultFallbackUsesWindowsCurrentWhenSameApplicationIsUnavailable()
    {
        var controller = new RecordingMediaController(MediaControlResult.Succeeded);
        await using var router = new MediaRouter(controller);
        var locked = Session("locked", "music", "primary");
        var current = Session("current", "browser");
        await router.DispatchAsync(
            new RouterIntent.CatalogUpdated([locked, current], locked.Key),
            CancellationToken.None);
        await router.DispatchAsync(new RouterIntent.LockSession(locked.Key), CancellationToken.None);
        var lost = await router.DispatchAsync(
            new RouterIntent.CatalogUpdated([current], current.Key),
            CancellationToken.None);

        await router.DispatchAsync(
            new RouterIntent.RecoveryTimedOut(lost.State.RecoveryEpoch!.Value),
            CancellationToken.None);
        var routed = await router.DispatchAsync(
            new RouterIntent.Route(MediaCommand.Play),
            CancellationToken.None);

        Assert.Equal(current.Key, routed.Decision.Target);
        Assert.Equal(RouteReason.FallbackWindowsCurrentSession, routed.Decision.Reason);
    }

    private static MediaSessionSnapshot Session(
        string key,
        string source,
        string? instanceHint = null,
        PlaybackStatus playbackStatus = PlaybackStatus.Paused,
        DateTimeOffset? observedAt = null,
        MediaCommandCapabilities capabilities = MediaCommandCapabilities.All,
        MediaMetadata? metadata = null) => new(
        new SessionKey(key),
        source,
        playbackStatus,
        capabilities,
        observedAt ?? DateTimeOffset.Parse("2026-08-22T00:00:00Z"),
        instanceHint,
        metadata);

    private sealed class RecordingMediaController(MediaControlResult result) : IMediaController
    {
        public ValueTask<MediaControlResult> TryExecuteAsync(
            SessionKey target,
            MediaCommand command,
            CancellationToken cancellationToken) => ValueTask.FromResult(result);
    }

    private sealed class BlockingMediaController : IMediaController
    {
        private readonly TaskCompletionSource firstEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource releaseFirst = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int invocation;
        private int running;

        public List<MediaCommand> Commands { get; } = [];

        public Task FirstEntered => firstEntered.Task;

        public int MaximumConcurrency { get; private set; }

        public void ReleaseFirst() => releaseFirst.TrySetResult();

        public async ValueTask<MediaControlResult> TryExecuteAsync(
            SessionKey target,
            MediaCommand command,
            CancellationToken cancellationToken)
        {
            var currentRunning = Interlocked.Increment(ref running);
            MaximumConcurrency = Math.Max(MaximumConcurrency, currentRunning);
            Commands.Add(command);
            try
            {
                if (Interlocked.Increment(ref invocation) == 1)
                {
                    firstEntered.TrySetResult();
                    await releaseFirst.Task.WaitAsync(cancellationToken);
                }

                return MediaControlResult.Succeeded;
            }
            finally
            {
                Interlocked.Decrement(ref running);
            }
        }
    }

    private sealed class ThrowingMediaController : IMediaController
    {
        public ValueTask<MediaControlResult> TryExecuteAsync(
            SessionKey target,
            MediaCommand command,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The controller seam should not be invoked.");
    }

    private sealed class FailOnceMediaController : IMediaController
    {
        private int calls;

        public ValueTask<MediaControlResult> TryExecuteAsync(
            SessionKey target,
            MediaCommand command,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                throw new InvalidOperationException("controller unavailable");
            }

            return ValueTask.FromResult(MediaControlResult.Succeeded);
        }
    }
}
