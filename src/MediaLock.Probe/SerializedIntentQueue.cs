using System.Threading.Channels;

namespace MediaLock.Probe;

internal sealed class SerializedIntentQueue : IAsyncDisposable
{
    private readonly Channel<Func<ValueTask>> channel;
    private readonly Task worker;

    public SerializedIntentQueue()
    {
        channel = Channel.CreateUnbounded<Func<ValueTask>>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
        worker = ProcessAsync();
    }

    public event Action<Exception>? UnhandledException;

    public bool TryPost(Func<ValueTask> intent) => channel.Writer.TryWrite(intent);

    public Task InvokeAsync(Action intent) => InvokeAsync(() =>
    {
        intent();
        return ValueTask.CompletedTask;
    });

    public Task InvokeAsync(Func<Task> intent) => InvokeAsync(() => new ValueTask(intent()));

    public async Task InvokeAsync(Func<ValueTask> intent)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!TryPost(async () =>
            {
                try
                {
                    await intent();
                    completion.SetResult();
                }
                catch (Exception exception)
                {
                    completion.SetException(exception);
                }
            }))
        {
            throw new InvalidOperationException("The serialized intent queue is no longer accepting work.");
        }

        await completion.Task;
    }

    public Task<TResult> InvokeAsync<TResult>(Func<Task<TResult>> intent) =>
        InvokeAsync(() => new ValueTask<TResult>(intent()));

    public async Task<TResult> InvokeAsync<TResult>(Func<ValueTask<TResult>> intent)
    {
        var completion = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!TryPost(async () =>
            {
                try
                {
                    completion.SetResult(await intent());
                }
                catch (Exception exception)
                {
                    completion.SetException(exception);
                }
            }))
        {
            throw new InvalidOperationException("The serialized intent queue is no longer accepting work.");
        }

        return await completion.Task;
    }

    public async ValueTask DisposeAsync()
    {
        channel.Writer.TryComplete();
        await worker;
    }

    private async Task ProcessAsync()
    {
        await foreach (var intent in channel.Reader.ReadAllAsync())
        {
            try
            {
                await intent();
            }
            catch (Exception exception)
            {
                UnhandledException?.Invoke(exception);
            }
        }
    }
}
