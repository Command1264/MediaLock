namespace MediaLock.Application;

public sealed record SourceApplicationMetadata(
    string DisplayName,
    string? HostDisplayName = null);

public interface ISourceApplicationMetadataResolver
{
    SourceApplicationMetadata? TryResolve(string sourceAppUserModelId);
}
