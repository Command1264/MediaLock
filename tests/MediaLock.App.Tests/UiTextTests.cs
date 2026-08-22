using System.Globalization;
using MediaLock.App.Localization;
using MediaLock.Core.Configuration;
using Xunit;

namespace MediaLock.App.Tests;

[Collection("Localization")]
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
        Assert.Equal(
            "English",
            UiText.Get("Language_English", CultureInfo.GetCultureInfo("zh-TW")));
        Assert.Equal(
            "繁體中文",
            UiText.Get("Language_TraditionalChinese", CultureInfo.GetCultureInfo("en-US")));
    }

    [Fact]
    public void ApplyingANewCultureNotifiesLocalizedCallers()
    {
        var notifications = 0;
        UiText.CultureChanged += OnCultureChanged;
        try
        {
            UiText.Apply(UiLanguagePreference.TraditionalChinese);

            Assert.Equal("zh-TW", UiText.CurrentCulture.Name);
            Assert.Equal("設定", UiText.Get("Settings_Title"));
            Assert.Equal(1, notifications);
        }
        finally
        {
            UiText.Apply(UiLanguagePreference.EnglishUnitedStates);
            UiText.CultureChanged -= OnCultureChanged;
        }

        void OnCultureChanged(object? sender, EventArgs args) => notifications++;
    }
}

[CollectionDefinition("Localization", DisableParallelization = true)]
public sealed class LocalizationCollection;
