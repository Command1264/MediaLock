using MediaLock.Core.Configuration;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MediaLock.Windows.Startup;

public sealed class RegistryLoginStartupManager :
    ILoginStartupManager,
    ILoginStartupChangeSource
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private readonly RegistryKey root;
    private readonly string subKeyPath;
    private readonly string valueName;
    private readonly string command;

    public RegistryLoginStartupManager()
        : this(
            Registry.CurrentUser,
            RunKeyPath,
            "MediaLock",
            Environment.ProcessPath ?? throw new InvalidOperationException(
                "Could not determine the Media Lock executable path."))
    {
    }

    internal RegistryLoginStartupManager(
        RegistryKey root,
        string subKeyPath,
        string valueName,
        string executablePath)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(subKeyPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(valueName);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        this.root = root;
        this.subKeyPath = subKeyPath;
        this.valueName = valueName;
        command = $"\"{Path.GetFullPath(executablePath)}\" --startup";
    }

    public ValueTask<bool> IsEnabledAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var key = root.OpenSubKey(subKeyPath, writable: false);
        var configured = key?.GetValue(valueName) as string;
        return ValueTask.FromResult(string.Equals(
            configured,
            command,
            StringComparison.Ordinal));
    }

    public ValueTask SetEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var key = root.CreateSubKey(subKeyPath, writable: true) ??
            throw new InvalidOperationException(
                $"Could not open current-user startup registry key '{subKeyPath}'.");
        if (enabled)
        {
            key.SetValue(valueName, command, RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(valueName, throwOnMissingValue: false);
        }

        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<bool> WatchEnabledAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var key = root.CreateSubKey(subKeyPath, writable: false) ??
                throw new InvalidOperationException(
                    $"Could not open current-user startup registry key '{subKeyPath}'.");
            using var changed = new AutoResetEvent(initialState: false);
            var error = RegNotifyChangeKeyValue(
                key.Handle,
                watchSubtree: false,
                RegistryNotifyFilter.LastSet,
                changed.SafeWaitHandle,
                asynchronous: true);
            if (error != 0)
            {
                throw new Win32Exception(
                    error,
                    $"Could not monitor current-user startup registry key '{subKeyPath}'.");
            }

            yield return await IsEnabledAsync(cancellationToken);
            await WaitForSignalAsync(changed, cancellationToken);
        }
    }

    public ValueTask<bool> RemoveIfOwnedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var key = root.OpenSubKey(subKeyPath, writable: true);
        if (key?.GetValue(valueName) is not string configured ||
            !string.Equals(configured, command, StringComparison.Ordinal))
        {
            return ValueTask.FromResult(false);
        }

        key.DeleteValue(valueName, throwOnMissingValue: false);
        return ValueTask.FromResult(true);
    }

    private static async Task WaitForSignalAsync(
        WaitHandle signal,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellationRegistration = cancellationToken.Register(
            () => completion.TrySetCanceled(cancellationToken));
        var waitRegistration = ThreadPool.RegisterWaitForSingleObject(
            signal,
            (_, _) => completion.TrySetResult(),
            state: null,
            Timeout.InfiniteTimeSpan,
            executeOnlyOnce: true);
        try
        {
            await completion.Task;
        }
        finally
        {
            waitRegistration.Unregister(waitObject: null);
        }
    }

    [Flags]
    private enum RegistryNotifyFilter : uint
    {
        LastSet = 0x00000004,
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern int RegNotifyChangeKeyValue(
        SafeRegistryHandle hKey,
        [MarshalAs(UnmanagedType.Bool)] bool watchSubtree,
        RegistryNotifyFilter notifyFilter,
        SafeWaitHandle hEvent,
        [MarshalAs(UnmanagedType.Bool)] bool asynchronous);
}
