using MediaLock.Core.Configuration;
using MediaLock.Windows.Startup;
using Microsoft.Win32;
using Xunit;

namespace MediaLock.Windows.Tests;

public sealed class RegistryLoginStartupManagerTests
{
    [Fact]
    public async Task LoginStartupCanBeEnabledAndReversiblyDisabledForTheCurrentUser()
    {
        var subKey = $"Software\\MediaLock.Tests\\{Guid.NewGuid():N}";
        try
        {
            ILoginStartupManager manager = new RegistryLoginStartupManager(
                Registry.CurrentUser,
                subKey,
                "MediaLock",
                @"C:\Program Files\Media Lock\MediaLock.App.exe");

            await manager.SetEnabledAsync(true, CancellationToken.None);
            Assert.True(await manager.IsEnabledAsync(CancellationToken.None));

            await manager.SetEnabledAsync(false, CancellationToken.None);
            Assert.False(await manager.IsEnabledAsync(CancellationToken.None));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public async Task OwnedLoginStartupCanBeRemovedDuringUninstall()
    {
        var subKey = $"Software\\MediaLock.Tests\\{Guid.NewGuid():N}";
        try
        {
            var manager = new RegistryLoginStartupManager(
                Registry.CurrentUser,
                subKey,
                "MediaLock",
                @"C:\Users\Example\AppData\Local\Programs\MediaLock\MediaLock.exe");

            await manager.SetEnabledAsync(true, CancellationToken.None);

            var removed = await manager.RemoveIfOwnedAsync(CancellationToken.None);

            Assert.True(removed);
            Assert.False(await manager.IsEnabledAsync(CancellationToken.None));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public async Task UninstallDoesNotRemoveLoginStartupOwnedByAnotherExecutable()
    {
        var subKey = $"Software\\MediaLock.Tests\\{Guid.NewGuid():N}";
        const string portableCommand =
            "\"C:\\Users\\Example\\Desktop\\MediaLock.exe\" --startup";
        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(subKey, writable: true))
            {
                key.SetValue("MediaLock", portableCommand, RegistryValueKind.String);
            }

            var manager = new RegistryLoginStartupManager(
                Registry.CurrentUser,
                subKey,
                "MediaLock",
                @"C:\Users\Example\AppData\Local\Programs\MediaLock\MediaLock.exe");

            var removed = await manager.RemoveIfOwnedAsync(CancellationToken.None);

            Assert.False(removed);
            using var remainingKey = Registry.CurrentUser.OpenSubKey(subKey);
            Assert.Equal(portableCommand, remainingKey?.GetValue("MediaLock"));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
        }
    }
}
