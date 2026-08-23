using MediaLock.Core.Configuration;
using MediaLock.Core.Media;
using MediaLock.Core.Routing;

namespace MediaLock.Application;

public sealed record MediaLockApplicationState(
    RouterState Router,
    string? ErrorMessage,
    MediaLockSettings Settings,
    MediaSessionCatalogStatus CatalogStatus = MediaSessionCatalogStatus.Available,
    string? CatalogStatusMessage = null)
{
    public MediaLockApplicationState(
        RouterState router,
        string? errorMessage = null)
        : this(router, errorMessage, MediaLockSettings.Default)
    {
    }

    public static MediaLockApplicationState Initial { get; } = new(
        RouterState.Initial,
        null,
        MediaLockSettings.Default);
}

public sealed class MediaLockApplicationStateChangedEventArgs(
    MediaLockApplicationState state) : EventArgs
{
    public MediaLockApplicationState State { get; } = state;
}
