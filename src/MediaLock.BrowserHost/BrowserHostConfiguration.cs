using System.Text.Json;
using MediaLock.Browser;

namespace MediaLock.BrowserHost;

public sealed record BrowserHostConfiguration(string ExtensionId, string PipeName)
{
    private const int MaximumConfigurationBytes = 4096;
    public static BrowserHostConfiguration Load(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        var directory = Path.GetDirectoryName(executablePath)
            ?? throw new InvalidDataException("The Browser Host executable directory is unavailable.");
        var path = Path.Combine(directory, "browser-host.json");
        var info = new FileInfo(path);
        if (!info.Exists || info.Length is <= 0 or > MaximumConfigurationBytes)
        {
            throw new InvalidDataException("The Browser Host configuration is missing or invalid.");
        }

        using var document = JsonDocument.Parse(File.ReadAllBytes(path), new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 4,
        });
        var root = document.RootElement;
        var fields = root.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal);
        if (!fields.SequenceEqual(new[] { "extensionId", "pipeName" }, StringComparer.Ordinal))
        {
            throw new InvalidDataException("The Browser Host configuration schema is invalid.");
        }

        var extensionId = root.GetProperty("extensionId").GetString();
        var pipeName = root.GetProperty("pipeName").GetString();
        if (!string.Equals(
                extensionId,
                BrowserMediaAdapterOptions.ProductionExtensionId,
                StringComparison.Ordinal) ||
            !string.Equals(
                pipeName,
                BrowserMediaBridgeServer.DefaultPipeName,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("The Browser Host configuration values are invalid.");
        }

        return new BrowserHostConfiguration(extensionId!, pipeName!);
    }

    public void ValidateLaunchOrigin(string origin)
    {
        if (!string.Equals(
                origin,
                $"chrome-extension://{ExtensionId}/",
                StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("The Browser Host launch origin is not authorized.");
        }
    }
}
