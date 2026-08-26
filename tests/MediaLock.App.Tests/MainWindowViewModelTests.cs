using MediaLock.App.Localization;
using MediaLock.App.ViewModels;
using MediaLock.Application;
using MediaLock.Core.Configuration;
using MediaLock.Core.Media;
using MediaLock.Core.Playback;
using MediaLock.Core.Routing;
using Xunit;

namespace MediaLock.App.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public async Task KeepPlayingCanBeEnabledForThePlayingRoutedTarget()
    {
        var session = new MediaSessionSnapshot(
            new SessionKey("music"),
            "music",
            PlaybackStatus.Playing,
            MediaCommandCapabilities.All,
            DateTimeOffset.Parse("2026-08-24T00:00:00Z"));
        var application = new FakeApplication(StateWith(session));
        using var viewModel = new MainWindowViewModel(
            application,
            synchronizationContext: null);

        Assert.True(viewModel.KeepPlayingCommand.CanExecute(null));
        await viewModel.KeepPlayingCommand.ExecuteAsync(null);

        var intent = Assert.IsType<ApplicationIntent.SetPlaybackStateLock>(
            Assert.Single(application.Intents));
        Assert.Equal(PlaybackStateLockMode.KeepPlaying, intent.Mode);
    }

    [Fact]
    public async Task ActiveKeepPlayingCanBeTurnedOffExplicitly()
    {
        var session = new MediaSessionSnapshot(
            new SessionKey("music"),
            "music",
            PlaybackStatus.Playing,
            MediaCommandCapabilities.All,
            DateTimeOffset.Parse("2026-08-24T00:00:00Z"));
        var application = new FakeApplication(StateWith(session) with
        {
            PlaybackStateLock = new PlaybackStateLockState(
                PlaybackStateLockMode.KeepPlaying,
                PlaybackStateLockStatus.Ready,
                session.Key),
        });
        using var viewModel = new MainWindowViewModel(
            application,
            synchronizationContext: null);

        Assert.True(viewModel.IsKeepPlaying);
        Assert.True(viewModel.PlaybackStateLockOffCommand.CanExecute(null));
        await viewModel.PlaybackStateLockOffCommand.ExecuteAsync(null);

        var intent = Assert.IsType<ApplicationIntent.SetPlaybackStateLock>(
            Assert.Single(application.Intents));
        Assert.Equal(PlaybackStateLockMode.Off, intent.Mode);
    }

    [Fact]
    public void FailedKeepPlayingHasAnActionableLocalizedNotice()
    {
        var application = new FakeApplication(MediaLockApplicationState.Initial with
        {
            PlaybackStateLock = new PlaybackStateLockState(
                PlaybackStateLockMode.KeepPlaying,
                PlaybackStateLockStatus.Failed,
                new SessionKey("music"),
                "diagnostic detail"),
        });
        using var viewModel = new MainWindowViewModel(
            application,
            synchronizationContext: null);

        Assert.True(viewModel.HasPlaybackStateLockNotice);
        Assert.Equal(
            UiText.Get("Main_KeepPlayingFailed"),
            viewModel.PlaybackStateLockNotice);
    }

    [Fact]
    public async Task RepeatedPauseReleasePlaysOneSoundAndShowsATransientNotice()
    {
        var application = new FakeApplication(MediaLockApplicationState.Initial);
        var feedback = new RecordingPlaybackStateLockFeedback();
        using var viewModel = new MainWindowViewModel(
            application,
            synchronizationContext: null,
            playbackStateLockFeedback: feedback,
            playbackStateLockNoticeDuration: TimeSpan.FromMilliseconds(50));
        var released = application.State with
        {
            PlaybackStateLock = new PlaybackStateLockState(
                PlaybackStateLockMode.Off,
                PlaybackStateLockStatus.Released,
                ArmedTarget: null),
        };

        application.Publish(released);
        application.Publish(released with { Router = released.Router with { Revision = 1 } });

        Assert.Equal(1, feedback.PlayCount);
        Assert.True(viewModel.HasPlaybackStateLockNotice);
        Assert.Equal(UiText.Get("Main_KeepPlayingReleased"), viewModel.PlaybackStateLockNotice);
        await Task.Delay(100);
        Assert.False(viewModel.HasPlaybackStateLockNotice);
    }

    [Fact]
    public void RepeatedPauseReleaseRespectsTheSoundSetting()
    {
        var application = new FakeApplication(MediaLockApplicationState.Initial with
        {
            Settings = MediaLockSettings.Default with
            {
                PlaybackStateLock = MediaLockSettings.Default.PlaybackStateLock! with
                {
                    PlayOverrideSound = false,
                },
            },
        });
        var feedback = new RecordingPlaybackStateLockFeedback();
        using var viewModel = new MainWindowViewModel(
            application,
            synchronizationContext: null,
            playbackStateLockFeedback: feedback);

        application.Publish(application.State with
        {
            PlaybackStateLock = new PlaybackStateLockState(
                PlaybackStateLockMode.Off,
                PlaybackStateLockStatus.Released,
                ArmedTarget: null),
        });

        Assert.Equal(0, feedback.PlayCount);
        Assert.True(viewModel.HasPlaybackStateLockNotice);
    }

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
    public async Task CompletedSeekPreviewCommitsOneAbsoluteMediaCommand()
    {
        var observedAt = DateTimeOffset.Parse("2026-08-23T06:00:00Z");
        var session = new MediaSessionSnapshot(
            new SessionKey("music"),
            "Brave.Music",
            PlaybackStatus.Playing,
            MediaCommandCapabilities.All,
            observedAt,
            Timeline: new MediaTimeline(
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(190),
                TimeSpan.FromSeconds(40),
                observedAt));
        var application = new FakeApplication(StateWith(session))
        {
            Decision = new RouteDecision(
                RouteDecisionKind.Routed,
                RouteReason.WindowsCurrentSession,
                Target: session.Key,
                ControlResult: MediaControlResult.Succeeded),
        };
        using var viewModel = new MainWindowViewModel(application, synchronizationContext: null);

        viewModel.BeginSeekPreview();
        viewModel.PreviewSeek(TimeSpan.FromSeconds(60));
        viewModel.PreviewSeek(TimeSpan.FromSeconds(75));
        await viewModel.CommitSeekPreviewAsync();

        var intent = Assert.IsType<ApplicationIntent.Route>(Assert.Single(application.Intents));
        Assert.Equal(MediaCommandKind.SeekAbsolute, intent.Command.Kind);
        Assert.Equal(TimeSpan.FromSeconds(85), intent.Command.AbsolutePosition);
    }

    [Fact]
    public async Task AcceptedSeekKeepsPreviewUntilATimelineSnapshotConfirmsIt()
    {
        var observedAt = DateTimeOffset.Parse("2026-08-23T06:00:00Z");
        var session = new MediaSessionSnapshot(
            new SessionKey("music"),
            "Brave.Music",
            PlaybackStatus.Paused,
            MediaCommandCapabilities.All,
            observedAt,
            Timeline: new MediaTimeline(
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(190),
                TimeSpan.FromSeconds(40),
                observedAt));
        var application = new FakeApplication(StateWith(session))
        {
            Decision = new RouteDecision(
                RouteDecisionKind.Routed,
                RouteReason.WindowsCurrentSession,
                Target: session.Key,
                ControlResult: MediaControlResult.Succeeded),
        };
        using var viewModel = new MainWindowViewModel(application, synchronizationContext: null);
        viewModel.BeginSeekPreview();
        viewModel.PreviewSeek(TimeSpan.FromSeconds(75));

        await viewModel.CommitSeekPreviewAsync();

        Assert.Equal("1:15", viewModel.NowPlayingElapsed);
        application.Publish(new MediaLockApplicationState(
            application.State.Router with
            {
                Sessions =
                [
                    session with
                    {
                        Timeline = session.Timeline! with
                        {
                            Position = TimeSpan.FromSeconds(85),
                            LastUpdatedAt = observedAt.AddSeconds(1),
                        },
                    },
                ],
                Revision = 2,
            }));
        application.Publish(new MediaLockApplicationState(
            application.State.Router with
            {
                Sessions =
                [
                    session with
                    {
                        Timeline = session.Timeline! with
                        {
                            Position = TimeSpan.FromSeconds(100),
                            LastUpdatedAt = observedAt.AddSeconds(2),
                        },
                    },
                ],
                Revision = 3,
            }));

        Assert.Equal("1:30", viewModel.NowPlayingElapsed);
    }

    [Fact]
    public async Task UnconfirmedSeekReturnsToTheObservedTimelineAfterItsBoundedTimeout()
    {
        var observedAt = DateTimeOffset.Parse("2026-08-23T06:00:00Z");
        var clock = new TestTimeProvider(observedAt);
        var session = new MediaSessionSnapshot(
            new SessionKey("music"),
            "Brave.Music",
            PlaybackStatus.Paused,
            MediaCommandCapabilities.All,
            observedAt,
            Timeline: new MediaTimeline(
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(190),
                TimeSpan.FromSeconds(40),
                observedAt));
        var application = new FakeApplication(StateWith(session))
        {
            Decision = new RouteDecision(
                RouteDecisionKind.Routed,
                RouteReason.WindowsCurrentSession,
                Target: session.Key,
                ControlResult: MediaControlResult.Succeeded),
        };
        using var viewModel = new MainWindowViewModel(
            application,
            synchronizationContext: null,
            timeProvider: clock);
        viewModel.BeginSeekPreview();
        viewModel.PreviewSeek(TimeSpan.FromSeconds(75));
        await viewModel.CommitSeekPreviewAsync();
        clock.Advance(TimeSpan.FromSeconds(2.1));

        viewModel.RefreshTimeline();

        Assert.Equal("0:30", viewModel.NowPlayingElapsed);
        Assert.True(viewModel.HasError);
        Assert.Equal("The requested playback position was not confirmed.", viewModel.ErrorMessage);
    }

    [Fact]
    public async Task SeekAvailabilityBelongsToTheRoutedTargetAndStopsDuringRecovery()
    {
        var observedAt = DateTimeOffset.Parse("2026-08-23T06:00:00Z");
        var routed = new MediaSessionSnapshot(
            new SessionKey("music"),
            "Brave.Music",
            PlaybackStatus.Paused,
            MediaCommandCapabilities.All,
            observedAt,
            Timeline: new MediaTimeline(
                TimeSpan.Zero,
                TimeSpan.FromMinutes(3),
                TimeSpan.FromSeconds(30),
                observedAt));
        var selectedOnly = new MediaSessionSnapshot(
            new SessionKey("video"),
            "Brave",
            PlaybackStatus.Paused,
            MediaCommandCapabilities.TogglePlayPause,
            observedAt,
            Timeline: routed.Timeline);
        var application = new FakeApplication(new MediaLockApplicationState(
            RouterState.Initial with
            {
                Sessions = [routed, selectedOnly],
                WindowsCurrentSession = routed.Key,
                Revision = 1,
            }))
        {
            Decision = new RouteDecision(
                RouteDecisionKind.Routed,
                RouteReason.WindowsCurrentSession,
                Target: routed.Key,
                ControlResult: MediaControlResult.Succeeded),
        };
        using var viewModel = new MainWindowViewModel(application, synchronizationContext: null);
        viewModel.SelectedSession = Assert.Single(
            viewModel.Sessions,
            session => session.Key == selectedOnly.Key);

        Assert.True(viewModel.CanSeek);
        Assert.Equal(180, viewModel.NowPlayingDurationSeconds);
        viewModel.BeginSeekPreview();
        viewModel.PreviewSeek(TimeSpan.FromSeconds(75));
        await viewModel.CommitSeekPreviewAsync();
        application.Publish(application.State with
        {
            Router = application.State.Router with
            {
                Mode = RoutingMode.SessionLock,
                Status = RouterStatus.Recovering,
                WindowsCurrentSession = null,
                LockedTarget = new LockedTarget(
                    SessionFingerprint.From(routed),
                    ResolvedSession: null),
                RecoveryEpoch = 1,
                Revision = 2,
            },
        });

        Assert.False(viewModel.CanSeek);
        Assert.True(viewModel.HasError);
        Assert.Equal("Seeking was interrupted because the media target changed or became unavailable.",
            viewModel.ErrorMessage);
    }

    [Fact]
    public void NegativeAbsoluteTimelineCannotEnableSeek()
    {
        var observedAt = DateTimeOffset.Parse("2026-08-23T06:00:00Z");
        var session = new MediaSessionSnapshot(
            new SessionKey("music"),
            "Brave.Music",
            PlaybackStatus.Paused,
            MediaCommandCapabilities.All,
            observedAt,
            Timeline: new MediaTimeline(
                TimeSpan.FromSeconds(-10),
                TimeSpan.FromMinutes(3),
                TimeSpan.Zero,
                observedAt));
        var application = new FakeApplication(StateWith(session));
        using var viewModel = new MainWindowViewModel(application, synchronizationContext: null);

        Assert.False(viewModel.CanSeek);
    }

    [Fact]
    public async Task RejectedSeekImmediatelyReturnsToTheObservedTimeline()
    {
        var observedAt = DateTimeOffset.Parse("2026-08-23T06:00:00Z");
        var session = new MediaSessionSnapshot(
            new SessionKey("music"),
            "Brave.Music",
            PlaybackStatus.Paused,
            MediaCommandCapabilities.All,
            observedAt,
            Timeline: new MediaTimeline(
                TimeSpan.Zero,
                TimeSpan.FromMinutes(3),
                TimeSpan.FromSeconds(30),
                observedAt));
        var application = new FakeApplication(StateWith(session))
        {
            Decision = new RouteDecision(
                RouteDecisionKind.Skipped,
                RouteReason.ControlRejected,
                MediaCommand.SeekAbsolute(TimeSpan.FromSeconds(75)),
                session.Key,
                MediaControlResult.Rejected),
        };
        using var viewModel = new MainWindowViewModel(application, synchronizationContext: null);
        viewModel.BeginSeekPreview();
        viewModel.PreviewSeek(TimeSpan.FromSeconds(75));

        await viewModel.CommitSeekPreviewAsync();

        Assert.Equal("0:30", viewModel.NowPlayingElapsed);
        Assert.True(viewModel.HasError);
    }

    [Fact]
    public async Task SkippedSeekReturnsToTheObservedTimelineWithAnActionableError()
    {
        var observedAt = DateTimeOffset.Parse("2026-08-23T06:00:00Z");
        var session = new MediaSessionSnapshot(
            new SessionKey("music"),
            "Brave.Music",
            PlaybackStatus.Paused,
            MediaCommandCapabilities.All,
            observedAt,
            Timeline: new MediaTimeline(
                TimeSpan.Zero,
                TimeSpan.FromMinutes(3),
                TimeSpan.FromSeconds(30),
                observedAt));
        var application = new FakeApplication(StateWith(session))
        {
            Decision = new RouteDecision(
                RouteDecisionKind.Skipped,
                RouteReason.SeekTimelineUnavailable,
                MediaCommand.SeekAbsolute(TimeSpan.FromSeconds(75)),
                session.Key),
        };
        using var viewModel = new MainWindowViewModel(application, synchronizationContext: null);
        viewModel.BeginSeekPreview();
        viewModel.PreviewSeek(TimeSpan.FromSeconds(75));

        await viewModel.CommitSeekPreviewAsync();

        Assert.Equal("0:30", viewModel.NowPlayingElapsed);
        Assert.True(viewModel.HasError);
        Assert.Contains(nameof(RouteReason.SeekTimelineUnavailable), viewModel.ErrorMessage);
    }

    [Fact]
    public async Task TargetChangeCannotRetainAPendingSeekPreview()
    {
        var observedAt = DateTimeOffset.Parse("2026-08-23T06:00:00Z");
        var original = new MediaSessionSnapshot(
            new SessionKey("music"),
            "Brave.Music",
            PlaybackStatus.Paused,
            MediaCommandCapabilities.All,
            observedAt,
            Timeline: new MediaTimeline(
                TimeSpan.Zero,
                TimeSpan.FromMinutes(3),
                TimeSpan.FromSeconds(30),
                observedAt));
        var replacement = original with
        {
            Key = new SessionKey("video"),
            SourceAppUserModelId = "Brave",
            Timeline = original.Timeline! with { Position = TimeSpan.FromSeconds(20) },
        };
        var application = new FakeApplication(StateWith(original))
        {
            Decision = new RouteDecision(
                RouteDecisionKind.Routed,
                RouteReason.WindowsCurrentSession,
                Target: original.Key,
                ControlResult: MediaControlResult.Succeeded),
        };
        using var viewModel = new MainWindowViewModel(application, synchronizationContext: null);
        viewModel.BeginSeekPreview();
        viewModel.PreviewSeek(TimeSpan.FromSeconds(75));
        await viewModel.CommitSeekPreviewAsync();

        application.Publish(StateWith(replacement));

        Assert.Equal("0:20", viewModel.NowPlayingElapsed);
        Assert.True(viewModel.HasError);
        Assert.Equal("Seeking was interrupted because the media target changed or became unavailable.",
            viewModel.ErrorMessage);
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

    [Theory]
    [InlineData(RoutingMode.WindowsAuto)]
    [InlineData(RoutingMode.PriorityRules)]
    [InlineData(RoutingMode.AppLock)]
    [InlineData(RoutingMode.SessionLock)]
    public void ExactlyOneRoutingModeSelectionIsProjectedFromRouterMode(RoutingMode mode)
    {
        var application = new FakeApplication(new MediaLockApplicationState(
            RouterState.Initial with
            {
                Mode = mode,
                Status = mode is RoutingMode.AppLock or RoutingMode.SessionLock
                    ? RouterStatus.Recovering
                    : RouterStatus.Ready,
                Revision = 1,
            }));
        using var viewModel = new MainWindowViewModel(application, synchronizationContext: null);

        Assert.Equal(mode == RoutingMode.WindowsAuto, viewModel.IsWindowsAutoMode);
        Assert.Equal(mode == RoutingMode.PriorityRules, viewModel.IsPriorityRulesMode);
        Assert.Equal(mode == RoutingMode.AppLock, viewModel.IsAppLockMode);
        Assert.Equal(mode == RoutingMode.SessionLock, viewModel.IsSessionLockMode);
    }

    [Theory]
    [InlineData(RoutingMode.AppLock)]
    [InlineData(RoutingMode.SessionLock)]
    public void LockedTargetSelectionReturnsAfterRecoveryWithANewSessionKey(RoutingMode mode)
    {
        var observedAt = DateTimeOffset.Parse("2026-08-23T10:00:00Z");
        var brave = new MediaSessionSnapshot(
            new SessionKey("video"),
            "Brave",
            PlaybackStatus.Paused,
            MediaCommandCapabilities.All,
            observedAt);
        var music = new MediaSessionSnapshot(
            new SessionKey("music-old"),
            "Brave._crx_music",
            PlaybackStatus.Playing,
            MediaCommandCapabilities.All,
            observedAt);
        var fingerprint = SessionFingerprint.From(music);
        var application = new FakeApplication(new MediaLockApplicationState(
            RouterState.Initial with
            {
                Mode = mode,
                Status = RouterStatus.Locked,
                Sessions = [brave, music],
                LockedTarget = new LockedTarget(fingerprint, music.Key),
                Revision = 1,
            }));
        using var viewModel = new MainWindowViewModel(application, synchronizationContext: null);
        viewModel.SelectedSession = Assert.Single(
            viewModel.Sessions,
            session => session.Key == music.Key);

        application.Publish(application.State with
        {
            Router = application.State.Router with
            {
                Status = RouterStatus.Recovering,
                Sessions = [brave],
                LockedTarget = new LockedTarget(fingerprint, ResolvedSession: null),
                RecoveryEpoch = 2,
                Revision = 2,
            },
        });
        application.Publish(application.State with
        {
            Router = application.State.Router with { Revision = 3 },
        });

        Assert.Null(viewModel.SelectedSession);
        Assert.False(viewModel.LockCommand.CanExecute(null));
        Assert.False(viewModel.AppLockCommand.CanExecute(null));

        var recovered = music with { Key = new SessionKey("music-new") };
        application.Publish(application.State with
        {
            Router = application.State.Router with
            {
                Status = RouterStatus.Locked,
                Sessions = [brave, recovered],
                LockedTarget = new LockedTarget(fingerprint, recovered.Key),
                RecoveryEpoch = null,
                Revision = 4,
            },
        });

        Assert.Equal(recovered.Key, viewModel.SelectedSession?.Key);
    }

    [Fact]
    public void ExplicitSelectionDuringRecoveryIsNotOverriddenByTheRecoveredTarget()
    {
        var observedAt = DateTimeOffset.Parse("2026-08-23T10:00:00Z");
        var brave = new MediaSessionSnapshot(
            new SessionKey("video"),
            "Brave",
            PlaybackStatus.Paused,
            MediaCommandCapabilities.All,
            observedAt);
        var music = new MediaSessionSnapshot(
            new SessionKey("music-old"),
            "Brave._crx_music",
            PlaybackStatus.Playing,
            MediaCommandCapabilities.All,
            observedAt);
        var fingerprint = SessionFingerprint.From(music);
        var application = new FakeApplication(new MediaLockApplicationState(
            RouterState.Initial with
            {
                Mode = RoutingMode.AppLock,
                Status = RouterStatus.Locked,
                Sessions = [brave, music],
                LockedTarget = new LockedTarget(fingerprint, music.Key),
                Revision = 1,
            }));
        using var viewModel = new MainWindowViewModel(application, synchronizationContext: null);
        viewModel.SelectedSession = Assert.Single(
            viewModel.Sessions,
            session => session.Key == music.Key);
        application.Publish(application.State with
        {
            Router = application.State.Router with
            {
                Status = RouterStatus.Recovering,
                Sessions = [brave],
                LockedTarget = new LockedTarget(fingerprint, ResolvedSession: null),
                RecoveryEpoch = 2,
                Revision = 2,
            },
        });
        viewModel.SelectedSession = Assert.Single(viewModel.Sessions);

        var recovered = music with { Key = new SessionKey("music-new") };
        application.Publish(application.State with
        {
            Router = application.State.Router with
            {
                Status = RouterStatus.Locked,
                Sessions = [brave, recovered],
                LockedTarget = new LockedTarget(fingerprint, recovered.Key),
                RecoveryEpoch = null,
                Revision = 3,
            },
        });

        Assert.Equal(brave.Key, viewModel.SelectedSession?.Key);
    }

    [Theory]
    [InlineData(RoutingMode.WindowsAuto)]
    [InlineData(RoutingMode.PriorityRules)]
    public void AutomaticModesRestoreTheSelectionBookmarkInsteadOfFallingBackToTheFirstRow(
        RoutingMode mode)
    {
        var observedAt = DateTimeOffset.Parse("2026-08-23T10:00:00Z");
        var brave = new MediaSessionSnapshot(
            new SessionKey("video"),
            "Brave",
            PlaybackStatus.Paused,
            MediaCommandCapabilities.All,
            observedAt);
        var music = new MediaSessionSnapshot(
            new SessionKey("music"),
            "Brave._crx_music",
            PlaybackStatus.Playing,
            MediaCommandCapabilities.All,
            observedAt);
        var application = new FakeApplication(new MediaLockApplicationState(
            RouterState.Initial with
            {
                Mode = mode,
                Status = RouterStatus.Ready,
                Sessions = [brave, music],
                WindowsCurrentSession = brave.Key,
                Revision = 1,
            }));
        using var viewModel = new MainWindowViewModel(application, synchronizationContext: null);
        viewModel.SelectedSession = Assert.Single(
            viewModel.Sessions,
            session => session.Key == music.Key);

        application.Publish(application.State with
        {
            Router = application.State.Router with
            {
                Sessions = [brave],
                Revision = 2,
            },
        });
        Assert.Null(viewModel.SelectedSession);

        application.Publish(application.State with
        {
            Router = application.State.Router with { Revision = 3 },
        });
        Assert.Null(viewModel.SelectedSession);

        var recovered = music with { Key = new SessionKey("music-new") };
        application.Publish(application.State with
        {
            Router = application.State.Router with
            {
                Sessions = [brave, recovered],
                Revision = 4,
            },
        });

        Assert.Equal(recovered.Key, viewModel.SelectedSession?.Key);
    }

    [Theory]
    [InlineData(RoutingMode.WindowsAuto)]
    [InlineData(RoutingMode.PriorityRules)]
    public void ExpiredSelectionBookmarkStaysUnselectedWhenTheSessionReturns(RoutingMode mode)
    {
        var observedAt = DateTimeOffset.Parse("2026-08-23T10:00:00Z");
        var clock = new TestTimeProvider(observedAt);
        var brave = new MediaSessionSnapshot(
            new SessionKey("video"),
            "Brave",
            PlaybackStatus.Paused,
            MediaCommandCapabilities.All,
            observedAt);
        var music = new MediaSessionSnapshot(
            new SessionKey("music"),
            "Brave._crx_music",
            PlaybackStatus.Playing,
            MediaCommandCapabilities.All,
            observedAt);
        var application = new FakeApplication(new MediaLockApplicationState(
            RouterState.Initial with
            {
                Mode = mode,
                Status = RouterStatus.Ready,
                Sessions = [brave, music],
                WindowsCurrentSession = brave.Key,
                Revision = 1,
            }));
        using var viewModel = new MainWindowViewModel(
            application,
            synchronizationContext: null,
            timeProvider: clock);
        viewModel.SelectedSession = Assert.Single(
            viewModel.Sessions,
            session => session.Key == music.Key);
        application.Publish(application.State with
        {
            Router = application.State.Router with
            {
                Sessions = [brave],
                Revision = 2,
            },
        });
        clock.Advance(TimeSpan.FromSeconds(15.1));

        viewModel.RefreshTimeline();
        var recovered = music with { Key = new SessionKey("music-new") };
        application.Publish(application.State with
        {
            Router = application.State.Router with
            {
                Sessions = [brave, recovered],
                Revision = 3,
            },
        });

        Assert.Null(viewModel.SelectedSession);
    }

    [Theory]
    [InlineData(RoutingMode.WindowsAuto)]
    [InlineData(RoutingMode.PriorityRules)]
    public void ExplicitSelectionReplacesTheAutomaticModeBookmark(RoutingMode mode)
    {
        var observedAt = DateTimeOffset.Parse("2026-08-23T10:00:00Z");
        var brave = new MediaSessionSnapshot(
            new SessionKey("video"),
            "Brave",
            PlaybackStatus.Paused,
            MediaCommandCapabilities.All,
            observedAt);
        var music = new MediaSessionSnapshot(
            new SessionKey("music"),
            "Brave._crx_music",
            PlaybackStatus.Playing,
            MediaCommandCapabilities.All,
            observedAt);
        var application = new FakeApplication(new MediaLockApplicationState(
            RouterState.Initial with
            {
                Mode = mode,
                Status = RouterStatus.Ready,
                Sessions = [brave, music],
                WindowsCurrentSession = brave.Key,
                Revision = 1,
            }));
        using var viewModel = new MainWindowViewModel(application, synchronizationContext: null);
        viewModel.SelectedSession = Assert.Single(
            viewModel.Sessions,
            session => session.Key == music.Key);
        application.Publish(application.State with
        {
            Router = application.State.Router with
            {
                Sessions = [brave],
                Revision = 2,
            },
        });
        viewModel.SelectedSession = Assert.Single(viewModel.Sessions);

        var recovered = music with { Key = new SessionKey("music-new") };
        application.Publish(application.State with
        {
            Router = application.State.Router with
            {
                Sessions = [brave, recovered],
                Revision = 3,
            },
        });

        Assert.Equal(brave.Key, viewModel.SelectedSession?.Key);
    }

    [Fact]
    public void UnlockedSelectionDoesNotGuessBetweenSameSourceSuccessors()
    {
        var observedAt = DateTimeOffset.Parse("2026-08-23T10:00:00Z");
        var selected = new MediaSessionSnapshot(
            new SessionKey("brave-old"),
            "Brave",
            PlaybackStatus.Paused,
            MediaCommandCapabilities.All,
            observedAt);
        var application = new FakeApplication(new MediaLockApplicationState(
            RouterState.Initial with
            {
                Sessions = [selected],
                WindowsCurrentSession = selected.Key,
                Revision = 1,
            }));
        using var viewModel = new MainWindowViewModel(application, synchronizationContext: null);
        viewModel.SelectedSession = Assert.Single(viewModel.Sessions);

        application.Publish(application.State with
        {
            Router = application.State.Router with
            {
                Sessions =
                [
                    selected with { Key = new SessionKey("brave-new-1") },
                    selected with { Key = new SessionKey("brave-new-2") },
                ],
                WindowsCurrentSession = new SessionKey("brave-new-1"),
                Revision = 2,
            },
        });

        Assert.Null(viewModel.SelectedSession);
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
    public async Task FriendlySourceNameNeverChangesTheAppLockIdentity()
    {
        var session = new MediaSessionSnapshot(
            new SessionKey("music"),
            "Brave._crx_music",
            PlaybackStatus.Playing,
            MediaCommandCapabilities.All,
            DateTimeOffset.Parse("2026-08-26T00:00:00Z"),
            Metadata: new MediaMetadata("Song", "Artist", null, null));
        var application = new FakeApplication(StateWith(session));
        var metadata = new Dictionary<string, SourceApplicationMetadata>
        {
            [session.SourceAppUserModelId] = new("YouTube Music", "Brave Browser"),
        };
        using var viewModel = new MainWindowViewModel(
            application,
            synchronizationContext: null,
            sourceApplicationMetadataResolver: new FakeSourceApplicationMetadataResolver(metadata));

        var presented = Assert.Single(viewModel.Sessions);
        Assert.Equal("Brave._crx_music", presented.SourceApplication);
        Assert.Equal("YouTube Music — Brave Browser", presented.SourceApplicationDisplayName);
        Assert.Equal("Brave._crx_music", presented.SourceApplicationDetails);
        Assert.Equal("YouTube Music — Brave Browser — Song", viewModel.TargetDescription);

        await viewModel.AppLockCommand.ExecuteAsync(null);

        var intent = Assert.IsType<ApplicationIntent.LockApplication>(
            Assert.Single(application.Intents));
        Assert.Equal("Brave._crx_music", intent.SourceAppUserModelId);
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
    public async Task PresentedErrorCanBeExplicitlyDismissed()
    {
        var application = new FakeApplication(MediaLockApplicationState.Initial);
        using var viewModel = new MainWindowViewModel(application, synchronizationContext: null);
        application.Publish(new MediaLockApplicationState(
            RouterState.Initial,
            "GSMTC catalog became unavailable."));

        await viewModel.DismissErrorCommand.ExecuteAsync(null);

        Assert.False(viewModel.HasError);
        Assert.Null(viewModel.ErrorMessage);
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

    private sealed class RecordingPlaybackStateLockFeedback : IPlaybackStateLockFeedback
    {
        public int PlayCount { get; private set; }

        public void PlayOverrideReleasedSound() => PlayCount++;
    }

    private sealed class FakeSourceApplicationMetadataResolver(
        IReadOnlyDictionary<string, SourceApplicationMetadata> metadata)
        : ISourceApplicationMetadataResolver
    {
        public SourceApplicationMetadata? TryResolve(string sourceAppUserModelId) =>
            metadata.GetValueOrDefault(sourceAppUserModelId);
    }
}
