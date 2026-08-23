using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MediaLock.App.Localization;
using MediaLock.App.ViewModels;
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
            using var viewModel = new SettingsViewModel(new FakeMediaLockApplication());
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

}
