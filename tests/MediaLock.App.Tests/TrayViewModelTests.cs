using MediaLock.App.ViewModels;
using MediaLock.Application;
using MediaLock.Core.Media;
using MediaLock.Core.Routing;
using Xunit;

namespace MediaLock.App.Tests;

public sealed class TrayViewModelTests
{
    [Fact]
    public async Task TrayProjectsRoutingStateAndSubmitsEssentialActions()
    {
        var application = new FakeApplication(MediaLockApplicationState.Initial);
        var shown = 0;
        var settingsShown = 0;
        var exited = 0;
        using var viewModel = new TrayViewModel(
            application,
            () => shown++,
            () => exited++,
            showSettings: () => settingsShown++);

        application.Publish(application.State with
        {
            Router = RouterState.Initial with
            {
                Mode = RoutingMode.SessionLock,
                Status = RouterStatus.Locked,
                Revision = 1,
            },
        });
        Assert.Equal("Locked", viewModel.StatusText);
        application.Publish(application.State with
        {
            Router = application.State.Router with
            {
                Status = RouterStatus.Recovering,
                RecoveryEpoch = 2,
                Revision = 2,
            },
        });
        Assert.Equal("Recovering", viewModel.StatusText);
        application.Publish(application.State with
        {
            CatalogStatus = MediaSessionCatalogStatus.Reacquiring,
        });
        Assert.Equal("Reacquiring", viewModel.StatusText);

        await viewModel.ShowCommand.ExecuteAsync(null);
        await viewModel.SettingsCommand.ExecuteAsync(null);
        await viewModel.TogglePlayPauseCommand.ExecuteAsync(null);
        await viewModel.WindowsAutoCommand.ExecuteAsync(null);
        await viewModel.ExitCommand.ExecuteAsync(null);

        Assert.Equal(1, shown);
        Assert.Equal(1, settingsShown);
        Assert.Equal(1, exited);
        Assert.Collection(
            application.Intents,
            intent => Assert.Equal(
                MediaCommand.TogglePlayPause,
                Assert.IsType<ApplicationIntent.Route>(intent).Command),
            intent => Assert.IsType<ApplicationIntent.UseWindowsAutoForCurrentRun>(intent));
    }

    [Fact]
    public async Task TrayCommandFailureBecomesObservableInsteadOfEscapingTheUiCommand()
    {
        var application = new FakeApplication(MediaLockApplicationState.Initial)
        {
            DispatchFailure = new InvalidOperationException("GSMTC unavailable"),
        };
        using var viewModel = new TrayViewModel(application, () => { }, () => { });

        await viewModel.NextCommand.ExecuteAsync(null);

        Assert.Contains("ML-APP-003", viewModel.ErrorMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("GSMTC unavailable", viewModel.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void TrayMakesAppLockExplicit()
    {
        var application = new FakeApplication(MediaLockApplicationState.Initial);
        using var viewModel = new TrayViewModel(application, () => { }, () => { });

        application.Publish(application.State with
        {
            Router = RouterState.Initial with
            {
                Mode = RoutingMode.AppLock,
                Status = RouterStatus.Locked,
                Revision = 1,
            },
        });

        Assert.Equal("App Locked", viewModel.StatusText);
    }

    [Fact]
    public void PriorityRulesStatusIsExplicit()
    {
        var application = new FakeApplication(MediaLockApplicationState.Initial);
        using var viewModel = new TrayViewModel(application, () => { }, () => { });

        application.Publish(MediaLockApplicationState.Initial with
        {
            Router = RouterState.Initial with
            {
                Mode = RoutingMode.PriorityRules,
                Revision = 1,
            },
        });

        Assert.Equal("Priority Rules", viewModel.StatusText);
    }

    private sealed class FakeApplication(MediaLockApplicationState initial) : IMediaLockApplication
    {
        public event EventHandler<MediaLockApplicationStateChangedEventArgs>? StateChanged;

        public List<ApplicationIntent> Intents { get; } = [];

        public Exception? DispatchFailure { get; init; }

        public MediaLockApplicationState State { get; private set; } = initial;

        public string? LastReportedProblemCode { get; private set; }

        public void Publish(MediaLockApplicationState state)
        {
            State = state;
            StateChanged?.Invoke(this, new MediaLockApplicationStateChangedEventArgs(state));
        }

        public ValueTask StartAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask<ApplicationResult> DispatchAsync(
            ApplicationIntent intent,
            CancellationToken cancellationToken)
        {
            if (DispatchFailure is not null)
            {
                throw DispatchFailure;
            }

            Intents.Add(intent);
            return ValueTask.FromResult(new ApplicationResult(State, RouteDecision.StateUpdated));
        }

        public ValueTask ReportProblemAsync(
            MediaLockProblem problem,
            CancellationToken cancellationToken)
        {
            LastReportedProblemCode = problem.Code;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
