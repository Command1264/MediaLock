using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MediaLock.Probe;

internal sealed class LowLevelMediaKeyHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const uint WmQuit = 0x0012;
    private const uint VkMediaNextTrack = 0xB0;
    private const uint VkMediaPrevTrack = 0xB1;
    private const uint VkMediaStop = 0xB2;
    private const uint VkMediaPlayPause = 0xB3;

    private const uint PmNoRemove = 0x0000;

    private readonly Func<MediaKeyCommand, bool> handler;
    private readonly HookProc hookProc;
    private readonly Lock sync = new();
    private readonly Dictionary<uint, bool> pressDecisions = [];
    private Thread? thread;
    private uint threadId;
    private nint hookHandle;
    private bool disposed;

    public LowLevelMediaKeyHook(Func<MediaKeyCommand, bool> handler)
    {
        this.handler = handler;
        hookProc = HookCallback;
    }

    public bool IsRunning
    {
        get
        {
            lock (sync)
            {
                return hookHandle != 0;
            }
        }
    }

    public async Task StartAsync()
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        TaskCompletionSource started;
        lock (sync)
        {
            if (thread is not null)
            {
                return;
            }

            started = new(TaskCreationOptions.RunContinuationsAsynchronously);
            thread = new Thread(() => RunMessageLoop(started))
            {
                IsBackground = true,
                Name = "MediaLock low-level keyboard hook",
            };
            thread.Start();
        }

        await started.Task;
    }

    public void Stop()
    {
        Thread? runningThread;
        uint runningThreadId;
        lock (sync)
        {
            runningThread = thread;
            runningThreadId = threadId;
        }

        if (runningThread is null)
        {
            return;
        }

        if (runningThreadId != 0 && !PostThreadMessageW(runningThreadId, WmQuit, 0, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to stop the keyboard hook message loop.");
        }

        if (!runningThread.Join(TimeSpan.FromSeconds(5)))
        {
            throw new TimeoutException("The keyboard hook thread did not stop within five seconds.");
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        Stop();
        disposed = true;
    }

    private void RunMessageLoop(TaskCompletionSource started)
    {
        try
        {
            var currentThreadId = GetCurrentThreadId();
            _ = PeekMessageW(out _, 0, 0, 0, PmNoRemove);
            var moduleHandle = GetModuleHandleW(null);
            var installedHook = SetWindowsHookExW(WhKeyboardLl, hookProc, moduleHandle, 0);
            if (installedHook == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to install WH_KEYBOARD_LL.");
            }

            lock (sync)
            {
                threadId = currentThreadId;
                hookHandle = installedHook;
            }

            started.SetResult();

            while (true)
            {
                var result = GetMessageW(out _, 0, 0, 0);
                if (result == 0)
                {
                    break;
                }

                if (result == -1)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "The keyboard hook message loop failed.");
                }
            }
        }
        catch (Exception exception)
        {
            started.TrySetException(exception);
            ConsoleLog.Error($"Keyboard hook stopped unexpectedly: {exception.Message}");
        }
        finally
        {
            pressDecisions.Clear();
            nint installedHook;
            lock (sync)
            {
                installedHook = hookHandle;
                hookHandle = 0;
                threadId = 0;
                thread = null;
            }

            if (installedHook != 0 && !UnhookWindowsHookEx(installedHook))
            {
                ConsoleLog.Error($"Unable to remove keyboard hook (Win32 error {Marshal.GetLastWin32Error()}).");
            }
        }
    }

    private nint HookCallback(int code, nuint wParam, nint lParam)
    {
        if (code >= 0 && TryMapMediaKey(lParam, out var virtualKeyCode, out var command))
        {
            var message = unchecked((int)wParam);
            var isKeyDown = message is WmKeyDown or WmSysKeyDown;
            var isKeyUp = message is WmKeyUp or WmSysKeyUp;

            if (isKeyDown)
            {
                if (!pressDecisions.TryGetValue(virtualKeyCode, out var consumed))
                {
                    consumed = handler(command);
                    pressDecisions.Add(virtualKeyCode, consumed);
                }

                if (consumed)
                {
                    return 1;
                }
            }

            if (isKeyUp &&
                pressDecisions.Remove(virtualKeyCode, out var consumedOnKeyDown) &&
                consumedOnKeyDown)
            {
                return 1;
            }
        }

        return CallNextHookEx(0, code, wParam, lParam);
    }

    private static bool TryMapMediaKey(nint lParam, out uint virtualKeyCode, out MediaKeyCommand command)
    {
        var data = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
        virtualKeyCode = data.VirtualKeyCode;
        command = virtualKeyCode switch
        {
            VkMediaPlayPause => MediaKeyCommand.PlayPause,
            VkMediaNextTrack => MediaKeyCommand.Next,
            VkMediaPrevTrack => MediaKeyCommand.Previous,
            VkMediaStop => MediaKeyCommand.Stop,
            _ => default,
        };

        return virtualKeyCode is VkMediaPlayPause or VkMediaNextTrack or VkMediaPrevTrack or VkMediaStop;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct KbdLlHookStruct
    {
        public readonly uint VirtualKeyCode;
        public readonly uint ScanCode;
        public readonly uint Flags;
        public readonly uint Time;
        public readonly nuint ExtraInfo;
    }

    private delegate nint HookProc(int code, nuint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookExW(int hookId, HookProc callback, nint moduleHandle, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hookHandle);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hookHandle, int code, nuint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetMessageW(out Message message, nint windowHandle, uint minimumMessage, uint maximumMessage);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessageW(
        out Message message,
        nint windowHandle,
        uint minimumMessage,
        uint maximumMessage,
        uint removeMessage);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessageW(uint threadId, uint message, nuint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandleW(string? moduleName);

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public nint WindowHandle;
        public uint Value;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public Point Position;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }
}
