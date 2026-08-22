namespace MediaLock.Core.Lifecycle;

public enum InstanceStartResult
{
    Primary,
    ActivatedExisting,
}

public interface IInstanceCoordinator : IAsyncDisposable
{
    event EventHandler ActivationRequested;

    ValueTask<InstanceStartResult> StartAsync(CancellationToken cancellationToken);
}
