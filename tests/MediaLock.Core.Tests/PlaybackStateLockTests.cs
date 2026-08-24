using MediaLock.Core.Media;
using MediaLock.Core.Playback;

namespace MediaLock.Core.Tests;

public sealed class PlaybackStateLockTests
{
    [Fact]
    public void KeepPlayingCorrectsPausedPlaybackWithPlay()
    {
        var correction = PlaybackStateLockRules.Decide(
            PlaybackStateLockMode.KeepPlaying,
            PlaybackStatus.Paused);

        Assert.Equal(PlaybackStateCorrection.Play, correction);
    }

    [Fact]
    public void KeepPlayingCanOnlyArmWhilePlaybackIsPlaying()
    {
        Assert.True(PlaybackStateLockRules.CanArm(PlaybackStatus.Playing));
        Assert.False(PlaybackStateLockRules.CanArm(PlaybackStatus.Paused));
    }

    [Theory]
    [InlineData(PlaybackStateLockMode.Off, PlaybackStatus.Playing)]
    [InlineData(PlaybackStateLockMode.Off, PlaybackStatus.Paused)]
    [InlineData(PlaybackStateLockMode.KeepPlaying, PlaybackStatus.Playing)]
    [InlineData(PlaybackStateLockMode.KeepPlaying, PlaybackStatus.Stopped)]
    [InlineData(PlaybackStateLockMode.KeepPlaying, PlaybackStatus.Closed)]
    [InlineData(PlaybackStateLockMode.KeepPlaying, PlaybackStatus.Unknown)]
    public void OtherPolicyAndPlaybackCombinationsDoNotCorrect(
        PlaybackStateLockMode mode,
        PlaybackStatus status)
    {
        Assert.Equal(
            PlaybackStateCorrection.None,
            PlaybackStateLockRules.Decide(mode, status));
    }
}
