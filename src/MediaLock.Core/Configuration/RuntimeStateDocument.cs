using MediaLock.Core.Routing;

namespace MediaLock.Core.Configuration;

public sealed record RuntimeStateDocument(
    int SchemaVersion,
    RoutingMode RoutingMode,
    PersistedLockedTarget? LockedTarget)
{
    public const int CurrentSchemaVersion = 1;
}

public sealed record PersistedLockedTarget(
    string SourceAppUserModelId,
    string? SessionInstanceHint);
