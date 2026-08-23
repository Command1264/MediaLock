using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MediaLock.App.Localization;
using MediaLock.App.ViewModels;
using MediaLock.Application;
using MediaLock.Core.Configuration;
using MediaLock.Core.Routing;
using Xunit;

namespace MediaLock.App.Tests;

[Collection("Localization")]
public sealed class SettingsWindowContractTests
{
    [Fact]
    public void SettingsUsesAFixedFramelessSurfaceWithSelectedAppearanceOptions()
    {
        RunOnStaThread(() =>
        {
            var app = new App();
            app.InitializeComponent();
            using var viewModel = new SettingsViewModel(new FakeApplication());
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
        var comboBoxes = FindVisualChildren<ComboBox>(window).ToArray();
        var language = Assert.Single(comboBoxes, comboBox =>
            ReferenceEquals(comboBox.ItemsSource, viewModel.Languages));
        var theme = Assert.Single(comboBoxes, comboBox =>
            ReferenceEquals(comboBox.ItemsSource, viewModel.Themes));

        var selectedLanguage = Assert.IsType<LocalizedOption<string>>(language.SelectedItem);
        var selectedTheme = Assert.IsType<LocalizedOption<string>>(theme.SelectedItem);
        Assert.Equal(viewModel.SelectedLanguage, selectedLanguage.Value);
        Assert.Equal(viewModel.SelectedTheme, selectedTheme.Value);
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static void RunOnStaThread(Action action)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }

    private sealed class FakeApplication : IMediaLockApplication
    {
        public event EventHandler<MediaLockApplicationStateChangedEventArgs>? StateChanged
        {
            add { }
            remove { }
        }

        public MediaLockApplicationState State { get; } = MediaLockApplicationState.Initial;

        public ValueTask StartAsync(CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask<ApplicationResult> DispatchAsync(
            ApplicationIntent intent,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new ApplicationResult(State, RouteDecision.StateUpdated));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
