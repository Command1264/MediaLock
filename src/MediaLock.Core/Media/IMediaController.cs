namespace MediaLock.Core.Media;

public interface IMediaController
{
    ValueTask<MediaControlResult> TryExecuteAsync(
        SessionKey target,
        MediaCommand command,
        CancellationToken cancellationToken);
}

public enum MediaControlResult
{
    Succeeded,
    Rejected,
    Failed,
}
