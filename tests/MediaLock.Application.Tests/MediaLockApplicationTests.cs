using System.Collections.Immutable;
using System.Threading.Channels;
using MediaLock.Application;
using MediaLock.Core.Configuration;
using MediaLock.Core.Diagnostics;
using MediaLock.Core.Lifecycle;
using MediaLock.Core.Media;
using MediaLock.Core.Playback;
using MediaLock.Core.Routing;
using Xunit;

namespace MediaLock.Application.Tests;

public sealed class MediaLockApplicationTests
{
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    [Fact]
    public async Task KeepPlayingArmsTheCurrentPlayingTarget()
    {
        var session = Session("music", "music");
        var catalog = new InMemoryCatalog(
            new MediaSessionCatalogSnapshot([session], session.Key));
        await using var application = new MediaLockApplication(
            catalog,
            new MediaRouter(new SuccessfulController()));
        await application.StartAsync(CancellationToken.None);

        var result = await application.DispatchAsync(
            new ApplicationIntent.SetPlaybackStateLock(
                PlaybackStateLockMode.KeepPlaying),
            CancellationToken.None);

        Assert.Equal(
            PlaybackStateLockMode.KeepPlaying,
            result.State.PlaybackStateLock.Mode);
        Assert.Equal(PlaybackStateLockStatus.Ready, result.State.PlaybackStateLock.Status);
        Assert.Equal(session.Key, result.State.PlaybackStateLock.ArmedTarget);
    }

    [Fact]
    public async Task KeepPlayingCannotArmAPausedTarget()
    {
        var session = Session("music", "music") with
        {
            PlaybackStatus = PlaybackStatus.Paused,
        };
        var catalog = new InMemoryCatalog(
            new MediaSessionCatalogSnapshot([session], session.Key));
        await using var application = new MediaLockApplication(
            catalog,
            new MediaRouter(new SuccessfulController()));
        await application.StartAsync(CancellationToken.None);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            application.DispatchAsync(
                new ApplicationIntent.SetPlaybackStateLock(
                    PlaybackStateLockMode.KeepPlaying),
                CancellationToken.None).AsTask());

