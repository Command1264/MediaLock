using MediaLock.App.ViewModels;
using MediaLock.Application;
using MediaLock.Core.Configuration;
using MediaLock.Core.Routing;
using Xunit;

namespace MediaLock.App.Tests;

public sealed class SettingsViewModelTests
{
    [Fact]
    public void UnrelatedRouterUpdateDoesNotOverwriteUnsavedSettingsEdits()
    {
        var application = new FakeApplication(MediaLockApplicationState.Initial);
        using var viewModel = new SettingsViewModel(application);
        Assert.Contains(RoutingMode.AppLock, viewModel.RoutingModes);
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
        viewModel.DefaultRoutingMode = RoutingMode.AppLock;

        await viewModel.SaveCommand.ExecuteAsync(null);

        var intent = Assert.IsType<ApplicationIntent.UpdateSettings>(
            Assert.Single(application.Intents));
        Assert.False(intent.Settings.Desktop!.CloseToTray);
        Assert.True(intent.Settings.Desktop.StartWithWindows);
        Assert.Equal(RoutingMode.AppLock, intent.Settings.DefaultRoutingMode);
    }

    [Fact]
    public async Task SuccessfulSaveRequestsSettingsClose()
    {
        var application = new FakeApplication(MediaLockApplicationState.Initial);
        var closeRequests = 0;
        using var viewModel = new SettingsViewModel(application, requestClose: () => closeRequests++);

        await viewModel.SaveCommand.ExecuteAsync(null);

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
        using var viewModel = new SettingsViewModel(application, requestClose: () => closeRequests++);

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(0, closeRequests);
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
        viewModel.DefaultRoutingMode = RoutingMode.PriorityRules;

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
