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

    [Fact]
    public async Task ProblemEventsWriteTheStableCodeWithoutPrivateExceptionText()
    {
        using var directory = new TemporaryDirectory();
        await using var log = new JsonLinesDiagnosticLog(
            directory.Path,
            maxFileBytes: 4096,
            maxFiles: 1);

        await log.WriteAsync(
            new DiagnosticEvent(
                "runtime.state.save_failed",
                new Dictionary<string, string>
                {
                    ["exceptionType"] = typeof(IOException).FullName!,
                },
                "ML-CFG-009"),
            CancellationToken.None);

        var content = File.ReadAllText(Path.Combine(directory.Path, "medialock.jsonl"));

        Assert.Contains("ML-CFG-009", content, StringComparison.Ordinal);
        Assert.Contains(nameof(IOException), content, StringComparison.Ordinal);
        Assert.DoesNotContain("PrivateAccount", content, StringComparison.Ordinal);
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
