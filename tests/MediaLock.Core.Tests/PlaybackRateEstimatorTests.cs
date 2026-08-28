using MediaLock.Core.Media;

namespace MediaLock.Core.Tests;

public sealed class PlaybackRateEstimatorTests
{
    [Fact]
    public void ValidReportedRateIsImmediatelyAuthoritative()
    {
        var estimator = new PlaybackRateEstimator();
        var result = estimator.Observe(new PlaybackRateObservation(
            MediaTargetId.FromBrowserPageBinding("reported-rate"),
            PlaybackStatus.Playing,
            TimelineAt(positionSeconds: 30),
            TimeSpan.FromSeconds(10),
            ReportedPlaybackRate: 1.5d));

        Assert.Equal(1.5d, result.Rate);
        Assert.Equal(PlaybackRateResolutionSource.Reported, result.Source);
        Assert.Equal(1d, result.Confidence);
    }

    [Theory]
    [InlineData(0.5d)]
    [InlineData(1d)]
    [InlineData(1.5d)]
    [InlineData(2d)]
    public void MissingRateConvergesAfterThreeConsistentPlayingObservations(double rate)
    {
        var estimator = new PlaybackRateEstimator();
        var target = MediaTargetId.FromBrowserPageBinding("estimated-rate");

        var first = estimator.Observe(new PlaybackRateObservation(
            target,
            PlaybackStatus.Playing,
            TimelineAt(positionSeconds: 10),
            TimeSpan.Zero));
        var second = estimator.Observe(new PlaybackRateObservation(
            target,
            PlaybackStatus.Playing,
            TimelineAt(positionSeconds: 10 + (2 * rate)),
            TimeSpan.FromSeconds(2)));
        var third = estimator.Observe(new PlaybackRateObservation(
            target,
            PlaybackStatus.Playing,
            TimelineAt(positionSeconds: 10 + (4 * rate)),
            TimeSpan.FromSeconds(4)));

        Assert.Equal(PlaybackRateResolutionSource.Fallback, first.Source);
        Assert.Equal(PlaybackRateResolutionSource.Fallback, second.Source);
        Assert.Equal(PlaybackRateResolutionSource.Estimated, third.Source);
        Assert.Equal(rate, third.Rate, precision: 6);
        Assert.InRange(third.Confidence, 0.5d, 1d);
    }

    [Fact]
    public void SeekResetDiscardsAConfidentEstimate()
    {
        var estimator = new PlaybackRateEstimator();
        var target = MediaTargetId.FromBrowserPageBinding("seek-reset");
        _ = estimator.Observe(new PlaybackRateObservation(
            target,
            PlaybackStatus.Playing,
            TimelineAt(positionSeconds: 10),
            TimeSpan.Zero));
        _ = estimator.Observe(new PlaybackRateObservation(
            target,
            PlaybackStatus.Playing,
            TimelineAt(positionSeconds: 14),
            TimeSpan.FromSeconds(2)));
        var beforeReset = estimator.Observe(new PlaybackRateObservation(
            target,
            PlaybackStatus.Playing,
            TimelineAt(positionSeconds: 18),
            TimeSpan.FromSeconds(4)));

        estimator.Reset(target, PlaybackRateResetReason.Seek);
        var afterReset = estimator.Observe(new PlaybackRateObservation(
            target,
            PlaybackStatus.Playing,
            TimelineAt(positionSeconds: 90),
            TimeSpan.FromSeconds(5)));

        Assert.Equal(PlaybackRateResolutionSource.Estimated, beforeReset.Source);
        Assert.Equal(PlaybackRateResolutionSource.Fallback, afterReset.Source);
    }

    [Fact]
    public void QuantizedHalfSpeedTimelineStillConverges()
    {
        var estimator = new PlaybackRateEstimator();
        var target = MediaTargetId.FromBrowserPageBinding("quantized-half-speed");
        PlaybackRateResolution result = default;
        var quantizedPositions = new[] { 0d, 0d, 1d, 1d, 2d, 2d };

        for (var second = 0; second < quantizedPositions.Length; second++)
        {
            result = estimator.Observe(new PlaybackRateObservation(
                target,
                PlaybackStatus.Playing,
                TimelineAt(quantizedPositions[second]),
                TimeSpan.FromSeconds(second)));
        }

        Assert.Equal(PlaybackRateResolutionSource.Estimated, result.Source);
        Assert.InRange(result.Rate, 0.4d, 0.6d);
    }

