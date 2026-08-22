using MediaLock.Core.Configuration;
using MediaLock.Core.Routing;
using MediaLock.Windows.Persistence;
using Xunit;

namespace MediaLock.Windows.Tests;

public sealed class JsonSettingsRepositoryTests
{
    [Fact]
    public async Task MissingSettingsReturnCurrentDefaultsWithoutAnError()
    {
        using var directory = new TemporaryDirectory();
        ISettingsRepository repository = new JsonSettingsRepository(
            directory.Path,
            TimeProvider.System);

        var result = await repository.LoadAsync(CancellationToken.None);

        Assert.Equal(MediaLockSettings.Default, result.Value);
        Assert.True(result.UsedDefaults);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public async Task SavedSettingsAreLoadedThroughTheRepositoryInterface()
    {
        using var directory = new TemporaryDirectory();
        ISettingsRepository repository = new JsonSettingsRepository(
            directory.Path,
            TimeProvider.System);
        var expected = MediaLockSettings.Default with
        {
            Desktop = new DesktopSettings(
                CloseToTray: false,
                StartWithWindows: true),
        };

        await repository.SaveAsync(expected, CancellationToken.None);
        var result = await new JsonSettingsRepository(
            directory.Path,
            TimeProvider.System).LoadAsync(CancellationToken.None);

        Assert.Equal(expected, result.Value);
        Assert.False(result.UsedDefaults);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public async Task VersionOneSettingsMigrateToDesktopDefaults()
    {
        using var directory = new TemporaryDirectory();
        await File.WriteAllTextAsync(
            System.IO.Path.Combine(directory.Path, "settings.json"),
            """
            {
              "schemaVersion": 1,
              "defaultRoutingMode": "windowsAuto",
              "recovery": {
                "timeout": "00:00:30",
                "fallbackPolicy": "wait"
              }
            }
            """);
        ISettingsRepository repository = new JsonSettingsRepository(
            directory.Path,
            TimeProvider.System);

        var result = await repository.LoadAsync(CancellationToken.None);

        Assert.Equal(2, result.Value.SchemaVersion);
        Assert.Equal(TimeSpan.FromSeconds(30), result.Value.Recovery!.Timeout);
        Assert.Equal(FallbackPolicy.Wait, result.Value.Recovery.FallbackPolicy);
        Assert.Equal(MediaLockSettings.Default.Desktop, result.Value.Desktop);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public async Task CorruptSettingsReturnActionableDefaultsWithoutChangingTheFile()
    {
        using var directory = new TemporaryDirectory();
        var path = System.IO.Path.Combine(directory.Path, "settings.json");
        const string corrupt = "{ definitely not json";
        await File.WriteAllTextAsync(path, corrupt);
        ISettingsRepository repository = new JsonSettingsRepository(
            directory.Path,
            TimeProvider.System);

        var result = await repository.LoadAsync(CancellationToken.None);

        Assert.Equal(MediaLockSettings.Default, result.Value);
        Assert.True(result.UsedDefaults);
        var issue = Assert.Single(result.Issues);
        Assert.Equal("$", issue.Path);
        Assert.Contains("settings.json", issue.Message, StringComparison.Ordinal);
        Assert.Equal(corrupt, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task SavingAfterCorruptLoadPreservesTheOriginalAsARecoveryCopy()
    {
        using var directory = new TemporaryDirectory();
        var path = System.IO.Path.Combine(directory.Path, "settings.json");
        const string corrupt = "{ keep this recoverable";
        await File.WriteAllTextAsync(path, corrupt);
        ISettingsRepository repository = new JsonSettingsRepository(
            directory.Path,
            TimeProvider.System);
        await repository.LoadAsync(CancellationToken.None);

        await repository.SaveAsync(MediaLockSettings.Default, CancellationToken.None);

        var recoveryPath = Assert.Single(Directory.GetFiles(
            directory.Path,
            "settings.corrupt.*.json"));
        Assert.Equal(corrupt, await File.ReadAllTextAsync(recoveryPath));
        var reloaded = await new JsonSettingsRepository(
            directory.Path,
            TimeProvider.System).LoadAsync(CancellationToken.None);
        Assert.Equal(MediaLockSettings.Default, reloaded.Value);
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
