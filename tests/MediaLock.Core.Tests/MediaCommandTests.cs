using MediaLock.Core.Media;
using Xunit;

namespace MediaLock.Core.Tests;

public sealed class MediaCommandTests
{
    [Fact]
    public void AbsoluteSeekCarriesItsValidatedPosition()
    {
        var command = MediaCommand.SeekAbsolute(TimeSpan.FromSeconds(75));

        Assert.Equal(MediaCommandKind.SeekAbsolute, command.Kind);
        Assert.Equal(TimeSpan.FromSeconds(75), command.AbsolutePosition);
    }

    [Fact]
    public void AbsoluteSeekRejectsANegativePosition()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MediaCommand.SeekAbsolute(TimeSpan.FromTicks(-1)));
    }
}
