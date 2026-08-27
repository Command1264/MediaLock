using MediaLock.Core.Media;

namespace MediaLock.Windows.Gsmtc;

internal enum MediaControlResult
{
    Succeeded,
    Rejected,
    Failed,
}

internal interface IGsmtcSessionManagerFactory
{
    ValueTask<IGsmtcSessionManager> CreateAsync(CancellationToken cancellationToken);
}

internal interface IGsmtcSessionManager : IAsyncDisposable
{
    event EventHandler SessionsChanged;

    IReadOnlyList<IGsmtcSession> GetSessions();

    IGsmtcSession? GetCurrentSession();
}

internal interface IGsmtcSession
{
    event EventHandler Changed;

    string SourceAppUserModelId { get; }

    ValueTask<MediaSessionSnapshot> ReadAsync(
        SessionKey key,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken);

    ValueTask<MediaControlResult> TryExecuteAsync(
        MediaCommand command,
        CancellationToken cancellationToken);
}
