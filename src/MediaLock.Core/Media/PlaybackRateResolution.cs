namespace MediaLock.Core.Media;

public enum PlaybackRateResolutionSource
{
    Fallback,
    Estimated,
    Reported,
}

public readonly record struct PlaybackRateResolution
{
    private const double MaximumReportedRate = 16d;

    private PlaybackRateResolution(
        double rate,
        PlaybackRateResolutionSource source,
        double confidence)
    {
        Rate = rate;
        Source = source;
        Confidence = confidence;
    }

    public double Rate { get; }

    public PlaybackRateResolutionSource Source { get; }

    public double Confidence { get; }

    public static PlaybackRateResolution Fallback { get; } = new(
        1d,
        PlaybackRateResolutionSource.Fallback,
        0d);

    public static PlaybackRateResolution FromReported(double? reportedRate) =>
        NormalizeReported(reportedRate) is { } rate
            ? new PlaybackRateResolution(
                rate,
                PlaybackRateResolutionSource.Reported,
                1d)
            : Fallback;

    internal static double? NormalizeReported(double? reportedRate) =>
        reportedRate is { } rate &&
        double.IsFinite(rate) &&
        rate > 0d &&
        rate <= MaximumReportedRate
            ? rate
            : null;

    internal static PlaybackRateResolution FromEstimate(
        double estimatedRate,
        double confidence) => new(
            estimatedRate,
            PlaybackRateResolutionSource.Estimated,
            Math.Clamp(confidence, 0d, 1d));

    internal static PlaybackRateResolution Validate(PlaybackRateResolution resolution)
    {
        if (!double.IsFinite(resolution.Rate) ||
            resolution.Rate <= 0d ||
            resolution.Rate > MaximumReportedRate ||
            !Enum.IsDefined(resolution.Source) ||
            !double.IsFinite(resolution.Confidence) ||
            resolution.Confidence < 0d ||
            resolution.Confidence > 1d)
        {
            throw new ArgumentException(
                "A valid playback-rate resolution is required.",
                nameof(resolution));
        }

        return resolution;
    }
}
