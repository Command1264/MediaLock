using MediaLock.Core.Media;
using MediaLock.Core.Routing;

namespace MediaLock.App.Localization;

internal static class UiDescriptions
{
    public static string DescribeRoutingMode(RoutingMode mode) => UiText.Get(mode switch
    {
        RoutingMode.WindowsAuto => "Mode_WindowsAuto",
        RoutingMode.PriorityRules => "Mode_PriorityRules",
        RoutingMode.AppLock => "Mode_AppLock",
        RoutingMode.SessionLock => "Mode_SessionLock",
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    });

    public static string DescribeRouterStatus(RouterStatus status) => UiText.Get(status switch
    {
        RouterStatus.Ready => "Status_Ready",
        RouterStatus.Locked => "Status_Locked",
        RouterStatus.Recovering => "Status_Recovering",
        RouterStatus.Fallback => "Status_Fallback",
        RouterStatus.Unavailable => "Status_Unavailable",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    });

    public static string DescribeCatalogStatus(MediaSessionCatalogStatus status) => UiText.Get(status switch
    {
        MediaSessionCatalogStatus.Available => "Status_Ready",
        MediaSessionCatalogStatus.Suspended => "Status_Suspended",
        MediaSessionCatalogStatus.Reacquiring => "Status_Reacquiring",
        MediaSessionCatalogStatus.Unavailable => "Status_Unavailable",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    });

    public static string DescribePlaybackStatus(PlaybackStatus status) => UiText.Get(status switch
    {
        PlaybackStatus.Unknown => "Playback_Unknown",
        PlaybackStatus.Closed => "Playback_Closed",
        PlaybackStatus.Opened => "Playback_Opened",
        PlaybackStatus.Changing => "Playback_Changing",
        PlaybackStatus.Stopped => "Playback_Stopped",
        PlaybackStatus.Playing => "Playback_Playing",
        PlaybackStatus.Paused => "Playback_Paused",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    });

    public static string DescribeFallbackPolicy(FallbackPolicy policy) => UiText.Get(policy switch
    {
        FallbackPolicy.Wait => "Fallback_Wait",
        FallbackPolicy.SameApplication => "Fallback_SameApplication",
        FallbackPolicy.WindowsCurrentSession => "Fallback_WindowsCurrent",
        FallbackPolicy.SameApplicationThenWindowsCurrentSession =>
            "Fallback_SameApplicationThenWindows",
        FallbackPolicy.DisableRouting => "Fallback_DisableRouting",
        _ => throw new ArgumentOutOfRangeException(nameof(policy)),
    });
}
