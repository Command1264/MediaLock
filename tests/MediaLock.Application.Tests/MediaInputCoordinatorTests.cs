using MediaLock.Application;
using MediaLock.Core.Configuration;
using MediaLock.Core.Diagnostics;
using MediaLock.Core.Input;
using MediaLock.Core.Media;
using MediaLock.Core.Routing;
using Xunit;

namespace MediaLock.Application.Tests;

public sealed class MediaInputCoordinatorTests
{
    [Fact]
    public async Task RoutableInputIsConsumedAndDispatchedToTheCapturedTarget()
    {
        var target = Session("music", MediaCommandCapabilities.TogglePlayPause);
        var application = new RecordingApplication(State(target));
        await using var source = new FakeInputSource();
        await using var coordinator = new MediaInputCoordinator(application, source);
        await coordinator.StartAsync(CancellationToken.None);

        var consumed = source.Emit(MediaCommand.TogglePlayPause);
        var intent = await application.NextIntent.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(consumed);
        var route = Assert.IsType<ApplicationIntent.Route>(intent);
        Assert.Equal(MediaCommand.TogglePlayPause, route.Command);
        Assert.Equal(MediaTargetId.FromGsmtc(target.Key), route.ExpectedTarget);
    }

    [Fact]
    public async Task BrowserToggleInputIsConsumedAndDispatchedToTheExactLockedTarget()
    {
        var target = MediaTargetSnapshot.FromBrowserPageBinding(
            "page-binding",
            new MediaTargetPresentation(
                "Browser video",
                PlaybackStatus.Playing,
                MediaCommandCapabilities.TogglePlayPause,
                DateTimeOffset.Parse("2026-08-28T00:00:00Z")));
        var application = new RecordingApplication(MediaLockApplicationState.Initial with
        {
            Router = RouterState.Initial with
            {
                Mode = RoutingMode.SessionLock,
                Status = RouterStatus.Locked,
                Targets = [target],
                LockedMediaTarget = target.Id,
            },
        });
        await using var source = new FakeInputSource();
        await using var coordinator = new MediaInputCoordinator(application, source);
        await coordinator.StartAsync(CancellationToken.None);

        var consumed = source.Emit(MediaCommand.TogglePlayPause);
        var intent = await application.NextIntent.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(consumed);
        var route = Assert.IsType<ApplicationIntent.Route>(intent);
        Assert.Equal(MediaCommand.TogglePlayPause, route.Command);
        Assert.Equal(target.Id, route.ExpectedTarget);
    }

    [Fact]
    public async Task DisabledInterceptionPassesTheInputThrough()
    {
        var target = Session("music", MediaCommandCapabilities.All);
        var settings = MediaLockSettings.Default with
        {
            Desktop = MediaLockSettings.Default.Desktop! with { InterceptMediaKeys = false },
        };
        var application = new RecordingApplication(State(target) with { Settings = settings });
        await using var source = new FakeInputSource();
        await using var coordinator = new MediaInputCoordinator(application, source);
        await coordinator.StartAsync(CancellationToken.None);

        Assert.False(source.Emit(MediaCommand.TogglePlayPause));
        Assert.False(application.NextIntent.Task.IsCompleted);
    }

    [Fact]
    public async Task UpdatedInterceptionSettingAppliesToTheNextInput()
    {
        var target = Session("music", MediaCommandCapabilities.All);
        var application = new RecordingApplication(State(target));
        await using var source = new FakeInputSource();
        await using var coordinator = new MediaInputCoordinator(application, source);
        await coordinator.StartAsync(CancellationToken.None);
        var disabled = application.State.Settings with
        {
            Desktop = application.State.Settings.Desktop! with
            {
                InterceptMediaKeys = false,
            },
        };

        application.Publish(application.State with { Settings = disabled });

        Assert.False(source.Emit(MediaCommand.TogglePlayPause));
        Assert.False(application.NextIntent.Task.IsCompleted);
    }

    [Fact]
    public async Task CaptureDecisionReadsOneImmutableApplicationStateSnapshot()
    {
        var target = Session("music", MediaCommandCapabilities.All);
        var application = new StateReadCountingApplication(State(target));
        await using var source = new FakeInputSource();
        await using var coordinator = new MediaInputCoordinator(application, source);
        await coordinator.StartAsync(CancellationToken.None);

        Assert.True(source.Emit(MediaCommand.TogglePlayPause));
        Assert.Equal(1, application.StateReadCount);
    }

