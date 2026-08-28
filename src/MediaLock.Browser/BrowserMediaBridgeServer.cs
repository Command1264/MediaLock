using System.IO.Pipes;
using System.Diagnostics;

namespace MediaLock.Browser;

public sealed class BrowserMediaBridgeServer : IAsyncDisposable
{
    public const string DefaultPipeName = "Command1264.MediaLock.Browser.v1";

    private readonly BrowserMediaAdapter adapter;
    private readonly string pipeName;
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim connectionSlots = new(8, 8);
    private readonly object workersGate = new();
    private readonly HashSet<Task> workers = [];
    private Task? acceptWorker;
    private int disposed;

    public BrowserMediaBridgeServer(
        BrowserMediaAdapter adapter,
        string pipeName = DefaultPipeName)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        if (pipeName.Length > 128)
        {
            throw new ArgumentException("The Browser bridge pipe name is too long.", nameof(pipeName));
        }

        this.adapter = adapter;
        this.pipeName = pipeName;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (Interlocked.CompareExchange(ref acceptWorker, Task.CompletedTask, null) is not null)
        {
            throw new InvalidOperationException("The Browser bridge server has already started.");
        }

        acceptWorker = AcceptConnectionsAsync(lifetime.Token);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        await lifetime.CancelAsync();
        if (acceptWorker is { } accept)
        {
            try
            {
                await accept;
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
            {
                Trace.TraceInformation("Browser bridge listener stopped.");
            }
        }

        Task[] active;
        lock (workersGate)
        {
            active = workers.ToArray();
        }
        try
        {
            await Task.WhenAll(active);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            Trace.TraceInformation("Browser bridge connections stopped.");
        }
        finally
        {
            connectionSlots.Dispose();
            lifetime.Dispose();
        }
    }

    private async Task AcceptConnectionsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await connectionSlots.WaitAsync(cancellationToken);
            var pipe = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                8,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try
            {
                await pipe.WaitForConnectionAsync(cancellationToken);
            }
            catch
            {
                await pipe.DisposeAsync();
                connectionSlots.Release();
                throw;
            }

            var worker = RunConnectionAsync(pipe, cancellationToken);
            lock (workersGate)
            {
                workers.Add(worker);
            }
            _ = worker.ContinueWith(
                completed =>
                {
                    lock (workersGate)
                    {
                        workers.Remove(completed);
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private async Task RunConnectionAsync(
        NamedPipeServerStream pipe,
        CancellationToken cancellationToken)
    {
        await using (pipe)
        {
            try
            {
                await adapter.RunConnectionAsync(pipe, cancellationToken);
            }
            catch (Exception exception) when (exception is
                InvalidDataException or UnauthorizedAccessException or IOException)
            {
                Trace.TraceWarning(
                    "Browser integration connection was rejected: {0}",
                    exception.GetType().Name);
            }
            finally
            {
                connectionSlots.Release();
            }
        }
    }
}
