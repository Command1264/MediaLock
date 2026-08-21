using System.Collections.Immutable;
using MediaLock.Core.Routing;

namespace MediaLock.Core.Configuration;

public sealed record RuntimeStateDocument(
    int SchemaVersion,
    RoutingMode Mode,
    PersistedLockedTarget? LockedTarget)
{
    public const int CurrentSchemaVersion = 1;

    public ImmutableArray<ConfigurationIssue> Validate()
    {
        var issues = ImmutableArray.CreateBuilder<ConfigurationIssue>();
        if (SchemaVersion != CurrentSchemaVersion)
        {
            issues.Add(new ConfigurationIssue(
                "schemaVersion",
                $"Expected schema version {CurrentSchemaVersion}, but found {SchemaVersion}."));
        }

        if (!Enum.IsDefined(Mode))
        {
            issues.Add(new ConfigurationIssue(
                "routingMode",
                $"Unknown routing mode value {(int)Mode}."));
        }
        else if (Mode == RoutingMode.WindowsAuto && LockedTarget is not null)
        {
            issues.Add(new ConfigurationIssue(
                "lockedTarget",
                "Windows Auto runtime state must not contain a Locked Target."));
        }
        else if (Mode is RoutingMode.AppLock or RoutingMode.SessionLock && LockedTarget is null)
        {
            issues.Add(new ConfigurationIssue(
                "lockedTarget",
                $"{Mode} runtime state requires a Locked Target."));
        }

        if (LockedTarget is not null)
        {
            if (string.IsNullOrWhiteSpace(LockedTarget.SourceAppUserModelId))
            {
                issues.Add(new ConfigurationIssue(
                    "lockedTarget.sourceAppUserModelId",
                    "Locked Target source application ID must not be blank."));
            }

            if (LockedTarget.SessionInstanceHint is not null &&
                string.IsNullOrWhiteSpace(LockedTarget.SessionInstanceHint))
            {
                issues.Add(new ConfigurationIssue(
                    "lockedTarget.sessionInstanceHint",
                    "Locked Target Session instance hint must be null or non-blank."));
            }
        }

        return issues.ToImmutable();
    }
}

public sealed record PersistedLockedTarget(
    string SourceAppUserModelId,
    string? SessionInstanceHint);
