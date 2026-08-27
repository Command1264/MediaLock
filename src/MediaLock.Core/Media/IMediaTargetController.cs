namespace MediaLock.Core.Media;

public interface IMediaTargetController
{
    ValueTask<MediaCommandOutcome> TryExecuteAsync(
        MediaTargetId target,
        MediaCommand command,
        CancellationToken cancellationToken);
}

public enum MediaCommandOutcome
{
    Succeeded,
    Unsupported,
    Rejected,
    Failed,
    OutcomeUnknown,
}
