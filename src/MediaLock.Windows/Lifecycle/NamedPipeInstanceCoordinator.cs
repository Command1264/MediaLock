using System.IO.Pipes;
using MediaLock.Core.Lifecycle;

namespace MediaLock.Windows.Lifecycle;

public sealed class NamedPipeInstanceCoordinator : IInstanceCoordinator
{
    private readonly string semaphoreName;
    private readonly string pipeName;
    private readonly CancellationTokenSource lifetime = new();
    private Semaphore? instanceSemaphore;
    private Task? listener;
    private bool ownsPermit;
    private bool started;
    private bool disposed;

    public NamedPipeInstanceCoordinator()
        : this("MediaLock.Command1264")
    {
    }

    internal NamedPipeInstanceCoordinator(string instanceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceName);
        semaphoreName = $@"Local\{instanceName}";
        pipeName = instanceName;
    }

    public event EventHandler? ActivationRequested;

    public async ValueTask<InstanceStartResult> StartAsync(
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (started)
        {
            throw new InvalidOperationException("Instance coordinator has already started.");
        }

        started = true;
        instanceSemaphore = new Semaphore(1, 1, semaphoreName);
        ownsPermit = instanceSemaphore.WaitOne(millisecondsTimeout: 0);
        if (ownsPermit)
        {
            listener = ListenAsync(lifetime.Token);
            return InstanceStartResult.Primary;
        }

        using var client = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.Out,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await client.ConnectAsync(TimeSpan.FromSeconds(2), cancellationToken);
        await client.WriteAsync(new byte[] { 1 }, cancellationToken);
        await client.FlushAsync(cancellationToken);
        return InstanceStartResult.ActivatedExisting;
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        await lifetime.CancelAsync();
        if (listener is not null)
        {
            try
            {
                await listener;
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
            {
            }
        }

        if (ownsPermit)
        {
            instanceSemaphore?.Release();
        }

        instanceSemaphore?.Dispose();
        lifetime.Dispose();
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var server = new NamedPipeServerStream(
                pipeName,
                PipeDirection.In,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await server.WaitForConnectionAsync(cancellationToken);
            var buffer = new byte[1];
            if (await server.ReadAsync(buffer, cancellationToken) > 0)
            {
                ActivationRequested?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
