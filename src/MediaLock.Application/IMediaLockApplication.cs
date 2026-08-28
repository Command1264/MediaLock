namespace MediaLock.Application;

public interface IMediaLockApplication : IAsyncDisposable
{
    event EventHandler<MediaLockApplicationStateChangedEventArgs> StateChanged;

    MediaLockApplicationState State { get; }

    string? LastReportedProblemCode { get; }

    ValueTask StartAsync(CancellationToken cancellationToken);

    ValueTask<ApplicationResult> DispatchAsync(
        ApplicationIntent intent,
        CancellationToken cancellationToken);

    ValueTask ReportProblemAsync(
        MediaLockProblem problem,
        CancellationToken cancellationToken);
}
