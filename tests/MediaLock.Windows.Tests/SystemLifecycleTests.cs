using MediaLock.Windows.Lifecycle;
using Microsoft.Win32;
using Xunit;

namespace MediaLock.Windows.Tests;

public sealed class SystemLifecycleTests
{
    [Fact]
    public void SessionLockAndUnlockPublishDistinctWorkstationTransitions()
    {
        using var lifecycle = new SystemLifecycle();
        var transitions = new List<string>();
        lifecycle.Locked += () => transitions.Add("locked");
        lifecycle.Unlocked += () => transitions.Add("unlocked");

        lifecycle.HandleSessionSwitch(SessionSwitchReason.SessionLock);
        lifecycle.HandleSessionSwitch(SessionSwitchReason.SessionLock);

        Assert.True(lifecycle.IsLocked);
        Assert.Equal(["locked"], transitions);

        lifecycle.HandleSessionSwitch(SessionSwitchReason.SessionUnlock);
        lifecycle.HandleSessionSwitch(SessionSwitchReason.SessionUnlock);

        Assert.False(lifecycle.IsLocked);
        Assert.Equal(["locked", "unlocked"], transitions);
    }
}