    [Fact]
    public void SingleInconsistentSampleDoesNotReplaceAConfidentEstimate()
    {
        var estimator = new PlaybackRateEstimator();
        var target = MediaTargetId.FromBrowserPageBinding("single-jitter");
        _ = estimator.Observe(new PlaybackRateObservation(
            target,
            PlaybackStatus.Playing,
            TimelineAt(10),
            TimeSpan.Zero));
        _ = estimator.Observe(new PlaybackRateObservation(
            target,
            PlaybackStatus.Playing,
            TimelineAt(12),
            TimeSpan.FromSeconds(2)));
        var stable = estimator.Observe(new PlaybackRateObservation(
            target,
            PlaybackStatus.Playing,
            TimelineAt(14),
            TimeSpan.FromSeconds(4)));

        var afterJitter = estimator.Observe(new PlaybackRateObservation(
            target,
            PlaybackStatus.Playing,
            TimelineAt(17),
            TimeSpan.FromSeconds(5)));

        Assert.Equal(1d, stable.Rate, precision: 6);
        Assert.Equal(PlaybackRateResolutionSource.Estimated, afterJitter.Source);
        Assert.Equal(1d, afterJitter.Rate, precision: 6);
    }

    [Fact]
    public void SustainedMidPlayChangesReplaceThePreviousEstimate()
    {
        var estimator = new PlaybackRateEstimator();
        var target = MediaTargetId.FromBrowserPageBinding("continuous-rate-change");
        var position = 0d;
        PlaybackRateResolution result = default;

        for (var second = 0; second <= 4; second += 2)
        {
            position = second;
            result = Observe(estimator, target, second, position);
        }
        Assert.Equal(1d, result.Rate, precision: 6);

        for (var second = 5; second <= 9; second++)
        {
            position += 2d;
            result = Observe(estimator, target, second, position);
        }
        Assert.Equal(2d, result.Rate, precision: 6);

        for (var second = 10; second <= 17; second++)
        {
            position += 0.5d;
            result = Observe(estimator, target, second, position);
        }
        Assert.Equal(0.5d, result.Rate, precision: 6);
    }

    [Fact]
    public void SameTitleTargetsFromDifferentProvidersNeverShareSamples()
    {
        var estimator = new PlaybackRateEstimator();
        var gsmtc = MediaTargetId.FromGsmtc(new SessionKey("same-title"));
        var browser = MediaTargetId.FromBrowserPageBinding("same-title");
        PlaybackRateResolution gsmtcResult = default;
        PlaybackRateResolution browserResult = default;

        for (var second = 0; second <= 4; second += 2)
        {
            gsmtcResult = Observe(estimator, gsmtc, second, 10 + second);
            browserResult = Observe(estimator, browser, second, 50 + (2 * second));
        }

        Assert.Equal(1d, gsmtcResult.Rate, precision: 6);
        Assert.Equal(2d, browserResult.Rate, precision: 6);
    }

    [Fact]
    public void TimelineBoundsChangeDiscardsThePreviousEstimate()
    {
        var estimator = new PlaybackRateEstimator();
        var target = MediaTargetId.FromBrowserPageBinding("bounds-change");
        _ = Observe(estimator, target, 0, 10);
        _ = Observe(estimator, target, 2, 12);
        var beforeChange = Observe(estimator, target, 4, 14);

        var afterChange = estimator.Observe(new PlaybackRateObservation(
            target,
            PlaybackStatus.Playing,
            new MediaTimeline(
                TimeSpan.Zero,
                TimeSpan.FromMinutes(5),
                TimeSpan.FromSeconds(15),
                DateTimeOffset.Parse("2026-08-28T00:00:00Z")),
            TimeSpan.FromSeconds(5)));

        Assert.Equal(PlaybackRateResolutionSource.Estimated, beforeChange.Source);
        Assert.Equal(PlaybackRateResolutionSource.Fallback, afterChange.Source);
    }

