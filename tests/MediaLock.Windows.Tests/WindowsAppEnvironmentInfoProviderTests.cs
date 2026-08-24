using System.Runtime.InteropServices;
using Xunit;

namespace MediaLock.Windows.Tests;

public sealed class WindowsAppEnvironmentInfoProviderTests
{
    [Fact]
    public void GetCurrentNormalizesStaleWindows10ProductNameForWindows11Build()
    {
        var registry = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["ProductName"] = "Windows 10 Enterprise",
            ["DisplayVersion"] = "24H2",
            ["CurrentBuild"] = "26100",
            ["UBR"] = "9168",
        };
        var provider = new WindowsAppEnvironmentInfoProvider(
            () => "0.2.0-rc.3+abcdef123456",
            name => registry.GetValueOrDefault(name),
            () => Architecture.X64,
            () => false);

        var info = provider.GetCurrent();

        Assert.Equal("0.2.0-rc.3", info.AppVersion);
        Assert.Equal("Windows 11 Enterprise", info.WindowsProductName);
        Assert.Equal("24H2", info.WindowsDisplayVersion);
        Assert.Equal("26100.9168", info.WindowsBuild);
        Assert.Equal("X64", info.Architecture);
        Assert.False(info.IsSigned);
        Assert.True(info.IsPrerelease);
    }

    [Fact]
    public void GetCurrentPreservesProductNameOnPreWindows11BuildAndHandlesMissingUbr()
    {
        var registry = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["ProductName"] = "Windows 10 Pro",
            ["DisplayVersion"] = "22H2",
            ["CurrentBuild"] = "19045",
            ["UBR"] = null,
        };
        var provider = new WindowsAppEnvironmentInfoProvider(
            () => "1.0.0",
            name => registry.GetValueOrDefault(name),
            () => Architecture.Arm64,
            () => true);

        var info = provider.GetCurrent();

        Assert.Equal("Windows 10 Pro", info.WindowsProductName);
        Assert.Equal("19045", info.WindowsBuild);
        Assert.Equal("Arm64", info.Architecture);
        Assert.True(info.IsSigned);
        Assert.False(info.IsPrerelease);
    }
}
