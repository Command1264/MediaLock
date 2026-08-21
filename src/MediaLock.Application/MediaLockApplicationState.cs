using MediaLock.Core.Routing;

namespace MediaLock.Application;

public sealed record MediaLockApplicationState(
    RouterState Router,
    string? ErrorMessage = null)
{
    public static MediaLockApplicationState Initial { get; } = new(RouterState.Initial);
}

public sealed class MediaLockApplicationStateChangedEventArgs(
    MediaLockApplicationState state) : EventArgs
{
    public MediaLockApplicationState State { get; } = state;
}
