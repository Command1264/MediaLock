using System.Media;

namespace MediaLock.App;

public interface IPlaybackStateLockFeedback
{
    void PlayOverrideReleasedSound();
}

internal sealed class SystemPlaybackStateLockFeedback : IPlaybackStateLockFeedback
{
    public void PlayOverrideReleasedSound() => SystemSounds.Asterisk.Play();
}
