namespace MediaLock.Core.Media;

public sealed record PlaybackRateObservation(
    MediaTargetId Target,
    PlaybackStatus PlaybackStatus,
    MediaTimeline? Timeline,
    TimeSpan MonotonicTime,
    double? ReportedPlaybackRate = null);

public enum PlaybackRateResetReason
{
    Seek,
    PlaybackStopped,
    PlaybackPaused,
    PlaybackChanging,
    Recovery,
    Reconnected,
    TargetReplaced,
    DocumentReplaced,
    InvalidTimeline,
    PositionDiscontinuity,
    TargetRemoved,
}

public sealed class PlaybackRateEstimator
{
    private static readonly TimeSpan MinimumObservationSpan = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ObservationWindow = TimeSpan.FromSeconds(5);
    private const double MinimumEstimatedRate = 0.25d;
    private const double MaximumEstimatedRate = 4d;
    private const int MaximumTrackedTargets = 256;
    private readonly Dictionary<MediaTargetId, TargetState> states = [];
    private readonly Dictionary<MediaTargetId, LinkedListNode<MediaTargetId>> recencyNodes = [];
    private readonly LinkedList<MediaTargetId> recency = [];

    public void Reset(MediaTargetId target, PlaybackRateResetReason reason)
    {
        _ = reason;
        RemoveState(target);
    }

    public PlaybackRateResolution Observe(PlaybackRateObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        var reported = PlaybackRateResolution.FromReported(observation.ReportedPlaybackRate);
        if (reported.Source == PlaybackRateResolutionSource.Reported)
        {
            RemoveState(observation.Target);
            return reported;
        }

        if (!CanSample(observation))
        {
            RemoveState(observation.Target);
            return PlaybackRateResolution.Fallback;
        }

        var sample = new Sample(
            observation.MonotonicTime,
            observation.Timeline!.Start,
            observation.Timeline.End,
            observation.Timeline.Position);
        if (states.TryGetValue(observation.Target, out var existing) &&
            sample.MonotonicTime <= existing.Samples[^1].MonotonicTime)
        {
            Touch(observation.Target);
            return existing.Resolution;
        }

        if (existing is not null)
        {
            var previous = existing.Samples[^1];
            var elapsed = sample.MonotonicTime - previous.MonotonicTime;
            var positionDelta = sample.Position - previous.Position;
            var incrementalSlope = positionDelta.TotalSeconds / elapsed.TotalSeconds;
            if (sample.Start != previous.Start ||
                sample.End != previous.End ||
                positionDelta < TimeSpan.Zero ||
                incrementalSlope > MaximumEstimatedRate)
            {
                var reset = new TargetState([sample], PlaybackRateResolution.Fallback);
                SetState(observation.Target, reset);
                return reset.Resolution;
            }
        }

        var samples = existing is null
            ? [sample]
            : existing.Samples.Append(sample).ToArray();
        samples = samples
            .Where(candidate => sample.MonotonicTime - candidate.MonotonicTime <= ObservationWindow)
            .ToArray();
        var candidate = Resolve(samples);
        var next = Stabilize(samples, existing, candidate);
        SetState(observation.Target, next);
        return next.Resolution;
    }

    private void SetState(MediaTargetId target, TargetState state)
    {
        states[target] = state;
        Touch(target);
        while (states.Count > MaximumTrackedTargets)
        {
            RemoveState(recency.First!.Value);
        }
    }

    private void Touch(MediaTargetId target)
    {
        if (recencyNodes.Remove(target, out var existing))
        {
            recency.Remove(existing);
        }

        recencyNodes[target] = recency.AddLast(target);
    }

    private void RemoveState(MediaTargetId target)
    {
        states.Remove(target);
        if (recencyNodes.Remove(target, out var node))
        {
            recency.Remove(node);
        }
    }

    private static bool CanSample(PlaybackRateObservation observation) =>
        observation.Target.IsValid &&
        observation.PlaybackStatus == PlaybackStatus.Playing &&
        observation.MonotonicTime >= TimeSpan.Zero &&
        observation.Timeline is
        {
            Start: var start,
            End: var end,
            Position: var position,
        } &&
        end > start &&
        position >= start &&
        position <= end;

    private static PlaybackRateResolution Resolve(IReadOnlyList<Sample> samples)
    {
        if (samples.Count < 3 ||
            samples[^1].MonotonicTime - samples[0].MonotonicTime < MinimumObservationSpan)
        {
            return PlaybackRateResolution.Fallback;
        }

        var slopes = new List<double>();
        for (var startIndex = 0; startIndex < samples.Count - 1; startIndex++)
        {
            for (var endIndex = startIndex + 1; endIndex < samples.Count; endIndex++)
            {
                var elapsed = samples[endIndex].MonotonicTime - samples[startIndex].MonotonicTime;
                var positionDelta = samples[endIndex].Position - samples[startIndex].Position;
                var slope = positionDelta.TotalSeconds / elapsed.TotalSeconds;
                if (double.IsFinite(slope) &&
                    slope >= 0d &&
                    slope <= MaximumEstimatedRate)
                {
                    slopes.Add(slope);
                }
            }
        }

        if (slopes.Count == 0)
        {
            return PlaybackRateResolution.Fallback;
        }

        slopes.Sort();
        var middle = slopes.Count / 2;
        var median = slopes.Count % 2 == 0
            ? (slopes[middle - 1] + slopes[middle]) / 2d
            : slopes[middle];
        if (median < MinimumEstimatedRate)
        {
            return PlaybackRateResolution.Fallback;
        }
        var span = samples[^1].MonotonicTime - samples[0].MonotonicTime;
        var confidence = Math.Max(0.5d, span.TotalSeconds / ObservationWindow.TotalSeconds);
        return PlaybackRateResolution.FromEstimate(median, confidence);
    }

    private static TargetState Stabilize(
        IReadOnlyList<Sample> samples,
        TargetState? previous,
        PlaybackRateResolution candidate)
    {
        if (previous?.Resolution is not
            {
                Source: PlaybackRateResolutionSource.Estimated,
            } published ||
            candidate.Source != PlaybackRateResolutionSource.Estimated)
        {
            return new TargetState(samples, candidate, PendingDirection: 0, PendingCount: 0);
        }

        var relativeDifference = Math.Abs(candidate.Rate - published.Rate) / published.Rate;
        if (relativeDifference <= 0.1d)
        {
            return new TargetState(samples, published, PendingDirection: 0, PendingCount: 0);
        }

        var direction = Math.Sign(candidate.Rate - published.Rate);
        var pendingCount = previous.PendingDirection == direction
            ? previous.PendingCount + 1
            : 1;
        return pendingCount >= 2
            ? new TargetState(samples, candidate, PendingDirection: 0, PendingCount: 0)
            : new TargetState(samples, published, direction, pendingCount);
    }

    private sealed record TargetState(
        IReadOnlyList<Sample> Samples,
        PlaybackRateResolution Resolution,
        int PendingDirection = 0,
        int PendingCount = 0);

    private readonly record struct Sample(
        TimeSpan MonotonicTime,
        TimeSpan Start,
        TimeSpan End,
        TimeSpan Position);
}
