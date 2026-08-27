using MediaLock.App.Localization;
using MediaLock.Core.Media;

namespace MediaLock.App.ViewModels;

public sealed record BrowserTargetItemViewModel(
    MediaTargetId Id,
    string SourceDisplayName,
    string Title,
    string Artist,
    string PlaybackStatus,
    string TargetDetails,
    MediaCommandCapabilities Capabilities)
{
    internal static BrowserTargetItemViewModel From(MediaTargetSnapshot target) => new(
        target.Id,
        target.Presentation.SourceDisplayName,
        target.Presentation.Metadata?.Title ?? UiText.Get("Media_UnknownTitle"),
        target.Presentation.Metadata?.Artist ?? UiText.Get("Media_UnknownArtist"),
        UiDescriptions.DescribePlaybackStatus(target.Presentation.PlaybackStatus),
        target.Id.ToString(),
        target.Presentation.Capabilities);
}
