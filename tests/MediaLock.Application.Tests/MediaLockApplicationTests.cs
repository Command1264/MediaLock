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
}