    [Fact]
    public void TargetStateRetentionIsBounded()
    {
        var estimator = new PlaybackRateEstimator();
        var oldest = MediaTargetId.FromBrowserPageBinding("oldest-target");
        _ = Observe(estimator, oldest, 0, 10);
        _ = Observe(estimator, oldest, 2, 12);
        var established = Observe(estimator, oldest, 4, 14);

        for (var index = 0; index < 300; index++)
        {
            _ = Observe(
                estimator,
                MediaTargetId.FromBrowserPageBinding($"new-target-{index}"),
                4,
                index);
        }

        var afterPressure = Observe(estimator, oldest, 5, 15);

        Assert.Equal(PlaybackRateResolutionSource.Estimated, established.Source);
        Assert.Equal(PlaybackRateResolutionSource.Fallback, afterPressure.Source);
    }

    [Theory]
    [InlineData(PlaybackStatus.Paused)]
    [InlineData(PlaybackStatus.Stopped)]
    [InlineData(PlaybackStatus.Changing)]
    [InlineData(PlaybackStatus.Closed)]
    public void NonPlayingObservationResetsConfidence(PlaybackStatus status)
    {
        var estimator = new PlaybackRateEstimator();
        var target = MediaTargetId.FromBrowserPageBinding($"status-{status}");
        _ = Observe(estimator, target, 0, 10);
        _ = Observe(estimator, target, 2, 12);
        var established = Observe(estimator, target, 4, 14);

        var nonPlaying = estimator.Observe(new PlaybackRateObservation(
            target,
            status,
            TimelineAt(14),
            TimeSpan.FromSeconds(5)));
        var resumed = Observe(estimator, target, 6, 15);

        Assert.Equal(PlaybackRateResolutionSource.Estimated, established.Source);
        Assert.Equal(PlaybackRateResolutionSource.Fallback, nonPlaying.Source);
        Assert.Equal(PlaybackRateResolutionSource.Fallback, resumed.Source);
    }

    [Fact]
    public void DuplicateOrOlderTimestampCannotChangeAConfidentEstimate()
    {
        var estimator = new PlaybackRateEstimator();
        var target = MediaTargetId.FromBrowserPageBinding("stale-time");
        _ = Observe(estimator, target, 0, 10);
        _ = Observe(estimator, target, 2, 12);
        var established = Observe(estimator, target, 4, 14);

        var duplicate = Observe(estimator, target, 4, 100);
        var older = Observe(estimator, target, 3, 100);

        Assert.Equal(1d, established.Rate, precision: 6);
        Assert.Equal(established, duplicate);
        Assert.Equal(established, older);
    }

    [Fact]
    public void UnexplainedPositionJumpStartsANewSampleWindow()
    {
        var estimator = new PlaybackRateEstimator();
        var target = MediaTargetId.FromBrowserPageBinding("position-jump");
        _ = Observe(estimator, target, 0, 10);
        _ = Observe(estimator, target, 2, 12);
        var established = Observe(estimator, target, 4, 14);

        var afterJump = Observe(estimator, target, 5, 90);

        Assert.Equal(PlaybackRateResolutionSource.Estimated, established.Source);
        Assert.Equal(PlaybackRateResolutionSource.Fallback, afterJump.Source);
    }

    private static PlaybackRateResolution Observe(
        PlaybackRateEstimator estimator,
        MediaTargetId target,
        double monotonicSeconds,
        double positionSeconds) => estimator.Observe(new PlaybackRateObservation(
            target,
            PlaybackStatus.Playing,
            TimelineAt(positionSeconds),
            TimeSpan.FromSeconds(monotonicSeconds)));

    private static MediaTimeline TimelineAt(double positionSeconds) => new(
        TimeSpan.Zero,
        TimeSpan.FromMinutes(10),
        TimeSpan.FromSeconds(positionSeconds),
        DateTimeOffset.Parse("2026-08-28T00:00:00Z"));
}
