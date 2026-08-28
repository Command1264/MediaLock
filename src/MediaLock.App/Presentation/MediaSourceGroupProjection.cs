using MediaLock.App.ViewModels;

namespace MediaLock.App.Presentation;

internal static class MediaSourceGroupProjection
{
    public static IReadOnlyList<MediaSourceGroupViewModel> Create(
        IReadOnlyList<SessionItemViewModel> sessions,
        IReadOnlyList<BrowserTargetItemViewModel> browserTargets,
        IReadOnlyDictionary<string, bool>? expandedState = null)
    {
        var builders = new List<GroupBuilder>();
        var groupsByKey = new Dictionary<string, GroupBuilder>(StringComparer.Ordinal);

        foreach (var session in sessions)
        {
            var key = $"gsmtc-application:{session.SourceApplication}";
            var group = GetOrAdd(
                builders,
                groupsByKey,
                key,
                session.SourceApplicationDisplayName);
            group.SourceApplication ??= session.SourceApplication;
            group.Sessions.Add(session);
        }

        foreach (var target in browserTargets)
        {
            var browserFamily = target.SourceGroup?.DisplayName;
            var group = browserFamily is null
                ? null
                : builders.FirstOrDefault(candidate =>
                    candidate.Sessions.Count > 0 &&
                    string.Equals(
                        candidate.DisplayName,
                        browserFamily,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        candidate.SourceApplication,
                        browserFamily,
                        StringComparison.OrdinalIgnoreCase));
            if (group is null)
            {
                var key = target.SourceGroup?.Key ?? $"browser-target:{target.Id}";
                var displayName = browserFamily ?? target.SourceDisplayName;
                group = GetOrAdd(builders, groupsByKey, key, displayName);
            }

            group.BrowserTargets.Add(target);
        }

        return builders
            .Select(builder => new MediaSourceGroupViewModel(
                builder.Key,
                builder.DisplayName,
                builder.SourceApplication,
                builder.BrowserTargets,
                builder.Sessions,
                expandedState?.GetValueOrDefault(builder.Key) ?? true))
            .ToArray();
    }

    private static GroupBuilder GetOrAdd(
        ICollection<GroupBuilder> builders,
        IDictionary<string, GroupBuilder> groupsByKey,
        string key,
        string displayName)
    {
        if (groupsByKey.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var created = new GroupBuilder(key, displayName);
        builders.Add(created);
        groupsByKey.Add(key, created);
        return created;
    }

    private sealed class GroupBuilder(string key, string displayName)
    {
        public string Key { get; } = key;

        public string DisplayName { get; } = displayName;

        public string? SourceApplication { get; set; }

        public List<BrowserTargetItemViewModel> BrowserTargets { get; } = [];

        public List<SessionItemViewModel> Sessions { get; } = [];
    }
}
