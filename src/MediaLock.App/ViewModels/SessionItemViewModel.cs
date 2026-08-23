using MediaLock.App.Localization;
using MediaLock.Core.Media;

namespace MediaLock.App.ViewModels;

public sealed record SessionItemViewModel(
    SessionKey Key,
    string SourceApplication,
    string Title,
    string Artist,
    string PlaybackStatus,
    MediaCommandCapabilities Capabilities,
    PlaybackStatus PlaybackState,
    MediaTimeline? Timeline,
    MediaArtwork? Artwork)
{
    internal static SessionItemViewModel From(MediaSessionSnapshot session) => new(
        session.Key,
        session.SourceAppUserModelId,
        session.Metadata?.Title ?? UiText.Get("Media_UnknownTitle"),
        session.Metadata?.Artist ?? UiText.Get("Media_UnknownArtist"),
        UiDescriptions.DescribePlaybackStatus(session.PlaybackStatus),
        session.Capabilities,
        session.PlaybackStatus,
        session.Timeline,
        session.Artwork);
}
