namespace MediaLock.Core.Lifecycle;

public interface ISystemLifecycle
{
    event Action Suspending;

    event Action Resumed;
}
