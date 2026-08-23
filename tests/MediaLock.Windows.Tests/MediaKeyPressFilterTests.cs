using MediaLock.Core.Media;
using MediaLock.Windows.Input;
using Xunit;

namespace MediaLock.Windows.Tests;

public sealed class MediaKeyPressFilterTests
{
    [Fact]
    public void AcceptedPressConsumesRepeatsAndKeyUpButEmitsOneCommand()
    {
        var filter = new MediaKeyPressFilter();
        var commands = new List<MediaCommand>();

        var firstDown = filter.Process(
            MediaKeyVirtualKeys.PlayPause,
            MediaKeyTransition.KeyDown,
            command =>
            {
                commands.Add(command);
                return true;
            });
        var repeatDown = filter.Process(
            MediaKeyVirtualKeys.PlayPause,
            MediaKeyTransition.KeyDown,
            _ => throw new InvalidOperationException("Repeat must not emit another command."));
        var keyUp = filter.Process(
            MediaKeyVirtualKeys.PlayPause,
            MediaKeyTransition.KeyUp,
            _ => throw new InvalidOperationException("Key-up must not emit another command."));

        Assert.True(firstDown);
        Assert.True(repeatDown);
        Assert.True(keyUp);
        Assert.Equal([MediaCommand.TogglePlayPause], commands);
    }

    [Fact]
    public void RejectedPressPassesRepeatsAndKeyUpThrough()
    {
        var filter = new MediaKeyPressFilter();
        var calls = 0;

        Assert.False(filter.Process(
            MediaKeyVirtualKeys.Next,
            MediaKeyTransition.KeyDown,
            _ =>
            {
                calls++;
                return false;
            }));
        Assert.False(filter.Process(
            MediaKeyVirtualKeys.Next,
            MediaKeyTransition.KeyDown,
            _ => throw new InvalidOperationException("Repeat must reuse the first decision.")));
        Assert.False(filter.Process(
            MediaKeyVirtualKeys.Next,
            MediaKeyTransition.KeyUp,
            _ => throw new InvalidOperationException("Key-up must reuse the first decision.")));

        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(MediaKeyVirtualKeys.PlayPause, MediaCommandKind.TogglePlayPause)]
    [InlineData(MediaKeyVirtualKeys.Next, MediaCommandKind.Next)]
    [InlineData(MediaKeyVirtualKeys.Previous, MediaCommandKind.Previous)]
    [InlineData(MediaKeyVirtualKeys.Stop, MediaCommandKind.Stop)]
    public void SupportedVirtualKeysMapToMediaCommands(uint virtualKey, MediaCommandKind expected)
    {
        var filter = new MediaKeyPressFilter();
        MediaCommand? observed = null;

        var consumed = filter.Process(
            virtualKey,
            MediaKeyTransition.KeyDown,
            command =>
            {
                observed = command;
                return true;
            });

        Assert.True(consumed);
        Assert.Equal(expected, observed!.Value.Kind);
    }

    [Fact]
    public void UnknownVirtualKeyPassesThroughWithoutCallingTheHandler()
    {
        var filter = new MediaKeyPressFilter();

        Assert.False(filter.Process(
            0x41,
            MediaKeyTransition.KeyDown,
            _ => throw new InvalidOperationException("Unknown keys must be ignored.")));
    }

    [Fact]
    public void ANewPressAfterKeyUpMakesANewDecision()
    {
        var filter = new MediaKeyPressFilter();
        var calls = 0;
        bool Handler(MediaCommand _)
        {
            calls++;
            return true;
        }

        Assert.True(filter.Process(
            MediaKeyVirtualKeys.Stop,
            MediaKeyTransition.KeyDown,
            Handler));
        Assert.True(filter.Process(
            MediaKeyVirtualKeys.Stop,
            MediaKeyTransition.KeyUp,
            Handler));
        Assert.True(filter.Process(
            MediaKeyVirtualKeys.Stop,
            MediaKeyTransition.KeyDown,
            Handler));

        Assert.Equal(2, calls);
    }
}