    [Fact]
    public async Task UnsupportedInputPassesThroughWithoutDispatching()
    {
        var target = Session("music", MediaCommandCapabilities.TogglePlayPause);
        var application = new RecordingApplication(State(target));
        await using var source = new FakeInputSource();
        await using var coordinator = new MediaInputCoordinator(application, source);
        await coordinator.StartAsync(CancellationToken.None);

        Assert.False(source.Emit(MediaCommand.Next));
        Assert.False(application.NextIntent.Task.IsCompleted);
    }

    [Fact]
    public async Task RecoveringLockPassesInputThrough()
    {
        var target = Session("music", MediaCommandCapabilities.All);
        var state = State(target) with
        {
            Router = State(target).Router with
            {
                Mode = RoutingMode.SessionLock,
                Status = RouterStatus.Recovering,
                LockedTarget = new LockedTarget(SessionFingerprint.From(target), null),
            },
        };
        var application = new RecordingApplication(state);
        await using var source = new FakeInputSource();
        await using var coordinator = new MediaInputCoordinator(application, source);
        await coordinator.StartAsync(CancellationToken.None);

        Assert.False(source.Emit(MediaCommand.TogglePlayPause));
        Assert.False(application.NextIntent.Task.IsCompleted);
    }

    [Fact]
    public async Task DisposalStopsTheInputSource()
    {
        var target = Session("music", MediaCommandCapabilities.All);
        var application = new RecordingApplication(State(target));
        var source = new FakeInputSource();
        var coordinator = new MediaInputCoordinator(application, source);
        await coordinator.StartAsync(CancellationToken.None);

        await coordinator.DisposeAsync();

        Assert.True(source.StopCalled);
        Assert.True(source.DisposeCalled);
    }

    [Fact]
    public async Task FullQueuePassesAdditionalInputThrough()
    {
        var target = Session("music", MediaCommandCapabilities.All);
        var application = new BlockingApplication(State(target));
        await using var source = new FakeInputSource();
        await using var coordinator = new MediaInputCoordinator(application, source, capacity: 1);
        await coordinator.StartAsync(CancellationToken.None);

        Assert.True(source.Emit(MediaCommand.Next));
        await application.DispatchStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(source.Emit(MediaCommand.Previous));
        Assert.False(source.Emit(MediaCommand.Stop));

        application.ReleaseDispatch.TrySetResult();
    }

