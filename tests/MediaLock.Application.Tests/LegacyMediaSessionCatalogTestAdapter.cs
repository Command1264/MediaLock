using System.Collections.Immutable;
using MediaLock.Core.Media;

namespace MediaLock.Application.Tests;

internal sealed record MediaSessionCatalogSnapshot(
    ImmutableArray<MediaSessionSnapshot> Sessions,
    SessionKey? WindowsCurrentSession,
    MediaSessionCatalogStatus Status = MediaSessionCatalogStatus.Available,
    string? StatusMessage = null);

internal interface IMediaSessionCatalog : IMediaTargetCatalog
{
    new IAsyncEnumerable<MediaSessionCatalogSnapshot> WatchAsync(
        CancellationToken cancellationToken);

    async IAsyncEnumerable<MediaTargetCatalogSnapshot> IMediaTargetCatalog.WatchAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var snapshot in WatchAsync(cancellationToken))
        {
            yield return new MediaTargetCatalogSnapshot(
                snapshot.Sessions.Select(MediaTargetSnapshot.FromGsmtc).ToImmutableArray(),
                snapshot.WindowsCurrentSession is { } current
                    ? MediaTargetId.FromGsmtc(current)
                    : null,
                [],
                snapshot.Status,
                snapshot.StatusMessage);
        }
    }
}
