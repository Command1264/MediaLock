using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using MediaLock.Application;

namespace MediaLock.Windows;

public sealed class WindowsSourceApplicationMetadataResolver
    : ISourceApplicationMetadataResolver
{
    private readonly Lazy<IReadOnlyDictionary<string, SourceApplicationMetadata>> metadata;

    public WindowsSourceApplicationMetadataResolver()
        : this(LoadShellApplicationMetadata)
    {
    }

    internal WindowsSourceApplicationMetadataResolver(
        Func<IReadOnlyDictionary<string, SourceApplicationMetadata>> loadMetadata)
    {
        ArgumentNullException.ThrowIfNull(loadMetadata);
        metadata = new Lazy<IReadOnlyDictionary<string, SourceApplicationMetadata>>(
            () => LoadSafely(loadMetadata),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public SourceApplicationMetadata? TryResolve(string sourceAppUserModelId)
    {
        if (string.IsNullOrWhiteSpace(sourceAppUserModelId))
        {
            return null;
        }

        return metadata.Value.GetValueOrDefault(sourceAppUserModelId);
    }

    internal static SourceApplicationMetadata? CreateMetadata(
        string? displayName,
        string? targetProductName)
    {
        var normalizedDisplayName = displayName?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedDisplayName))
        {
            return null;
        }

        var normalizedTargetProductName = targetProductName?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedTargetProductName) ||
            normalizedDisplayName.Contains(
                normalizedTargetProductName,
                StringComparison.OrdinalIgnoreCase) ||
            normalizedTargetProductName.Contains(
                normalizedDisplayName,
                StringComparison.OrdinalIgnoreCase))
        {
            normalizedTargetProductName = null;
        }

        return new SourceApplicationMetadata(
            normalizedDisplayName,
            normalizedTargetProductName);
    }

    private static IReadOnlyDictionary<string, SourceApplicationMetadata> LoadSafely(
        Func<IReadOnlyDictionary<string, SourceApplicationMetadata>> loadMetadata)
    {
        try
        {
            return loadMetadata() ??
                new Dictionary<string, SourceApplicationMetadata>(StringComparer.Ordinal);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return new Dictionary<string, SourceApplicationMetadata>(StringComparer.Ordinal);
        }
    }

    private static IReadOnlyDictionary<string, SourceApplicationMetadata>
        LoadShellApplicationMetadata()
    {
        var result = new Dictionary<string, SourceApplicationMetadata>(StringComparer.Ordinal);
        object? shell = null;
        object? folder = null;
        object? items = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application", throwOnError: false);
            if (shellType is null)
            {
                return result;
            }

            shell = Activator.CreateInstance(shellType);
            if (shell is null)
            {
                return result;
            }

            dynamic shellAutomation = shell;
            folder = shellAutomation.NameSpace("shell:AppsFolder");
            if (folder is null)
            {
                return result;
            }

            dynamic folderAutomation = folder;
            items = folderAutomation.Items();
            if (items is null)
            {
                return result;
            }

            dynamic itemsAutomation = items;
            var count = Convert.ToInt32(itemsAutomation.Count, CultureInfo.InvariantCulture);
            for (var index = 0; index < count; index++)
            {
                object? item = null;
                try
                {
                    item = itemsAutomation.Item(index);
                    if (item is null)
                    {
                        continue;
                    }

                    dynamic itemAutomation = item;
                    var sourceAppUserModelId = Convert.ToString(
                        itemAutomation.ExtendedProperty("System.AppUserModel.ID"),
                        CultureInfo.InvariantCulture)?.Trim();
                    if (string.IsNullOrWhiteSpace(sourceAppUserModelId))
                    {
                        continue;
                    }

                    var displayName = Convert.ToString(
                        itemAutomation.Name,
                        CultureInfo.CurrentCulture);
                    var targetPath = Convert.ToString(
                        itemAutomation.ExtendedProperty("System.Link.TargetParsingPath"),
                        CultureInfo.InvariantCulture);
                    var entry = CreateMetadata(
                        displayName,
                        ReadTargetProductName(targetPath));
                    if (entry is not null)
                    {
                        result[sourceAppUserModelId] = entry;
                    }
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    // One malformed Shell item cannot remove otherwise trustworthy names.
                }
                finally
                {
                    ReleaseComObject(item);
                }
            }

            return result;
        }
        finally
        {
            ReleaseComObject(items);
            ReleaseComObject(folder);
            ReleaseComObject(shell);
        }
    }

    private static string? ReadTargetProductName(string? targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath) || !File.Exists(targetPath))
        {
            return null;
        }

        try
        {
            return FileVersionInfo.GetVersionInfo(targetPath).ProductName;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }
    }

    private static void ReleaseComObject(object? instance)
    {
        if (instance is not null && Marshal.IsComObject(instance))
        {
            _ = Marshal.FinalReleaseComObject(instance);
        }
    }
}
