using System.Collections.Immutable;
using MediaLock.Core.Media;
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
            if (LockedTarget.Fingerprint is null)
            {
                issues.Add(new ConfigurationIssue(
                    "lockedTarget.fingerprint",
                    "Locked Target fingerprint is required."));
                return issues.ToImmutable();
            }

            if (string.IsNullOrWhiteSpace(LockedTarget.Fingerprint.SourceAppUserModelId))
            {
                issues.Add(new ConfigurationIssue(
                    "lockedTarget.fingerprint.sourceAppUserModelId",
                    "Locked Target source application ID must not be blank."));
            }

            if (LockedTarget.Fingerprint.SessionInstanceHint is not null &&
                string.IsNullOrWhiteSpace(LockedTarget.Fingerprint.SessionInstanceHint))
            {
                issues.Add(new ConfigurationIssue(
                    "lockedTarget.fingerprint.sessionInstanceHint",
                    "Locked Target Session instance hint must be null or non-blank."));
            }

            if (!Enum.IsDefined(LockedTarget.Fingerprint.PlaybackStatus))
            {
                issues.Add(new ConfigurationIssue(
                    "lockedTarget.fingerprint.playbackStatus",
                    $"Unknown playback status value {(int)LockedTarget.Fingerprint.PlaybackStatus}."));
            }
        }

        return issues.ToImmutable();
    }
}

public sealed record PersistedLockedTarget(PersistedSessionFingerprint Fingerprint);

public sealed record PersistedSessionFingerprint(
    string SourceAppUserModelId,
    string? SessionInstanceHint,
    PlaybackStatus PlaybackStatus,
    DateTimeOffset ObservedAt,
    string? Title,
    string? Artist);
