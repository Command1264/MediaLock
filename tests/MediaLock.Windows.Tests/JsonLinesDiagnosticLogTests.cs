using MediaLock.Core.Diagnostics;
using MediaLock.Windows.Diagnostics;
using Xunit;

namespace MediaLock.Windows.Tests;

public sealed class JsonLinesDiagnosticLogTests
{
    [Fact]
    public async Task LogRotatesWithinTheConfiguredFileBoundAndWritesOnlyProvidedProperties()
    {
        using var directory = new TemporaryDirectory();
        IAsyncDisposable log = new JsonLinesDiagnosticLog(
            directory.Path,
            maxFileBytes: 180,
            maxFiles: 2);
        var diagnosticLog = (IDiagnosticLog)log;

        for (var index = 0; index < 12; index++)
        {
            await diagnosticLog.WriteAsync(
                new DiagnosticEvent(
                    "route.completed",
                    new Dictionary<string, string>
                    {
                        ["decision"] = "Routed",
                    }),
                CancellationToken.None);
        }

        await log.DisposeAsync();
        var files = Directory.GetFiles(directory.Path, "*.jsonl");
        var content = string.Join(Environment.NewLine, files.Select(File.ReadAllText));

        Assert.InRange(files.Length, 1, 2);
        Assert.Contains("route.completed", content, StringComparison.Ordinal);
        Assert.DoesNotContain("title", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("artist", content, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"MediaLock.Tests.{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
