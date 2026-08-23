using MediaLock.App.Theming;
using MediaLock.Core.Configuration;
using Xunit;

namespace MediaLock.App.Tests;

public sealed class UiThemeTests
{
    [Fact]
    public void WindowFrameUsesTheResolvedClientTheme()
    {
        Assert.False(WindowFrameTheme.UsesImmersiveDarkMode(UiThemeKind.Light));
        Assert.True(WindowFrameTheme.UsesImmersiveDarkMode(UiThemeKind.Dark));
    }

    [Theory]
    [InlineData(UiThemePreference.System, true, true)]
    [InlineData(UiThemePreference.System, false, false)]
    [InlineData(UiThemePreference.Light, false, true)]
    [InlineData(UiThemePreference.Dark, true, false)]
    public void PreferenceResolvesAgainstWindowsTheme(
        string preference,
        bool windowsUsesLightTheme,
        bool expectsLight)
    {
        var expected = expectsLight ? UiThemeKind.Light : UiThemeKind.Dark;
        Assert.Equal(expected, UiTheme.Resolve(preference, windowsUsesLightTheme));
    }

    [Fact]
    public void UnsupportedPreferenceIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => UiTheme.Resolve("neon", true));
    }
}
