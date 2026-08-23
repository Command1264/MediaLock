using System.ComponentModel;
using MediaLock.App.ViewModels;
using MediaLock.Application;
using MediaLock.Core.Configuration;
using MediaLock.Core.Routing;
using Xunit;

namespace MediaLock.App.Tests;

public sealed class SettingsViewModelTests
{
    [Theory]
    [InlineData("-1")]
    [InlineData("301")]
    [InlineData("1e2")]
    [InlineData("1e309")]
    [InlineData("∞")]
    [InlineData("not-a-number")]
    public async Task InvalidRecoveryTimeoutInputIsReportedImmediatelyAndCannotBeSaved(
        string input)
    {
        var application = new FakeApplication(MediaLockApplicationState.Initial);
        using var viewModel = new SettingsViewModel(application);
        var validation = Assert.IsAssignableFrom<INotifyDataErrorInfo>(viewModel);
        var changedProperties = new List<string?>();
        validation.ErrorsChanged += (_, args) => changedProperties.Add(args.PropertyName);

        viewModel.RecoveryTimeoutText = input;

        Assert.True(validation.HasErrors);
        Assert.Contains(nameof(SettingsViewModel.RecoveryTimeoutText), changedProperties);
        Assert.NotEmpty(validation.GetErrors(nameof(SettingsViewModel.RecoveryTimeoutText)).Cast<string>());
        Assert.False(viewModel.SaveCommand.CanExecute(null));

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Empty(application.Intents);
    }

    [Theory]
    [InlineData("0", 0.0)]
    [InlineData("0.5", 0.5)]
    [InlineData("300", 300.0)]
    public async Task RecoveryTimeoutAcceptsFiniteDecimalValuesAndBoundaries(
        string input,
        double expectedSeconds)
    {
        var application = new FakeApplication(MediaLockApplicationState.Initial);
        using var viewModel = new SettingsViewModel(application);
        var validation = Assert.IsAssignableFrom<INotifyDataErrorInfo>(viewModel);

        viewModel.RecoveryTimeoutText = input;

        Assert.False(validation.HasErrors);
        Assert.True(viewModel.SaveCommand.CanExecute(null));
        await viewModel.SaveCommand.ExecuteAsync(null);

        var intent = Assert.IsType<ApplicationIntent.UpdateSettings>(
            Assert.Single(application.Intents));
        Assert.Equal(
            TimeSpan.FromSeconds(expectedSeconds),
            intent.Settings.Recovery!.Timeout);
    }

    [Fact]
    public async Task StartupRoutingModeIsReadOnlyAndPreservedWhenSettingsAreSaved()
    {
        var state = MediaLockApplicationState.Initial with
        {
            Settings = MediaLockSettings.Default with
            {
                DefaultRoutingMode = RoutingMode.PriorityRules,
            },
        };
        var application = new FakeApplication(state);
        using var viewModel = new SettingsViewModel(application);

        Assert.Equal("Priority Rules", viewModel.StartupRoutingModeText);
        viewModel.RecoveryTimeoutSeconds = 30;
        await viewModel.SaveCommand.ExecuteAsync(null);

        var intent = Assert.IsType<ApplicationIntent.UpdateSettings>(
            Assert.Single(application.Intents));
        Assert.Equal(RoutingMode.PriorityRules, intent.Settings.DefaultRoutingMode);
    }

    [Fact]
    public void StartupRoutingModeUpdateDoesNotOverwriteUnsavedSettingsEdits()
    {
        var application = new FakeApplication(MediaLockApplicationState.Initial);
        using var viewModel = new SettingsViewModel(application);
        viewModel.CloseToTray = false;
        viewModel.RecoveryTimeoutSeconds = 42;

        application.Publish(application.State with
        {
            Settings = application.State.Settings with
            {
                DefaultRoutingMode = RoutingMode.PriorityRules,
            },
        });

        Assert.Equal("Priority Rules", viewModel.StartupRoutingModeText);
        Assert.False(viewModel.CloseToTray);
        Assert.Equal(42, viewModel.RecoveryTimeoutSeconds);
    }

    [Fact]
    public void UnrelatedRouterUpdateDoesNotOverwriteUnsavedSettingsEdits()
    {
        var application = new FakeApplication(MediaLockApplicationState.Initial);
        using var viewModel = new SettingsViewModel(application);
        viewModel.CloseToTray = false;
        viewModel.RecoveryTimeoutSeconds = 42;

        application.Publish(application.State with
        {
            Router = application.State.Router with { Revision = 1 },
        });

        Assert.False(viewModel.CloseToTray);
        Assert.Equal(42, viewModel.RecoveryTimeoutSeconds);
    }

