using System.Collections.Immutable;
using MediaLock.Core.Configuration;
using MediaLock.Core.Media;
using MediaLock.Core.Playback;
using MediaLock.Core.Routing;

namespace MediaLock.Application;

public sealed record MediaLockApplicationState(
    RouterState Router,
    string? ErrorMessage,
    MediaLockSettings Settings,
    MediaSessionCatalogStatus CatalogStatus = MediaSessionCatalogStatus.Available,
    string? CatalogStatusMessage = null)
{
    public ImmutableArray<MediaTargetSnapshot> Targets { get; init; } = [];

    public PlaybackStateLockState PlaybackStateLock { get; init; } =
        PlaybackStateLockState.Off;

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

public enum PlaybackStateLockStatus
{
    Off,
    Ready,
    Suspended,
    Failed,
    Released,
}

public sealed record PlaybackStateLockState(
    PlaybackStateLockMode Mode,
    PlaybackStateLockStatus Status,
    MediaTargetId? ArmedTarget,
    string? Message = null)
{
    public static PlaybackStateLockState Off { get; } = new(
        PlaybackStateLockMode.Off,
        PlaybackStateLockStatus.Off,
        ArmedTarget: null);
}

public sealed class MediaLockApplicationStateChangedEventArgs(
    MediaLockApplicationState state) : EventArgs
{
    public MediaLockApplicationState State { get; } = state;
}
