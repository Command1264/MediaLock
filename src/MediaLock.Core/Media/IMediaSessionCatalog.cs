using System.Collections.Immutable;

namespace MediaLock.Core.Media;

public sealed record MediaSessionCatalogSnapshot(
    ImmutableArray<MediaSessionSnapshot> Sessions,
    SessionKey? WindowsCurrentSession);

public interface IMediaSessionCatalog : IAsyncDisposable
{
    IAsyncEnumerable<MediaSessionCatalogSnapshot> WatchAsync(
        CancellationToken cancellationToken);
}
