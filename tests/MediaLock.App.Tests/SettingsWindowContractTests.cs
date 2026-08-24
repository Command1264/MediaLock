using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MediaLock.App.Localization;
using MediaLock.App.ViewModels;
using MediaLock.Application;
using MediaLock.Core.Configuration;
using Xunit;

namespace MediaLock.App.Tests;

[Collection("Localization")]
public sealed class SettingsWindowContractTests
{
    [Fact]
    public void SettingsUsesAFixedFramelessSurfaceWithSelectedAppearanceOptions()
    {
        WpfTestHost.Run(() =>
        {
            using var viewModel = new SettingsViewModel(
                new FakeMediaLockApplication(),
                environmentInfoProvider: new FixedEnvironmentInfoProvider());
            var window = new SettingsWindow(viewModel);
            var originalLanguage = UiText.CurrentCulture.Name == "zh-TW"
                ? UiLanguagePreference.TraditionalChinese
                : UiLanguagePreference.EnglishUnitedStates;

            Assert.Equal(680, window.Width);
            Assert.Equal(720, window.Height);
            Assert.Equal(ResizeMode.NoResize, window.ResizeMode);
            Assert.Equal(WindowStyle.None, window.WindowStyle);
            Assert.True(window.AllowsTransparency);
            Assert.False(window.ShowInTaskbar);

            window.Show();
            try
            {
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                AssertSelectedAppearanceOptions(window, viewModel);
                AssertMediaKeyInterceptionSwitch(window);
                AssertRecoveryFieldsAreAligned(window, viewModel);
                AssertAboutAndDiagnosticActions(window, viewModel);

                UiText.Apply(UiLanguagePreference.EnglishUnitedStates);
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                AssertSelectedAppearanceOptions(window, viewModel);
            }
            finally
            {
                window.Close();
                UiText.Apply(originalLanguage);
            }
        });
    }

    private static void AssertAboutAndDiagnosticActions(
        SettingsWindow window,
        SettingsViewModel viewModel)
    {
        var buttons = WpfTestHost.FindVisualChildren<Button>(window).ToArray();
        Assert.Single(buttons, button => ReferenceEquals(
            button.Command,
            viewModel.CopyDiagnosticsCommand));
        Assert.Single(buttons, button => ReferenceEquals(
            button.Command,
            viewModel.OpenLogsFolderCommand));
        Assert.Single(buttons, button => ReferenceEquals(
            button.Command,
            viewModel.OpenSupportCommand));
        Assert.Single(buttons, button => ReferenceEquals(
            button.Command,
            viewModel.ReportBugCommand));

        var text = WpfTestHost.FindVisualChildren<TextBlock>(window)
            .Select(textBlock => textBlock.Text)
            .ToArray();
        Assert.Contains("0.2.0-rc.3", text);
        Assert.Contains(text, value => value.Contains(
            "Windows 11 Enterprise 24H2",
            StringComparison.Ordinal));
    }

    private static void AssertSelectedAppearanceOptions(
        SettingsWindow window,
        SettingsViewModel viewModel)
    {
        var comboBoxes = WpfTestHost.FindVisualChildren<ComboBox>(window).ToArray();
        var language = Assert.Single(comboBoxes, comboBox =>
            ReferenceEquals(comboBox.ItemsSource, viewModel.Languages));
        var theme = Assert.Single(comboBoxes, comboBox =>
            ReferenceEquals(comboBox.ItemsSource, viewModel.Themes));

        var selectedLanguage = Assert.IsType<LocalizedOption<string>>(language.SelectedItem);
        var selectedTheme = Assert.IsType<LocalizedOption<string>>(theme.SelectedItem);
        Assert.Equal(viewModel.SelectedLanguage, selectedLanguage.Value);
        Assert.Equal(viewModel.SelectedTheme, selectedTheme.Value);
    }

    private static void AssertMediaKeyInterceptionSwitch(SettingsWindow window)
    {
        var checkBox = Assert.Single(
            WpfTestHost.FindVisualChildren<CheckBox>(window),
            candidate => candidate.GetBindingExpression(CheckBox.IsCheckedProperty)?
                .ParentBinding.Path.Path == nameof(SettingsViewModel.InterceptMediaKeys));

        Assert.True(checkBox.IsChecked);
    }

    private static void AssertRecoveryFieldsAreAligned(
        SettingsWindow window,
        SettingsViewModel viewModel)
    {
        var timeout = Assert.Single(
            WpfTestHost.FindVisualChildren<TextBox>(window),
            candidate => candidate.GetBindingExpression(TextBox.TextProperty)?
                .ParentBinding.Path.Path == nameof(SettingsViewModel.RecoveryTimeoutText));
        var fallback = Assert.Single(
            WpfTestHost.FindVisualChildren<ComboBox>(window),
            comboBox => ReferenceEquals(comboBox.ItemsSource, viewModel.FallbackPolicies));

        Assert.Equal(36, timeout.ActualHeight);
        Assert.Equal(fallback.ActualHeight, timeout.ActualHeight);

        timeout.Text = "-1";
        timeout.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

        Assert.True(Validation.GetHasError(timeout));
        var chrome = Assert.Single(
            WpfTestHost.FindVisualChildren<Border>(timeout),
            candidate => candidate.Name == "Chrome");
        Assert.Equal(window.FindResource("DangerBrush"), chrome.BorderBrush);
    }

    private sealed class FixedEnvironmentInfoProvider : IAppEnvironmentInfoProvider
    {
        public AppEnvironmentInfo GetCurrent() => new(
            "0.2.0-rc.3",
            "Windows 11 Enterprise",
            "24H2",
            "26100.9168",
            "X64",
            IsSigned: false);
    }

}
