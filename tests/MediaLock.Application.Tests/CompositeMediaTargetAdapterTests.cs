using System.Collections.Immutable;
using MediaLock.Core.Media;
using Xunit;

namespace MediaLock.Application.Tests;

public sealed class CompositeMediaTargetAdapterTests
{
    [Fact]
    public async Task PrimaryOnlyCompositionPreservesTheGsmtcSnapshotAndController()
    {
        var session = Session("gsmtc", "Music");
        var snapshot = new MediaTargetCatalogSnapshot(
            [MediaTargetSnapshot.FromGsmtc(session)],
            MediaTargetId.FromGsmtc(session.Key),
            []);
        var primary = new SingleSnapshotAdapter(snapshot);
        await using var composite = new CompositeMediaTargetAdapter(
            new MediaTargetAdapterRegistration(
                MediaTargetProviderId.Gsmtc,
                primary,
                primary));

        var observed = await FirstAsync(composite);
        var outcome = await composite.TryExecuteAsync(
            MediaTargetId.FromGsmtc(session.Key),
            MediaCommand.Pause,
            CancellationToken.None);

        var observedTarget = Assert.Single(observed.ObservedTargets);
        Assert.Equal(MediaTargetId.FromGsmtc(session.Key), observedTarget.Id);
        Assert.Equal(session, observedTarget.GsmtcSession);
        Assert.Equal(snapshot.WindowsCurrentTarget, observed.WindowsCurrentTarget);
        Assert.Equal(snapshot.Status, observed.Status);
        Assert.Equal(snapshot.StatusMessage, observed.StatusMessage);
        Assert.Equal(MediaCommandOutcome.Succeeded, outcome);
        Assert.Equal(
            [(MediaTargetId.FromGsmtc(session.Key), MediaCommand.Pause)],
            primary.Commands);
    }

    [Fact]
    public async Task OptionalBrowserProviderAddsItsTargetsWithoutReplacingPrimaryStatus()
    {
        var session = Session("gsmtc", "Brave");
        var gsmtc = MediaTargetSnapshot.FromGsmtc(session);
        var browser = MediaTargetSnapshot.FromBrowserPageBinding(
            "page-binding",
            new MediaTargetPresentation(
                "Video — Brave profile",
                PlaybackStatus.Playing,
                MediaCommandCapabilities.Play | MediaCommandCapabilities.Pause,
                DateTimeOffset.Parse("2026-08-27T00:00:00Z")));
        var primary = new SingleSnapshotAdapter(new MediaTargetCatalogSnapshot(
            [gsmtc],
            gsmtc.Id,
            [],
            MediaSessionCatalogStatus.Available));
        var optional = new SingleSnapshotAdapter(new MediaTargetCatalogSnapshot(
            [browser],
            null,
            [],
            MediaSessionCatalogStatus.Unavailable,
            "Optional provider status must stay local."));
        await using var composite = new CompositeMediaTargetAdapter(
            new MediaTargetAdapterRegistration(
                MediaTargetProviderId.Gsmtc,
                primary,
                primary),
            new MediaTargetAdapterRegistration(
                MediaTargetProviderId.Browser,
                optional,
                optional));

        var observed = await FirstAsync(
            composite,
            snapshot => snapshot.ObservedTargets.Length == 2);

        Assert.Equal(MediaSessionCatalogStatus.Available, observed.Status);
        Assert.Null(observed.StatusMessage);
        Assert.Contains(observed.Targets, target => target.Id == gsmtc.Id);
        Assert.Contains(observed.Targets, target => target.Id == browser.Id);
    }

    private static async Task<MediaTargetCatalogSnapshot> FirstAsync(
        IMediaTargetCatalog catalog,
        Func<MediaTargetCatalogSnapshot, bool>? predicate = null)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await foreach (var snapshot in catalog.WatchAsync(timeout.Token))
        {
            if (predicate?.Invoke(snapshot) ?? true)
            {
                return snapshot;
            }
        }

        throw new InvalidOperationException("The composite catalog completed before the expected snapshot.");
    }

    private static MediaSessionSnapshot Session(string key, string source) => new(
        new SessionKey(key),
        source,
        PlaybackStatus.Playing,
        MediaCommandCapabilities.All,
        DateTimeOffset.Parse("2026-08-27T00:00:00Z"));

    private sealed class SingleSnapshotAdapter(MediaTargetCatalogSnapshot snapshot)
        : IMediaTargetCatalog, IMediaTargetController
    {
        public List<(MediaTargetId Target, MediaCommand Command)> Commands { get; } = [];

        public async IAsyncEnumerable<MediaTargetCatalogSnapshot> WatchAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return snapshot;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        public ValueTask<MediaCommandOutcome> TryExecuteAsync(
            MediaTargetId target,
            MediaCommand command,
            CancellationToken cancellationToken)
        {
            Commands.Add((target, command));
            return ValueTask.FromResult(MediaCommandOutcome.Succeeded);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
