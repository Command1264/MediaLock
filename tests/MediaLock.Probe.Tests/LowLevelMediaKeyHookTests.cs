using System.Reflection;
using System.Runtime.InteropServices;
using MediaLock.Probe;

namespace MediaLock.Probe.Tests;

public sealed class LowLevelMediaKeyHookTests
{
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const uint VkMediaPlayPause = 0xB3;

    [Fact]
    public void ConsumedPressKeepsDecisionForRepeatAndKeyUp()
    {
        var handlerCalls = 0;
        using var hook = new LowLevelMediaKeyHook(_ =>
        {
            handlerCalls++;
            return handlerCalls == 1;
        });

        using var keyData = new NativeKeyData(VkMediaPlayPause);

        Assert.Equal(1, InvokeHook(hook, WmKeyDown, keyData.Pointer));
        Assert.Equal(1, InvokeHook(hook, WmKeyDown, keyData.Pointer));
        Assert.Equal(1, InvokeHook(hook, WmKeyUp, keyData.Pointer));
        Assert.Equal(1, handlerCalls);
    }

    [Fact]
    public void PassedThroughPressKeepsDecisionForRepeatAndKeyUp()
    {
        var handlerCalls = 0;
        using var hook = new LowLevelMediaKeyHook(_ =>
        {
            handlerCalls++;
            return handlerCalls > 1;
        });

        using var keyData = new NativeKeyData(VkMediaPlayPause);

        Assert.Equal(0, InvokeHook(hook, WmKeyDown, keyData.Pointer));
        Assert.Equal(0, InvokeHook(hook, WmKeyDown, keyData.Pointer));
        Assert.Equal(0, InvokeHook(hook, WmKeyUp, keyData.Pointer));
        Assert.Equal(1, handlerCalls);
    }

    private static nint InvokeHook(LowLevelMediaKeyHook hook, int message, nint keyData)
    {
        var method = typeof(LowLevelMediaKeyHook).GetMethod(
            "HookCallback",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);
        return (nint)method.Invoke(hook, [0, (nuint)message, keyData])!;
    }

    private sealed class NativeKeyData : IDisposable
    {
        public NativeKeyData(uint virtualKeyCode)
        {
            Pointer = Marshal.AllocHGlobal(24);
            Marshal.WriteInt32(Pointer, unchecked((int)virtualKeyCode));
        }

        public nint Pointer { get; }

        public void Dispose()
        {
            Marshal.FreeHGlobal(Pointer);
        }
    }
}
