using MediaLock.Application;
using MediaLock.Windows.Diagnostics;
using Xunit;

namespace MediaLock.Windows.Tests;

public sealed class DesktopSupportActionsTests
{
    [Fact]
    public async Task ExecuteAsyncCopiesDiagnosticsWithoutChangingTheirLineEndings()
    {
        string? copied = null;
        var actions = new DesktopSupportActions(
            text => copied = text,
            _ => throw new InvalidOperationException("Shell should not be used."),
            "unused");
        var summary = $"first{Environment.NewLine}second";

        await actions.ExecuteAsync(
            new DesktopSupportRequest(DesktopSupportAction.CopyDiagnostics, summary),
            CancellationToken.None);

        Assert.Equal(summary, copied);
    }

    [Fact]
    public async Task ExecuteAsyncCreatesLogsFolderAndOpensExpectedSupportTargets()
    {
        var temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            "MediaLock.Tests",
            Guid.NewGuid().ToString("N"));
        var logsDirectory = Path.Combine(temporaryRoot, "logs");
        var opened = new List<string>();
        var actions = new DesktopSupportActions(
            _ => throw new InvalidOperationException("Clipboard should not be used."),
            opened.Add,
            logsDirectory);

        try
        {
            await actions.ExecuteAsync(
                new DesktopSupportRequest(DesktopSupportAction.OpenLogsFolder),
                CancellationToken.None);
            await actions.ExecuteAsync(
                new DesktopSupportRequest(DesktopSupportAction.OpenSupport),
                CancellationToken.None);
            await actions.ExecuteAsync(
                new DesktopSupportRequest(DesktopSupportAction.ReportBug),
                CancellationToken.None);

            Assert.True(Directory.Exists(logsDirectory));
            Assert.Equal(logsDirectory, opened[0]);
            Assert.Equal("https://github.com/Command1264/MediaLock/issues", opened[1]);
            Assert.Equal(
                "https://github.com/Command1264/MediaLock/issues/new?template=bug-report.yml",
                opened[2]);
        }
        finally
        {
            if (Directory.Exists(temporaryRoot))
            {
                Directory.Delete(temporaryRoot, recursive: true);
            }
        }
    }
}
