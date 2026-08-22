using System.Collections.Immutable;

namespace MediaLock.Core.Media;

public enum MediaSessionCatalogStatus
{
    Available,
    Suspended,
    Reacquiring,
    Unavailable,
}

public sealed record MediaSessionCatalogSnapshot(
    ImmutableArray<MediaSessionSnapshot> Sessions,
    SessionKey? WindowsCurrentSession,
    MediaSessionCatalogStatus Status = MediaSessionCatalogStatus.Available,
    string? StatusMessage = null);

public interface IMediaSessionCatalog : IAsyncDisposable
{
    IAsyncEnumerable<MediaSessionCatalogSnapshot> WatchAsync(
        CancellationToken cancellationToken);
}
