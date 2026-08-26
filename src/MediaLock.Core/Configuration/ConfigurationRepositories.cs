using System.Collections.Immutable;

namespace MediaLock.Core.Configuration;

public sealed record ConfigurationLoadResult<T>(
    T Value,
    bool UsedDefaults,
    ImmutableArray<ConfigurationIssue> Issues);

public interface ISettingsRepository
{
    ValueTask<ConfigurationLoadResult<MediaLockSettings>> LoadAsync(
        CancellationToken cancellationToken);

    ValueTask SaveAsync(
        MediaLockSettings settings,
        CancellationToken cancellationToken);
}

public interface IRuntimeStateRepository
{
    ValueTask<ConfigurationLoadResult<RuntimeStateDocument>> LoadAsync(
        CancellationToken cancellationToken);

    ValueTask SaveAsync(
        RuntimeStateDocument state,
        CancellationToken cancellationToken);
}

public interface ILoginStartupManager
{
    ValueTask<bool> IsEnabledAsync(CancellationToken cancellationToken);

    ValueTask SetEnabledAsync(bool enabled, CancellationToken cancellationToken);
}

public interface ILoginStartupChangeSource
{
    IAsyncEnumerable<bool> WatchEnabledAsync(CancellationToken cancellationToken);
}
