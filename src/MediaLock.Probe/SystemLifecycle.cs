using Microsoft.Win32;

namespace MediaLock.Probe;

internal interface ISystemLifecycle
{
    event Action? Suspending;

    event Action? Resumed;
}

internal sealed class SystemLifecycle : ISystemLifecycle, IDisposable
{
    private bool disposed;

    public SystemLifecycle()
    {
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    public event Action? Suspending;

    public event Action? Resumed;

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        disposed = true;
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs args)
    {
        if (args.Mode is PowerModes.Suspend)
        {
            Suspending?.Invoke();
        }
        else if (args.Mode is PowerModes.Resume)
        {
            Resumed?.Invoke();
        }
    }
}
