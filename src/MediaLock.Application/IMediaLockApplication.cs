namespace MediaLock.Application;

public interface IMediaLockApplication : IAsyncDisposable
{
    event EventHandler<MediaLockApplicationStateChangedEventArgs> StateChanged;

    MediaLockApplicationState State { get; }

    string? LastReportedProblemCode => State.Problem?.Code;

    ValueTask StartAsync(CancellationToken cancellationToken);

    ValueTask<ApplicationResult> DispatchAsync(
        ApplicationIntent intent,
        CancellationToken cancellationToken);

    ValueTask ReportProblemAsync(
        MediaLockProblem problem,
        CancellationToken cancellationToken) => ValueTask.CompletedTask;

    ValueTask ReportProblemEventAsync(
        string eventName,
        MediaLockProblem problem,
        CancellationToken cancellationToken) => ReportProblemAsync(problem, cancellationToken);
}
