using MediaLock.App.ViewModels;
using MediaLock.Application;
using MediaLock.Core.Media;
using MediaLock.Core.Routing;
using Xunit;

namespace MediaLock.App.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void NowPlayingUsesTheRoutedTargetAndInterpolatesOnlyWhilePlaying()
    {
        var observedAt = DateTimeOffset.Parse("2026-08-23T06:00:00Z");
        var clock = new TestTimeProvider(observedAt.AddSeconds(5));
        Assert.True(MediaArtwork.TryCreate([0xFF, 0xD8, 0xFF, 0x01], out var artwork));
        var routed = new MediaSessionSnapshot(
            new SessionKey("music"),
            "Brave.Music",
            PlaybackStatus.Playing,
            MediaCommandCapabilities.All,
            observedAt,
            Metadata: new MediaMetadata("Song", "Artist", null, null),
            Timeline: new MediaTimeline(
                TimeSpan.Zero,
                TimeSpan.FromMinutes(3),
                TimeSpan.FromSeconds(30),
                observedAt),
            Artwork: artwork);
        var selectedOnly = new MediaSessionSnapshot(
            new SessionKey("video"),
            "Brave",
            PlaybackStatus.Paused,
            MediaCommandCapabilities.All,
            observedAt,
            Metadata: new MediaMetadata("Other", "Publisher", null, null));
        var application = new FakeApplication(new MediaLockApplicationState(
            RouterState.Initial with
            {
                Sessions = [routed, selectedOnly],
                WindowsCurrentSession = routed.Key,
                Revision = 1,
            }));
        using var viewModel = new MainWindowViewModel(
            application,
            synchronizationContext: null,
            timeProvider: clock);
        viewModel.SelectedSession = Assert.Single(viewModel.Sessions, item => item.Key == selectedOnly.Key);

        viewModel.RefreshTimeline();

        Assert.Equal("Song", viewModel.NowPlayingTitle);
        Assert.Equal("Artist", viewModel.NowPlayingArtist);
        Assert.Same(artwork, viewModel.NowPlayingArtwork);
        Assert.True(viewModel.HasNowPlayingTimeline);
        Assert.Equal("0:35", viewModel.NowPlayingElapsed);
        Assert.Equal("3:00", viewModel.NowPlayingDuration);
        Assert.Equal(35d / 180d, viewModel.NowPlayingProgress, precision: 6);

        application.Publish(new MediaLockApplicationState(
            application.State.Router with
            {
                Sessions = [routed with { PlaybackStatus = PlaybackStatus.Paused }],
                Revision = 2,
            }));
        clock.Advance(TimeSpan.FromSeconds(20));
        viewModel.RefreshTimeline();

        Assert.Equal("0:30", viewModel.NowPlayingElapsed);
    }

    [Fact]
    public void InvalidOrMissingTimelineIsHiddenAndCannotRetainThePreviousTarget()
    {
        var observedAt = DateTimeOffset.Parse("2026-08-23T06:00:00Z");
        var session = new MediaSessionSnapshot(
            new SessionKey("music"),
            "Brave.Music",
            PlaybackStatus.Playing,
            MediaCommandCapabilities.All,
            observedAt,
            Timeline: new MediaTimeline(
                TimeSpan.FromMinutes(1),
                TimeSpan.FromMinutes(1),
                TimeSpan.FromMinutes(2),
                observedAt));
        var application = new FakeApplication(StateWith(session));
        using var viewModel = new MainWindowViewModel(application, synchronizationContext: null);

        Assert.False(viewModel.HasNowPlayingTimeline);

        application.Publish(MediaLockApplicationState.Initial);

        Assert.Null(viewModel.NowPlayingArtwork);
        Assert.False(viewModel.HasNowPlayingTimeline);
        Assert.Equal(string.Empty, viewModel.NowPlayingElapsed);
    }

    [Fact]
    public async Task SettingsCommandUsesTheDesktopNavigationSeam()
    {
        var application = new FakeApplication(MediaLockApplicationState.Initial);
        var opened = 0;
        using var viewModel = new MainWindowViewModel(
            application,
            synchronizationContext: null,
            showSettings: () => opened++);

        await viewModel.SettingsCommand.ExecuteAsync(null);

        Assert.Equal(1, opened);
    }

    [Fact]
    public async Task SessionSelectionCanBeLockedThroughTheApplicationSeam()
    {
        var session = new MediaSessionSnapshot(
            new SessionKey("music"),
            "Brave",
            PlaybackStatus.Playing,
            MediaCommandCapabilities.All,
            DateTimeOffset.Parse("2026-08-22T00:00:00Z"),
            Metadata: new MediaMetadata("Song", "Artist", null, null));
        var application = new FakeApplication(StateWith(session));
        using var viewModel = new MainWindowViewModel(application, synchronizationContext: null);

        var item = Assert.Single(viewModel.Sessions);
        viewModel.SelectedSession = item;
        await viewModel.LockCommand.ExecuteAsync(null);

        Assert.Equal("Brave", item.SourceApplication);
        Assert.Equal("Song", item.Title);
        var intent = Assert.IsType<ApplicationIntent.LockSession>(Assert.Single(application.Intents));
        Assert.Equal(session.Key, intent.Session);
    }

    [Fact]
    public async Task SelectedApplicationCanBeLockedThroughTheApplicationSeam()
    {
        var session = new MediaSessionSnapshot(
            new SessionKey("music"),
            "Brave._crx_music",
            PlaybackStatus.Playing,
            MediaCommandCapabilities.All,
            DateTimeOffset.Parse("2026-08-22T00:00:00Z"));
        var application = new FakeApplication(StateWith(session));
        using var viewModel = new MainWindowViewModel(application, synchronizationContext: null);
        viewModel.SelectedSession = Assert.Single(viewModel.Sessions);

        await viewModel.AppLockCommand.ExecuteAsync(null);

        var intent = Assert.IsType<ApplicationIntent.LockApplication>(
            Assert.Single(application.Intents));
        Assert.Equal("Brave._crx_music", intent.SourceAppUserModelId);
    }

    [Fact]
    public void RecoveringStateShowsAnActionableEmptyState()
    {
        var application = new FakeApplication(MediaLockApplicationState.Initial);
        using var viewModel = new MainWindowViewModel(application, synchronizationContext: null);

        application.Publish(new MediaLockApplicationState(
            RouterState.Initial with
            {
                Mode = RoutingMode.SessionLock,
                Status = RouterStatus.Recovering,
                RecoveryEpoch = 7,
                Revision = 2,
            }));

        Assert.Equal("Recovering", viewModel.RoutingStatus);
        Assert.Equal("Waiting for the locked Media Session to return.", viewModel.EmptyStateText);
        Assert.False(viewModel.HasSessions);
        Assert.False(viewModel.NextCommand.CanExecute(null));
    }

    [Fact]
    public void AppLockIsExplicitInTheRoutingStatus()
    {
        var application = new FakeApplication(MediaLockApplicationState.Initial);
        using var viewModel = new MainWindowViewModel(application, synchronizationContext: null);

        application.Publish(MediaLockApplicationState.Initial with
        {
            Router = RouterState.Initial with
            {
                Mode = RoutingMode.AppLock,
                Status = RouterStatus.Locked,
                Revision = 1,
            },
        });

        Assert.Equal("App Locked", viewModel.RoutingStatus);
    }

    [Fact]
    public async Task PriorityRulesCanBeActivatedThroughTheApplicationSeam()
    {
        var application = new FakeApplication(MediaLockApplicationState.Initial);
        using var viewModel = new MainWindowViewModel(application, synchronizationContext: null);

        await viewModel.PriorityRulesCommand.ExecuteAsync(null);

        Assert.IsType<ApplicationIntent.UsePriorityRules>(Assert.Single(application.Intents));
    }

    [Fact]
    public void PriorityRulesAreExplicitInTheRoutingStatus()
    {
        var application = new FakeApplication(MediaLockApplicationState.Initial);
        using var viewModel = new MainWindowViewModel(application, synchronizationContext: null);

        application.Publish(MediaLockApplicationState.Initial with
        {
            Router = RouterState.Initial with
            {
                Mode = RoutingMode.PriorityRules,
                Revision = 1,
            },
        });

        Assert.Equal("Priority Rules", viewModel.RoutingStatus);
    }

    [Fact]
    public void PriorityRulesWithoutATargetDoNotClaimALockedTarget()
    {
        var application = new FakeApplication(MediaLockApplicationState.Initial);
        using var viewModel = new MainWindowViewModel(application, synchronizationContext: null);

        application.Publish(MediaLockApplicationState.Initial with
        {
            Router = RouterState.Initial with
            {
                Mode = RoutingMode.PriorityRules,
                Revision = 1,
            },
        });

        Assert.Equal(
            "No Priority Rule or Windows Current Session is available.",
            viewModel.TargetDescription);
    }

    [Fact]
    public void CatalogReacquisitionOverridesRoutingStatusWithActionableState()
    {
        var application = new FakeApplication(MediaLockApplicationState.Initial);
        using var viewModel = new MainWindowViewModel(application, synchronizationContext: null);

        application.Publish(MediaLockApplicationState.Initial with
        {
            CatalogStatus = MediaSessionCatalogStatus.Reacquiring,
            CatalogStatusMessage = "Reacquiring GSMTC after Windows resumed.",
        });

        Assert.Equal("Reacquiring", viewModel.RoutingStatus);
        Assert.Equal(
            "Reacquiring media sessions after Windows resumed.",
            viewModel.EmptyStateText);
        Assert.False(viewModel.NextCommand.CanExecute(null));
    }

    [Fact]
    public async Task ManualCommandUsesResolvedTargetCapabilities()
    {
        var session = new MediaSessionSnapshot(
            new SessionKey("music"),
            "Brave",
            PlaybackStatus.Playing,
            MediaCommandCapabilities.Next,
            DateTimeOffset.Parse("2026-08-22T00:00:00Z"));
        var application = new FakeApplication(new MediaLockApplicationState(
            StateWith(session).Router with
            {
                Mode = RoutingMode.SessionLock,
                Status = RouterStatus.Locked,
                LockedTarget = new LockedTarget(
                    new SessionFingerprint(
                        session.Descriptor,
                        session.PlaybackStatus,
                        session.ObservedAt,
                        session.PlaybackType,
                        session.Metadata?.Title,
                        session.Metadata?.Artist),
                    session.Key),
            }));
        using var viewModel = new MainWindowViewModel(application, synchronizationContext: null);

        Assert.True(viewModel.NextCommand.CanExecute(null));
        Assert.False(viewModel.PauseCommand.CanExecute(null));
        await viewModel.NextCommand.ExecuteAsync(null);

        var intent = Assert.IsType<ApplicationIntent.Route>(Assert.Single(application.Intents));
        Assert.Equal(MediaCommand.Next, intent.Command);
    }

    [Fact]
    public void ApplicationFailureIsPresentedAsAnActionableErrorState()
    {
        var application = new FakeApplication(MediaLockApplicationState.Initial);
        using var viewModel = new MainWindowViewModel(application, synchronizationContext: null);

        application.Publish(new MediaLockApplicationState(
            RouterState.Initial,
            "GSMTC catalog became unavailable."));

        Assert.True(viewModel.HasError);
        Assert.Equal("GSMTC catalog became unavailable.", viewModel.ErrorMessage);
    }

    [Fact]
    public async Task FailedManualControlIsPresentedInsteadOfBeingSwallowed()
    {
        var session = new MediaSessionSnapshot(
            new SessionKey("music"),
            "Brave",
            PlaybackStatus.Playing,
            MediaCommandCapabilities.Next,
            DateTimeOffset.Parse("2026-08-22T00:00:00Z"));
        var application = new FakeApplication(StateWith(session))
        {
            Decision = new RouteDecision(
                RouteDecisionKind.Failed,
                RouteReason.ControlFailed,
                MediaCommand.Next,
                session.Key,
                Error: "GSMTC control failed."),
        };
        using var viewModel = new MainWindowViewModel(application, synchronizationContext: null);

        await viewModel.NextCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasError);
        Assert.Equal("GSMTC control failed.", viewModel.ErrorMessage);
    }

    [Fact]
    public async Task ApplicationExceptionBecomesUiErrorInsteadOfEscapingTheCommand()
    {
        var session = new MediaSessionSnapshot(
            new SessionKey("music"),
            "Brave",
            PlaybackStatus.Playing,
            MediaCommandCapabilities.Next,
            DateTimeOffset.Parse("2026-08-22T00:00:00Z"));
        var application = new FakeApplication(StateWith(session))
        {
            DispatchException = new InvalidOperationException("Session changed before lock."),
        };
        using var viewModel = new MainWindowViewModel(application, synchronizationContext: null);

        await viewModel.NextCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasError);
        Assert.Equal("Session changed before lock.", viewModel.ErrorMessage);
    }

    private static MediaLockApplicationState StateWith(MediaSessionSnapshot session) => new(
        RouterState.Initial with
        {
            Sessions = [session],
            WindowsCurrentSession = session.Key,
            Revision = 1,
        });

    private sealed class FakeApplication(MediaLockApplicationState state) : IMediaLockApplication
    {
        public event EventHandler<MediaLockApplicationStateChangedEventArgs>? StateChanged;

        public List<ApplicationIntent> Intents { get; } = [];

        public RouteDecision Decision { get; set; } = RouteDecision.StateUpdated;

        public Exception? DispatchException { get; set; }

        public MediaLockApplicationState State { get; private set; } = state;

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
            return ValueTask.FromResult(new ApplicationResult(State, Decision));
        }

        public void Publish(MediaLockApplicationState next)
        {
            State = next;
            StateChanged?.Invoke(this, new MediaLockApplicationStateChangedEventArgs(next));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TestTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset current = utcNow;

        public override DateTimeOffset GetUtcNow() => current;

        public void Advance(TimeSpan amount) => current += amount;
    }
}
