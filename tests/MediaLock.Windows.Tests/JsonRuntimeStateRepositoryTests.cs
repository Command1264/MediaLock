using MediaLock.Core.Configuration;
using MediaLock.Core.Routing;
using MediaLock.Windows.Persistence;
using Xunit;

namespace MediaLock.Windows.Tests;

public sealed class JsonRuntimeStateRepositoryTests
{
    [Fact]
    public async Task SavedWindowsAutoStateIsLoadedThroughTheRepositoryInterface()
    {
        using var directory = new TemporaryDirectory();
        IRuntimeStateRepository repository = new JsonRuntimeStateRepository(directory.Path);
        var expected = new RuntimeStateDocument(
            RuntimeStateDocument.CurrentSchemaVersion,
            RoutingMode.WindowsAuto,
            LockedTarget: null);

        await repository.SaveAsync(expected, CancellationToken.None);
        var result = await new JsonRuntimeStateRepository(directory.Path)
            .LoadAsync(CancellationToken.None);

        Assert.Equal(expected, result.Value);
        Assert.False(result.UsedDefaults);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public async Task CorruptRuntimeStateIsPreservedBeforeAutomaticReplacement()
    {
        using var directory = new TemporaryDirectory();
        var path = System.IO.Path.Combine(directory.Path, "state.json");
        const string corrupt = "{ preserve runtime state";
        await File.WriteAllTextAsync(path, corrupt);
        IRuntimeStateRepository repository = new JsonRuntimeStateRepository(directory.Path);
        var loaded = await repository.LoadAsync(CancellationToken.None);

        await repository.SaveAsync(loaded.Value, CancellationToken.None);

        var recoveryPath = Assert.Single(Directory.GetFiles(
            directory.Path,
            "state.corrupt.*.json"));
        Assert.Equal(corrupt, await File.ReadAllTextAsync(recoveryPath));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"MediaLock.Tests.{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
