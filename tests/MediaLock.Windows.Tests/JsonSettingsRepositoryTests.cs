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
            DefaultRoutingMode = RoutingMode.PriorityRules,
            PriorityRules =
            [
                new PriorityRule("Brave._crx_music"),
                new PriorityRule("Chrome", IsEnabled: false),
            ],
        };

        await repository.SaveAsync(expected, CancellationToken.None);
        var result = await new JsonSettingsRepository(
            directory.Path,
            TimeProvider.System).LoadAsync(CancellationToken.None);

        Assert.Equal(expected.SchemaVersion, result.Value.SchemaVersion);
        Assert.Equal(expected.DefaultRoutingMode, result.Value.DefaultRoutingMode);
        Assert.Equal(expected.Recovery, result.Value.Recovery);
        Assert.Equal(expected.Desktop, result.Value.Desktop);
        Assert.Equal(expected.PlaybackStateLock, result.Value.PlaybackStateLock);
        Assert.Equal(expected.PriorityRules.ToArray(), result.Value.PriorityRules.ToArray());
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

        Assert.Equal(7, result.Value.SchemaVersion);
        Assert.Equal(TimeSpan.FromSeconds(30), result.Value.Recovery!.Timeout);
        Assert.Equal(FallbackPolicy.Wait, result.Value.Recovery.FallbackPolicy);
        Assert.Equal(MediaLockSettings.Default.Desktop, result.Value.Desktop);
        Assert.Equal(MediaLockSettings.Default.PlaybackStateLock, result.Value.PlaybackStateLock);
        Assert.Empty(result.Value.PriorityRules);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public async Task VersionTwoSettingsMigrateToEmptyPriorityRules()
    {
        using var directory = new TemporaryDirectory();
        await File.WriteAllTextAsync(
            System.IO.Path.Combine(directory.Path, "settings.json"),
            """
            {
              "schemaVersion": 2,
              "defaultRoutingMode": "appLock",
              "recovery": {
                "timeout": "00:00:15",
                "fallbackPolicy": "wait"
              },
              "desktop": {
                "closeToTray": false,
                "startWithWindows": true
              }
            }
            """);
        ISettingsRepository repository = new JsonSettingsRepository(
            directory.Path,
            TimeProvider.System);

        var result = await repository.LoadAsync(CancellationToken.None);

        Assert.Equal(7, result.Value.SchemaVersion);
        Assert.Equal(RoutingMode.AppLock, result.Value.DefaultRoutingMode);
        Assert.Equal(
            new DesktopSettings(
                false,
                true,
                UiLanguagePreference.System,
                UiThemePreference.System),
            result.Value.Desktop);
        Assert.Empty(result.Value.PriorityRules);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public async Task VersionThreeSettingsMigrateToSystemLanguage()
    {
        using var directory = new TemporaryDirectory();
        await File.WriteAllTextAsync(
            System.IO.Path.Combine(directory.Path, "settings.json"),
            """
            {
              "schemaVersion": 3,
              "defaultRoutingMode": "windowsAuto",
              "recovery": {
                "timeout": "00:00:15",
                "fallbackPolicy": "sameApplicationThenWindowsCurrentSession"
              },
              "desktop": {
                "closeToTray": false,
                "startWithWindows": true
              },
              "priorityRules": [
                {
                  "sourceAppUserModelId": "Brave._crx_music",
                  "isEnabled": false
                }
              ]
            }
            """);

        var result = await new JsonSettingsRepository(
            directory.Path,
            TimeProvider.System).LoadAsync(CancellationToken.None);

        Assert.Equal(7, result.Value.SchemaVersion);
        Assert.Equal(
            new DesktopSettings(
                false,
                true,
                UiLanguagePreference.System,
                UiThemePreference.System),
            result.Value.Desktop);
        var rule = Assert.Single(result.Value.PriorityRules);
        Assert.Equal("Brave._crx_music", rule.SourceAppUserModelId);
        Assert.False(rule.IsEnabled);
        Assert.False(result.UsedDefaults);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public async Task VersionFourSettingsPreserveLanguageAndMigrateToSystemTheme()
    {
        using var directory = new TemporaryDirectory();
        await File.WriteAllTextAsync(
            System.IO.Path.Combine(directory.Path, "settings.json"),
            """
            {
              "schemaVersion": 4,
              "defaultRoutingMode": "priorityRules",
              "recovery": {
                "timeout": "00:00:15",
                "fallbackPolicy": "sameApplicationThenWindowsCurrentSession"
              },
              "desktop": {
                "closeToTray": true,
                "startWithWindows": false,
                "language": "zh-TW"
              },
              "priorityRules": []
            }
            """);

        var result = await new JsonSettingsRepository(
            directory.Path,
            TimeProvider.System).LoadAsync(CancellationToken.None);

        Assert.Equal(7, result.Value.SchemaVersion);
        Assert.Equal(
            new DesktopSettings(
                true,
                false,
                UiLanguagePreference.TraditionalChinese,
                UiThemePreference.System),
            result.Value.Desktop);
        Assert.Equal(RoutingMode.PriorityRules, result.Value.DefaultRoutingMode);
        Assert.False(result.UsedDefaults);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public async Task VersionFiveSettingsEnableMediaKeyInterceptionDuringMigration()
    {
        using var directory = new TemporaryDirectory();
        await File.WriteAllTextAsync(
            System.IO.Path.Combine(directory.Path, "settings.json"),
            """
            {
              "schemaVersion": 5,
              "defaultRoutingMode": "priorityRules",
              "recovery": {
                "timeout": "00:00:15",
                "fallbackPolicy": "sameApplicationThenWindowsCurrentSession"
              },
              "desktop": {
                "closeToTray": true,
                "startWithWindows": false,
                "language": "zh-TW",
                "theme": "dark"
              },
              "priorityRules": []
            }
            """);

        var result = await new JsonSettingsRepository(
            directory.Path,
            TimeProvider.System).LoadAsync(CancellationToken.None);

        Assert.Equal(7, result.Value.SchemaVersion);
        Assert.True(result.Value.Desktop!.InterceptMediaKeys);
        Assert.Equal(UiLanguagePreference.TraditionalChinese, result.Value.Desktop.Language);
        Assert.Equal(UiThemePreference.Dark, result.Value.Desktop.Theme);
        Assert.False(result.UsedDefaults);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public async Task VersionSixSettingsMigrateToRepeatedPauseOverrideDefaults()
    {
        using var directory = new TemporaryDirectory();
        await File.WriteAllTextAsync(
            System.IO.Path.Combine(directory.Path, "settings.json"),
            """
            {
              "schemaVersion": 6,
              "defaultRoutingMode": "windowsAuto",
              "recovery": {
                "timeout": "00:00:15",
                "fallbackPolicy": "sameApplicationThenWindowsCurrentSession"
              },
              "desktop": {
                "closeToTray": true,
                "startWithWindows": false,
                "language": "system",
                "theme": "system",
                "interceptMediaKeys": true
              },
              "priorityRules": []
            }
            """);

        var result = await new JsonSettingsRepository(
            directory.Path,
            TimeProvider.System).LoadAsync(CancellationToken.None);

        Assert.Equal(7, result.Value.SchemaVersion);
        Assert.Equal(MediaLockSettings.Default.PlaybackStateLock, result.Value.PlaybackStateLock);
        Assert.False(result.UsedDefaults);
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
