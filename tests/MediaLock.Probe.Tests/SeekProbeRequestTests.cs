using MediaLock.Probe;

namespace MediaLock.Probe.Tests;

public sealed class SeekProbeRequestTests
{
    [Fact]
    public void ParsesInvariantSecondsAndPreservesTimeSpanTicks()
    {
        Assert.True(SeekProbeRequest.TryParse(
            ["seek", "75.5"],
            out var request,
            out var error));

        Assert.Null(error);
        Assert.Equal(TimeSpan.FromSeconds(75.5), request.Position);
        Assert.Equal(TimeSpan.FromSeconds(75.5).Ticks, request.RequestedTicks);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("1,5")]
    [InlineData("1e309")]
    [InlineData("1e308")]
    public void RejectsNegativeNonFiniteOrNonInvariantSeconds(string value)
    {
        Assert.False(SeekProbeRequest.TryParse(
            ["seek", value],
            out _,
            out var error));

        Assert.NotNull(error);
    }

    [Fact]
    public void RejectsWrongArgumentCount()
    {
        Assert.False(SeekProbeRequest.TryParse(["seek"], out _, out var missing));
        Assert.False(SeekProbeRequest.TryParse(["seek", "1", "2"], out _, out var extra));

        Assert.Equal("Usage: seek <seconds>", missing);
        Assert.Equal("Usage: seek <seconds>", extra);
    }

    [Theory]
    [InlineData(10, 10, 20, true)]
    [InlineData(20, 10, 20, true)]
    [InlineData(9.999, 10, 20, false)]
    [InlineData(20.001, 10, 20, false)]
    public void ValidatesInclusiveCurrentTimelineBounds(
        double requestedSeconds,
        double startSeconds,
        double endSeconds,
        bool expected)
    {
        var request = new SeekProbeRequest(TimeSpan.FromSeconds(requestedSeconds));

        var actual = request.TryValidateTimeline(
            TimeSpan.FromSeconds(startSeconds),
            TimeSpan.FromSeconds(endSeconds),
            out var error);

        Assert.Equal(expected, actual);
        Assert.Equal(expected, error is null);
    }

    [Fact]
    public void RejectsAnInvalidTimeline()
    {
        var request = new SeekProbeRequest(TimeSpan.FromSeconds(10));

        Assert.False(request.TryValidateTimeline(
            TimeSpan.FromSeconds(20),
            TimeSpan.FromSeconds(20),
            out var error));

        Assert.Contains("invalid timeline", error, StringComparison.OrdinalIgnoreCase);
    }
}
