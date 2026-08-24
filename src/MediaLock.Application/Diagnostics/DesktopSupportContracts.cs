namespace MediaLock.Application;

public enum DesktopSupportAction
{
    CopyDiagnostics,
    OpenLogsFolder,
    OpenSupport,
    ReportBug,
}

public sealed record DesktopSupportRequest(
    DesktopSupportAction Action,
    string? DiagnosticSummary = null);

public interface IDesktopSupportActions
{
    ValueTask ExecuteAsync(
        DesktopSupportRequest request,
        CancellationToken cancellationToken);
}
