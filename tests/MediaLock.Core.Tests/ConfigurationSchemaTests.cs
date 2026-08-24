using MediaLock.Core.Configuration;
using MediaLock.Core.Media;
using MediaLock.Core.Routing;

namespace MediaLock.Core.Tests;

public sealed class ConfigurationSchemaTests
{
    [Fact]
    public void DefaultSettingsEnableCloseToTrayAndMediaKeyInterceptionButNotLoginStartup()
    {
        Assert.Equal(7, MediaLockSettings.CurrentSchemaVersion);
        Assert.True(MediaLockSettings.Default.Desktop!.CloseToTray);
        Assert.False(MediaLockSettings.Default.Desktop.StartWithWindows);
        Assert.True(MediaLockSettings.Default.Desktop.InterceptMediaKeys);
        Assert.Equal(UiLanguagePreference.System, MediaLockSettings.Default.Desktop.Language);
        Assert.Equal(UiThemePreference.System, MediaLockSettings.Default.Desktop.Theme);
        Assert.True(MediaLockSettings.Default.PlaybackStateLock!.RepeatedPauseOverrideEnabled);
        Assert.Equal(TimeSpan.FromSeconds(5), MediaLockSettings.Default.PlaybackStateLock.RepeatedPauseWindow);
        Assert.Equal(3, MediaLockSettings.Default.PlaybackStateLock.RepeatedPauseCount);
        Assert.True(MediaLockSettings.Default.PlaybackStateLock.PlayOverrideSound);
    }

    [Fact]
    public void InvalidSettingsReturnActionableValidationIssues()
    {
        var settings = new MediaLockSettings(
            SchemaVersion: 99,
            DefaultRoutingMode: RoutingMode.AppLock,
            Recovery: new RecoverySettings(
                Timeout: TimeSpan.FromMilliseconds(-1),
                FallbackPolicy.DisableRouting),
            Desktop: MediaLockSettings.Default.Desktop);

        var issues = settings.Validate();

        Assert.Equal(2, issues.Length);
        Assert.Contains(issues, issue =>
            issue.Path == "schemaVersion" &&
            issue.Message == "Expected schema version 7, but found 99.");
        Assert.Contains(issues, issue =>
            issue.Path == "recovery.timeout" &&
            issue.Message == "Recovery timeout must be between 0 seconds and 5 minutes.");
    }

    [Theory]
    [InlineData(0, 3, "playbackStateLock.repeatedPauseWindow")]
    [InlineData(61, 3, "playbackStateLock.repeatedPauseWindow")]
    [InlineData(5, 1, "playbackStateLock.repeatedPauseCount")]
    [InlineData(5, 11, "playbackStateLock.repeatedPauseCount")]
    public void InvalidRepeatedPauseOverrideSettingsReturnActionableIssues(
        int windowSeconds,
        int count,
        string expectedPath)
    {
        var settings = MediaLockSettings.Default with
        {
            PlaybackStateLock = new PlaybackStateLockSettings(
                true,
                TimeSpan.FromSeconds(windowSeconds),
                count,
                true),
        };

        var issue = Assert.Single(settings.Validate());

        Assert.Equal(expectedPath, issue.Path);
    }

