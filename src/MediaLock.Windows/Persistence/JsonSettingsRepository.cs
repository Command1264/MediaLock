using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using MediaLock.Core.Configuration;

namespace MediaLock.Windows.Persistence;

public sealed class JsonSettingsRepository : ISettingsRepository
{
    private readonly string settingsPath;
    private readonly TimeProvider timeProvider;
    private bool preserveExistingOnNextSave;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public JsonSettingsRepository()
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MediaLock"),
            TimeProvider.System)
    {
    }

    internal JsonSettingsRepository(string rootPath, TimeProvider timeProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(timeProvider);
        settingsPath = Path.Combine(rootPath, "settings.json");
        this.timeProvider = timeProvider;
    }

    public async ValueTask<ConfigurationLoadResult<MediaLockSettings>> LoadAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(settingsPath))
        {
            return new ConfigurationLoadResult<MediaLockSettings>(
                MediaLockSettings.Default,
                UsedDefaults: true,
                Issues: ImmutableArray<ConfigurationIssue>.Empty);
        }

        try
        {
            await using var stream = new FileStream(
                settingsPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var settings = await JsonSerializer.DeserializeAsync<MediaLockSettings>(
                stream,
                SerializerOptions,
                cancellationToken);
            if (settings?.SchemaVersion is >= 1 and <= 4)
            {
                var sourceVersion = settings.SchemaVersion;
                settings = settings with
                {
                    SchemaVersion = MediaLockSettings.CurrentSchemaVersion,
                    Desktop = sourceVersion == 1
                        ? MediaLockSettings.Default.Desktop
                        : settings.Desktop is null
                            ? null
                            : settings.Desktop with
                            {
                                Language = sourceVersion <= 3
                                    ? UiLanguagePreference.System
                                    : settings.Desktop.Language,
                                Theme = UiThemePreference.System,
                            },
                    PriorityRules = sourceVersion <= 2 ? [] : settings.PriorityRules,
                };
            }

            var issues = settings?.Validate() ??
            [new ConfigurationIssue("$", "Settings document must contain a JSON object.")];
            if (issues.Length > 0)
            {
                preserveExistingOnNextSave = true;
                return new ConfigurationLoadResult<MediaLockSettings>(
                    MediaLockSettings.Default,
                    UsedDefaults: true,
                    Issues: issues);
            }

            return new ConfigurationLoadResult<MediaLockSettings>(
                settings!,
                UsedDefaults: false,
                Issues: ImmutableArray<ConfigurationIssue>.Empty);
        }
        catch (JsonException exception)
        {
            preserveExistingOnNextSave = true;
            return new ConfigurationLoadResult<MediaLockSettings>(
                MediaLockSettings.Default,
                UsedDefaults: true,
                Issues:
                [
                    new ConfigurationIssue(
                        "$",
                        $"Could not read '{settingsPath}': {exception.Message}"),
                ]);
        }
    }

    public async ValueTask SaveAsync(
        MediaLockSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var directory = Path.GetDirectoryName(settingsPath)!;
        Directory.CreateDirectory(directory);
        if (preserveExistingOnNextSave && File.Exists(settingsPath))
        {
            PreserveCorruptSettings(directory);
        }

        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(settingsPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    settings,
                    SerializerOptions,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, settingsPath, overwrite: true);
            preserveExistingOnNextSave = false;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private void PreserveCorruptSettings(string directory)
    {
        var timestamp = timeProvider.GetUtcNow().ToString("yyyyMMddTHHmmssfffZ");
        var recoveryPath = Path.Combine(directory, $"settings.corrupt.{timestamp}.json");
        var suffix = 1;
        while (File.Exists(recoveryPath))
        {
            recoveryPath = Path.Combine(
                directory,
                $"settings.corrupt.{timestamp}.{suffix++}.json");
        }

        File.Copy(settingsPath, recoveryPath);
    }
}
