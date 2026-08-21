using MediaLock.Core.Configuration;
using MediaLock.Core.Routing;

namespace MediaLock.Core.Tests;

public sealed class ConfigurationSchemaTests
{
    [Fact]
    public void InvalidSettingsReturnActionableValidationIssues()
    {
        var settings = new MediaLockSettings(
            SchemaVersion: 99,
            DefaultRoutingMode: RoutingMode.AppLock,
            Recovery: new RecoverySettings(
                Timeout: TimeSpan.FromMilliseconds(-1),
                FallbackPolicy.DisableRouting));

        var issues = settings.Validate();

        Assert.Equal(2, issues.Length);
        Assert.Contains(issues, issue =>
            issue.Path == "schemaVersion" &&
            issue.Message == "Expected schema version 1, but found 99.");
        Assert.Contains(issues, issue =>
            issue.Path == "recovery.timeout" &&
            issue.Message == "Recovery timeout must be between 0 seconds and 5 minutes.");
    }

    [Fact]
    public void RuntimeStateSchemaStoresDescriptorInsteadOfLiveSessionKey()
    {
        var state = new RuntimeStateDocument(
            RuntimeStateDocument.CurrentSchemaVersion,
            RoutingMode.SessionLock,
            new PersistedLockedTarget("browser", "pwa"));

        Assert.Equal("browser", state.LockedTarget!.SourceAppUserModelId);
        Assert.Equal("pwa", state.LockedTarget.SessionInstanceHint);
    }

    [Fact]
    public void InvalidRuntimeStateReturnsActionableValidationIssues()
    {
        var state = new RuntimeStateDocument(
            SchemaVersion: 99,
            RoutingMode.WindowsAuto,
            new PersistedLockedTarget(" ", " "));

        var issues = state.Validate();

        Assert.Equal(4, issues.Length);
        Assert.Contains(issues, issue => issue.Path == "schemaVersion");
        Assert.Contains(issues, issue =>
            issue.Path == "lockedTarget" &&
            issue.Message == "Windows Auto runtime state must not contain a Locked Target.");
        Assert.Contains(issues, issue => issue.Path == "lockedTarget.sourceAppUserModelId");
        Assert.Contains(issues, issue => issue.Path == "lockedTarget.sessionInstanceHint");
    }

    [Fact]
    public void MissingRecoverySettingsReturnActionableValidationIssue()
    {
        var settings = new MediaLockSettings(
            MediaLockSettings.CurrentSchemaVersion,
            RoutingMode.WindowsAuto,
            Recovery: null);

        var issues = settings.Validate();

        var issue = Assert.Single(issues);
        Assert.Equal("recovery", issue.Path);
        Assert.Equal("Recovery settings are required.", issue.Message);
    }
}
