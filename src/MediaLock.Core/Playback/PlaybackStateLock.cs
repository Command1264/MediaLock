using MediaLock.Core.Media;

namespace MediaLock.Core.Playback;

public enum PlaybackStateLockMode
{
    Off,
    KeepPlaying,
}

public enum PlaybackStateCorrection
{
    None,
    Play,
}

public static class PlaybackStateLockRules
{
    public static bool CanArm(PlaybackStatus observedStatus) =>
        observedStatus == PlaybackStatus.Playing;

    public static PlaybackStateCorrection Decide(
        PlaybackStateLockMode mode,
        PlaybackStatus observedStatus) =>
        mode == PlaybackStateLockMode.KeepPlaying &&
        observedStatus == PlaybackStatus.Paused
            ? PlaybackStateCorrection.Play
            : PlaybackStateCorrection.None;
}