    [Fact]
    public async Task DesktopSwitchesSaveThroughTheApplicationSeam()
    {
        var application = new FakeApplication(MediaLockApplicationState.Initial);
        using var viewModel = new SettingsViewModel(application);
        viewModel.CloseToTray = false;
        viewModel.StartWithWindows = true;
        viewModel.InterceptMediaKeys = false;
        viewModel.SelectedLanguageOption = Assert.Single(
            viewModel.Languages,
            option => option.Value == UiLanguagePreference.TraditionalChinese);
        viewModel.SelectedThemeOption = Assert.Single(
            viewModel.Themes,
            option => option.Value == UiThemePreference.Dark);

        await viewModel.SaveCommand.ExecuteAsync(null);

        var intent = Assert.IsType<ApplicationIntent.UpdateSettings>(
            Assert.Single(application.Intents));
        Assert.False(intent.Settings.Desktop!.CloseToTray);
        Assert.True(intent.Settings.Desktop.StartWithWindows);
        Assert.False(intent.Settings.Desktop.InterceptMediaKeys);
        Assert.Equal(UiLanguagePreference.TraditionalChinese, intent.Settings.Desktop.Language);
        Assert.Equal(UiThemePreference.Dark, intent.Settings.Desktop.Theme);
        Assert.Equal(RoutingMode.WindowsAuto, intent.Settings.DefaultRoutingMode);
    }

    [Fact]
    public void LanguageChoicesIncludeSystemEnglishAndTraditionalChinese()
    {
        var application = new FakeApplication(MediaLockApplicationState.Initial);
        using var viewModel = new SettingsViewModel(application);

        Assert.Equal(
            [
                UiLanguagePreference.System,
                UiLanguagePreference.EnglishUnitedStates,
                UiLanguagePreference.TraditionalChinese,
            ],
            viewModel.Languages.Select(option => option.Value));
    }

    [Fact]
    public void ThemeChoicesIncludeSystemLightAndDark()
    {
        var application = new FakeApplication(MediaLockApplicationState.Initial);
        using var viewModel = new SettingsViewModel(application);

        Assert.Equal(
            [UiThemePreference.System, UiThemePreference.Light, UiThemePreference.Dark],
            viewModel.Themes.Select(option => option.Value));
    }

    [Fact]
    public async Task SuccessfulSaveRequestsSettingsClose()
    {
        var application = new FakeApplication(MediaLockApplicationState.Initial);
        var callbacks = new List<string>();
        string? appliedLanguage = null;
        string? appliedTheme = null;
        using var viewModel = new SettingsViewModel(
            application,
            requestClose: () => callbacks.Add("close"),
            applyLanguage: language =>
            {
                appliedLanguage = language;
                callbacks.Add("apply");
            },
            applyTheme: theme =>
            {
                appliedTheme = theme;
                callbacks.Add("theme");
            });
        viewModel.SelectedLanguage = UiLanguagePreference.TraditionalChinese;
        viewModel.SelectedTheme = UiThemePreference.Dark;

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(["apply", "theme", "close"], callbacks);
        Assert.Equal(UiLanguagePreference.TraditionalChinese, appliedLanguage);
        Assert.Equal(UiThemePreference.Dark, appliedTheme);
    }

    [Fact]
    public async Task CancelDiscardsUnsavedEditsWithoutSavingAndRequestsClose()
    {
        var application = new FakeApplication(MediaLockApplicationState.Initial);
        var closeRequests = 0;
        using var viewModel = new SettingsViewModel(
            application,
            requestClose: () => closeRequests++);
        viewModel.CloseToTray = false;
        viewModel.RecoveryTimeoutSeconds = 42;

        await viewModel.CancelCommand.ExecuteAsync(null);

        Assert.Empty(application.Intents);
        Assert.True(viewModel.CloseToTray);
        Assert.Equal(15, viewModel.RecoveryTimeoutSeconds);
        Assert.Equal(1, closeRequests);
    }

