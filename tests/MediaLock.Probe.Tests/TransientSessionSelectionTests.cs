using MediaLock.Probe;

namespace MediaLock.Probe.Tests;

public sealed class TransientSessionSelectionTests
{
    [Fact]
    public void UniqueSameSourceSessionReturningWithinWindowRestoresSelection()
    {
        var selectedAt = new DateTimeOffset(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);
        var original = new TestSession("youtube-music", "original");
        var replacement = new TestSession("youtube-music", "replacement");
        var other = new TestSession("youtube", "other");
        var selection = new TransientSessionSelection<TestSession>(
            session => session.Source,
            TimeSpan.FromSeconds(2));

        selection.Select(original);
        selection.Observe([other], selectedAt);

        Assert.Equal(TransientSelectionStatus.Recovering, selection.Status);
        Assert.Null(selection.Current);

        selection.Observe([other, replacement], selectedAt.AddMilliseconds(500));

        Assert.Equal(TransientSelectionStatus.Selected, selection.Status);
        Assert.Same(replacement, selection.Current);
    }

    [Fact]
    public void UniqueSameSourceSessionReturningAfterExpiryRestoresSelection()
    {
        var lostAt = new DateTimeOffset(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);
        var original = new TestSession("youtube-music", "original");
        var replacement = new TestSession("youtube-music", "replacement");
        var other = new TestSession("youtube", "other");
        var selection = new TransientSessionSelection<TestSession>(
            session => session.Source,
            TimeSpan.FromSeconds(2));

        selection.Select(original);
        selection.Observe([other], lostAt);
        selection.Expire(lostAt.AddSeconds(2));

        selection.Observe([other, replacement], lostAt.AddSeconds(10));

        Assert.Equal(TransientSelectionStatus.Selected, selection.Status);
        Assert.Same(replacement, selection.Current);
    }

    [Fact]
    public void SessionSnapshotAfterRecoveryDeadlineCanRestoreTarget()
    {
        var lostAt = new DateTimeOffset(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);
        var original = new TestSession("youtube-music", "original");
        var replacement = new TestSession("youtube-music", "replacement");
        var other = new TestSession("youtube", "other");
        var selection = new TransientSessionSelection<TestSession>(
            session => session.Source,
            TimeSpan.FromSeconds(2));

        selection.Select(original);
        selection.Observe([other], lostAt);

        selection.Observe([other, replacement], lostAt.AddSeconds(10));

        Assert.Equal(TransientSelectionStatus.Selected, selection.Status);
        Assert.Same(replacement, selection.Current);
    }

    [Fact]
    public void AmbiguousSameSourceSessionsRemainRecovering()
    {
        var lostAt = new DateTimeOffset(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);
        var original = new TestSession("youtube-music", "original");
        var firstCandidate = new TestSession("youtube-music", "first");
        var secondCandidate = new TestSession("youtube-music", "second");
        var selection = new TransientSessionSelection<TestSession>(
            session => session.Source,
            TimeSpan.FromSeconds(2));

        selection.Select(original);
        selection.Observe([], lostAt);
        selection.Observe([firstCandidate, secondCandidate], lostAt.AddMilliseconds(500));

        Assert.Equal(TransientSelectionStatus.Recovering, selection.Status);
        Assert.Null(selection.Current);
    }

    [Fact]
    public void ClearDuringRecoveryPreventsReacquisition()
    {
        var lostAt = new DateTimeOffset(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);
        var original = new TestSession("youtube-music", "original");
        var replacement = new TestSession("youtube-music", "replacement");
        var selection = new TransientSessionSelection<TestSession>(
            session => session.Source,
            TimeSpan.FromSeconds(2));

        selection.Select(original);
        selection.Observe([], lostAt);
        selection.Clear();
        selection.Observe([replacement], lostAt.AddMilliseconds(500));

        Assert.Equal(TransientSelectionStatus.None, selection.Status);
        Assert.Null(selection.Current);
    }

    [Fact]
    public void RecoveryExpiryKeepsLockedTargetUnavailable()
    {
        var lostAt = new DateTimeOffset(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);
        var original = new TestSession("youtube-music", "original");
        var selection = new TransientSessionSelection<TestSession>(
            session => session.Source,
            TimeSpan.FromSeconds(2));

        selection.Select(original);
        selection.Observe([], lostAt);
        selection.Expire(lostAt.AddMilliseconds(2001));

        Assert.Equal(TransientSelectionStatus.Unavailable, selection.Status);
        Assert.Null(selection.Current);
        Assert.Equal("youtube-music", selection.SelectedSource);
    }

    [Fact]
    public void UnavailableTargetStaysUnavailableWhenNoCandidateAppears()
    {
        var lostAt = new DateTimeOffset(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);
        var original = new TestSession("youtube-music", "original");
        var other = new TestSession("youtube", "other");
        var selection = new TransientSessionSelection<TestSession>(
            session => session.Source,
            TimeSpan.FromSeconds(2));

        selection.Select(original);
        selection.Observe([other], lostAt);
        selection.Expire(lostAt.AddSeconds(2));

        selection.Observe([other], lostAt.AddSeconds(10));

        Assert.Equal(TransientSelectionStatus.Unavailable, selection.Status);
        Assert.Null(selection.Current);
        Assert.Equal("youtube-music", selection.SelectedSource);
    }

    [Fact]
    public void RecoveryWindowDeadlineMakesLockedTargetUnavailable()
    {
        var lostAt = new DateTimeOffset(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);
        var original = new TestSession("youtube-music", "original");
        var selection = new TransientSessionSelection<TestSession>(
            session => session.Source,
            TimeSpan.FromSeconds(2));

        selection.Select(original);
        selection.Observe([], lostAt);
        selection.Expire(lostAt.AddSeconds(2));

        Assert.Equal(TransientSelectionStatus.Unavailable, selection.Status);
        Assert.Null(selection.Current);
        Assert.Equal("youtube-music", selection.SelectedSource);
    }

    private sealed record TestSession(string Source, string Instance);
}
