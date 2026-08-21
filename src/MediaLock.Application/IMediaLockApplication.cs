namespace MediaLock.Application;

public interface IMediaLockApplication : IAsyncDisposable
{
    event EventHandler<MediaLockApplicationStateChangedEventArgs> StateChanged;

    MediaLockApplicationState State { get; }

    ValueTask StartAsync(CancellationToken cancellationToken);

    ValueTask<ApplicationResult> DispatchAsync(
        ApplicationIntent intent,
        CancellationToken cancellationToken);
}