    [Fact]
    public void UnsupportedLanguageReturnsAnActionableValidationIssue()
    {
        var settings = MediaLockSettings.Default with
        {
            Desktop = MediaLockSettings.Default.Desktop! with { Language = "fr-FR" },
        };

        var issue = Assert.Single(settings.Validate());

        Assert.Equal("desktop.language", issue.Path);
        Assert.Contains("fr-FR", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnsupportedThemeReturnsAnActionableValidationIssue()
    {
        var settings = MediaLockSettings.Default with
        {
            Desktop = MediaLockSettings.Default.Desktop! with { Theme = "neon" },
        };

        var issue = Assert.Single(settings.Validate());

        Assert.Equal("desktop.theme", issue.Path);
        Assert.Contains("neon", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeStateSchemaStoresFingerprintInsteadOfLiveSessionKey()
    {
        var observedAt = DateTimeOffset.Parse("2026-08-22T00:00:00Z");
        var state = new RuntimeStateDocument(
            RuntimeStateDocument.CurrentSchemaVersion,
            RoutingMode.SessionLock,
            new PersistedLockedTarget(new PersistedSessionFingerprint(
                "browser",
                "pwa",
                PlaybackStatus.Playing,
                observedAt,
                MediaPlaybackType.Music,
                "title",
                "artist")));

        Assert.Equal("browser", state.LockedTarget!.Fingerprint.SourceAppUserModelId);
        Assert.Equal("pwa", state.LockedTarget.Fingerprint.SessionInstanceHint);
        Assert.Equal(PlaybackStatus.Playing, state.LockedTarget.Fingerprint.PlaybackStatus);
        Assert.Equal(observedAt, state.LockedTarget.Fingerprint.ObservedAt);
        Assert.Equal(MediaPlaybackType.Music, state.LockedTarget.Fingerprint.PlaybackType);
        Assert.Equal("title", state.LockedTarget.Fingerprint.Title);
        Assert.Equal("artist", state.LockedTarget.Fingerprint.Artist);
    }

    [Fact]
    public void InvalidRuntimeStateReturnsActionableValidationIssues()
    {
        var state = new RuntimeStateDocument(
            SchemaVersion: 99,
            RoutingMode.WindowsAuto,
            new PersistedLockedTarget(new PersistedSessionFingerprint(
                " ",
                " ",
                (PlaybackStatus)99,
                DateTimeOffset.Parse("2026-08-22T00:00:00Z"),
                (MediaPlaybackType)99,
                null,
                null)));

        var issues = state.Validate();

        Assert.Equal(6, issues.Length);
        Assert.Contains(issues, issue => issue.Path == "schemaVersion");
        Assert.Contains(issues, issue =>
            issue.Path == "lockedTarget" &&
            issue.Message == "Windows Auto runtime state must not contain a Locked Target.");
        Assert.Contains(issues, issue => issue.Path == "lockedTarget.fingerprint.sourceAppUserModelId");
        Assert.Contains(issues, issue => issue.Path == "lockedTarget.fingerprint.sessionInstanceHint");
        Assert.Contains(issues, issue => issue.Path == "lockedTarget.fingerprint.playbackStatus");
        Assert.Contains(issues, issue => issue.Path == "lockedTarget.fingerprint.playbackType");
    }

    [Fact]
    public void MissingPersistedFingerprintReturnsActionableValidationIssue()
    {
        var state = new RuntimeStateDocument(
            RuntimeStateDocument.CurrentSchemaVersion,
            RoutingMode.SessionLock,
            new PersistedLockedTarget(null!));

        var issue = Assert.Single(state.Validate());

        Assert.Equal("lockedTarget.fingerprint", issue.Path);
        Assert.Equal("Locked Target fingerprint is required.", issue.Message);
    }

    [Fact]
    public void PriorityRulesRuntimeStateRejectsALockedTarget()
    {
        var state = new RuntimeStateDocument(
            RuntimeStateDocument.CurrentSchemaVersion,
            RoutingMode.PriorityRules,
            new PersistedLockedTarget(new PersistedSessionFingerprint(
                "Brave",
                null,
                PlaybackStatus.Paused,
                DateTimeOffset.Parse("2026-08-22T00:00:00Z"),
                MediaPlaybackType.Video,
                null,
                null)));

        var issue = Assert.Single(state.Validate());

        Assert.Equal("lockedTarget", issue.Path);
        Assert.Equal("Priority Rules runtime state must not contain a Locked Target.", issue.Message);
    }

    [Fact]
    public void MissingRecoverySettingsReturnActionableValidationIssue()
    {
        var settings = new MediaLockSettings(
            MediaLockSettings.CurrentSchemaVersion,
            RoutingMode.WindowsAuto,
            Recovery: null,
            Desktop: MediaLockSettings.Default.Desktop);

        var issues = settings.Validate();

        var issue = Assert.Single(issues);
        Assert.Equal("recovery", issue.Path);
        Assert.Equal("Recovery settings are required.", issue.Message);
    }

    [Fact]
    public void MissingDesktopSettingsReturnActionableValidationIssue()
    {
        var settings = new MediaLockSettings(
            MediaLockSettings.CurrentSchemaVersion,
            RoutingMode.WindowsAuto,
            MediaLockSettings.Default.Recovery,
            Desktop: null);

        var issue = Assert.Single(settings.Validate());

        Assert.Equal("desktop", issue.Path);
        Assert.Equal("Desktop settings are required.", issue.Message);
    }

    [Fact]
    public void DuplicatePriorityRuleApplicationsReturnAnActionableValidationIssue()
    {
        var settings = new MediaLockSettings(
            MediaLockSettings.CurrentSchemaVersion,
            RoutingMode.PriorityRules,
            MediaLockSettings.Default.Recovery,
            MediaLockSettings.Default.Desktop,
            [new PriorityRule("Brave"), new PriorityRule("Brave", IsEnabled: false)]);

        var issue = Assert.Single(settings.Validate());

        Assert.Equal("priorityRules[1].sourceAppUserModelId", issue.Path);
        Assert.Equal("Priority Rule source application ID 'Brave' is duplicated.", issue.Message);
    }
}
