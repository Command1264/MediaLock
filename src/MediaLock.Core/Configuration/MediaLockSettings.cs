using System.Collections.Immutable;
using System.Text.Json.Serialization;
using MediaLock.Core.Routing;

namespace MediaLock.Core.Configuration;

public sealed record RecoverySettings(
    TimeSpan Timeout,
    FallbackPolicy FallbackPolicy);

public sealed record DesktopSettings(
    bool CloseToTray,
    bool StartWithWindows,
    string Language = UiLanguagePreference.System,
    string Theme = UiThemePreference.System,
    bool InterceptMediaKeys = true);

public static class UiLanguagePreference
{
    public const string System = "system";
    public const string EnglishUnitedStates = "en-US";
    public const string TraditionalChinese = "zh-TW";

    public static bool IsSupported(string? value) => value is
        System or EnglishUnitedStates or TraditionalChinese;
}

public static class UiThemePreference
{
    public const string System = "system";
    public const string Light = "light";
    public const string Dark = "dark";

    public static bool IsSupported(string? value) => value is System or Light or Dark;
}

[method: JsonConstructor]
public sealed record MediaLockSettings(
    int SchemaVersion,
    RoutingMode DefaultRoutingMode,
    RecoverySettings? Recovery,
    DesktopSettings? Desktop,
    ImmutableArray<PriorityRule> PriorityRules)
{
    public MediaLockSettings(
        int SchemaVersion,
        RoutingMode DefaultRoutingMode,
        RecoverySettings? Recovery,
        DesktopSettings? Desktop = null)
        : this(SchemaVersion, DefaultRoutingMode, Recovery, Desktop, [])
    {
    }

    public const int CurrentSchemaVersion = 6;

    public static MediaLockSettings Default { get; } = new(
        CurrentSchemaVersion,
        RoutingMode.WindowsAuto,
        new RecoverySettings(
            TimeSpan.FromSeconds(15),
            FallbackPolicy.SameApplicationThenWindowsCurrentSession),
        new DesktopSettings(
            CloseToTray: true,
            StartWithWindows: false),
        []);

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
        else
        {
            if (!UiLanguagePreference.IsSupported(Desktop.Language))
            {
                issues.Add(new ConfigurationIssue(
                    "desktop.language",
                    $"Unsupported UI language preference '{Desktop.Language}'."));
            }

            if (!UiThemePreference.IsSupported(Desktop.Theme))
            {
                issues.Add(new ConfigurationIssue(
                    "desktop.theme",
                    $"Unsupported UI theme preference '{Desktop.Theme}'."));
            }
        }

        if (PriorityRules.IsDefault)
        {
            issues.Add(new ConfigurationIssue(
                "priorityRules",
                "Priority Rules must be present."));
        }
        else
        {
            var sourceApplications = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < PriorityRules.Length; index++)
            {
                var rule = PriorityRules[index];
                if (rule is null || string.IsNullOrWhiteSpace(rule.SourceAppUserModelId))
                {
                    issues.Add(new ConfigurationIssue(
                        $"priorityRules[{index}].sourceAppUserModelId",
                        "Priority Rule source application ID must not be blank."));
                    continue;
                }

                if (!sourceApplications.Add(rule.SourceAppUserModelId))
                {
                    issues.Add(new ConfigurationIssue(
                        $"priorityRules[{index}].sourceAppUserModelId",
                        $"Priority Rule source application ID '{rule.SourceAppUserModelId}' is duplicated."));
                }
            }
        }

        return issues.ToImmutable();
    }
}

public sealed record ConfigurationIssue(string Path, string Message);
