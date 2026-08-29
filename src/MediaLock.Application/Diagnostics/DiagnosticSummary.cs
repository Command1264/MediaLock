using System.Globalization;

namespace MediaLock.Application;

public static class DiagnosticSummary
{
    public static string Create(
        AppEnvironmentInfo environment,
        MediaLockApplicationState state,
        bool isMediaInputRunning,
        string? lastReportedProblemCode = null)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(state);

        var desktop = state.Settings.Desktop;
        var recovery = state.Settings.Recovery;
        var lines = new[]
        {
            "Media Lock diagnostics",
            $"Version: {environment.AppVersion}",
            $"Release: {(environment.IsPrerelease ? "Prerelease" : "Stable")}",
            $"Signature: {(environment.IsSigned ? "Signed" : "Unsigned")}",
            $"Windows: {FormatWindows(environment)}",
            $"Architecture: {environment.Architecture}",
            $"Routing mode: {state.Router.Mode}",
            $"Routing status: {state.Router.Status}",
            $"Media catalog: {state.CatalogStatus}",
            $"Problem code: {lastReportedProblemCode ?? state.Problem?.Code ?? "None"}",
            $"Media-key interception: {FormatInterception(desktop?.InterceptMediaKeys, isMediaInputRunning)}",
            $"Session count: {state.Router.Sessions.Length}",
            $"Recovery timeout: {FormatTimeout(recovery?.Timeout)}",
            $"Fallback policy: {recovery?.FallbackPolicy.ToString() ?? "Unknown"}",
        };

        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatWindows(AppEnvironmentInfo environment)
    {
        var displayVersion = string.IsNullOrWhiteSpace(environment.WindowsDisplayVersion)
            ? string.Empty
            : $" {environment.WindowsDisplayVersion}";
        return $"{environment.WindowsProductName}{displayVersion} " +
            $"(build {environment.WindowsBuild})";
    }

    private static string FormatInterception(bool? enabled, bool isRunning) => enabled switch
    {
        true when isRunning => "Active",
        true => "Unavailable",
        false => "Disabled",
        null => "Unknown",
    };

    private static string FormatTimeout(TimeSpan? timeout) => timeout is null
        ? "Unknown"
        : $"{timeout.Value.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)} seconds";
}
