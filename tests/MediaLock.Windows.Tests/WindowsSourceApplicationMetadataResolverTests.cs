using MediaLock.Application;
using MediaLock.Core.Diagnostics;
using Xunit;

namespace MediaLock.Windows.Tests;

public sealed class WindowsSourceApplicationMetadataResolverTests
{
    [Fact]
    public void TryResolveLoadsTheShellCatalogOnceAndUsesExactIdentity()
    {
        var loads = 0;
        var resolver = new WindowsSourceApplicationMetadataResolver(() =>
        {
            loads++;
            return new Dictionary<string, SourceApplicationMetadata>(StringComparer.Ordinal)
            {
                ["Brave._crx_music"] = new("YouTube Music", "Brave Browser"),
            };
        });

        var first = resolver.TryResolve("Brave._crx_music");
        var second = resolver.TryResolve("Brave._crx_music");
        var unknown = resolver.TryResolve("brave._crx_music");

        Assert.Equal(new SourceApplicationMetadata("YouTube Music", "Brave Browser"), first);
        Assert.Equal(first, second);
        Assert.Null(unknown);
        Assert.Equal(1, loads);
    }

    [Fact]
    public void TryResolveFallsBackWithoutRetryingWhenTheShellCatalogIsUnavailable()
    {
        var loads = 0;
        var diagnostics = new RecordingDiagnosticLog();
        var resolver = new WindowsSourceApplicationMetadataResolver(() =>
        {
            loads++;
            throw new InvalidOperationException("Shell catalog unavailable.");
        }, diagnostics);

        Assert.Null(resolver.TryResolve("Brave._crx_music"));
        Assert.Null(resolver.TryResolve("Brave._crx_music"));
        Assert.Equal(1, loads);
        var diagnostic = Assert.Single(diagnostics.Events);
        Assert.Equal("source.metadata.failed", diagnostic.Name);
        Assert.Equal("catalog", diagnostic.Properties?["stage"]);
        Assert.Equal(
            typeof(InvalidOperationException).FullName,
            diagnostic.Properties?["exceptionType"]);
    }

    [Theory]
    [InlineData("YouTube Music", "Brave Browser", "YouTube Music", "Brave Browser")]
    [InlineData("Brave", "Brave Browser", "Brave", null)]
    [InlineData("Google Chrome", "Google Chrome", "Google Chrome", null)]
    [InlineData("Player", "", "Player", null)]
    public void CreateMetadataAddsOnlyAUsefulDistinctHostQualifier(
        string displayName,
        string targetProductName,
        string expectedDisplayName,
        string? expectedHostDisplayName)
    {
        var metadata = WindowsSourceApplicationMetadataResolver.CreateMetadata(
            displayName,
            targetProductName);

        Assert.Equal(expectedDisplayName, metadata?.DisplayName);
        Assert.Equal(expectedHostDisplayName, metadata?.HostDisplayName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateMetadataRejectsMissingDisplayNames(string displayName)
    {
        Assert.Null(WindowsSourceApplicationMetadataResolver.CreateMetadata(
            displayName,
            "Host"));
    }

    private sealed class RecordingDiagnosticLog : IDiagnosticLog
    {
        public List<DiagnosticEvent> Events { get; } = [];

        public ValueTask WriteAsync(
            DiagnosticEvent diagnosticEvent,
            CancellationToken cancellationToken)
        {
            Events.Add(diagnosticEvent);
            return ValueTask.CompletedTask;
        }
    }
}
