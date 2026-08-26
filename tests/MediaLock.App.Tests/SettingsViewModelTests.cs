using System.ComponentModel;
using MediaLock.App.Localization;
using MediaLock.App.ViewModels;
using MediaLock.Application;
using MediaLock.Core.Configuration;
using MediaLock.Core.Routing;
using Xunit;

namespace MediaLock.App.Tests;

[Collection("Localization")]
public sealed class SettingsViewModelTests
{
    [Fact]
    public async Task DiagnosticCommandsUseTheDesktopSupportSeamWithPrivacySafeContent()
    {
        var session = Session("secret-session", "MSEdge") with
        {
            Metadata = new MediaLock.Core.Media.MediaMetadata(
                "Private title",
                "Private artist",
                null,
                null),
        };
        var state = MediaLockApplicationState.Initial with
        {
            Router = RouterState.Initial with { Sessions = [session] },
        };
        var application = new FakeApplication(state);
        var actions = new FakeDesktopSupportActions();
        var environment = new FixedEnvironmentInfoProvider(new AppEnvironmentInfo(
            "0.2.0-rc.3",
            "Windows 11 Enterprise",
            "24H2",
            "26100.9168",
            "X64",
            IsSigned: false));
        using var viewModel = new SettingsViewModel(
            application,
            environmentInfoProvider: environment,
            desktopSupportActions: actions,
            isMediaInputRunning: () => true);

        await viewModel.CopyDiagnosticsCommand.ExecuteAsync(null);
        var copyStatus = viewModel.SupportStatusMessage;
        await viewModel.OpenLogsFolderCommand.ExecuteAsync(null);
        await viewModel.OpenSupportCommand.ExecuteAsync(null);
        await viewModel.ReportBugCommand.ExecuteAsync(null);

        Assert.Equal(
            [
                DesktopSupportAction.CopyDiagnostics,
                DesktopSupportAction.OpenLogsFolder,
                DesktopSupportAction.OpenSupport,
                DesktopSupportAction.ReportBug,
            ],
            actions.Requests.Select(request => request.Action));
        var copied = Assert.Single(
            actions.Requests,
            request => request.Action == DesktopSupportAction.CopyDiagnostics);
        Assert.Contains("Version: 0.2.0-rc.3", copied.DiagnosticSummary, StringComparison.Ordinal);
        Assert.Contains("Media-key interception: Active", copied.DiagnosticSummary, StringComparison.Ordinal);
        Assert.DoesNotContain("Private title", copied.DiagnosticSummary, StringComparison.Ordinal);
        Assert.DoesNotContain("Private artist", copied.DiagnosticSummary, StringComparison.Ordinal);
        Assert.NotNull(copyStatus);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task CopiedDiagnosticsConfirmationExpires()
    {
        var application = new FakeApplication(MediaLockApplicationState.Initial);
        using var viewModel = new SettingsViewModel(
            application,
            desktopSupportActions: new FakeDesktopSupportActions());

        await viewModel.CopyDiagnosticsCommand.ExecuteAsync(null);
        await Task.Delay(TimeSpan.FromSeconds(5.5));

        Assert.Null(viewModel.SupportStatusMessage);
    }

    [Fact]
    public async Task CancelingSettingsClearsCopiedDiagnosticsConfirmation()
    {
        var application = new FakeApplication(MediaLockApplicationState.Initial);
        using var viewModel = new SettingsViewModel(
            application,
            desktopSupportActions: new FakeDesktopSupportActions());

        await viewModel.CopyDiagnosticsCommand.ExecuteAsync(null);
        await viewModel.CancelCommand.ExecuteAsync(null);

        Assert.Null(viewModel.SupportStatusMessage);
    }

    [Fact]
    public async Task SavingSettingsClearsCopiedDiagnosticsConfirmation()
    {
        var application = new FakeApplication(MediaLockApplicationState.Initial);
        using var viewModel = new SettingsViewModel(
            application,
            desktopSupportActions: new FakeDesktopSupportActions());

        await viewModel.CopyDiagnosticsCommand.ExecuteAsync(null);
        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Null(viewModel.SupportStatusMessage);
    }

    [Fact]
    public async Task VisibleCopiedDiagnosticsConfirmationUsesTheCurrentLanguage()
    {
        UiText.Apply(UiLanguagePreference.EnglishUnitedStates);
        try
        {
            var application = new FakeApplication(MediaLockApplicationState.Initial);
            using var viewModel = new SettingsViewModel(
                application,
                desktopSupportActions: new FakeDesktopSupportActions());

            await viewModel.CopyDiagnosticsCommand.ExecuteAsync(null);
            Assert.Equal(
                "Diagnostics copied. Review the text before sharing it.",
                viewModel.SupportStatusMessage);

            UiText.Apply(UiLanguagePreference.TraditionalChinese);

            Assert.Equal(
                "已複製診斷摘要。分享前請先檢閱內容。",
                viewModel.SupportStatusMessage);
        }
        finally
        {
            UiText.Apply(UiLanguagePreference.EnglishUnitedStates);
        }
    }

    [Fact]
    public async Task FailedDesktopSupportActionShowsAnActionableError()
    {
        var application = new FakeApplication(MediaLockApplicationState.Initial);
        var actions = new FakeDesktopSupportActions
        {
            Exception = new InvalidOperationException("Clipboard is unavailable."),
        };
        using var viewModel = new SettingsViewModel(
            application,
            environmentInfoProvider: new FixedEnvironmentInfoProvider(new AppEnvironmentInfo(
                "0.2.0-rc.3",
                "Windows 11",
                "24H2",
                "26100.9168",
                "X64",
                IsSigned: false)),
            desktopSupportActions: actions);

        await viewModel.CopyDiagnosticsCommand.ExecuteAsync(null);

        Assert.Contains("Clipboard is unavailable.", viewModel.ErrorMessage, StringComparison.Ordinal);
        Assert.Null(viewModel.SupportStatusMessage);
    }

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

    [Theory]
    [InlineData("0", "3", nameof(SettingsViewModel.RepeatedPauseWindowText))]
    [InlineData("61", "3", nameof(SettingsViewModel.RepeatedPauseWindowText))]
    [InlineData("1e2", "3", nameof(SettingsViewModel.RepeatedPauseWindowText))]
    [InlineData("5", "1", nameof(SettingsViewModel.RepeatedPauseCountText))]
    [InlineData("5", "11", nameof(SettingsViewModel.RepeatedPauseCountText))]
    public void InvalidRepeatedPauseSettingsAreReportedImmediately(
        string window,
        string count,
        string invalidProperty)
    {
        using var viewModel = new SettingsViewModel(
            new FakeApplication(MediaLockApplicationState.Initial));

        viewModel.RepeatedPauseWindowText = window;
        viewModel.RepeatedPauseCountText = count;

        Assert.True(viewModel.HasErrors);
        Assert.NotEmpty(viewModel.GetErrors(invalidProperty).Cast<string>());
        Assert.False(viewModel.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task RepeatedPauseOverrideSettingsSaveThroughTheApplicationSeam()
    {
        var application = new FakeApplication(MediaLockApplicationState.Initial);
        using var viewModel = new SettingsViewModel(application)
        {
            RepeatedPauseOverrideEnabled = false,
            RepeatedPauseWindowText = "12",
            RepeatedPauseCountText = "4",
            PlayRepeatedPauseOverrideSound = false,
        };

        await viewModel.SaveCommand.ExecuteAsync(null);

        var intent = Assert.IsType<ApplicationIntent.UpdateSettings>(
            Assert.Single(application.Intents));
        Assert.Equal(
            new PlaybackStateLockSettings(false, TimeSpan.FromSeconds(12), 4, false),
            intent.Settings.PlaybackStateLock);
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
    public async Task PresentationFailureRollsBackTheCommittedSettingsAndKeepsSettingsOpen()
    {
        var initial = MediaLockApplicationState.Initial;
        var application = new FakeApplication(initial);
        var closeRequests = 0;
        var appliedLanguages = new List<string>();
        var appliedThemes = new List<string>();
        using var viewModel = new SettingsViewModel(
            application,
            requestClose: () => closeRequests++,
            applyLanguage: appliedLanguages.Add,
            applyTheme: theme =>
            {
                appliedThemes.Add(theme);
                if (theme == UiThemePreference.Dark)
                {
                    throw new InvalidOperationException("Theme could not be applied.");
                }
            });
        viewModel.SelectedLanguage = UiLanguagePreference.TraditionalChinese;
        viewModel.SelectedTheme = UiThemePreference.Dark;

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(0, closeRequests);
        Assert.Equal(initial.Settings, application.State.Settings);
        Assert.Equal(2, application.Intents.Count);
        Assert.Equal(
            [UiLanguagePreference.TraditionalChinese, UiLanguagePreference.System],
            appliedLanguages);
        Assert.Equal([UiThemePreference.Dark, UiThemePreference.System], appliedThemes);
        Assert.Contains(
            "Settings presentation could not be applied",
            viewModel.ErrorMessage,
            StringComparison.Ordinal);
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
    public async Task PriorityRulePresentationUsesFriendlyNamesButPersistsRawIdentity()
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
        var metadata = new Dictionary<string, SourceApplicationMetadata>
        {
            ["Brave._crx_music"] = new("YouTube Music", "Brave Browser"),
            ["Brave"] = new("Brave"),
        };
        var application = new FakeApplication(state);
        using var viewModel = new SettingsViewModel(
            application,
            sourceApplicationMetadataResolver: new FakeSourceApplicationMetadataResolver(metadata));

        var available = Assert.Single(viewModel.AvailableApplications);
        Assert.Equal("Brave._crx_music", available.SourceAppUserModelId);
        Assert.Equal("YouTube Music — Brave Browser", available.DisplayName);
        Assert.Equal("Brave", Assert.Single(viewModel.PriorityRules).DisplayName);

        viewModel.SelectedAvailableApplication = available.SourceAppUserModelId;
        await viewModel.AddPriorityRuleCommand.ExecuteAsync(null);
        await viewModel.SaveCommand.ExecuteAsync(null);

        var intent = Assert.IsType<ApplicationIntent.UpdateSettings>(
            Assert.Single(application.Intents));
        Assert.Equal(
            ["Brave", "Brave._crx_music"],
            intent.Settings.PriorityRules.Select(rule => rule.SourceAppUserModelId));
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

    private sealed class FixedEnvironmentInfoProvider(AppEnvironmentInfo info)
        : IAppEnvironmentInfoProvider
    {
        public AppEnvironmentInfo GetCurrent() => info;
    }

    private sealed class FakeDesktopSupportActions : IDesktopSupportActions
    {
        public List<DesktopSupportRequest> Requests { get; } = [];

        public Exception? Exception { get; init; }

        public ValueTask ExecuteAsync(
            DesktopSupportRequest request,
            CancellationToken cancellationToken)
        {
            if (Exception is not null)
            {
                throw Exception;
            }

            Requests.Add(request);
            return ValueTask.CompletedTask;
        }
    }

}
