using System.Collections.Immutable;
using System.Threading.Channels;
using MediaLock.Application;
using MediaLock.Core.Configuration;
using MediaLock.Core.Diagnostics;
using MediaLock.Core.Media;
using MediaLock.Core.Routing;
using Xunit;

namespace MediaLock.Application.Tests;

public sealed class MediaLockApplicationTests
{
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
        Assert.Empty(failed.Router.Sessions);
        Assert.Equal(RouterStatus.Recovering, failed.Router.Status);
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

    private static MediaSessionSnapshot Session(string key, string source) => new(
        new SessionKey(key),
        source,
        PlaybackStatus.Playing,
        MediaCommandCapabilities.All,
        DateTimeOffset.Parse("2026-08-22T00:00:00Z"));

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
        public List<(SessionKey Target, MediaCommand Command)> Commands { get; } = [];

        public ValueTask<MediaControlResult> TryExecuteAsync(
            SessionKey target,
            MediaCommand command,
            CancellationToken cancellationToken)
        {
            Commands.Add((target, command));
            return ValueTask.FromResult(MediaControlResult.Succeeded);
        }
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
            CancellationToken cancellationToken) =>
            ValueTask.FromException(new IOException("Could not write state.json."));
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
}
