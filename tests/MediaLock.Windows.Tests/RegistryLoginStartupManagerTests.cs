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
}
