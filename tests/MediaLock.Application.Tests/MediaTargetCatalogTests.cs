using System.Collections.Immutable;
using MediaLock.Core.Media;
using MediaLock.Core.Playback;
using MediaLock.Core.Routing;
using Xunit;

namespace MediaLock.Application.Tests;

public sealed class MediaTargetCatalogTests
{
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

    private sealed class SuccessfulTargetController : IMediaTargetController
    {
        public ValueTask<MediaCommandOutcome> TryExecuteAsync(
            MediaTargetId target,
            MediaCommand command,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(MediaCommandOutcome.Succeeded);
    }
}
