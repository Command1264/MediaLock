using MediaLock.Core.Lifecycle;
using Microsoft.Win32;

namespace MediaLock.Windows.Lifecycle;

public sealed class SystemLifecycle : ISystemLifecycle, IWorkstationLockState, IDisposable
{
    private int workstationLocked;
    private bool disposed;

    public SystemLifecycle()
    {
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    public event Action? Suspending;

    public event Action? Resumed;

    public event Action? Locked;

    public event Action? Unlocked;

    public bool IsLocked => Volatile.Read(ref workstationLocked) != 0;

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        disposed = true;
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs args)
    {
        if (args.Mode == PowerModes.Suspend)
        {
            Suspending?.Invoke();
        }
        else if (args.Mode == PowerModes.Resume)
        {
            Resumed?.Invoke();
        }
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs args) =>
        HandleSessionSwitch(args.Reason);

    internal void HandleSessionSwitch(SessionSwitchReason reason)
    {
        if (reason == SessionSwitchReason.SessionLock &&
            Interlocked.Exchange(ref workstationLocked, 1) == 0)
        {
            Locked?.Invoke();
        }
        else if (reason == SessionSwitchReason.SessionUnlock &&
            Interlocked.Exchange(ref workstationLocked, 0) != 0)
        {
            Unlocked?.Invoke();
        }
    }
}
