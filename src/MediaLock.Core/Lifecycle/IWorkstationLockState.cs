namespace MediaLock.Core.Lifecycle;

public interface IWorkstationLockState
{
    bool IsLocked { get; }

    event Action Locked;

    event Action Unlocked;
}
