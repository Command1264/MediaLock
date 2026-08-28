using MediaLock.Application;

namespace MediaLock.App.Presentation;

internal sealed record SourceApplicationPresentation(
    string SourceAppUserModelId,
    string DisplayName,
    string Details);

internal static class SourceApplicationPresentationCatalog
{
    public static IReadOnlyDictionary<string, SourceApplicationPresentation> Resolve(
        IEnumerable<string> sourceAppUserModelIds,
        ISourceApplicationMetadataResolver? metadataResolver)
    {
        ArgumentNullException.ThrowIfNull(sourceAppUserModelIds);

        var basePresentations = sourceAppUserModelIds
            .Where(source => !string.IsNullOrWhiteSpace(source))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(source => source, StringComparer.Ordinal)
            .Select(source => CreateBasePresentation(source, metadataResolver))
            .ToArray();
        var duplicateNames = basePresentations
            .GroupBy(presentation => presentation.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return basePresentations.ToDictionary(
            presentation => presentation.SourceAppUserModelId,
            presentation => duplicateNames.Contains(presentation.DisplayName)
                ? presentation with
                {
                    DisplayName = $"{presentation.DisplayName} — " +
                        presentation.SourceAppUserModelId,
                }
                : presentation,
            StringComparer.Ordinal);
    }

    private static SourceApplicationPresentation CreateBasePresentation(
        string sourceAppUserModelId,
        ISourceApplicationMetadataResolver? metadataResolver)
    {
        var metadata = metadataResolver?.TryResolve(sourceAppUserModelId);
        var metadataDisplayName = metadata?.DisplayName.Trim();
        var isFallback = string.IsNullOrWhiteSpace(metadataDisplayName);
        var displayName = isFallback
            ? sourceAppUserModelId
            : metadataDisplayName!;

        var hostDisplayName = metadata?.HostDisplayName?.Trim();
        if (!string.IsNullOrWhiteSpace(hostDisplayName))
        {
            displayName = $"{displayName} — {hostDisplayName}";
        }

        return new SourceApplicationPresentation(
            sourceAppUserModelId,
            displayName,
            sourceAppUserModelId);
    }
}
