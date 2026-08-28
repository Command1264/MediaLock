using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using MediaLock.Core.Configuration;
using MediaLock.Core.Routing;

namespace MediaLock.Windows.Persistence;

public sealed class JsonRuntimeStateRepository : IRuntimeStateRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
    private readonly string statePath;
    private bool preserveExistingOnNextSave;

    public JsonRuntimeStateRepository()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MediaLock"))
    {
    }

    internal JsonRuntimeStateRepository(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        statePath = Path.Combine(rootPath, "state.json");
    }

    public async ValueTask<ConfigurationLoadResult<RuntimeStateDocument>> LoadAsync(
        CancellationToken cancellationToken)
    {
        var fallback = new RuntimeStateDocument(
            RuntimeStateDocument.CurrentSchemaVersion,
            RoutingMode.WindowsAuto,
            LockedTarget: null);
        if (!File.Exists(statePath))
        {
            return new ConfigurationLoadResult<RuntimeStateDocument>(
                fallback,
                UsedDefaults: true,
                Issues: ImmutableArray<ConfigurationIssue>.Empty);
        }

        try
        {
            await using var stream = new FileStream(
                statePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var state = await JsonSerializer.DeserializeAsync<RuntimeStateDocument>(
                stream,
                SerializerOptions,
                cancellationToken);
            var issues = state?.Validate() ??
                [new ConfigurationIssue("$", "Runtime state document must contain a JSON object.")];
            if (issues.Length == 0)
            {
                return new ConfigurationLoadResult<RuntimeStateDocument>(
                    state!,
                    UsedDefaults: false,
                    Issues: ImmutableArray<ConfigurationIssue>.Empty);
            }

            preserveExistingOnNextSave = true;
            return new ConfigurationLoadResult<RuntimeStateDocument>(
                fallback,
                UsedDefaults: true,
                Issues: issues);
        }
        catch (JsonException exception)
        {
            preserveExistingOnNextSave = true;
            return new ConfigurationLoadResult<RuntimeStateDocument>(
                fallback,
                UsedDefaults: true,
                Issues:
                [
                    new ConfigurationIssue(
                        "$",
                        $"Could not read '{statePath}': {exception.Message}"),
                ]);
        }
    }

    public async ValueTask SaveAsync(
        RuntimeStateDocument state,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        var issues = state.Validate();
        if (issues.Length > 0)
        {
            throw new InvalidDataException(
                $"Runtime state is invalid: {string.Join(" ", issues.Select(issue => issue.Message))}");
        }
        var directory = Path.GetDirectoryName(statePath)!;
        Directory.CreateDirectory(directory);
        if (preserveExistingOnNextSave && File.Exists(statePath))
        {
            PreserveCorruptState(directory);
        }

        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(statePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    state,
                    SerializerOptions,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, statePath, overwrite: true);
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

    private void PreserveCorruptState(string directory)
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffZ");
        var recoveryPath = Path.Combine(directory, $"state.corrupt.{timestamp}.json");
        var suffix = 1;
        while (File.Exists(recoveryPath))
        {
            recoveryPath = Path.Combine(
                directory,
                $"state.corrupt.{timestamp}.{suffix++}.json");
        }

        File.Copy(statePath, recoveryPath);
    }
}
