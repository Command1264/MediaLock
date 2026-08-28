using System.Collections.Immutable;
using MediaLock.Core.Configuration;
using MediaLock.Core.Media;
using MediaLock.Core.Routing;
using Xunit;

namespace MediaLock.Application.Tests;

public sealed class DiagnosticSummaryTests
{
    [Fact]
    public void CreateReportsEnvironmentAndRoutingStateWithoutMediaMetadata()
    {
        var environment = new AppEnvironmentInfo(
            "0.2.0-rc.3",
            "Windows 11 Enterprise",
            "24H2",
            "26100.9168",
            "x64",
            IsSigned: false);
        var session = new MediaSessionSnapshot(
            new SessionKey("secret-session-key"),
            "MSEdge",
            PlaybackStatus.Playing,
            MediaCommandCapabilities.All,
            DateTimeOffset.Parse("2026-08-24T00:00:00Z"),
            SessionInstanceHint: @"C:\Users\PrivateAccount\session",
            Metadata: new MediaMetadata(
                "Private song title",
                "Private artist",
                @"C:\Users\PrivateAccount\album",
                null));
        var state = MediaLockApplicationState.Initial with
        {
            Router = RouterState.Initial with
            {
                Mode = RoutingMode.PriorityRules,
                Status = RouterStatus.Recovering,
                Sessions = ImmutableArray.Create(session),
            },
            CatalogStatus = MediaSessionCatalogStatus.Reacquiring,
            Settings = MediaLockSettings.Default with
            {
                Recovery = new RecoverySettings(
                    TimeSpan.FromSeconds(30),
                    FallbackPolicy.Wait),
                Desktop = MediaLockSettings.Default.Desktop! with
                {
                    InterceptMediaKeys = true,
                },
            },
        };

        var summary = DiagnosticSummary.Create(environment, state, isMediaInputRunning: true);

        var expected = string.Join(
            Environment.NewLine,
            "Media Lock diagnostics",
            "Version: 0.2.0-rc.3",
            "Release: Prerelease",
            "Signature: Unsigned",
            "Windows: Windows 11 Enterprise 24H2 (build 26100.9168)",
            "Architecture: x64",
            "Routing mode: PriorityRules",
            "Routing status: Recovering",
            "Media catalog: Reacquiring",
            "Problem code: None",
            "Media-key interception: Active",
            "Session count: 1",
            "Recovery timeout: 30 seconds",
            "Fallback policy: Wait");
        Assert.Equal(expected, summary);
        Assert.DoesNotContain("Private song title", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("Private artist", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-session-key", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("PrivateAccount", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateIncludesTheActiveProblemCodeWithoutTechnicalContext()
    {
        var environment = new AppEnvironmentInfo(
            "0.3.0",
            "Windows 11 Pro",
            "25H2",
            "26200.1000",
            "x64",
            IsSigned: false);
        var state = MediaLockApplicationState.Initial with
        {
            Problem = MediaLockProblem.Error(
                MediaLockProblemId.RuntimeStateSaveFailed,
                new IOException(@"C:\Users\PrivateAccount\state.json")),
        };

        var summary = DiagnosticSummary.Create(environment, state, isMediaInputRunning: true);

        Assert.Contains("Problem code: ML-CFG-009", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("PrivateAccount", summary, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(IOException), summary, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateCanIncludeTheLatestSurfaceProblemWithoutPromotingItToApplicationState()
    {
        var environment = new AppEnvironmentInfo(
            "0.3.0",
            "Windows 11 Pro",
            "25H2",
            "26200.1000",
            "x64",
            IsSigned: false);

        var summary = DiagnosticSummary.Create(
            environment,
            MediaLockApplicationState.Initial,
            isMediaInputRunning: true,
            lastReportedProblemCode: "ML-SET-003");

        Assert.Contains("Problem code: ML-SET-003", summary, StringComparison.Ordinal);
        Assert.Null(MediaLockApplicationState.Initial.Problem);
    }

    [Fact]
    public void CreateHandlesStableSignedReleaseAndMissingOptionalSettings()
    {
        var environment = new AppEnvironmentInfo(
            "1.0.0",
            "Windows 11 Pro",
            "25H2",
            "26200.1000",
            "Arm64",
            IsSigned: true);
        var state = MediaLockApplicationState.Initial with
        {
            Settings = MediaLockSettings.Default with
            {
                Recovery = null,
                Desktop = null,
            },
        };

        var summary = DiagnosticSummary.Create(environment, state, isMediaInputRunning: false);

        Assert.Contains($"Release: Stable{Environment.NewLine}", summary, StringComparison.Ordinal);
        Assert.Contains($"Signature: Signed{Environment.NewLine}", summary, StringComparison.Ordinal);
        Assert.Contains("Media-key interception: Unknown", summary, StringComparison.Ordinal);
        Assert.Contains("Recovery timeout: Unknown", summary, StringComparison.Ordinal);
        Assert.Contains("Fallback policy: Unknown", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateReportsUnavailableWhenInterceptionIsEnabledButHookIsNotRunning()
    {
        var environment = new AppEnvironmentInfo(
            "0.2.0-rc.3",
            "Windows 11 Pro",
            "25H2",
            "26200.1000",
            "X64",
            IsSigned: false);
        var state = MediaLockApplicationState.Initial with
        {
            Settings = MediaLockSettings.Default with
            {
                Desktop = MediaLockSettings.Default.Desktop! with
                {
                    InterceptMediaKeys = true,
                },
            },
        };

        var summary = DiagnosticSummary.Create(environment, state, isMediaInputRunning: false);

        Assert.Contains("Media-key interception: Unavailable", summary, StringComparison.Ordinal);
    }
}
