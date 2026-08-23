using System.Threading.Channels;
using MediaLock.Core.Diagnostics;
using MediaLock.Core.Input;
using MediaLock.Core.Media;
using MediaLock.Core.Routing;

namespace MediaLock.Application;

public sealed class MediaInputCoordinator : IAsyncDisposable
{
    private readonly IMediaLockApplication application;
    private readonly IMediaInputSource source;
    private readonly IDiagnosticLog? diagnosticLog;
    private readonly Channel<PendingInput> inputs;
    private readonly CancellationTokenSource lifetime = new();
    private Task? worker;
    private long nextSequence;
    private bool disposed;

    public MediaInputCoordinator(
        IMediaLockApplication application,
        IMediaInputSource source,
        IDiagnosticLog? diagnosticLog = null,
        int capacity = 128)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        this.application = application;
        this.source = source;
        this.diagnosticLog = diagnosticLog;
        source.Faulted += OnSourceFaulted;
        inputs = Channel.CreateBounded<PendingInput>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false,
        });
    }

    public event EventHandler<MediaInputSourceFaultedEventArgs>? Faulted;

    public bool IsRunning => source.IsRunning;

    public async ValueTask StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (worker is not null)
        {
            throw new InvalidOperationException("The media input coordinator has already started.");
        }

        worker = ProcessInputsAsync();
        try
        {
            await source.StartAsync(TryAccept, cancellationToken);
        }
        catch
        {
            inputs.Writer.TryComplete();
            await worker;
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        var failures = new List<Exception>();
        try
        {
            source.Stop();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        inputs.Writer.TryComplete();
        await lifetime.CancelAsync();
        try
        {
            if (worker is not null)
            {
                await worker;
            }
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        source.Faulted -= OnSourceFaulted;
        try
        {
            await source.DisposeAsync();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        lifetime.Dispose();
        if (failures.Count > 0)
        {
            throw new AggregateException("Media input shutdown failed.", failures);
        }
    }

    private bool TryAccept(MediaCommand command)
    {
        if (disposed || application.State.Settings.Desktop?.InterceptMediaKeys != true)
        {
            return false;
        }

        var router = application.State.Router;
        if (router.Mode is RoutingMode.SessionLock or RoutingMode.AppLock &&
            router.Status is RouterStatus.Recovering or RouterStatus.Unavailable)
        {
            return false;
        }

        var targetKey = router.ActiveTarget;
        if (targetKey is null)
        {
            return false;
        }

        var target = router.Sessions.FirstOrDefault(session => session.Key == targetKey.Value);
        return target is not null &&
            target.Capabilities.Supports(command) &&
            inputs.Writer.TryWrite(new PendingInput(
                Interlocked.Increment(ref nextSequence),
                command,
                target.Key));
    }

    private async Task ProcessInputsAsync()
    {
        try
        {
            await foreach (var input in inputs.Reader.ReadAllAsync(lifetime.Token))
            {
                try
                {
                    await TryWriteAcceptedDiagnosticAsync(input, lifetime.Token);
                    await application.DispatchAsync(
                        new ApplicationIntent.Route(input.Command, input.ExpectedTarget),
                        lifetime.Token);
                }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    NotifyFaulted(exception);
                }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
    }

    private async ValueTask TryWriteAcceptedDiagnosticAsync(
        PendingInput input,
        CancellationToken cancellationToken)
    {
        if (diagnosticLog is null)
        {
            return;
        }

        try
        {
            await diagnosticLog.WriteAsync(
                new DiagnosticEvent(
                    "input.accepted",
                    new Dictionary<string, string>
                    {
                        ["sequence"] = input.Sequence.ToString(
                            System.Globalization.CultureInfo.InvariantCulture),
                        ["command"] = input.Command.ToString(),
                        ["target"] = input.ExpectedTarget.Value,
                    }),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.TraceError(exception.ToString());
        }
    }

    private void OnSourceFaulted(object? sender, MediaInputSourceFaultedEventArgs args) =>
        NotifyFaulted(args.Exception);

    private void NotifyFaulted(Exception exception)
    {
        var subscribers = Faulted;
        if (subscribers is null)
        {
            System.Diagnostics.Trace.TraceError(exception.ToString());
            return;
        }

        foreach (EventHandler<MediaInputSourceFaultedEventArgs> subscriber in
            subscribers.GetInvocationList())
        {
            try
            {
                subscriber(this, new MediaInputSourceFaultedEventArgs(exception));
            }
            catch (Exception subscriberException)
            {
                System.Diagnostics.Trace.TraceError(subscriberException.ToString());
            }
        }
    }

    private sealed record PendingInput(
        long Sequence,
        MediaCommand Command,
        SessionKey ExpectedTarget);
}