    [Fact]
    public async Task DispatchFailureIsReportedAndLaterInputsContinue()
    {
        var target = Session("music", MediaCommandCapabilities.All);
        var application = new FailingOnceApplication(State(target));
        await using var source = new FakeInputSource();
        await using var coordinator = new MediaInputCoordinator(application, source);
        var fault = new TaskCompletionSource<Exception>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.Faulted += (_, args) => fault.TrySetResult(args.Exception);
        await coordinator.StartAsync(CancellationToken.None);

        Assert.True(source.Emit(MediaCommand.Next));
        Assert.Equal("route failed", (await fault.Task.WaitAsync(TimeSpan.FromSeconds(2))).Message);
        Assert.True(source.Emit(MediaCommand.Previous));
        await application.SecondDispatch.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task AcceptedPhysicalInputIsLoggedWithSequenceCommandAndTarget()
    {
        var target = Session("music", MediaCommandCapabilities.All);
        var application = new RecordingApplication(State(target));
        var log = new RecordingDiagnosticLog();
        await using var source = new FakeInputSource();
        await using var coordinator = new MediaInputCoordinator(application, source, log);
        await coordinator.StartAsync(CancellationToken.None);

        Assert.True(source.Emit(MediaCommand.Next));
        await application.NextIntent.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var entry = await log.NextEvent.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal("input.accepted", entry.Name);
        Assert.Equal("1", entry.Properties?["sequence"]);
        Assert.Equal("Next", entry.Properties?["command"]);
        Assert.Equal(target.Key.Value, entry.Properties?["target"]);
    }

    private static MediaLockApplicationState State(MediaSessionSnapshot target) =>
        MediaLockApplicationState.Initial with
        {
            Router = RouterState.Initial with
            {
                Mode = RoutingMode.PriorityRules,
                Targets = [MediaTargetSnapshot.FromGsmtc(target)],
                Sessions = [target],
                WindowsCurrentSession = target.Key,
                PriorityTarget = target.Key,
            },
        };

    private static MediaSessionSnapshot Session(
        string key,
        MediaCommandCapabilities capabilities) => new(
        new SessionKey(key),
        "Brave._crx_music",
        PlaybackStatus.Playing,
        capabilities,
        DateTimeOffset.Parse("2026-08-23T00:00:00Z"));

    private sealed class FakeInputSource : IMediaInputSource
    {
        private MediaInputHandler? handler;

        public event EventHandler<MediaInputSourceFaultedEventArgs>? Faulted;

        public bool IsRunning { get; private set; }

        public bool StopCalled { get; private set; }

        public bool DisposeCalled { get; private set; }

        public ValueTask StartAsync(
            MediaInputHandler handler,
            CancellationToken cancellationToken)
        {
            this.handler = handler;
            IsRunning = true;
            return ValueTask.CompletedTask;
        }

        public bool Emit(MediaCommand command) => handler!(command);

        public void RaiseFault(Exception exception) =>
            Faulted?.Invoke(this, new MediaInputSourceFaultedEventArgs(exception));

        public void Stop()
        {
            StopCalled = true;
            IsRunning = false;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCalled = true;
            IsRunning = false;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingApplication(MediaLockApplicationState state) : IMediaLockApplication
    {
        public event EventHandler<MediaLockApplicationStateChangedEventArgs>? StateChanged
        {
            add { }
            remove { }
        }

        public MediaLockApplicationState State { get; private set; } = state;

        public TaskCompletionSource<ApplicationIntent> NextIntent { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask StartAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask<ApplicationResult> DispatchAsync(
            ApplicationIntent intent,
            CancellationToken cancellationToken)
        {
            NextIntent.TrySetResult(intent);
            return ValueTask.FromResult(new ApplicationResult(State, RouteDecision.StateUpdated));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Publish(MediaLockApplicationState updated) => State = updated;
    }

    private sealed class StateReadCountingApplication(
        MediaLockApplicationState state) : IMediaLockApplication
    {
        private int stateReadCount;

        public event EventHandler<MediaLockApplicationStateChangedEventArgs>? StateChanged
        {
            add { }
            remove { }
        }

        public int StateReadCount => Volatile.Read(ref stateReadCount);

        public MediaLockApplicationState State
        {
            get
            {
                Interlocked.Increment(ref stateReadCount);
                return state;
            }
        }

        public ValueTask StartAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask<ApplicationResult> DispatchAsync(
            ApplicationIntent intent,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new ApplicationResult(state, RouteDecision.StateUpdated));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class BlockingApplication(MediaLockApplicationState state) : IMediaLockApplication
    {
        public event EventHandler<MediaLockApplicationStateChangedEventArgs>? StateChanged
        {
            add { }
            remove { }
        }

        public MediaLockApplicationState State { get; } = state;

        public TaskCompletionSource DispatchStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseDispatch { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask StartAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public async ValueTask<ApplicationResult> DispatchAsync(
            ApplicationIntent intent,
            CancellationToken cancellationToken)
        {
            DispatchStarted.TrySetResult();
            await ReleaseDispatch.Task.WaitAsync(cancellationToken);
            return new ApplicationResult(State, RouteDecision.StateUpdated);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FailingOnceApplication(MediaLockApplicationState state) : IMediaLockApplication
    {
        private int dispatchCount;

        public event EventHandler<MediaLockApplicationStateChangedEventArgs>? StateChanged
        {
            add { }
            remove { }
        }

        public MediaLockApplicationState State { get; } = state;

        public TaskCompletionSource SecondDispatch { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask StartAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask<ApplicationResult> DispatchAsync(
            ApplicationIntent intent,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref dispatchCount) == 1)
            {
                return ValueTask.FromException<ApplicationResult>(
                    new InvalidOperationException("route failed"));
            }

            SecondDispatch.TrySetResult();
            return ValueTask.FromResult(new ApplicationResult(State, RouteDecision.StateUpdated));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingDiagnosticLog : IDiagnosticLog
    {
        public TaskCompletionSource<DiagnosticEvent> NextEvent { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask WriteAsync(
            DiagnosticEvent diagnosticEvent,
            CancellationToken cancellationToken)
        {
            NextEvent.TrySetResult(diagnosticEvent);
            return ValueTask.CompletedTask;
        }
    }
}
