using MediaLock.Core.Media;

namespace MediaLock.Core.Tests;

internal enum MediaControlResult
{
    Succeeded,
    Rejected,
    Failed,
}

internal interface IMediaController : IMediaTargetController
{
    ValueTask<MediaControlResult> TryExecuteAsync(
        SessionKey target,
        MediaCommand command,
        CancellationToken cancellationToken);

    async ValueTask<MediaCommandOutcome> IMediaTargetController.TryExecuteAsync(
        MediaTargetId target,
        MediaCommand command,
        CancellationToken cancellationToken)
    {
        var result = await TryExecuteAsync(
            new SessionKey(target.Value),
            command,
            cancellationToken);
        return result switch
        {
            MediaControlResult.Succeeded => MediaCommandOutcome.Succeeded,
            MediaControlResult.Rejected => MediaCommandOutcome.Rejected,
            MediaControlResult.Failed => MediaCommandOutcome.Failed,
            _ => throw new ArgumentOutOfRangeException(nameof(result)),
        };
    }
}
