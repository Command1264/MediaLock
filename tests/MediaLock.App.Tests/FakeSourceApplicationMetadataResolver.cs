using MediaLock.Application;

namespace MediaLock.App.Tests;

internal sealed class FakeSourceApplicationMetadataResolver(
    IReadOnlyDictionary<string, SourceApplicationMetadata> metadata)
    : ISourceApplicationMetadataResolver
{
    public SourceApplicationMetadata? TryResolve(string sourceAppUserModelId) =>
        metadata.GetValueOrDefault(sourceAppUserModelId);
}
