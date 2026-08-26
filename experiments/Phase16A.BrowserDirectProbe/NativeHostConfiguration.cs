using System.Text.Json;

namespace MediaLock.Phase16ABrowserDirectProbe;

internal sealed record NativeHostConfiguration(
    string ExtensionId,
    int CommandResponseDelayMilliseconds)
{
    private const int MaximumConfigurationBytes = 4 * 1024;

    public static NativeHostConfiguration Load(string executablePath)
    {
        var directory = Path.GetDirectoryName(executablePath)
            ?? throw new InvalidDataException("The Native Host executable directory is unavailable.");
        var path = Path.Combine(directory, "phase16a-native-host.json");
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            throw new FileNotFoundException("The Phase 16A Native Host configuration is missing.", path);
        }

        if (file.Length is <= 0 or > MaximumConfigurationBytes)
        {
            throw new InvalidDataException("The Phase 16A Native Host configuration has an invalid size.");
        }

        using var document = JsonDocument.Parse(File.ReadAllBytes(path), new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 4,
        });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 2)
        {
            throw new InvalidDataException("The Phase 16A Native Host configuration schema is invalid.");
        }

        if (!root.TryGetProperty("extensionId", out var extensionIdElement)
            || extensionIdElement.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException("The Phase 16A Native Host Extension ID is missing.");
        }

        var extensionId = extensionIdElement.GetString()!;
        _ = NativeHostOrigin.Validate($"chrome-extension://{extensionId}/", extensionId);
        if (!root.TryGetProperty(
                "commandResponseDelayMilliseconds",
                out var commandResponseDelayElement)
            || commandResponseDelayElement.ValueKind != JsonValueKind.Number
            || !commandResponseDelayElement.TryGetInt32(out var commandResponseDelayMilliseconds)
            || commandResponseDelayMilliseconds is < 0 or > 10000)
        {
            throw new InvalidDataException(
                "The Phase 16A command response delay must be an integer from 0 through 10000 milliseconds.");
        }

        return new NativeHostConfiguration(extensionId, commandResponseDelayMilliseconds);
    }
}
