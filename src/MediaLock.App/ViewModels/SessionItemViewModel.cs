using MediaLock.Core.Media;

namespace MediaLock.App.ViewModels;

public sealed record SessionItemViewModel(
    SessionKey Key,
    string SourceApplication,
    string Title,
    string Artist,
    string PlaybackStatus,
    MediaCommandCapabilities Capabilities)
{
    internal static SessionItemViewModel From(MediaSessionSnapshot session) => new(
        session.Key,
        session.SourceAppUserModelId,
        session.Metadata?.Title ?? "Unknown title",
        session.Metadata?.Artist ?? "Unknown artist",
        session.PlaybackStatus.ToString(),
        session.Capabilities);
}
