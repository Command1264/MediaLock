using MediaLock.Core.Configuration;
using Microsoft.Win32;

namespace MediaLock.Windows.Startup;

public sealed class RegistryLoginStartupManager : ILoginStartupManager
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
}
