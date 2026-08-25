using System.Globalization;
using MediaLock.App.Localization;
using MediaLock.Core.Configuration;
using Xunit;

namespace MediaLock.App.Tests;

[Collection("Localization")]
public sealed class UiTextTests
{
    [Theory]
    [InlineData(UiLanguagePreference.EnglishUnitedStates, "About and diagnostics", "Copy diagnostics")]
    [InlineData(UiLanguagePreference.TraditionalChinese, "關於與診斷", "複製診斷摘要")]
    public void AboutAndDiagnosticsActionsAreLocalized(
        string language,
        string expectedTitle,
        string expectedCopyAction)
    {
        UiText.Apply(language);

        Assert.Equal(expectedTitle, UiText.Get("Settings_AboutDiagnostics"));
        Assert.Equal(expectedCopyAction, UiText.Get("Settings_CopyDiagnostics"));
    }

    [Theory]
    [InlineData(UiLanguagePreference.EnglishUnitedStates, "Intercept global media keys")]
    [InlineData(UiLanguagePreference.TraditionalChinese, "攔截全域媒體鍵")]
    public void MediaKeyInterceptionSettingIsLocalized(string language, string expected)
    {
        UiText.Apply(language);

        Assert.Equal(expected, UiText.Get("Settings_InterceptMediaKeys"));
    }

    [Theory]
    [InlineData(UiLanguagePreference.EnglishUnitedStates, "apply immediately after you save", "restarts")]
    [InlineData(UiLanguagePreference.TraditionalChinese, "儲存後立即套用", "重新啟動後套用")]
    public void RoutingHelpDescribesImmediateSettingsSynchronization(
        string language,
        string expected,
        string obsolete)
    {
        UiText.Apply(language);

        var routingHelp = UiText.Get("Settings_RoutingHelp");

        Assert.Contains(expected, routingHelp, StringComparison.Ordinal);
        Assert.DoesNotContain(obsolete, routingHelp, StringComparison.OrdinalIgnoreCase);
    }

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
        Assert.Equal(
            "取消",
            UiText.Get("Settings_Cancel", CultureInfo.GetCultureInfo("zh-TW")));
        Assert.Equal(
            "Cancel",
            UiText.Get("Settings_Cancel", CultureInfo.GetCultureInfo("en-US")));
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
