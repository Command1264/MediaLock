using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using MediaLock.Core.Diagnostics;
using MediaLock.Core.Input;

namespace MediaLock.Windows.Input;

public sealed class LowLevelMediaKeyInputSource : IMediaInputSource
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const uint WmQuit = 0x0012;
    private const uint PmNoRemove = 0x0000;

    private readonly HookProc hookProc;
    private readonly Lock sync = new();
    private readonly MediaKeyPressFilter pressFilter = new();
    private MediaInputHandler? handler;
    private Thread? thread;
    private uint threadId;
    private nint hookHandle;
    private bool disposed;

    public LowLevelMediaKeyInputSource()
    {
        hookProc = HookCallback;
    }

    public event EventHandler<MediaInputSourceFaultedEventArgs>? Faulted;

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

    public async ValueTask StartAsync(
        MediaInputHandler handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        TaskCompletionSource started;
        lock (sync)
        {
            if (thread is not null)
            {
                throw new InvalidOperationException("The low-level media key hook is already running.");
            }

            this.handler = handler;
            started = new(TaskCreationOptions.RunContinuationsAsynchronously);
            thread = new Thread(() => RunMessageLoop(started))
            {
                IsBackground = true,
                Name = "Media Lock low-level keyboard hook",
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
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Unable to stop the media key hook message loop.");
        }

        if (!runningThread.Join(TimeSpan.FromSeconds(5)))
        {
            throw new TimeoutException("The media key hook thread did not stop within five seconds.");
        }
    }

    public ValueTask DisposeAsync()
    {
        if (!disposed)
        {
            Stop();
            disposed = true;
        }

        return ValueTask.CompletedTask;
    }

    private void RunMessageLoop(TaskCompletionSource started)
    {
        try
        {
            var currentThreadId = GetCurrentThreadId();
            _ = PeekMessageW(out _, 0, 0, 0, PmNoRemove);
            var installedHook = SetWindowsHookExW(
                WhKeyboardLl,
                hookProc,
                GetModuleHandleW(null),
                0);
            if (installedHook == 0)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Unable to install the WH_KEYBOARD_LL media key hook.");
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
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        "The media key hook message loop failed.");
                }
            }
        }
        catch (Exception exception)
        {
            if (!started.TrySetException(exception))
            {
                NotifyFaulted(exception);
            }
        }
        finally
        {
            pressFilter.Clear();
            nint installedHook;
            lock (sync)
            {
                installedHook = hookHandle;
                hookHandle = 0;
                threadId = 0;
                thread = null;
                handler = null;
            }

            if (installedHook != 0 && !UnhookWindowsHookEx(installedHook))
            {
                NotifyFaulted(new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Unable to remove the media key hook."));
            }
        }
    }

    private nint HookCallback(int code, nuint wParam, nint lParam)
    {
        if (code >= 0)
        {
            var message = unchecked((int)wParam);
            var transition = message switch
            {
                WmKeyDown or WmSysKeyDown => MediaKeyTransition.KeyDown,
                WmKeyUp or WmSysKeyUp => MediaKeyTransition.KeyUp,
                _ => (MediaKeyTransition?)null,
            };
            if (transition is not null)
            {
                try
                {
                    var data = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
                    var currentHandler = handler;
                    if (currentHandler is not null &&
                        pressFilter.Process(
                            data.VirtualKeyCode,
                            transition.Value,
                            currentHandler))
                    {
                        return 1;
                    }
                }
                catch (Exception exception)
                {
                    NotifyFaulted(exception);
                }
            }
        }

        return CallNextHookEx(0, code, wParam, lParam);
    }

    private void NotifyFaulted(Exception exception)
    {
        var subscribers = Faulted;
        if (subscribers is null)
        {
            BoundedDiagnosticTrace.WriteFailure("input.low_level_hook", exception);
            return;
        }

        foreach (EventHandler<MediaInputSourceFaultedEventArgs> subscriber in
            subscribers.GetInvocationList())
        {
            try
            {
                subscriber(this, new MediaInputSourceFaultedEventArgs(exception));
            }
            catch (Exception subscriberException)
            {
                BoundedDiagnosticTrace.WriteFailure(
                    "input.low_level_hook_subscriber",
                    subscriberException);
            }
        }
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
    private static extern nint SetWindowsHookExW(
        int hookId,
        HookProc callback,
        nint moduleHandle,
        uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hookHandle);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(
        nint hookHandle,
        int code,
        nuint wParam,
        nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetMessageW(
        out Message message,
        nint windowHandle,
        uint minimumMessage,
        uint maximumMessage);

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
    private static extern bool PostThreadMessageW(
        uint threadId,
        uint message,
        nuint wParam,
        nint lParam);

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
