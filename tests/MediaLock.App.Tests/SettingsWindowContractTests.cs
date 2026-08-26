using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Xml.Linq;
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
    public void PriorityRuleSourceNamesKeepRawIdentityAsAccessibleDetails()
    {
        var xaml = XDocument.Load(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "MediaLock.App",
            "SettingsWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var available = Assert.Single(
            xaml.Descendants(presentation + "ComboBox"),
            element => (string?)element.Attribute("ItemsSource") ==
                "{Binding AvailableApplications}");
        Assert.Equal("DisplayName", (string?)available.Attribute("DisplayMemberPath"));
        Assert.Equal(
            "SourceAppUserModelId",
            (string?)available.Attribute("SelectedValuePath"));
        Assert.Equal(
            "{Binding SelectedAvailableApplication}",
            (string?)available.Attribute("SelectedValue"));

        var ruleSource = Assert.Single(
            xaml.Descendants(presentation + "TextBlock"),
            element => (string?)element.Attribute("Text") == "{Binding DisplayName}");
        Assert.Equal("{Binding Details}", (string?)ruleSource.Attribute("ToolTip"));
        Assert.Equal(
            "{Binding Details}",
            (string?)ruleSource.Attribute("AutomationProperties.HelpText"));
    }

    [Fact]
    public void SettingsCardsFollowTheRequestedTopToBottomOrder()
    {
        var xaml = XDocument.Load(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "MediaLock.App",
            "SettingsWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var headings = xaml.Descendants(presentation + "TextBlock")
            .Select(element => (string?)element.Attribute("Text"))
            .Where(text => text is
                "{loc:Translate Settings_General}" or
                "{loc:Translate Settings_Appearance}" or
                "{loc:Translate Settings_PlaybackStateLock}" or
                "{loc:Translate Settings_RoutingRecovery}" or
                "{loc:Translate Settings_PriorityRules}" or
                "{loc:Translate Settings_AboutDiagnostics}")
            .Select(text => text!)
            .ToArray();

        Assert.Equal(
            [
                "{loc:Translate Settings_General}",
                "{loc:Translate Settings_Appearance}",
                "{loc:Translate Settings_PlaybackStateLock}",
                "{loc:Translate Settings_RoutingRecovery}",
                "{loc:Translate Settings_PriorityRules}",
                "{loc:Translate Settings_AboutDiagnostics}",
            ],
            headings);
    }

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
                AssertRepeatedPauseOverrideUsesSharedControls(window, viewModel);
                AssertAboutAndDiagnosticActions(window, viewModel);

                UiText.Apply(UiLanguagePreference.EnglishUnitedStates);
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                AssertSelectedAppearanceOptions(window, viewModel);
                AssertStableReleaseStatus(window);
            }
            finally
            {
                window.Close();
                UiText.Apply(originalLanguage);
            }
        });
    }

    private static void AssertStableReleaseStatus(SettingsWindow window)
    {
        var text = WpfTestHost.FindVisualChildren<TextBlock>(window)
            .Select(textBlock => textBlock.Text)
            .ToArray();

        Assert.Contains(text, value => value.Contains(
            "Stable release",
            StringComparison.Ordinal));
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
        Assert.Contains("0.2.0", text);
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

    private static void AssertRepeatedPauseOverrideUsesSharedControls(
        SettingsWindow window,
        SettingsViewModel viewModel)
    {
        var checkBoxes = WpfTestHost.FindVisualChildren<CheckBox>(window).ToArray();
        Assert.Single(checkBoxes, candidate =>
            candidate.GetBindingExpression(CheckBox.IsCheckedProperty)?
                .ParentBinding.Path.Path == nameof(SettingsViewModel.RepeatedPauseOverrideEnabled));
        Assert.Single(checkBoxes, candidate =>
            candidate.GetBindingExpression(CheckBox.IsCheckedProperty)?
                .ParentBinding.Path.Path == nameof(SettingsViewModel.PlayRepeatedPauseOverrideSound));

        var textBoxes = WpfTestHost.FindVisualChildren<TextBox>(window).ToArray();
        var windowField = Assert.Single(textBoxes, candidate =>
            candidate.GetBindingExpression(TextBox.TextProperty)?
                .ParentBinding.Path.Path == nameof(SettingsViewModel.RepeatedPauseWindowText));
        var countField = Assert.Single(textBoxes, candidate =>
            candidate.GetBindingExpression(TextBox.TextProperty)?
                .ParentBinding.Path.Path == nameof(SettingsViewModel.RepeatedPauseCountText));
        Assert.Equal(36, windowField.ActualHeight);
        Assert.Equal(36, countField.ActualHeight);
    }

    private sealed class FixedEnvironmentInfoProvider : IAppEnvironmentInfoProvider
    {
        public AppEnvironmentInfo GetCurrent() => new(
            "0.2.0",
            "Windows 11 Enterprise",
            "24H2",
            "26100.9168",
            "X64",
            IsSigned: false);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "MediaLock.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Media Lock repository root.");
    }

}
