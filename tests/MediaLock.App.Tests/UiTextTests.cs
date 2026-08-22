using System.Globalization;
using MediaLock.App.Localization;
using MediaLock.Core.Configuration;
using Xunit;

namespace MediaLock.App.Tests;

public sealed class UiTextTests
{
    [Theory]
    [InlineData(UiLanguagePreference.EnglishUnitedStates, "zh-TW", "en-US")]
    [InlineData(UiLanguagePreference.TraditionalChinese, "en-US", "zh-TW")]
    [InlineData(UiLanguagePreference.System, "zh-HK", "zh-TW")]
    [InlineData(UiLanguagePreference.System, "zh-CN", "en-US")]
    [InlineData(UiLanguagePreference.System, "fr-FR", "en-US")]
    public void ResolveCultureUsesSupportedPreferenceAndFallback(
        string preference,
        string systemCulture,
        string expected)
    {
        var result = UiText.ResolveCulture(
            preference,
            CultureInfo.GetCultureInfo(systemCulture));

        Assert.Equal(expected, result.Name);
    }

    [Fact]
    public void TraditionalChineseResourceOverridesEnglishFallback()
    {
        Assert.Equal(
            "設定",
            UiText.Get("Settings_Title", CultureInfo.GetCultureInfo("zh-TW")));
        Assert.Equal(
            "Settings",
            UiText.Get("Settings_Title", CultureInfo.GetCultureInfo("en-US")));
    }
}