    [Fact]
    public async Task FailedSaveKeepsSettingsOpenAndShowsTheError()
    {
        var application = new FakeApplication(MediaLockApplicationState.Initial)
        {
            DispatchException = new InvalidOperationException("Settings could not be saved."),
        };
        var closeRequests = 0;
        var languageApplyRequests = 0;
        var themeApplyRequests = 0;
        using var viewModel = new SettingsViewModel(
            application,
            requestClose: () => closeRequests++,
            applyLanguage: _ => languageApplyRequests++,
            applyTheme: _ => themeApplyRequests++);

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(0, closeRequests);
        Assert.Equal(0, languageApplyRequests);
        Assert.Equal(0, themeApplyRequests);
        Assert.Equal("Settings could not be saved.", viewModel.ErrorMessage);
    }

    [Fact]
    public async Task PriorityRulesCanBeAddedOrderedDisabledRemovedAndSaved()
    {
        var state = MediaLockApplicationState.Initial with
        {
            Router = RouterState.Initial with
            {
                Sessions =
                [
                    Session("music", "Brave._crx_music"),
                    Session("video", "Brave"),
                ],
            },
            Settings = MediaLockSettings.Default with
            {
                DefaultRoutingMode = RoutingMode.PriorityRules,
                PriorityRules = [new PriorityRule("Brave")],
            },
        };
        var application = new FakeApplication(state);
        using var viewModel = new SettingsViewModel(application);

        viewModel.SelectedAvailableApplication = "Brave._crx_music";
        await viewModel.AddPriorityRuleCommand.ExecuteAsync(null);
        Assert.Equal("Brave._crx_music", viewModel.PriorityRules[1].SourceAppUserModelId);
        await viewModel.MovePriorityRuleUpCommand.ExecuteAsync(viewModel.PriorityRules[1]);
        viewModel.PriorityRules[0].IsEnabled = false;
        await viewModel.RemovePriorityRuleCommand.ExecuteAsync(viewModel.PriorityRules[1]);
        await viewModel.SaveCommand.ExecuteAsync(null);

        var intent = Assert.IsType<ApplicationIntent.UpdateSettings>(
            Assert.Single(application.Intents));
        var savedRule = Assert.Single(intent.Settings.PriorityRules);
        Assert.Equal("Brave._crx_music", savedRule.SourceAppUserModelId);
        Assert.False(savedRule.IsEnabled);
        Assert.Equal(RoutingMode.PriorityRules, intent.Settings.DefaultRoutingMode);
    }

    [Fact]
    public void CatalogRefreshWithTheSameApplicationsDoesNotResetTheAvailableList()
    {
        var state = MediaLockApplicationState.Initial with
        {
            Router = RouterState.Initial with
            {
                Sessions = [Session("music", "Brave._crx_music")],
            },
        };
        var application = new FakeApplication(state);
        using var viewModel = new SettingsViewModel(application);
        viewModel.SelectedAvailableApplication = "Brave._crx_music";
        var collectionChanges = 0;
        viewModel.AvailableApplications.CollectionChanged += (_, _) => collectionChanges++;

        application.Publish(state with
        {
            Router = state.Router with { Revision = 1 },
        });

        Assert.Equal("Brave._crx_music", viewModel.SelectedAvailableApplication);
        Assert.Equal(0, collectionChanges);
    }

    private static MediaLock.Core.Media.MediaSessionSnapshot Session(string key, string source) => new(
        new MediaLock.Core.Media.SessionKey(key),
        source,
        MediaLock.Core.Media.PlaybackStatus.Playing,
        MediaLock.Core.Media.MediaCommandCapabilities.All,
        DateTimeOffset.Parse("2026-08-22T00:00:00Z"));

    private sealed class FakeApplication(MediaLockApplicationState initial) : IMediaLockApplication
    {
        public event EventHandler<MediaLockApplicationStateChangedEventArgs>? StateChanged;

        public List<ApplicationIntent> Intents { get; } = [];

        public Exception? DispatchException { get; init; }

        public MediaLockApplicationState State { get; private set; } = initial;

        public ValueTask StartAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask<ApplicationResult> DispatchAsync(
            ApplicationIntent intent,
            CancellationToken cancellationToken)
        {
            if (DispatchException is not null)
            {
                throw DispatchException;
            }

            Intents.Add(intent);
            if (intent is ApplicationIntent.UpdateSettings update)
            {
                State = State with { Settings = update.Settings };
                StateChanged?.Invoke(this, new MediaLockApplicationStateChangedEventArgs(State));
            }

            return ValueTask.FromResult(new ApplicationResult(State, RouteDecision.StateUpdated));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Publish(MediaLockApplicationState state)
        {
            State = state;
            StateChanged?.Invoke(this, new MediaLockApplicationStateChangedEventArgs(state));
        }
    }
}
