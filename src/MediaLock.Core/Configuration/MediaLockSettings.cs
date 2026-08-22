using System.Collections.Immutable;
using MediaLock.Core.Routing;

namespace MediaLock.Core.Configuration;

public sealed record RecoverySettings(
    TimeSpan Timeout,
    FallbackPolicy FallbackPolicy);

public sealed record DesktopSettings(
    bool CloseToTray,
    bool StartWithWindows);

public sealed record MediaLockSettings(
    int SchemaVersion,
    RoutingMode DefaultRoutingMode,
    RecoverySettings? Recovery,
    DesktopSettings? Desktop = null)
{
    public const int CurrentSchemaVersion = 2;

    public static MediaLockSettings Default { get; } = new(
        CurrentSchemaVersion,
        RoutingMode.WindowsAuto,
        new RecoverySettings(
            TimeSpan.FromSeconds(15),
            FallbackPolicy.SameApplicationThenWindowsCurrentSession),
        new DesktopSettings(
            CloseToTray: true,
            StartWithWindows: false));

    public ImmutableArray<ConfigurationIssue> Validate()
    {
        var issues = ImmutableArray.CreateBuilder<ConfigurationIssue>();
        if (SchemaVersion != CurrentSchemaVersion)
        {
            issues.Add(new ConfigurationIssue(
                "schemaVersion",
                $"Expected schema version {CurrentSchemaVersion}, but found {SchemaVersion}."));
        }

        if (Recovery is null)
        {
            issues.Add(new ConfigurationIssue(
                "recovery",
                "Recovery settings are required."));
        }
        else
        {
            if (Recovery.Timeout < TimeSpan.Zero || Recovery.Timeout > TimeSpan.FromMinutes(5))
            {
                issues.Add(new ConfigurationIssue(
                    "recovery.timeout",
                    "Recovery timeout must be between 0 seconds and 5 minutes."));
            }

            if (!Enum.IsDefined(Recovery.FallbackPolicy))
            {
                issues.Add(new ConfigurationIssue(
                    "recovery.fallbackPolicy",
                    $"Unknown fallback policy value {(int)Recovery.FallbackPolicy}."));
            }
        }

        if (!Enum.IsDefined(DefaultRoutingMode))
        {
            issues.Add(new ConfigurationIssue(
                "defaultRoutingMode",
                $"Unknown routing mode value {(int)DefaultRoutingMode}."));
        }

        if (Desktop is null)
        {
            issues.Add(new ConfigurationIssue(
                "desktop",
                "Desktop settings are required."));
        }

        return issues.ToImmutable();
    }
}

public sealed record ConfigurationIssue(string Path, string Message);
