using MediaLock.Core.Media;

namespace MediaLock.Core.Tests;

public sealed class PlaybackRatePresentationTests
{
    [Fact]
    public void ExplicitReportedOneIsDistinctFromMissingPlaybackRate()
    {
        var observedAt = DateTimeOffset.Parse("2026-08-28T00:00:00Z");
        var missing = new MediaTargetPresentation(
            "Missing rate",
            PlaybackStatus.Playing,
            MediaCommandCapabilities.None,
            observedAt);
        var reported = new MediaTargetPresentation(
            "Reported rate",
            PlaybackStatus.Playing,
            MediaCommandCapabilities.None,
            observedAt,
            ReportedPlaybackRate: 1d);

        Assert.Null(missing.ReportedPlaybackRate);
        Assert.Equal(PlaybackRateResolutionSource.Fallback, missing.PlaybackRate.Source);
        Assert.Equal(1d, missing.PlaybackRate.Rate);
        Assert.Equal(1d, reported.ReportedPlaybackRate);
        Assert.Equal(PlaybackRateResolutionSource.Reported, reported.PlaybackRate.Source);
        Assert.Equal(1d, reported.PlaybackRate.Rate);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(17d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidReportedRateIsNormalizedToMissing(double invalidRate)
    {
        var presentation = new MediaTargetPresentation(
            "Invalid rate",
            PlaybackStatus.Playing,
            MediaCommandCapabilities.None,
            DateTimeOffset.Parse("2026-08-28T00:00:00Z"),
            ReportedPlaybackRate: invalidRate);

        Assert.Null(presentation.ReportedPlaybackRate);
        Assert.Equal(PlaybackRateResolutionSource.Fallback, presentation.PlaybackRate.Source);
        Assert.Equal(1d, presentation.PlaybackRate.Rate);
    }

    [Fact]
    public void GsmtcTargetPreservesAValidReportedRate()
    {
        var session = new MediaSessionSnapshot(
            new SessionKey("reported-gsmtc"),
            "Music.App",
            PlaybackStatus.Playing,
            MediaCommandCapabilities.All,
            DateTimeOffset.Parse("2026-08-28T00:00:00Z"),
            ReportedPlaybackRate: 1.25d);

        var target = MediaTargetSnapshot.FromGsmtc(session);

        Assert.Equal(1.25d, target.Presentation.ReportedPlaybackRate);
        Assert.Equal(PlaybackRateResolutionSource.Reported, target.Presentation.PlaybackRate.Source);
        Assert.Equal(1.25d, target.Presentation.PlaybackRate.Rate);
    }
}
