using System.Diagnostics;
using MediaLock.Application;

namespace MediaLock.Windows.Diagnostics;

public sealed class DesktopSupportActions : IDesktopSupportActions
{
    private static readonly Uri SupportUri = new(
        "https://github.com/Command1264/MediaLock/issues");
    private static readonly Uri BugReportUri = new(
        "https://github.com/Command1264/MediaLock/issues/new?template=bug-report.yml");
    private readonly Action<string> copyText;
    private readonly Action<string> openPath;
    private readonly string logsDirectory;

    public DesktopSupportActions()
        : this(
            System.Windows.Forms.Clipboard.SetText,
            OpenWithShell,
            JsonLinesDiagnosticLog.DefaultDirectoryPath)
    {
    }

    internal DesktopSupportActions(
        Action<string> copyText,
        Action<string> openPath,
        string logsDirectory)
    {
        this.copyText = copyText;
        this.openPath = openPath;
        this.logsDirectory = logsDirectory;
    }

    public ValueTask ExecuteAsync(
        DesktopSupportRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        switch (request.Action)
        {
            case DesktopSupportAction.CopyDiagnostics:
                ArgumentException.ThrowIfNullOrWhiteSpace(request.DiagnosticSummary);
                copyText(request.DiagnosticSummary);
                break;
            case DesktopSupportAction.OpenLogsFolder:
                Directory.CreateDirectory(logsDirectory);
                openPath(logsDirectory);
                break;
            case DesktopSupportAction.OpenSupport:
                openPath(SupportUri.AbsoluteUri);
                break;
            case DesktopSupportAction.ReportBug:
                openPath(BugReportUri.AbsoluteUri);
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(request),
                    request.Action,
                    "Unsupported desktop support action.");
        }

        return ValueTask.CompletedTask;
    }

    private static void OpenWithShell(string target) => Process.Start(new ProcessStartInfo
    {
        FileName = target,
        UseShellExecute = true,
    });
}