        Assert.Contains("only be enabled while", exception.Message);
        Assert.Equal(PlaybackStateLockState.Off, application.State.PlaybackStateLock);
    }

    [Fact]
    public async Task KeepPlayingCorrectsAnExternalPauseOnTheArmedTarget()
    {
        var session = Session("music", "music");
        var catalog = new InMemoryCatalog(
            new MediaSessionCatalogSnapshot([session], session.Key));
        var controller = new RecordingController();
        await using var application = new MediaLockApplication(
            catalog,
            new MediaRouter(controller));
        await application.StartAsync(CancellationToken.None);
        await application.DispatchAsync(
            new ApplicationIntent.SetPlaybackStateLock(
                PlaybackStateLockMode.KeepPlaying),
            CancellationToken.None);

        await catalog.PublishAsync(new MediaSessionCatalogSnapshot(
            [session with { PlaybackStatus = PlaybackStatus.Paused }],
            session.Key));
        await controller.WaitForCommandCountAsync(1);

        Assert.Equal([(session.Key, MediaCommand.Play)], controller.Commands);
    }

    [Theory]
    [MemberData(nameof(CommandsThatDisarmKeepPlaying))]
    public async Task MediaLockPauseToggleAndStopDisarmKeepPlayingBeforeRouting(
        MediaCommand command)
    {
        var session = Session("music", "music");
        var catalog = new InMemoryCatalog(
            new MediaSessionCatalogSnapshot([session], session.Key));
        var controller = new RecordingController();
        await using var application = new MediaLockApplication(
            catalog,
            new MediaRouter(controller));
        await application.StartAsync(CancellationToken.None);
        await application.DispatchAsync(
            new ApplicationIntent.SetPlaybackStateLock(
                PlaybackStateLockMode.KeepPlaying),
            CancellationToken.None);

        var result = await application.DispatchAsync(
            new ApplicationIntent.Route(command),
            CancellationToken.None);

        Assert.Equal(PlaybackStateLockState.Off, result.State.PlaybackStateLock);
        Assert.Equal([(session.Key, command)], controller.Commands);
    }

    public static TheoryData<MediaCommand> CommandsThatDisarmKeepPlaying => new()
    {
        MediaCommand.Pause,
        MediaCommand.TogglePlayPause,
        MediaCommand.Stop,
    };

    [Theory]
    [MemberData(nameof(CommandsThatPreserveKeepPlaying))]
    public async Task NonPausingMediaLockCommandsPreserveKeepPlaying(MediaCommand command)
    {
        var session = Session("music", "music");
        var catalog = new InMemoryCatalog(
            new MediaSessionCatalogSnapshot([session], session.Key));
        await using var application = new MediaLockApplication(
            catalog,
            new MediaRouter(new SuccessfulController()));
        await application.StartAsync(CancellationToken.None);
        await application.DispatchAsync(
            new ApplicationIntent.SetPlaybackStateLock(
                PlaybackStateLockMode.KeepPlaying),
            CancellationToken.None);

        var result = await application.DispatchAsync(
            new ApplicationIntent.Route(command),
            CancellationToken.None);

        Assert.Equal(
            PlaybackStateLockMode.KeepPlaying,
            result.State.PlaybackStateLock.Mode);
    }

    public static TheoryData<MediaCommand> CommandsThatPreserveKeepPlaying => new()
    {
        MediaCommand.Play,
        MediaCommand.Next,
        MediaCommand.Previous,
    };

    [Fact]
    public async Task UnrelatedActiveTargetClearsKeepPlayingWithoutDispatching()
    {
        var music = Session("music", "music");
        var video = Session("video", "browser");
        var catalog = new InMemoryCatalog(
            new MediaSessionCatalogSnapshot([music, video], music.Key));
        var controller = new RecordingController();
        await using var application = new MediaLockApplication(
            catalog,
            new MediaRouter(controller));
        await application.StartAsync(CancellationToken.None);
        await application.DispatchAsync(
            new ApplicationIntent.SetPlaybackStateLock(
                PlaybackStateLockMode.KeepPlaying),
            CancellationToken.None);
        var cleared = new TaskCompletionSource<MediaLockApplicationState>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        application.StateChanged += (_, args) =>
        {
            if (args.State.PlaybackStateLock.Mode == PlaybackStateLockMode.Off)
            {
                cleared.TrySetResult(args.State);
            }
        };

        await catalog.PublishAsync(
            new MediaSessionCatalogSnapshot([music, video], video.Key));
        var state = await cleared.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(PlaybackStateLockState.Off, state.PlaybackStateLock);
        Assert.Empty(controller.Commands);
    }

    [Fact]
    public async Task LockedTargetRecoverySuspendsKeepPlayingWithoutUsingACompetitor()
    {
        var music = Session("music", "music");
        var video = Session("video", "browser");
        var catalog = new InMemoryCatalog(
            new MediaSessionCatalogSnapshot([music, video], music.Key));
        var controller = new RecordingController();
        await using var application = new MediaLockApplication(
            catalog,
            new MediaRouter(controller));
        await application.StartAsync(CancellationToken.None);
        await application.DispatchAsync(
            new ApplicationIntent.LockSession(music.Key),
            CancellationToken.None);
        await application.DispatchAsync(
            new ApplicationIntent.SetPlaybackStateLock(
                PlaybackStateLockMode.KeepPlaying),
            CancellationToken.None);
        var suspended = new TaskCompletionSource<MediaLockApplicationState>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        application.StateChanged += (_, args) =>
        {
            if (args.State.PlaybackStateLock.Status == PlaybackStateLockStatus.Suspended)
            {
                suspended.TrySetResult(args.State);
            }
        };

        await catalog.PublishAsync(
            new MediaSessionCatalogSnapshot([video], video.Key));
        var state = await suspended.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(
            PlaybackStateLockMode.KeepPlaying,
            state.PlaybackStateLock.Mode);
        Assert.Equal(music.Key, state.PlaybackStateLock.ArmedTarget);
        Assert.Empty(controller.Commands);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SystemSuspendDisarmsKeepPlayingAndDoesNotResumeAudio(
        bool usePriorityRules)
    {
        var music = Session("music", "music");
        var video = Session("video", "browser");
        var catalog = new InMemoryCatalog(
            new MediaSessionCatalogSnapshot([music, video], music.Key));
        var controller = new RecordingController();
        await using var application = new MediaLockApplication(
            catalog,
            new MediaRouter(controller));
        await application.StartAsync(CancellationToken.None);
        if (usePriorityRules)
        {
            await application.DispatchAsync(
                new ApplicationIntent.UsePriorityRules(),
                CancellationToken.None);
        }

        await application.DispatchAsync(
            new ApplicationIntent.SetPlaybackStateLock(
                PlaybackStateLockMode.KeepPlaying),
            CancellationToken.None);
        var disarmed = new TaskCompletionSource<MediaLockApplicationState>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        application.StateChanged += (_, args) =>
        {
            if (args.State.CatalogStatus == MediaSessionCatalogStatus.Suspended &&
                args.State.PlaybackStateLock.Mode == PlaybackStateLockMode.Off)
            {
                disarmed.TrySetResult(args.State);
            }
        };

        await catalog.PublishAsync(new MediaSessionCatalogSnapshot(
            [],
            null,
            MediaSessionCatalogStatus.Suspended,
            "Media sessions are suspended while Windows sleeps."));
        var suspendedState = await disarmed.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(PlaybackStateLockState.Off, suspendedState.PlaybackStateLock);
        Assert.Empty(controller.Commands);

        var resumed = new TaskCompletionSource<MediaLockApplicationState>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        application.StateChanged += (_, args) =>
        {
            if (args.State.CatalogStatus == MediaSessionCatalogStatus.Available &&
                args.State.Router.Sessions.Any(session => session.Key == music.Key))
            {
                resumed.TrySetResult(args.State);
            }
        };
        await catalog.PublishAsync(new MediaSessionCatalogSnapshot(
            [],
            null,
            MediaSessionCatalogStatus.Reacquiring,
            "Reacquiring GSMTC after Windows resumed."));
        await catalog.PublishAsync(new MediaSessionCatalogSnapshot(
            [music with { PlaybackStatus = PlaybackStatus.Paused }, video],
            music.Key));
        var resumedState = await resumed.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(PlaybackStateLockState.Off, resumedState.PlaybackStateLock);
        Assert.Empty(controller.Commands);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LockScreenPauseDisarmsKeepPlayingWithoutCorrection(
        bool pauseArrivesBeforeUnlock)
    {
        var music = Session("music", "music");
        var catalog = new InMemoryCatalog(
            new MediaSessionCatalogSnapshot([music], music.Key));
        var controller = new RecordingController();
        var workstation = new FakeWorkstationLockState();
        await using var application = new MediaLockApplication(
            catalog,
            new MediaRouter(controller),
            settingsRepository: null,
            loginStartupManager: null,
            workstationLockState: workstation);
        await application.StartAsync(CancellationToken.None);
        await application.DispatchAsync(
            new ApplicationIntent.SetPlaybackStateLock(
                PlaybackStateLockMode.KeepPlaying),
            CancellationToken.None);
        workstation.Lock();
        if (!pauseArrivesBeforeUnlock)
        {
            workstation.Unlock();
        }

        var cleared = new TaskCompletionSource<MediaLockApplicationState>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        application.StateChanged += (_, args) =>
        {
            if (args.State.PlaybackStateLock.Mode == PlaybackStateLockMode.Off)
            {
                cleared.TrySetResult(args.State);
            }
        };
        await catalog.PublishAsync(new MediaSessionCatalogSnapshot(
            [music with { PlaybackStatus = PlaybackStatus.Paused }],
            music.Key));
        var state = await cleared.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(PlaybackStateLockState.Off, state.PlaybackStateLock);
        Assert.Empty(controller.Commands);
    }

    [Fact]
    public async Task UnlockRefreshPreservesKeepPlayingAndEndsLockScreenIntentWindow()
    {
        var music = Session("music", "music");
        var catalog = new InMemoryCatalog(
            new MediaSessionCatalogSnapshot([music], music.Key));
        var controller = new RecordingController();
        var workstation = new FakeWorkstationLockState();
        await using var application = new MediaLockApplication(
            catalog,
            new MediaRouter(controller),
            settingsRepository: null,
            loginStartupManager: null,
            workstationLockState: workstation);
        await application.StartAsync(CancellationToken.None);
        await application.DispatchAsync(
            new ApplicationIntent.SetPlaybackStateLock(
                PlaybackStateLockMode.KeepPlaying),
            CancellationToken.None);
        workstation.Lock();
        workstation.Unlock();
        await catalog.PublishAsync(new MediaSessionCatalogSnapshot([music], music.Key));

        await catalog.PublishAsync(new MediaSessionCatalogSnapshot(
            [music with { PlaybackStatus = PlaybackStatus.Paused }],
            music.Key));
        await controller.WaitForCommandCountAsync(1);

        Assert.Equal(
            PlaybackStateLockMode.KeepPlaying,
            application.State.PlaybackStateLock.Mode);
        Assert.Equal([(music.Key, MediaCommand.Play)], controller.Commands);
    }

    [Fact]
    public async Task LiveLockedTargetObservationsRefreshKeepPlayingRecoveryIdentity()
    {
        var music = Session("music", "music");
        var video = Session("video", "browser");
        var catalog = new InMemoryCatalog(
            new MediaSessionCatalogSnapshot([music, video], music.Key));
        await using var application = new MediaLockApplication(
            catalog,
            new MediaRouter(new SuccessfulController()));
        await application.StartAsync(CancellationToken.None);
        await application.DispatchAsync(
            new ApplicationIntent.LockSession(music.Key),
            CancellationToken.None);
        await application.DispatchAsync(
            new ApplicationIntent.SetPlaybackStateLock(
                PlaybackStateLockMode.KeepPlaying),
            CancellationToken.None);
        var refreshed = music with
        {
            ObservedAt = music.ObservedAt.AddMinutes(1),
            Metadata = new MediaMetadata("new track", "artist", null, null),
        };
        await catalog.PublishAsync(
            new MediaSessionCatalogSnapshot([refreshed, video], refreshed.Key));
        var suspended = new TaskCompletionSource<MediaLockApplicationState>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        application.StateChanged += (_, args) =>
        {
            if (args.State.PlaybackStateLock.Status == PlaybackStateLockStatus.Suspended)
            {
                suspended.TrySetResult(args.State);
            }
        };

        await catalog.PublishAsync(
            new MediaSessionCatalogSnapshot([video], video.Key));
        var state = await suspended.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(
            PlaybackStateLockMode.KeepPlaying,
            state.PlaybackStateLock.Mode);
    }

    [Fact]
    public async Task AcceptedRecoverySuccessorResumesKeepPlayingOnTheNewSession()
    {
        var music = Session("music-old", "music");
        var video = Session("video", "browser");
        var catalog = new InMemoryCatalog(
            new MediaSessionCatalogSnapshot([music, video], music.Key));
        var controller = new RecordingController();
        await using var application = new MediaLockApplication(
            catalog,
            new MediaRouter(controller));
        await application.StartAsync(CancellationToken.None);
        await application.DispatchAsync(
            new ApplicationIntent.LockSession(music.Key),
            CancellationToken.None);
        await application.DispatchAsync(
            new ApplicationIntent.SetPlaybackStateLock(
                PlaybackStateLockMode.KeepPlaying),
            CancellationToken.None);
        var suspended = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        application.StateChanged += (_, args) =>
        {
            if (args.State.PlaybackStateLock.Status == PlaybackStateLockStatus.Suspended)
            {
                suspended.TrySetResult();
            }
        };
        await catalog.PublishAsync(
            new MediaSessionCatalogSnapshot([video], video.Key));
        await suspended.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var successor = music with
        {
            Key = new SessionKey("music-new"),
            PlaybackStatus = PlaybackStatus.Paused,
            ObservedAt = music.ObservedAt.AddSeconds(1),
        };

        await catalog.PublishAsync(
            new MediaSessionCatalogSnapshot([video, successor], successor.Key));
        await controller.WaitForCommandCountAsync(1);

        Assert.Equal([(successor.Key, MediaCommand.Play)], controller.Commands);
        Assert.Equal(successor.Key, application.State.PlaybackStateLock.ArmedTarget);
        Assert.Equal(
            PlaybackStateLockStatus.Ready,
            application.State.PlaybackStateLock.Status);
    }

    [Theory]
    [InlineData(RoutingMode.WindowsAuto)]
    [InlineData(RoutingMode.PriorityRules)]
    public async Task AutomaticRoutingPreservesKeepPlayingAcrossTransientTargetRecreation(
        RoutingMode mode)
    {
        var music = Session("music-old", "music");
        var video = Session("video", "browser");
        var settings = MediaLockSettings.Default with
        {
            DefaultRoutingMode = mode,
            PriorityRules = [new PriorityRule("music")],
        };
        var catalog = new InMemoryCatalog(
            new MediaSessionCatalogSnapshot([music, video], music.Key));
        var controller = new RecordingController();
        await using var application = new MediaLockApplication(
            catalog,
            new MediaRouter(controller),
            new RecordingSettingsRepository(settings),
            new RecordingLoginStartupManager(),
            new RecordingRuntimeStateRepository());
        await application.StartAsync(CancellationToken.None);
        await application.DispatchAsync(
            new ApplicationIntent.SetPlaybackStateLock(
                PlaybackStateLockMode.KeepPlaying),
            CancellationToken.None);
        var suspended = new TaskCompletionSource<MediaLockApplicationState>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        application.StateChanged += (_, args) =>
        {
            if (args.State.PlaybackStateLock.Status == PlaybackStateLockStatus.Suspended)
            {
                suspended.TrySetResult(args.State);
            }
        };

        await catalog.PublishAsync(
            new MediaSessionCatalogSnapshot([video], video.Key));
        var suspendedState = await suspended.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(
            PlaybackStateLockMode.KeepPlaying,
            suspendedState.PlaybackStateLock.Mode);
        Assert.Equal(
            PlaybackStateLockStatus.Suspended,
            suspendedState.PlaybackStateLock.Status);
        Assert.Empty(controller.Commands);

        var successor = music with
        {
            Key = new SessionKey("music-new"),
            PlaybackStatus = PlaybackStatus.Paused,
            ObservedAt = music.ObservedAt.AddSeconds(1),
        };
        await catalog.PublishAsync(
            new MediaSessionCatalogSnapshot([video, successor], successor.Key));
        await controller.WaitForCommandCountAsync(1);

        Assert.Equal([(successor.Key, MediaCommand.Play)], controller.Commands);
        Assert.Equal(
            PlaybackStateLockMode.KeepPlaying,
            application.State.PlaybackStateLock.Mode);
        Assert.Equal(
            PlaybackStateLockStatus.Ready,
            application.State.PlaybackStateLock.Status);
        Assert.Equal(successor.Key, application.State.PlaybackStateLock.ArmedTarget);
    }

    [Theory]
    [InlineData(RoutingMode.WindowsAuto)]
    [InlineData(RoutingMode.PriorityRules)]
    public async Task AutomaticRoutingDoesNotGuessBetweenAmbiguousPlaybackSuccessors(
        RoutingMode mode)
    {
        var music = Session("music-old", "music");
        var video = Session("video", "browser");
        var settings = MediaLockSettings.Default with
        {
            DefaultRoutingMode = mode,
            PriorityRules = [new PriorityRule("music")],
        };
        var catalog = new InMemoryCatalog(
            new MediaSessionCatalogSnapshot([music, video], music.Key));
        var controller = new RecordingController();
        await using var application = new MediaLockApplication(
            catalog,
            new MediaRouter(controller),
            new RecordingSettingsRepository(settings),
            new RecordingLoginStartupManager(),
            new RecordingRuntimeStateRepository());
        await application.StartAsync(CancellationToken.None);
        await application.DispatchAsync(
            new ApplicationIntent.SetPlaybackStateLock(
                PlaybackStateLockMode.KeepPlaying),
            CancellationToken.None);
        var suspended = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        application.StateChanged += (_, args) =>
        {
            if (args.State.PlaybackStateLock.Status == PlaybackStateLockStatus.Suspended)
            {
                suspended.TrySetResult();
            }
        };
        await catalog.PublishAsync(
            new MediaSessionCatalogSnapshot([video], video.Key));
        await suspended.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var first = music with
        {
            Key = new SessionKey("music-new-1"),
            PlaybackStatus = PlaybackStatus.Paused,
            ObservedAt = music.ObservedAt.AddSeconds(1),
        };
        var second = first with { Key = new SessionKey("music-new-2") };
        var ambiguousObserved = new TaskCompletionSource<MediaLockApplicationState>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var ambiguousStateChanges = 0;
        application.StateChanged += (_, args) =>
        {
            if (args.State.Router.Sessions.Length == 3 &&
                Interlocked.Increment(ref ambiguousStateChanges) >= 2)
            {
                ambiguousObserved.TrySetResult(args.State);
            }
        };

        await catalog.PublishAsync(
            new MediaSessionCatalogSnapshot([video, first, second], first.Key));
        var state = await ambiguousObserved.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(
            PlaybackStateLockMode.KeepPlaying,
            state.PlaybackStateLock.Mode);
        Assert.Equal(
            PlaybackStateLockStatus.Suspended,
            state.PlaybackStateLock.Status);
        Assert.Equal(music.Key, state.PlaybackStateLock.ArmedTarget);
        Assert.Empty(controller.Commands);
    }

    [Fact]
    public async Task WindowsAutoDoesNotRearmAnInactivePlaybackSuccessor()
    {
        var music = Session("music-old", "music");
        var video = Session("video", "browser");
        var catalog = new InMemoryCatalog(
            new MediaSessionCatalogSnapshot([music, video], music.Key));
        var controller = new RecordingController();
        await using var application = new MediaLockApplication(
            catalog,
            new MediaRouter(controller));
        await application.StartAsync(CancellationToken.None);
        await application.DispatchAsync(
            new ApplicationIntent.SetPlaybackStateLock(
                PlaybackStateLockMode.KeepPlaying),
            CancellationToken.None);
        var suspended = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        application.StateChanged += (_, args) =>
        {
            if (args.State.PlaybackStateLock.Status == PlaybackStateLockStatus.Suspended)
            {
                suspended.TrySetResult();
            }
        };
        await catalog.PublishAsync(
            new MediaSessionCatalogSnapshot([video], video.Key));
        await suspended.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var successor = music with
        {
            Key = new SessionKey("music-new"),
            PlaybackStatus = PlaybackStatus.Paused,
            ObservedAt = music.ObservedAt.AddSeconds(1),
        };
        var inactiveSuccessorObserved = new TaskCompletionSource<MediaLockApplicationState>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var inactiveSuccessorStateChanges = 0;
        application.StateChanged += (_, args) =>
        {
            if (args.State.Router.Sessions.Length == 2 &&
                args.State.Router.Sessions.Any(session => session.Key == successor.Key) &&
                Interlocked.Increment(ref inactiveSuccessorStateChanges) >= 2)
            {
                inactiveSuccessorObserved.TrySetResult(args.State);
            }
        };

        await catalog.PublishAsync(
            new MediaSessionCatalogSnapshot([video, successor], video.Key));
        var state = await inactiveSuccessorObserved.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(
            PlaybackStateLockMode.KeepPlaying,
            state.PlaybackStateLock.Mode);
        Assert.Equal(
            PlaybackStateLockStatus.Suspended,
            state.PlaybackStateLock.Status);
        Assert.Equal(music.Key, state.PlaybackStateLock.ArmedTarget);
        Assert.Empty(controller.Commands);
    }

    [Theory]
    [InlineData(RoutingMode.WindowsAuto)]
    [InlineData(RoutingMode.PriorityRules)]
    public async Task LiveAutomaticTargetObservationsRefreshKeepPlayingRecoveryIdentity(
        RoutingMode mode)
    {
        var music = Session("music-old", "music");
        var video = Session("video", "browser");
        var settings = MediaLockSettings.Default with
        {
            DefaultRoutingMode = mode,
            PriorityRules = [new PriorityRule("music")],
        };
        var catalog = new InMemoryCatalog(
            new MediaSessionCatalogSnapshot([music, video], music.Key));
        var controller = new RecordingController();
        await using var application = new MediaLockApplication(
            catalog,
            new MediaRouter(controller),
            new RecordingSettingsRepository(settings),
            new RecordingLoginStartupManager(),
            new RecordingRuntimeStateRepository());
        await application.StartAsync(CancellationToken.None);
        await application.DispatchAsync(
            new ApplicationIntent.SetPlaybackStateLock(
                PlaybackStateLockMode.KeepPlaying),
            CancellationToken.None);
        var refreshed = music with
        {
            ObservedAt = music.ObservedAt.AddMinutes(20),
            Metadata = new MediaMetadata("new track", "artist", null, null),
        };
        var refreshObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        application.StateChanged += (_, args) =>
        {
            if (args.State.Router.Sessions.Any(session =>
                    session.Key == refreshed.Key &&
                    session.Metadata?.Title == refreshed.Metadata.Title))
            {
                refreshObserved.TrySetResult();
            }
        };
        await catalog.PublishAsync(
            new MediaSessionCatalogSnapshot([refreshed, video], refreshed.Key));
        await refreshObserved.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var suspended = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        application.StateChanged += (_, args) =>
        {
            if (args.State.PlaybackStateLock.Status == PlaybackStateLockStatus.Suspended)
            {
                suspended.TrySetResult();
            }
        };
        await catalog.PublishAsync(
            new MediaSessionCatalogSnapshot([video], video.Key));
        await suspended.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var successor = refreshed with
        {
            Key = new SessionKey("music-new"),
            PlaybackStatus = PlaybackStatus.Paused,
            ObservedAt = refreshed.ObservedAt.AddSeconds(1),
        };

        await catalog.PublishAsync(
            new MediaSessionCatalogSnapshot([video, successor], successor.Key));
        await controller.WaitForCommandCountAsync(1);

        Assert.Equal([(successor.Key, MediaCommand.Play)], controller.Commands);
        Assert.Equal(successor.Key, application.State.PlaybackStateLock.ArmedTarget);
        Assert.Equal(
            PlaybackStateLockStatus.Ready,
            application.State.PlaybackStateLock.Status);
    }

    [Fact]
    public async Task RepeatedPausedObservationsExhaustBoundedCorrections()
    {
        var session = Session("music", "music");
        var catalog = new InMemoryCatalog(
            new MediaSessionCatalogSnapshot([session], session.Key));
        var controller = new RecordingController();
        await using var application = new MediaLockApplication(
            catalog,
            new MediaRouter(controller));
        await application.StartAsync(CancellationToken.None);
        await application.DispatchAsync(
            new ApplicationIntent.SetPlaybackStateLock(
                PlaybackStateLockMode.KeepPlaying),
            CancellationToken.None);
        var failed = new TaskCompletionSource<MediaLockApplicationState>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        application.StateChanged += (_, args) =>
        {
            if (args.State.PlaybackStateLock.Status == PlaybackStateLockStatus.Failed)
            {
                failed.TrySetResult(args.State);
            }
        };

        for (var observation = 1; observation <= 3; observation++)
        {
            await catalog.PublishAsync(new MediaSessionCatalogSnapshot(
                [session with
                {
                    PlaybackStatus = PlaybackStatus.Paused,
                    ObservedAt = session.ObservedAt.AddSeconds(observation),
                }],
                session.Key));
        }
        var state = await failed.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(2, controller.Commands.Count);
        Assert.All(controller.Commands, command =>
            Assert.Equal((session.Key, MediaCommand.Play), command));
        Assert.Contains("could not be confirmed", state.PlaybackStateLock.Message);
    }

    [Fact]
    public async Task ThirdDistinctExternalPauseWithinTheWindowReleasesKeepPlaying()
    {
        var session = Session("music", "music");
        var catalog = new InMemoryCatalog(
            new MediaSessionCatalogSnapshot([session], session.Key));
        var controller = new RecordingController();
        var clock = new ManualTimeProvider();
        await using var application = new MediaLockApplication(
            catalog,
            new MediaRouter(controller),
            settingsRepository: null,
            loginStartupManager: null,
            timeProvider: clock);
        await application.StartAsync(CancellationToken.None);
        await application.DispatchAsync(
            new ApplicationIntent.SetPlaybackStateLock(PlaybackStateLockMode.KeepPlaying),
            CancellationToken.None);

        for (var episode = 1; episode <= 3; episode++)
        {
            await catalog.PublishAsync(new MediaSessionCatalogSnapshot(
                [session with { PlaybackStatus = PlaybackStatus.Paused }],
                session.Key));
            if (episode < 3)
            {
                await controller.WaitForCommandCountAsync(episode);
                await catalog.PublishAsync(new MediaSessionCatalogSnapshot([session], session.Key));
                clock.Advance(TimeSpan.FromSeconds(1));
            }
        }

        await WaitUntilAsync(() =>
            application.State.PlaybackStateLock.Status == PlaybackStateLockStatus.Released);
        Assert.Equal(PlaybackStateLockMode.Off, application.State.PlaybackStateLock.Mode);
        Assert.Equal(2, controller.Commands.Count);
    }

    [Fact]
    public async Task RepeatedPauseWindowExpiryStartsANewSequence()
    {
        var session = Session("music", "music");
        var catalog = new InMemoryCatalog(
            new MediaSessionCatalogSnapshot([session], session.Key));
        var controller = new RecordingController();
        var clock = new ManualTimeProvider();
        await using var application = new MediaLockApplication(
            catalog,
            new MediaRouter(controller),
            settingsRepository: null,
            loginStartupManager: null,
            timeProvider: clock);
        await application.StartAsync(CancellationToken.None);
        await application.DispatchAsync(
            new ApplicationIntent.SetPlaybackStateLock(PlaybackStateLockMode.KeepPlaying),
            CancellationToken.None);

        for (var episode = 1; episode <= 3; episode++)
        {
            await catalog.PublishAsync(new MediaSessionCatalogSnapshot(
                [session with { PlaybackStatus = PlaybackStatus.Paused }],
                session.Key));
            await controller.WaitForCommandCountAsync(episode);
            await catalog.PublishAsync(new MediaSessionCatalogSnapshot([session], session.Key));
            clock.Advance(episode == 1 ? TimeSpan.FromSeconds(6) : TimeSpan.FromSeconds(1));
        }

        Assert.Equal(PlaybackStateLockMode.KeepPlaying, application.State.PlaybackStateLock.Mode);
        Assert.Equal(3, controller.Commands.Count);
    }

    [Fact]
    public async Task BufferingTransitionsDoNotCountAsDistinctPauseRequests()
    {
        var session = Session("music", "music");
        var catalog = new InMemoryCatalog(
            new MediaSessionCatalogSnapshot([session], session.Key));
        var controller = new RecordingController();
        await using var application = new MediaLockApplication(
            catalog,
            new MediaRouter(controller));
        await application.StartAsync(CancellationToken.None);
        await application.DispatchAsync(
            new ApplicationIntent.SetPlaybackStateLock(PlaybackStateLockMode.KeepPlaying),
            CancellationToken.None);

        for (var episode = 1; episode <= 3; episode++)
        {
            await catalog.PublishAsync(new MediaSessionCatalogSnapshot(
                [session with { PlaybackStatus = PlaybackStatus.Changing }],
                session.Key));
            await catalog.PublishAsync(new MediaSessionCatalogSnapshot(
                [session with { PlaybackStatus = PlaybackStatus.Paused }],
                session.Key));
            await controller.WaitForCommandCountAsync(episode);
            await catalog.PublishAsync(new MediaSessionCatalogSnapshot([session], session.Key));
        }

        Assert.Equal(PlaybackStateLockMode.KeepPlaying, application.State.PlaybackStateLock.Mode);
        Assert.Equal(3, controller.Commands.Count);
    }

    [Fact]
    public async Task ExplicitMediaLockCommandResetsTheRepeatedPauseSequence()
    {
        var session = Session("music", "music");
        var catalog = new InMemoryCatalog(
            new MediaSessionCatalogSnapshot([session], session.Key));
        var controller = new RecordingController();
        await using var application = new MediaLockApplication(
            catalog,
            new MediaRouter(controller));
        await application.StartAsync(CancellationToken.None);
        await application.DispatchAsync(
            new ApplicationIntent.SetPlaybackStateLock(PlaybackStateLockMode.KeepPlaying),
            CancellationToken.None);

        await catalog.PublishAsync(new MediaSessionCatalogSnapshot(
            [session with { PlaybackStatus = PlaybackStatus.Paused }], session.Key));
        await controller.WaitForCommandCountAsync(1);
        await catalog.PublishAsync(new MediaSessionCatalogSnapshot([session], session.Key));
        await application.DispatchAsync(
            new ApplicationIntent.Route(MediaCommand.Next),
            CancellationToken.None);

        for (var episode = 1; episode <= 2; episode++)
        {
            await catalog.PublishAsync(new MediaSessionCatalogSnapshot(
                [session with { PlaybackStatus = PlaybackStatus.Paused }], session.Key));
            await controller.WaitForCommandCountAsync(episode + 2);
            await catalog.PublishAsync(new MediaSessionCatalogSnapshot([session], session.Key));
        }

        Assert.Equal(PlaybackStateLockMode.KeepPlaying, application.State.PlaybackStateLock.Mode);
        Assert.Equal(4, controller.Commands.Count);
    }

    [Fact]
    public async Task DisabledRepeatedPauseOverrideNeverReleasesKeepPlaying()
    {
        var session = Session("music", "music");
        var catalog = new InMemoryCatalog(
            new MediaSessionCatalogSnapshot([session], session.Key));
        var controller = new RecordingController();
        var settings = MediaLockSettings.Default with
        {
            PlaybackStateLock = MediaLockSettings.Default.PlaybackStateLock! with
            {
                RepeatedPauseOverrideEnabled = false,
            },
        };
        await using var application = new MediaLockApplication(
            catalog,
            new MediaRouter(controller),
            new RecordingSettingsRepository(settings),
            loginStartupManager: null);
        await application.StartAsync(CancellationToken.None);
        await application.DispatchAsync(
            new ApplicationIntent.SetPlaybackStateLock(PlaybackStateLockMode.KeepPlaying),
            CancellationToken.None);

        for (var episode = 1; episode <= 3; episode++)
        {
            await catalog.PublishAsync(new MediaSessionCatalogSnapshot(
                [session with { PlaybackStatus = PlaybackStatus.Paused }], session.Key));
            await controller.WaitForCommandCountAsync(episode);
            await catalog.PublishAsync(new MediaSessionCatalogSnapshot([session], session.Key));
        }

        Assert.Equal(PlaybackStateLockMode.KeepPlaying, application.State.PlaybackStateLock.Mode);
        Assert.Equal(3, controller.Commands.Count);
    }

    [Fact]
    public async Task UpdatedRepeatedPauseOverrideAppliesWithoutRestarting()
    {
        var session = Session("music", "music");
        var catalog = new InMemoryCatalog(
            new MediaSessionCatalogSnapshot([session], session.Key));
        var controller = new RecordingController();
        await using var application = new MediaLockApplication(
            catalog,
            new MediaRouter(controller),
            new RecordingSettingsRepository(MediaLockSettings.Default),
            loginStartupManager: null);
        await application.StartAsync(CancellationToken.None);
        await application.DispatchAsync(
            new ApplicationIntent.SetPlaybackStateLock(PlaybackStateLockMode.KeepPlaying),
            CancellationToken.None);
        var updated = application.State.Settings with
        {
            PlaybackStateLock = application.State.Settings.PlaybackStateLock! with
            {
                RepeatedPauseOverrideEnabled = false,
            },
        };
        await application.DispatchAsync(
            new ApplicationIntent.UpdateSettings(updated),
            CancellationToken.None);

        for (var episode = 1; episode <= 3; episode++)
        {
            await catalog.PublishAsync(new MediaSessionCatalogSnapshot(
                [session with { PlaybackStatus = PlaybackStatus.Paused }], session.Key));
            await controller.WaitForCommandCountAsync(episode);
            await catalog.PublishAsync(new MediaSessionCatalogSnapshot([session], session.Key));
        }

        Assert.Equal(PlaybackStateLockMode.KeepPlaying, application.State.PlaybackStateLock.Mode);
        Assert.Equal(3, controller.Commands.Count);
    }

    [Fact]
    public async Task ExplicitTargetChangeClearsKeepPlayingImmediately()
    {
        var music = Session("music", "music");
        var video = Session("video", "browser");
        var catalog = new InMemoryCatalog(
            new MediaSessionCatalogSnapshot([music, video], music.Key));
        await using var application = new MediaLockApplication(
            catalog,
            new MediaRouter(new SuccessfulController()));
        await application.StartAsync(CancellationToken.None);
        await application.DispatchAsync(
            new ApplicationIntent.SetPlaybackStateLock(
                PlaybackStateLockMode.KeepPlaying),
            CancellationToken.None);

        var result = await application.DispatchAsync(
            new ApplicationIntent.LockSession(video.Key),
            CancellationToken.None);

        Assert.Equal(video.Key, result.State.Router.ActiveTarget);
        Assert.Equal(PlaybackStateLockState.Off, result.State.PlaybackStateLock);
    }

    [Fact]
    public async Task SavingUnrelatedSettingsPreservesKeepPlayingForTheSameTarget()
    {
        var session = Session("music", "music");
        var catalog = new InMemoryCatalog(
            new MediaSessionCatalogSnapshot([session], session.Key));
        await using var application = new MediaLockApplication(
            catalog,
            new MediaRouter(new SuccessfulController()));
        await application.StartAsync(CancellationToken.None);
        await application.DispatchAsync(
            new ApplicationIntent.SetPlaybackStateLock(
                PlaybackStateLockMode.KeepPlaying),
            CancellationToken.None);
        var updated = application.State.Settings with
        {
            Desktop = application.State.Settings.Desktop! with
            {
                CloseToTray = !application.State.Settings.Desktop.CloseToTray,
            },
        };

        var result = await application.DispatchAsync(
            new ApplicationIntent.UpdateSettings(updated),
            CancellationToken.None);

        Assert.Equal(
            PlaybackStateLockMode.KeepPlaying,
            result.State.PlaybackStateLock.Mode);
        Assert.Equal(session.Key, result.State.PlaybackStateLock.ArmedTarget);
    }

    [Fact]
    public async Task UpdatedPriorityRulesImmediatelyRecalculateTheApplicationTarget()
    {
        var brave = Session("brave", "Brave");
        var chrome = Session("chrome", "Chrome");
        var initial = MediaLockSettings.Default with
        {
            DefaultRoutingMode = RoutingMode.PriorityRules,
            PriorityRules = [new PriorityRule("Brave"), new PriorityRule("Chrome")],
        };
        var repository = new RecordingSettingsRepository(initial);
        await using var application = new MediaLockApplication(
            new InMemoryCatalog(
                new MediaSessionCatalogSnapshot([brave, chrome], brave.Key)),
            new MediaRouter(new SuccessfulController()),
            repository,
            loginStartupManager: null);
        await application.StartAsync(CancellationToken.None);
        var updated = initial with
        {
            PriorityRules = [new PriorityRule("Chrome"), new PriorityRule("Brave")],
        };

        var result = await application.DispatchAsync(
            new ApplicationIntent.UpdateSettings(updated),
            CancellationToken.None);

        Assert.Equal(RoutingMode.PriorityRules, result.State.Router.Mode);
        Assert.Equal(chrome.Key, result.State.Router.ActiveTarget);
        Assert.Equal(updated, result.State.Settings);
    }

    [Fact]
    public async Task PriorityRuleTargetChangePublishesPlaybackLockReleaseAtomically()
    {
        var brave = Session("brave", "Brave");
        var chrome = Session("chrome", "Chrome");
        var initial = MediaLockSettings.Default with
        {
            DefaultRoutingMode = RoutingMode.PriorityRules,
            PriorityRules = [new PriorityRule("Brave"), new PriorityRule("Chrome")],
        };
        await using var application = new MediaLockApplication(
            new InMemoryCatalog(
                new MediaSessionCatalogSnapshot([brave, chrome], brave.Key)),
            new MediaRouter(new SuccessfulController()),
            new RecordingSettingsRepository(initial),
            loginStartupManager: null);
        await application.StartAsync(CancellationToken.None);
        await application.DispatchAsync(
            new ApplicationIntent.SetPlaybackStateLock(PlaybackStateLockMode.KeepPlaying),
            CancellationToken.None);
        var observed = new List<MediaLockApplicationState>();
        application.StateChanged += (_, args) => observed.Add(args.State);
        var updated = initial with
        {
            PriorityRules = [new PriorityRule("Chrome"), new PriorityRule("Brave")],
        };

        var result = await application.DispatchAsync(
            new ApplicationIntent.UpdateSettings(updated),
            CancellationToken.None);

        Assert.Equal(chrome.Key, result.State.Router.ActiveTarget);
        Assert.Equal(PlaybackStateLockState.Off, result.State.PlaybackStateLock);
        Assert.DoesNotContain(observed, state =>
            state.Router.ActiveTarget == chrome.Key &&
            state.PlaybackStateLock.Mode == PlaybackStateLockMode.KeepPlaying);
    }

    [Fact]
    public async Task UpdatedRecoveryAndPrioritySettingsAreSentToTheRouter()
    {
        var session = Session("music", "Brave");
        var router = new RecordingIntentRouter();
        await using var application = new MediaLockApplication(
            new InMemoryCatalog(
                new MediaSessionCatalogSnapshot([session], session.Key)),
            router,
            new RecordingSettingsRepository(MediaLockSettings.Default),
            loginStartupManager: null);
        await application.StartAsync(CancellationToken.None);
        var updated = MediaLockSettings.Default with
        {
            Recovery = new RecoverySettings(
                TimeSpan.FromSeconds(27),
                FallbackPolicy.DisableRouting),
            PriorityRules = [new PriorityRule("Chrome")],
        };

        await application.DispatchAsync(
            new ApplicationIntent.UpdateSettings(updated),
            CancellationToken.None);

        var options = Assert.IsType<RouterIntent.UpdateOptions>(router.Intents[^1]).Options;
        Assert.Equal(TimeSpan.FromSeconds(27), options.RecoveryTimeout);
        Assert.Equal(FallbackPolicy.DisableRouting, options.FallbackPolicy);
        Assert.Equal(updated.PriorityRules, options.PriorityRules);
    }

    [Fact]
    public async Task UpdatedRecoveryTimeoutReplacesTheActiveApplicationDeadline()
    {
        var locked = Session("music", "Brave");
        var initial = MediaLockSettings.Default with
        {
            Recovery = new RecoverySettings(TimeSpan.FromSeconds(5), FallbackPolicy.Wait),
        };
        var catalog = new InMemoryCatalog(
            new MediaSessionCatalogSnapshot([locked], locked.Key));
        await using var application = new MediaLockApplication(
            catalog,
            new MediaRouter(new SuccessfulController()),
            new RecordingSettingsRepository(initial),
            loginStartupManager: null);
        await application.StartAsync(CancellationToken.None);
        await application.DispatchAsync(
            new ApplicationIntent.LockSession(locked.Key),
            CancellationToken.None);
        await catalog.PublishAsync(new MediaSessionCatalogSnapshot([], null));
        await WaitUntilAsync(() => application.State.Router.Status == RouterStatus.Recovering);
        var updated = initial with
        {
            Recovery = new RecoverySettings(TimeSpan.Zero, FallbackPolicy.DisableRouting),
        };

        await application.DispatchAsync(
            new ApplicationIntent.UpdateSettings(updated),
            CancellationToken.None);
        await WaitUntilAsync(() => application.State.Router.Status == RouterStatus.Unavailable);

        Assert.Equal(FallbackPolicy.DisableRouting, application.State.Router.ActiveFallback);
        Assert.Equal(updated, application.State.Settings);
    }

    [Fact]
    public async Task PriorityRulesDefaultActivatesWithoutPersistedLockedTarget()
    {
        var preferred = Session("preferred", "music");
        var current = Session("current", "browser");
        var settings = MediaLockSettings.Default with
        {
            DefaultRoutingMode = RoutingMode.PriorityRules,
            PriorityRules = [new PriorityRule("music")],
        };
        var catalog = new InMemoryCatalog(
            new MediaSessionCatalogSnapshot([current, preferred], current.Key));
        var controller = new RecordingController();
        await using var application = new MediaLockApplication(
            catalog,
            new MediaRouter(controller),
            new RecordingSettingsRepository(settings),
            new RecordingLoginStartupManager(),
            new RecordingRuntimeStateRepository());

        await application.StartAsync(CancellationToken.None);
        var routed = await application.DispatchAsync(
            new ApplicationIntent.Route(MediaCommand.Next),
            CancellationToken.None);

        Assert.Equal(RoutingMode.PriorityRules, application.State.Router.Mode);
        Assert.Equal(preferred.Key, routed.Decision.Target);
        Assert.Equal(RouteReason.PriorityRule, routed.Decision.Reason);
    }

    [Fact]
    public async Task CapturedInputTargetIsPreservedAcrossTheApplicationBoundary()
    {
        var captured = Session("captured", "music");
        var current = Session("current", "browser");
        var catalog = new InMemoryCatalog(
            new MediaSessionCatalogSnapshot([captured, current], current.Key));
        var controller = new RecordingController();
        await using var application = new MediaLockApplication(
            catalog,
            new MediaRouter(controller));
        await application.StartAsync(CancellationToken.None);

        var result = await application.DispatchAsync(
            new ApplicationIntent.Route(MediaCommand.TogglePlayPause, captured.Key),
            CancellationToken.None);

        Assert.Equal(RouteDecisionKind.Skipped, result.Decision.Kind);
        Assert.Equal(RouteReason.InputTargetChanged, result.Decision.Reason);
        Assert.Empty(controller.Commands);
    }

    [Fact]
    public async Task ActivatingPriorityRulesPersistsItAsTheStartupRoutingMode()
    {
        var session = Session("music", "music");
        var settings = MediaLockSettings.Default with
        {
            PriorityRules = [new PriorityRule("music")],
        };
        var catalog = new InMemoryCatalog(
            new MediaSessionCatalogSnapshot([session], session.Key));
        var settingsRepository = new RecordingSettingsRepository(settings);
        await using var application = new MediaLockApplication(
            catalog,
            new MediaRouter(new SuccessfulController()),
            settingsRepository,
            new RecordingLoginStartupManager());
        await application.StartAsync(CancellationToken.None);

        var result = await application.DispatchAsync(
            new ApplicationIntent.UsePriorityRules(),
            CancellationToken.None);

        Assert.Equal(RoutingMode.PriorityRules, result.State.Router.Mode);
        Assert.Equal(session.Key, result.State.Router.ActiveTarget);
        var saved = Assert.Single(settingsRepository.Saved);
        Assert.Equal(RoutingMode.PriorityRules, saved.DefaultRoutingMode);
        Assert.Equal(RoutingMode.PriorityRules, result.State.Settings.DefaultRoutingMode);
    }

    [Fact]
    public async Task ActivatingWindowsAutoPersistsItAsTheStartupRoutingMode()
    {
        var session = Session("music", "music");
        var settings = MediaLockSettings.Default with
        {
            DefaultRoutingMode = RoutingMode.PriorityRules,
        };
        var settingsRepository = new RecordingSettingsRepository(settings);
        await using var application = new MediaLockApplication(
            new InMemoryCatalog(new MediaSessionCatalogSnapshot([session], session.Key)),
            new MediaRouter(new SuccessfulController()),
            settingsRepository,
            new RecordingLoginStartupManager());
        await application.StartAsync(CancellationToken.None);

        var result = await application.DispatchAsync(
            new ApplicationIntent.UseWindowsAuto(),
            CancellationToken.None);

        Assert.Equal(RoutingMode.WindowsAuto, result.State.Router.Mode);
        var saved = Assert.Single(settingsRepository.Saved);
        Assert.Equal(RoutingMode.WindowsAuto, saved.DefaultRoutingMode);
        Assert.Equal(RoutingMode.WindowsAuto, result.State.Settings.DefaultRoutingMode);
    }

    [Fact]
    public async Task UsingWindowsAutoForCurrentRunDoesNotReplaceTheStartupRoutingMode()
    {
        var observedAt = DateTimeOffset.Parse("2026-08-22T00:00:00Z");
        var session = Session("music", "music");
        var settings = MediaLockSettings.Default with
        {
            DefaultRoutingMode = RoutingMode.AppLock,
        };
        var settingsRepository = new RecordingSettingsRepository(settings);
        var runtimeStateRepository = new RecordingRuntimeStateRepository(new RuntimeStateDocument(
            RuntimeStateDocument.CurrentSchemaVersion,
            RoutingMode.AppLock,
            new PersistedLockedTarget(new PersistedSessionFingerprint(
                "music",
                null,
                PlaybackStatus.Playing,
                observedAt,
                MediaPlaybackType.Unknown,
                null,
                null))));
        await using var application = new MediaLockApplication(
            new InMemoryCatalog(new MediaSessionCatalogSnapshot([session], session.Key)),
            new MediaRouter(new SuccessfulController()),
            settingsRepository,
            new RecordingLoginStartupManager(),
            runtimeStateRepository);
        await application.StartAsync(CancellationToken.None);
        var runtimeSaveCount = runtimeStateRepository.Saved.Count;

        var result = await application.DispatchAsync(
            new ApplicationIntent.UseWindowsAutoForCurrentRun(),
            CancellationToken.None);
        await application.DispatchAsync(
            new ApplicationIntent.Route(MediaCommand.TogglePlayPause),
            CancellationToken.None);

        Assert.Equal(RoutingMode.WindowsAuto, result.State.Router.Mode);
        Assert.Equal(RoutingMode.AppLock, result.State.Settings.DefaultRoutingMode);
        Assert.Empty(settingsRepository.Saved);
        Assert.Equal(runtimeSaveCount, runtimeStateRepository.Saved.Count);
        Assert.NotNull(runtimeStateRepository.Loaded.LockedTarget);
    }

    [Fact]
    public async Task StartupSettingsFailureRestoresAPreviouslyPersistedLockTarget()
    {
        var observedAt = DateTimeOffset.Parse("2026-08-22T00:00:00Z");
        var session = Session("music", "Brave");
        var settings = MediaLockSettings.Default with
        {
            DefaultRoutingMode = RoutingMode.AppLock,
        };
        var persistedAppLock = new RuntimeStateDocument(
            RuntimeStateDocument.CurrentSchemaVersion,
            RoutingMode.AppLock,
            new PersistedLockedTarget(new PersistedSessionFingerprint(
                "Brave",
                null,
                PlaybackStatus.Playing,
                observedAt,
                MediaPlaybackType.Unknown,
                null,
                null)));
        var runtimeStateRepository = new RecordingRuntimeStateRepository(persistedAppLock);
        await using var application = new MediaLockApplication(
            new InMemoryCatalog(new MediaSessionCatalogSnapshot([session], session.Key)),
            new MediaRouter(new SuccessfulController()),
            new FailingSaveSettingsRepository(settings),
            new RecordingLoginStartupManager(),
            runtimeStateRepository);
        await application.StartAsync(CancellationToken.None);
        var runtimeSaveCount = runtimeStateRepository.Saved.Count;

        await Assert.ThrowsAsync<InvalidOperationException>(() => application.DispatchAsync(
            new ApplicationIntent.UsePriorityRules(),
            CancellationToken.None).AsTask());
        var persistenceError = application.State.ErrorMessage;
        await application.DispatchAsync(
            new ApplicationIntent.Route(MediaCommand.TogglePlayPause),
            CancellationToken.None);

        Assert.Equal(RoutingMode.PriorityRules, application.State.Router.Mode);
        Assert.Equal(RoutingMode.AppLock, application.State.Settings.DefaultRoutingMode);
        Assert.Equal(runtimeSaveCount + 2, runtimeStateRepository.Saved.Count);
        Assert.Equal(persistedAppLock, runtimeStateRepository.Saved.Last());
        Assert.Contains(
            "previous runtime state was restored",
            persistenceError,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task LockingAnApplicationPersistsAppLockAsTheStartupRoutingMode()
    {
        var session = Session("music", "Brave");
        var settingsRepository = new RecordingSettingsRepository(MediaLockSettings.Default);
        var runtimeStateRepository = new RecordingRuntimeStateRepository();
        await using var application = new MediaLockApplication(
            new InMemoryCatalog(new MediaSessionCatalogSnapshot([session], session.Key)),
            new MediaRouter(new SuccessfulController()),
            settingsRepository,
            new RecordingLoginStartupManager(),
            runtimeStateRepository);
        await application.StartAsync(CancellationToken.None);

        var result = await application.DispatchAsync(
            new ApplicationIntent.LockApplication("Brave"),
            CancellationToken.None);

        Assert.Equal(RoutingMode.AppLock, result.State.Router.Mode);
        var savedSettings = Assert.Single(settingsRepository.Saved);
        Assert.Equal(RoutingMode.AppLock, savedSettings.DefaultRoutingMode);
        var savedRuntimeState = runtimeStateRepository.Saved.Last();
        Assert.Equal(RoutingMode.AppLock, savedRuntimeState.Mode);
        Assert.Equal("Brave", savedRuntimeState.LockedTarget?.Fingerprint.SourceAppUserModelId);
    }

    [Fact]
    public async Task LockingASessionPersistsSessionLockAsTheStartupRoutingMode()
    {
        var session = Session("music", "Brave");
        var settingsRepository = new RecordingSettingsRepository(MediaLockSettings.Default);
        var runtimeStateRepository = new RecordingRuntimeStateRepository();
        await using var application = new MediaLockApplication(
            new InMemoryCatalog(new MediaSessionCatalogSnapshot([session], session.Key)),
            new MediaRouter(new SuccessfulController()),
            settingsRepository,
            new RecordingLoginStartupManager(),
            runtimeStateRepository);
        await application.StartAsync(CancellationToken.None);

        var result = await application.DispatchAsync(
            new ApplicationIntent.LockSession(session.Key),
            CancellationToken.None);

        Assert.Equal(RoutingMode.SessionLock, result.State.Router.Mode);
        var savedSettings = Assert.Single(settingsRepository.Saved);
        Assert.Equal(RoutingMode.SessionLock, savedSettings.DefaultRoutingMode);
        var savedRuntimeState = runtimeStateRepository.Saved.Last();
        Assert.Equal(RoutingMode.SessionLock, savedRuntimeState.Mode);
        Assert.Equal("Brave", savedRuntimeState.LockedTarget?.Fingerprint.SourceAppUserModelId);
    }

    [Fact]
    public async Task FailedSessionLockDoesNotReplaceTheStartupRoutingMode()
    {
        var session = Session("music", "Brave");
        var settingsRepository = new RecordingSettingsRepository(MediaLockSettings.Default);
        await using var application = new MediaLockApplication(
            new InMemoryCatalog(new MediaSessionCatalogSnapshot([session], session.Key)),
            new MediaRouter(new SuccessfulController()),
            settingsRepository,
            new RecordingLoginStartupManager(),
            new RecordingRuntimeStateRepository());
        await application.StartAsync(CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentException>(() => application.DispatchAsync(
            new ApplicationIntent.LockSession(new SessionKey("missing")),
            CancellationToken.None).AsTask());

        Assert.Equal(RoutingMode.WindowsAuto, application.State.Router.Mode);
        Assert.Equal(RoutingMode.WindowsAuto, application.State.Settings.DefaultRoutingMode);
        Assert.Empty(settingsRepository.Saved);
    }

    [Fact]
    public async Task RuntimePersistenceFailureDoesNotSaveALockAsTheStartupRoutingMode()
    {
        var session = Session("music", "Brave");
        var settingsRepository = new RecordingSettingsRepository(MediaLockSettings.Default);
        var runtimeStateRepository = new FailingRuntimeStateRepository();
        await using var application = new MediaLockApplication(
            new InMemoryCatalog(new MediaSessionCatalogSnapshot([session], session.Key)),
            new MediaRouter(new SuccessfulController()),
            settingsRepository,
            new RecordingLoginStartupManager(),
            runtimeStateRepository);
        await application.StartAsync(CancellationToken.None);
        var runtimeSaveAttempts = runtimeStateRepository.SaveAttempts;

        var result = await application.DispatchAsync(
            new ApplicationIntent.LockApplication("Brave"),
            CancellationToken.None);
        var persistenceError = result.State.ErrorMessage;
        await application.DispatchAsync(
            new ApplicationIntent.Route(MediaCommand.TogglePlayPause),
            CancellationToken.None);

        Assert.Equal(RoutingMode.AppLock, result.State.Router.Mode);
        Assert.Equal(RoutingMode.WindowsAuto, result.State.Settings.DefaultRoutingMode);
        Assert.Empty(settingsRepository.Saved);
        Assert.Equal(runtimeSaveAttempts + 1, runtimeStateRepository.SaveAttempts);
        Assert.Contains("state.json", persistenceError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartupSettingsFailureKeepsTheCurrentRunChangeAndPriorStartupMode()
    {
        var session = Session("music", "Brave");
        await using var application = new MediaLockApplication(
            new InMemoryCatalog(new MediaSessionCatalogSnapshot([session], session.Key)),
            new MediaRouter(new SuccessfulController()),
            new FailingSaveSettingsRepository(MediaLockSettings.Default),
            new RecordingLoginStartupManager(),
            new RecordingRuntimeStateRepository());
        await application.StartAsync(CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => application.DispatchAsync(
            new ApplicationIntent.UsePriorityRules(),
            CancellationToken.None).AsTask());

        Assert.Equal(RoutingMode.PriorityRules, application.State.Router.Mode);
        Assert.Equal(RoutingMode.WindowsAuto, application.State.Settings.DefaultRoutingMode);
        Assert.Contains("startup mode could not be saved", application.State.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("settings.json", application.State.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TargetlessStartupModePreservesARuntimePersistenceError()
    {
        var session = Session("music", "Brave");
        var settingsRepository = new RecordingSettingsRepository(MediaLockSettings.Default);
        await using var application = new MediaLockApplication(
            new InMemoryCatalog(new MediaSessionCatalogSnapshot([session], session.Key)),
            new MediaRouter(new SuccessfulController()),
            settingsRepository,
            new RecordingLoginStartupManager(),
            new FailingRuntimeStateRepository());
        await application.StartAsync(CancellationToken.None);

        var result = await application.DispatchAsync(
            new ApplicationIntent.UsePriorityRules(),
            CancellationToken.None);

        Assert.Equal(RoutingMode.PriorityRules, result.State.Router.Mode);
        Assert.Equal(RoutingMode.PriorityRules, result.State.Settings.DefaultRoutingMode);
        Assert.Contains("state.json", result.State.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal(RoutingMode.PriorityRules, Assert.Single(settingsRepository.Saved).DefaultRoutingMode);
    }

    [Fact]
    public async Task LoadedRecoverySettingsConfigureTheRouterBeforeCatalogProcessing()
    {
        var session = Session("music", "Brave");
        var settings = MediaLockSettings.Default with
        {
            Recovery = new RecoverySettings(TimeSpan.FromSeconds(42), FallbackPolicy.Wait),
        };
        var router = new RecordingIntentRouter();
        await using var application = new MediaLockApplication(
            new InMemoryCatalog(new MediaSessionCatalogSnapshot([session], session.Key)),
            router,
            new RecordingSettingsRepository(settings),
            loginStartupManager: null);

        await application.StartAsync(CancellationToken.None);

        var options = Assert.IsType<RouterIntent.UpdateOptions>(router.Intents[0]).Options;
        Assert.Equal(TimeSpan.FromSeconds(42), options.RecoveryTimeout);
        Assert.Equal(FallbackPolicy.Wait, options.FallbackPolicy);
        Assert.IsType<RouterIntent.CatalogUpdated>(router.Intents[1]);
    }

    [Fact]
    public async Task DefaultSessionLockRestoresPersistedTargetAfterInitialCatalog()
    {
        var observedAt = DateTimeOffset.Parse("2026-08-22T00:00:00Z");
        var session = Session("replacement", "Brave");
        var settings = MediaLockSettings.Default with
        {
            DefaultRoutingMode = RoutingMode.SessionLock,
        };
        var runtimeState = new RecordingRuntimeStateRepository(new RuntimeStateDocument(
            RuntimeStateDocument.CurrentSchemaVersion,
            RoutingMode.SessionLock,
            new PersistedLockedTarget(new PersistedSessionFingerprint(
                "Brave",
                null,
                PlaybackStatus.Playing,
                observedAt,
                MediaPlaybackType.Unknown,
                null,
                null))));
        await using var application = new MediaLockApplication(
            new InMemoryCatalog(new MediaSessionCatalogSnapshot([session], session.Key)),
            new MediaRouter(new SuccessfulController()),
            new RecordingSettingsRepository(settings),
            loginStartupManager: null,
            runtimeState);

        await application.StartAsync(CancellationToken.None);

        Assert.Equal(RoutingMode.SessionLock, application.State.Router.Mode);
        Assert.Equal(RouterStatus.Locked, application.State.Router.Status);
        Assert.Equal(session.Key, application.State.Router.LockedTarget!.ResolvedSession);
        Assert.All(runtimeState.Saved, saved => Assert.Equal(RoutingMode.SessionLock, saved.Mode));
    }

    [Fact]
    public async Task DefaultAppLockRestoresPersistedApplicationAfterInitialCatalog()
    {
        var observedAt = DateTimeOffset.Parse("2026-08-22T00:00:00Z");
        var session = Session("music", "Brave");
        var settings = MediaLockSettings.Default with
        {
            DefaultRoutingMode = RoutingMode.AppLock,
        };
        var runtimeState = new RecordingRuntimeStateRepository(new RuntimeStateDocument(
            RuntimeStateDocument.CurrentSchemaVersion,
            RoutingMode.AppLock,
            new PersistedLockedTarget(new PersistedSessionFingerprint(
                "Brave",
                null,
                PlaybackStatus.Playing,
                observedAt,
                MediaPlaybackType.Unknown,
                null,
                null))));
        await using var application = new MediaLockApplication(
            new InMemoryCatalog(new MediaSessionCatalogSnapshot([session], session.Key)),
            new MediaRouter(new SuccessfulController()),
            new RecordingSettingsRepository(settings),
            loginStartupManager: null,
            runtimeState);

        await application.StartAsync(CancellationToken.None);

        Assert.Equal(RoutingMode.AppLock, application.State.Router.Mode);
        Assert.Equal(RouterStatus.Locked, application.State.Router.Status);
        Assert.Equal(session.Key, application.State.Router.LockedTarget!.ResolvedSession);
        Assert.All(runtimeState.Saved, saved => Assert.Equal(RoutingMode.AppLock, saved.Mode));
    }

    [Fact]
    public async Task DefaultAppLockWithoutPersistedTargetStaysWindowsAutoWithWarning()
    {
        var session = Session("music", "Brave");
        var settings = MediaLockSettings.Default with
        {
            DefaultRoutingMode = RoutingMode.AppLock,
        };
        await using var application = new MediaLockApplication(
            new InMemoryCatalog(new MediaSessionCatalogSnapshot([session], session.Key)),
            new MediaRouter(new SuccessfulController()),
            new RecordingSettingsRepository(settings),
            loginStartupManager: null,
            new RecordingRuntimeStateRepository());

        await application.StartAsync(CancellationToken.None);

        Assert.Equal(RoutingMode.WindowsAuto, application.State.Router.Mode);
        Assert.Contains("persisted App Lock target", application.State.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DefaultSessionLockWithoutPersistedTargetStaysWindowsAutoWithWarning()
    {
        var session = Session("music", "Brave");
        var settings = MediaLockSettings.Default with
        {
            DefaultRoutingMode = RoutingMode.SessionLock,
        };
        await using var application = new MediaLockApplication(
            new InMemoryCatalog(new MediaSessionCatalogSnapshot([session], session.Key)),
            new MediaRouter(new SuccessfulController()),
            new RecordingSettingsRepository(settings),
            loginStartupManager: null,
            new RecordingRuntimeStateRepository());

        await application.StartAsync(CancellationToken.None);

        Assert.Equal(RoutingMode.WindowsAuto, application.State.Router.Mode);
        Assert.Contains("persisted Session Lock target", application.State.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DefaultWindowsAutoIgnoresPersistedSessionLock()
    {
        var session = Session("replacement", "Brave");
        var runtimeState = new RecordingRuntimeStateRepository(new RuntimeStateDocument(
            RuntimeStateDocument.CurrentSchemaVersion,
            RoutingMode.SessionLock,
            new PersistedLockedTarget(new PersistedSessionFingerprint(
                "Brave",
                null,
                PlaybackStatus.Playing,
                DateTimeOffset.Parse("2026-08-22T00:00:00Z"),
                MediaPlaybackType.Unknown,
                null,
                null))));
        await using var application = new MediaLockApplication(
            new InMemoryCatalog(new MediaSessionCatalogSnapshot([session], session.Key)),
            new MediaRouter(new SuccessfulController()),
            new RecordingSettingsRepository(MediaLockSettings.Default),
            loginStartupManager: null,
            runtimeState);

        await application.StartAsync(CancellationToken.None);

        Assert.Equal(RoutingMode.WindowsAuto, application.State.Router.Mode);
        Assert.Null(application.State.Router.LockedTarget);
        Assert.Null(application.State.ErrorMessage);
    }

    [Fact]
    public async Task RouteDiagnosticsRemainInsideTheSerializedApplicationDispatch()
    {
        var session = Session("music", "Brave");
        var router = new ImmediateCountingRouter();
        var log = new BlockingRouteDiagnosticLog();
        await using var application = new MediaLockApplication(
            new InMemoryCatalog(new MediaSessionCatalogSnapshot([session], session.Key)),
            router,
            settingsRepository: null,
            loginStartupManager: null,
            runtimeStateRepository: null,
            diagnosticLog: log);
        await application.StartAsync(CancellationToken.None);

        var first = application.DispatchAsync(
            new ApplicationIntent.Route(MediaCommand.Next),
            CancellationToken.None).AsTask();
        await log.Started.WaitAsync(TimeSpan.FromSeconds(1));
        var second = application.DispatchAsync(
            new ApplicationIntent.Route(MediaCommand.Previous),
            CancellationToken.None).AsTask();

        try
        {
            Assert.False(await router.TryWaitForCallCountAsync(
                expected: 3,
                TimeSpan.FromMilliseconds(100)));
        }
        finally
        {
            log.Release();
        }

        await Task.WhenAll(first, second);
        Assert.Equal(3, router.CallCount);
    }

    [Fact]
    public async Task SettingsLoadIssueRemainsObservableAfterInitialCatalogSnapshot()
    {
        var session = Session("music", "Brave");
        var repository = new RecordingSettingsRepository(
            MediaLockSettings.Default,
            [new ConfigurationIssue("$", "settings.json is corrupt; defaults are active.")]);
        await using var application = new MediaLockApplication(
            new InMemoryCatalog(new MediaSessionCatalogSnapshot([session], session.Key)),
            new MediaRouter(new SuccessfulController()),
            repository,
            loginStartupManager: null);

        await application.StartAsync(CancellationToken.None);

        Assert.Contains("settings.json", application.State.ErrorMessage, StringComparison.Ordinal);
        Assert.Single(application.State.Router.Sessions);
    }

    [Fact]
    public async Task RouteOutcomeIsWrittenWithoutMediaMetadata()
    {
        var session = Session("music", "Brave");
        var log = new RecordingDiagnosticLog();
        await using var application = new MediaLockApplication(
            new InMemoryCatalog(new MediaSessionCatalogSnapshot([session], session.Key)),
            new MediaRouter(new SuccessfulController()),
            settingsRepository: null,
            loginStartupManager: null,
            runtimeStateRepository: null,
            diagnosticLog: log);
        await application.StartAsync(CancellationToken.None);

        await application.DispatchAsync(
            new ApplicationIntent.Route(MediaCommand.TogglePlayPause),
            CancellationToken.None);

        var route = Assert.Single(log.Events, entry => entry.Name == "route.completed");
        var stateChanged = Assert.Single(log.Events, entry => entry.Name == "state.changed");
        Assert.Equal("Routed", route.Properties?["decision"]);
        Assert.Equal(session.Key.Value, route.Properties?["target"]);
        Assert.Equal("WindowsAuto", stateChanged.Properties?["mode"]);
        Assert.DoesNotContain(log.Events.SelectMany(entry => entry.Properties?.Keys ?? []), key =>
            key.Contains("title", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("artist", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RouterTransitionsArePersistedWithoutRestoringThePreviousLock()
    {
        var session = Session("music", "Brave");
        var catalog = new InMemoryCatalog(
            new MediaSessionCatalogSnapshot([session], session.Key));
        var runtimeState = new RecordingRuntimeStateRepository();
        await using var application = new MediaLockApplication(
            catalog,
            new MediaRouter(new SuccessfulController()),
            settingsRepository: null,
            loginStartupManager: null,
            runtimeStateRepository: runtimeState);

        await application.StartAsync(CancellationToken.None);
        await application.DispatchAsync(
            new ApplicationIntent.LockSession(session.Key),
            CancellationToken.None);

        var saved = Assert.IsType<RuntimeStateDocument>(runtimeState.Saved.Last());
        Assert.Equal(RoutingMode.SessionLock, saved.Mode);
        Assert.Equal("Brave", saved.LockedTarget?.Fingerprint.SourceAppUserModelId);
        Assert.Equal(RoutingMode.WindowsAuto, runtimeState.Loaded.Mode);
    }

    [Fact]
    public async Task RuntimeAutosaveFailureIsObservableWithoutStoppingMediaRouting()
    {
        var session = Session("music", "Brave");
        await using var application = new MediaLockApplication(
            new InMemoryCatalog(new MediaSessionCatalogSnapshot([session], session.Key)),
            new MediaRouter(new SuccessfulController()),
            settingsRepository: null,
            loginStartupManager: null,
            runtimeStateRepository: new FailingRuntimeStateRepository());

        await application.StartAsync(CancellationToken.None);
        var result = await application.DispatchAsync(
            new ApplicationIntent.Route(MediaCommand.Next),
            CancellationToken.None);

        Assert.Equal(RouteDecisionKind.Routed, result.Decision.Kind);
        Assert.Contains("state.json", application.State.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdatingDesktopSettingsPersistsAndSynchronizesLoginStartup()
    {
        var session = Session("music", "Brave");
        var catalog = new InMemoryCatalog(
            new MediaSessionCatalogSnapshot([session], session.Key));
        var settingsRepository = new RecordingSettingsRepository(MediaLockSettings.Default);
        var startup = new RecordingLoginStartupManager();
        await using var application = new MediaLockApplication(
            catalog,
            new MediaRouter(new SuccessfulController()),
            settingsRepository,
            startup);
        await application.StartAsync(CancellationToken.None);
        var updated = MediaLockSettings.Default with
        {
            Desktop = new DesktopSettings(
                CloseToTray: false,
                StartWithWindows: true),
        };

        await application.DispatchAsync(
            new ApplicationIntent.UpdateSettings(updated),
            CancellationToken.None);

        Assert.Equal(updated, application.State.Settings);
        Assert.Equal([updated], settingsRepository.Saved);
        Assert.Equal([true], startup.Updates);
    }

    [Fact]
    public async Task FailedLoginStartupUpdateRollsSettingsBackToThePreviousValue()
    {
        var session = Session("music", "Brave");
        var repository = new RecordingSettingsRepository(MediaLockSettings.Default);
        await using var application = new MediaLockApplication(
            new InMemoryCatalog(new MediaSessionCatalogSnapshot([session], session.Key)),
            new MediaRouter(new SuccessfulController()),
            repository,
            new FailingLoginStartupManager());
        await application.StartAsync(CancellationToken.None);
        var updated = MediaLockSettings.Default with
        {
            Desktop = MediaLockSettings.Default.Desktop! with { StartWithWindows = true },
        };

        await Assert.ThrowsAnyAsync<Exception>(() => application.DispatchAsync(
            new ApplicationIntent.UpdateSettings(updated),
            CancellationToken.None).AsTask());

        Assert.Equal(MediaLockSettings.Default, application.State.Settings);
        Assert.Equal([updated, MediaLockSettings.Default], repository.Saved);
    }

    [Fact]
    public async Task CatalogSnapshotBecomesObservableApplicationState()
    {
        var session = Session("music", "Brave");
        var catalog = new InMemoryCatalog(
            new MediaSessionCatalogSnapshot([session], session.Key));
        var router = new MediaRouter(new SuccessfulController());
        await using var application = new MediaLockApplication(catalog, router);
        var observed = new List<MediaLockApplicationState>();
        application.StateChanged += (_, args) => observed.Add(args.State);

        await application.StartAsync(CancellationToken.None);

        Assert.Equal(session, Assert.Single(application.State.Router.Sessions));
        Assert.Equal(session.Key, application.State.Router.WindowsCurrentSession);
        Assert.Contains(application.State, observed);
    }

    [Fact]
    public async Task ReacquiringCatalogBecomesObservableAndSuspendsLockedRouting()
    {
        var session = Session("music", "Brave");
        var catalog = new InMemoryCatalog(
            new MediaSessionCatalogSnapshot([session], session.Key));
        var log = new RecordingDiagnosticLog();
        await using var application = new MediaLockApplication(
            catalog,
            new MediaRouter(new SuccessfulController()),
            settingsRepository: null,
            loginStartupManager: null,
            runtimeStateRepository: null,
            diagnosticLog: log);
        await application.StartAsync(CancellationToken.None);
        await application.DispatchAsync(
            new ApplicationIntent.LockSession(session.Key),
            CancellationToken.None);
        var observed = new TaskCompletionSource<MediaLockApplicationState>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        application.StateChanged += (_, args) =>
        {
            if (args.State.CatalogStatus == MediaSessionCatalogStatus.Reacquiring)
            {
                observed.TrySetResult(args.State);
            }
        };

        await catalog.PublishAsync(new MediaSessionCatalogSnapshot(
            [],
            null,
            MediaSessionCatalogStatus.Reacquiring,
            "Reacquiring GSMTC after Windows resumed."));
        var state = await observed.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(MediaSessionCatalogStatus.Reacquiring, state.CatalogStatus);
        Assert.Equal("Reacquiring GSMTC after Windows resumed.", state.CatalogStatusMessage);
        Assert.Equal(RouterStatus.Recovering, state.Router.Status);
        Assert.Empty(state.Router.Sessions);
        var diagnostic = Assert.Single(log.Events, entry => entry.Name == "catalog.status");
        Assert.Equal("Reacquiring", diagnostic.Properties!["status"]);
        Assert.DoesNotContain(diagnostic.Properties.Keys, key =>
            key.Contains("title", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("artist", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task UiIntentLocksAndRoutesThroughTheApplicationSeam()
    {
        var session = Session("music", "Brave");
        var catalog = new InMemoryCatalog(
            new MediaSessionCatalogSnapshot([session], session.Key));
        var controller = new RecordingController();
        var router = new MediaRouter(controller);
        await using var application = new MediaLockApplication(catalog, router);
        await application.StartAsync(CancellationToken.None);

        var locked = await application.DispatchAsync(
            new ApplicationIntent.LockSession(session.Key),
            CancellationToken.None);
        var routed = await application.DispatchAsync(
            new ApplicationIntent.Route(MediaCommand.Next),
            CancellationToken.None);

        Assert.Equal(RoutingMode.SessionLock, locked.State.Router.Mode);
        Assert.Equal(RouterStatus.Locked, locked.State.Router.Status);
        Assert.Equal(RouteDecisionKind.Routed, routed.Decision.Kind);
        Assert.Equal([(session.Key, MediaCommand.Next)], controller.Commands);
    }

    [Fact]
    public async Task UiIntentRoutesAnAbsoluteSeekWithoutAParallelApplicationInterface()
    {
        var session = Session(
            "music",
            "Brave",
            new MediaTimeline(
                TimeSpan.Zero,
                TimeSpan.FromMinutes(3),
                TimeSpan.FromSeconds(30),
                DateTimeOffset.Parse("2026-08-23T00:00:00Z")));
        var catalog = new InMemoryCatalog(
            new MediaSessionCatalogSnapshot([session], session.Key));
        var controller = new RecordingController();
        await using var application = new MediaLockApplication(
            catalog,
            new MediaRouter(controller));
        await application.StartAsync(CancellationToken.None);
        var command = MediaCommand.SeekAbsolute(TimeSpan.FromSeconds(75));

        var result = await application.DispatchAsync(
            new ApplicationIntent.Route(command),
            CancellationToken.None);

        Assert.Equal(RouteDecisionKind.Routed, result.Decision.Kind);
        Assert.Equal(command, result.Decision.Command);
        Assert.Equal([(session.Key, command)], controller.Commands);
    }

    [Fact]
    public async Task UiIntentLocksAnApplicationThroughTheApplicationSeam()
    {
        var session = Session("music", "Brave");
        var catalog = new InMemoryCatalog(
            new MediaSessionCatalogSnapshot([session], session.Key));
        var controller = new RecordingController();
        await using var application = new MediaLockApplication(
            catalog,
            new MediaRouter(controller));
        await application.StartAsync(CancellationToken.None);

        var locked = await application.DispatchAsync(
            new ApplicationIntent.LockApplication("Brave"),
            CancellationToken.None);
        var routed = await application.DispatchAsync(
            new ApplicationIntent.Route(MediaCommand.Next),
            CancellationToken.None);

        Assert.Equal(RoutingMode.AppLock, locked.State.Router.Mode);
        Assert.Equal(RouterStatus.Locked, locked.State.Router.Status);
        Assert.Equal(RouteReason.LockedApplication, routed.Decision.Reason);
        Assert.Equal([(session.Key, MediaCommand.Next)], controller.Commands);
    }

    [Fact]
    public async Task RecoveryDeadlineEffectAppliesFallbackWithoutUiCoordination()
    {
        var locked = Session("music", "Brave");
        var current = Session("video", "Chrome");
        var catalog = new InMemoryCatalog(
            new MediaSessionCatalogSnapshot([locked, current], locked.Key));
        var router = new MediaRouter(
            new SuccessfulController(),
            new RouterOptions(
                FallbackPolicy.WindowsCurrentSession,
                TimeSpan.Zero));
        await using var application = new MediaLockApplication(catalog, router);
        await application.StartAsync(CancellationToken.None);
        await application.DispatchAsync(
            new ApplicationIntent.LockSession(locked.Key),
            CancellationToken.None);
        var fallbackObserved = new TaskCompletionSource<MediaLockApplicationState>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        application.StateChanged += (_, args) =>
        {
            if (args.State.Router.Status == RouterStatus.Fallback)
            {
                fallbackObserved.TrySetResult(args.State);
            }
        };

        await catalog.PublishAsync(
            new MediaSessionCatalogSnapshot([current], current.Key));
        var fallback = await fallbackObserved.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(FallbackPolicy.WindowsCurrentSession, fallback.Router.ActiveFallback);
    }

    [Fact]
    public async Task UnexpectedCatalogCompletionBecomesObservableErrorState()
    {
        var session = Session("music", "Brave");
        var catalog = new InMemoryCatalog(
            new MediaSessionCatalogSnapshot([session], session.Key));
        var controller = new RecordingController();
        var router = new MediaRouter(controller);
        await using var application = new MediaLockApplication(catalog, router);
        await application.StartAsync(CancellationToken.None);
        await application.DispatchAsync(
            new ApplicationIntent.LockSession(session.Key),
            CancellationToken.None);
        await application.DispatchAsync(
            new ApplicationIntent.SetPlaybackStateLock(
                PlaybackStateLockMode.KeepPlaying),
            CancellationToken.None);
        var errorObserved = new TaskCompletionSource<MediaLockApplicationState>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        application.StateChanged += (_, args) =>
        {
            if (args.State.ErrorMessage is not null)
            {
                errorObserved.TrySetResult(args.State);
            }
        };

        catalog.Complete();
        var failed = await errorObserved.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal("Media Session catalog stopped unexpectedly.", failed.ErrorMessage);
        Assert.Equal(MediaSessionCatalogStatus.Unavailable, failed.CatalogStatus);
        Assert.Empty(failed.Router.Sessions);
        Assert.Equal(RouterStatus.Recovering, failed.Router.Status);
        Assert.Equal(
            PlaybackStateLockMode.KeepPlaying,
            failed.PlaybackStateLock.Mode);
        Assert.Equal(
            PlaybackStateLockStatus.Suspended,
            failed.PlaybackStateLock.Status);
        var routed = await application.DispatchAsync(
            new ApplicationIntent.Route(MediaCommand.Next),
            CancellationToken.None);
        Assert.Equal(RouteReason.LockedTargetRecovering, routed.Decision.Reason);
        Assert.Empty(controller.Commands);
    }

    [Fact]
    public async Task ConcurrentApplicationIntentsPublishRouterResultsInOrder()
    {
        var session = Session("music", "Brave");
        var catalog = new InMemoryCatalog(
            new MediaSessionCatalogSnapshot([session], session.Key));
        var router = new ControllableRouter();
        await using var application = new MediaLockApplication(catalog, router);
        await application.StartAsync(CancellationToken.None);

        var first = application.DispatchAsync(
            new ApplicationIntent.UseWindowsAuto(),
            CancellationToken.None).AsTask();
        await router.WaitForCallCountAsync(2);
        var second = application.DispatchAsync(
            new ApplicationIntent.UseWindowsAuto(),
            CancellationToken.None).AsTask();

        if (await router.TryWaitForCallCountAsync(3, TimeSpan.FromMilliseconds(100)))
        {
            router.CompleteCall(2, revision: 3);
            await second;
            router.CompleteCall(1, revision: 2);
        }
        else
        {
            router.CompleteCall(1, revision: 2);
            await router.WaitForCallCountAsync(3);
            router.CompleteCall(2, revision: 3);
        }

        await Task.WhenAll(first, second);

        Assert.Equal(3, application.State.Router.Revision);
    }

    [Fact]
    public async Task DisposalCancelsAnInFlightUiRouteBeforeDisposingTheRouter()
    {
        var session = Session("music", "Brave");
        var catalog = new InMemoryCatalog(
            new MediaSessionCatalogSnapshot([session], session.Key));
        var controller = new BlockingController();
        var application = new MediaLockApplication(catalog, new MediaRouter(controller));
        await application.StartAsync(CancellationToken.None);
        var route = application.DispatchAsync(
            new ApplicationIntent.Route(MediaCommand.Next),
            CancellationToken.None).AsTask();
        await controller.Started.WaitAsync(TimeSpan.FromSeconds(1));

        await application.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => route);
        Assert.True(controller.CancellationObserved);
    }

    private static MediaSessionSnapshot Session(
        string key,
        string source,
        MediaTimeline? timeline = null) => new(
        new SessionKey(key),
        source,
        PlaybackStatus.Playing,
        MediaCommandCapabilities.All,
        DateTimeOffset.Parse("2026-08-22T00:00:00Z"),
        Timeline: timeline);

    private sealed class InMemoryCatalog(MediaSessionCatalogSnapshot initial) : IMediaSessionCatalog
    {
        private readonly Channel<MediaSessionCatalogSnapshot> snapshots =
            Channel.CreateUnbounded<MediaSessionCatalogSnapshot>();

        public async IAsyncEnumerable<MediaSessionCatalogSnapshot> WatchAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return initial;
            await foreach (var snapshot in snapshots.Reader.ReadAllAsync(cancellationToken))
            {
                yield return snapshot;
            }
        }

        public ValueTask PublishAsync(MediaSessionCatalogSnapshot snapshot) =>
            snapshots.Writer.WriteAsync(snapshot);

        public void Complete() => snapshots.Writer.TryComplete();

        public ValueTask DisposeAsync()
        {
            snapshots.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SuccessfulController : IMediaController
    {
        public ValueTask<MediaControlResult> TryExecuteAsync(
            SessionKey target,
            MediaCommand command,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(MediaControlResult.Succeeded);
    }

    private sealed class RecordingController : IMediaController
    {
        private TaskCompletionSource changed = NewSignal();

        public List<(SessionKey Target, MediaCommand Command)> Commands { get; } = [];

        public ValueTask<MediaControlResult> TryExecuteAsync(
            SessionKey target,
            MediaCommand command,
            CancellationToken cancellationToken)
        {
            Commands.Add((target, command));
            changed.TrySetResult();
            changed = NewSignal();
            return ValueTask.FromResult(MediaControlResult.Succeeded);
        }

        public async Task WaitForCommandCountAsync(int expected)
        {
            while (Commands.Count < expected)
            {
                await changed.Task.WaitAsync(TimeSpan.FromSeconds(1));
            }
        }

        private static TaskCompletionSource NewSignal() => new(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class ControllableRouter : IMediaRouter
    {
        private readonly List<TaskCompletionSource<RouterResult>> calls = [];
        private readonly object sync = new();
        private TaskCompletionSource callCountChanged = NewSignal();

        public ValueTask<RouterResult> DispatchAsync(
            RouterIntent intent,
            CancellationToken cancellationToken)
        {
            lock (sync)
            {
                if (calls.Count == 0)
                {
                    calls.Add(new TaskCompletionSource<RouterResult>());
                    return ValueTask.FromResult(Result(revision: 1));
                }

                var completion = new TaskCompletionSource<RouterResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                calls.Add(completion);
                callCountChanged.TrySetResult();
                callCountChanged = NewSignal();
                return new ValueTask<RouterResult>(completion.Task.WaitAsync(cancellationToken));
            }
        }

        public async Task WaitForCallCountAsync(int expected)
        {
            while (true)
            {
                Task signal;
                lock (sync)
                {
                    if (calls.Count >= expected)
                    {
                        return;
                    }

                    signal = callCountChanged.Task;
                }

                await signal.WaitAsync(TimeSpan.FromSeconds(1));
            }
        }

        public async Task<bool> TryWaitForCallCountAsync(int expected, TimeSpan timeout)
        {
            try
            {
                await WaitForCallCountAsync(expected).WaitAsync(timeout);
                return true;
            }
            catch (TimeoutException)
            {
                return false;
            }
        }

        public void CompleteCall(int controlledCallIndex, long revision)
        {
            TaskCompletionSource<RouterResult> completion;
            lock (sync)
            {
                completion = calls[controlledCallIndex];
            }

            completion.SetResult(Result(revision));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private static RouterResult Result(long revision) => new(
            RouterState.Initial with { Revision = revision },
            RouteDecision.StateUpdated);

        private static TaskCompletionSource NewSignal() => new(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class BlockingController : IMediaController
    {
        private readonly TaskCompletionSource started = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => started.Task;

        public bool CancellationObserved { get; private set; }

        public async ValueTask<MediaControlResult> TryExecuteAsync(
            SessionKey target,
            MediaCommand command,
            CancellationToken cancellationToken)
        {
            started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return MediaControlResult.Succeeded;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CancellationObserved = true;
                throw;
            }
        }
    }

    private sealed class RecordingSettingsRepository(
        MediaLockSettings initial,
        System.Collections.Immutable.ImmutableArray<ConfigurationIssue> issues = default) : ISettingsRepository
    {
        public List<MediaLockSettings> Saved { get; } = [];

        public ValueTask<ConfigurationLoadResult<MediaLockSettings>> LoadAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new ConfigurationLoadResult<MediaLockSettings>(
                initial,
                UsedDefaults: false,
                Issues: issues.IsDefault ? [] : issues));

        public ValueTask SaveAsync(
            MediaLockSettings settings,
            CancellationToken cancellationToken)
        {
            Saved.Add(settings);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeWorkstationLockState : IWorkstationLockState
    {
        public bool IsLocked { get; private set; }

        public event Action? Locked;

        public event Action? Unlocked;

        public void Lock()
        {
            IsLocked = true;
            Locked?.Invoke();
        }

        public void Unlock()
        {
            IsLocked = false;
            Unlocked?.Invoke();
        }
    }

    private sealed class FailingSaveSettingsRepository(MediaLockSettings initial) : ISettingsRepository
    {
        public ValueTask<ConfigurationLoadResult<MediaLockSettings>> LoadAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new ConfigurationLoadResult<MediaLockSettings>(
                initial,
                UsedDefaults: false,
                Issues: []));

        public ValueTask SaveAsync(
            MediaLockSettings settings,
            CancellationToken cancellationToken) =>
            ValueTask.FromException(new IOException("Could not write settings.json."));
    }

    private sealed class RecordingLoginStartupManager : ILoginStartupManager
    {
        public List<bool> Updates { get; } = [];

        public ValueTask<bool> IsEnabledAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(false);

        public ValueTask SetEnabledAsync(bool enabled, CancellationToken cancellationToken)
        {
            Updates.Add(enabled);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingRuntimeStateRepository : IRuntimeStateRepository
    {
        public RecordingRuntimeStateRepository(RuntimeStateDocument? loaded = null)
        {
            Loaded = loaded ?? new RuntimeStateDocument(
                RuntimeStateDocument.CurrentSchemaVersion,
                RoutingMode.WindowsAuto,
                LockedTarget: null);
        }

        public RuntimeStateDocument Loaded { get; }

        public List<RuntimeStateDocument> Saved { get; } = [];

        public ValueTask<ConfigurationLoadResult<RuntimeStateDocument>> LoadAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new ConfigurationLoadResult<RuntimeStateDocument>(
                Loaded,
                UsedDefaults: false,
                Issues: []));

        public ValueTask SaveAsync(
            RuntimeStateDocument state,
            CancellationToken cancellationToken)
        {
            Saved.Add(state);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingDiagnosticLog : IDiagnosticLog
    {
        public List<DiagnosticEvent> Events { get; } = [];

        public ValueTask WriteAsync(
            DiagnosticEvent diagnosticEvent,
            CancellationToken cancellationToken)
        {
            Events.Add(diagnosticEvent);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FailingRuntimeStateRepository : IRuntimeStateRepository
    {
        public int SaveAttempts { get; private set; }

        public ValueTask<ConfigurationLoadResult<RuntimeStateDocument>> LoadAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new ConfigurationLoadResult<RuntimeStateDocument>(
                new RuntimeStateDocument(
                    RuntimeStateDocument.CurrentSchemaVersion,
                    RoutingMode.WindowsAuto,
                    LockedTarget: null),
                UsedDefaults: false,
                Issues: []));

        public ValueTask SaveAsync(
            RuntimeStateDocument state,
            CancellationToken cancellationToken)
        {
            SaveAttempts++;
            return ValueTask.FromException(new IOException("Could not write state.json."));
        }
    }

    private sealed class FailingLoginStartupManager : ILoginStartupManager
    {
        public ValueTask<bool> IsEnabledAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(false);

        public ValueTask SetEnabledAsync(bool enabled, CancellationToken cancellationToken) =>
            ValueTask.FromException(new IOException("Could not update the Run key."));
    }

    private sealed class ImmediateCountingRouter : IMediaRouter
    {
        private readonly object sync = new();
        private TaskCompletionSource changed = NewSignal();

        public int CallCount { get; private set; }

        public ValueTask<RouterResult> DispatchAsync(
            RouterIntent intent,
            CancellationToken cancellationToken)
        {
            int revision;
            lock (sync)
            {
                CallCount++;
                revision = CallCount;
                changed.TrySetResult();
                changed = NewSignal();
            }

            return ValueTask.FromResult(new RouterResult(
                RouterState.Initial with { Revision = revision },
                intent is RouterIntent.Route route
                    ? new RouteDecision(
                        RouteDecisionKind.Routed,
                        RouteReason.WindowsCurrentSession,
                        route.Command)
                    : RouteDecision.StateUpdated));
        }

        public async Task<bool> TryWaitForCallCountAsync(int expected, TimeSpan timeout)
        {
            try
            {
                while (true)
                {
                    Task signal;
                    lock (sync)
                    {
                        if (CallCount >= expected)
                        {
                            return true;
                        }

                        signal = changed.Task;
                    }

                    await signal.WaitAsync(timeout);
                }
            }
            catch (TimeoutException)
            {
                return false;
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private static TaskCompletionSource NewSignal() => new(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class BlockingRouteDiagnosticLog : IDiagnosticLog
    {
        private readonly TaskCompletionSource started = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource released = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => started.Task;

        public async ValueTask WriteAsync(
            DiagnosticEvent diagnosticEvent,
            CancellationToken cancellationToken)
        {
            if (diagnosticEvent.Name != "route.completed")
            {
                return;
            }

            started.TrySetResult();
            await released.Task.WaitAsync(cancellationToken);
        }

        public void Release() => released.TrySetResult();
    }

    private sealed class RecordingIntentRouter : IMediaRouter
    {
        public List<RouterIntent> Intents { get; } = [];

        public ValueTask<RouterResult> DispatchAsync(
            RouterIntent intent,
            CancellationToken cancellationToken)
        {
            Intents.Add(intent);
            return ValueTask.FromResult(new RouterResult(
                RouterState.Initial with { Revision = Intents.Count },
                RouteDecision.StateUpdated));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.Parse("2026-08-24T00:00:00Z");

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan amount) => now += amount;
    }
}
