using System.Globalization;
using MediaLock.App.Localization;
using MediaLock.Application;
using MediaLock.Core.Routing;

namespace MediaLock.App.Presentation;

internal static class ProblemPresentation
{
    public static MediaLockProblem FromRouteDecision(RouteDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        var id = decision.Reason switch
        {
            RouteReason.ControlFailed => MediaLockProblemId.CommandFailed,
            RouteReason.ControlRejected => MediaLockProblemId.CommandRejected,
            RouteReason.ControlOutcomeUnknown => MediaLockProblemId.CommandOutcomeUnknown,
            RouteReason.UnsupportedCommand => MediaLockProblemId.CommandUnsupported,
            RouteReason.SeekTimelineUnavailable => MediaLockProblemId.SeekTimelineUnavailable,
            RouteReason.SeekOutOfRange => MediaLockProblemId.SeekOutOfRange,
            RouteReason.LockedTargetRecovering or
                RouteReason.LockedTargetUnavailable or
                RouteReason.NoWindowsCurrentSession or
                RouteReason.InputTargetChanged => MediaLockProblemId.CommandTargetUnavailable,
            _ => MediaLockProblemId.ApplicationOperationFailed,
        };
        return MediaLockProblem.Create(id);
    }

    public static string Describe(MediaLockProblem problem) =>
        Describe(problem, UiText.CurrentCulture);

    public static string Describe(MediaLockProblem problem, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentNullException.ThrowIfNull(culture);
        var definition = MediaLockProblemCatalog.Get(problem.Id);
        var resourceKey = $"Problem_{definition.Id}";
        var english = CultureInfo.GetCultureInfo("en-US");
        var message = UiText.TryGetExact(resourceKey, culture) ??
            UiText.TryGetExact(resourceKey, english) ??
            UiText.TryGetExact($"Problem_{MediaLockProblemId.Unknown}", culture) ??
            UiText.TryGetExact($"Problem_{MediaLockProblemId.Unknown}", english) ??
            "An unexpected Media Lock error occurred. Try again.";

        return culture.Name.Equals("zh-TW", StringComparison.OrdinalIgnoreCase)
            ? $"{message}（{definition.Code}）"
            : $"{message} ({definition.Code})";
    }
}
