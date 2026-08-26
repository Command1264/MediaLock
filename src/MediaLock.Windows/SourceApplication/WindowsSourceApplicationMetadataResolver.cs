using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using MediaLock.Application;
using MediaLock.Core.Diagnostics;

namespace MediaLock.Windows;

public sealed class WindowsSourceApplicationMetadataResolver
    : ISourceApplicationMetadataResolver
{
    private readonly IDiagnosticLog? diagnosticLog;
    private readonly Lazy<IReadOnlyDictionary<string, SourceApplicationMetadata>> metadata;

    public WindowsSourceApplicationMetadataResolver(IDiagnosticLog? diagnosticLog = null)
    {
        this.diagnosticLog = diagnosticLog;
        metadata = new Lazy<IReadOnlyDictionary<string, SourceApplicationMetadata>>(
            () => LoadSafely(LoadShellApplicationMetadata),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    internal WindowsSourceApplicationMetadataResolver(
        Func<IReadOnlyDictionary<string, SourceApplicationMetadata>> loadMetadata,
        IDiagnosticLog? diagnosticLog = null)
    {
        ArgumentNullException.ThrowIfNull(loadMetadata);
        this.diagnosticLog = diagnosticLog;
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

    private IReadOnlyDictionary<string, SourceApplicationMetadata> LoadSafely(
        Func<IReadOnlyDictionary<string, SourceApplicationMetadata>> loadMetadata)
    {
        try
        {
            return loadMetadata() ??
                new Dictionary<string, SourceApplicationMetadata>(StringComparer.Ordinal);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ReportFailure("catalog", exception);
            return new Dictionary<string, SourceApplicationMetadata>(StringComparer.Ordinal);
        }
    }

    private IReadOnlyDictionary<string, SourceApplicationMetadata>
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
                    ReportFailure("shell-item", exception);
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

    private string? ReadTargetProductName(string? targetPath)
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
            ReportFailure("target-product", exception);
            return null;
        }
    }

    private void ReportFailure(string stage, Exception exception)
    {
        System.Diagnostics.Trace.TraceError(exception.ToString());
        if (diagnosticLog is null)
        {
            return;
        }

        try
        {
            diagnosticLog.WriteAsync(
                new DiagnosticEvent(
                    "source.metadata.failed",
                    new Dictionary<string, string>
                    {
                        ["stage"] = stage,
                        ["exceptionType"] = exception.GetType().FullName ??
                            exception.GetType().Name,
                    }),
                CancellationToken.None).AsTask().GetAwaiter().GetResult();
        }
        catch (Exception diagnosticException) when (
            diagnosticException is not OutOfMemoryException)
        {
            System.Diagnostics.Trace.TraceError(diagnosticException.ToString());
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
