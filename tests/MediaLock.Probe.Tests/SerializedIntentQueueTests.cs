using MediaLock.Probe;

namespace MediaLock.Probe.Tests;

public sealed class SerializedIntentQueueTests
{
    [Fact]
    public async Task PostedIntentsRunInOrderWithoutOverlap()
    {
        await using var queue = new SerializedIntentQueue();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sequence = new List<int>();
        var running = 0;
        var maximumRunning = 0;

        Assert.True(queue.TryPost(async () =>
        {
            maximumRunning = Math.Max(maximumRunning, Interlocked.Increment(ref running));
            sequence.Add(1);
            firstEntered.SetResult();
            await releaseFirst.Task;
            sequence.Add(2);
            Interlocked.Decrement(ref running);
        }));

        Assert.True(queue.TryPost(() =>
        {
            maximumRunning = Math.Max(maximumRunning, Interlocked.Increment(ref running));
            sequence.Add(3);
            Interlocked.Decrement(ref running);
            return ValueTask.CompletedTask;
        }));

        await firstEntered.Task;
        Assert.Equal([1], sequence);

        releaseFirst.SetResult();
        await queue.InvokeAsync(() =>
        {
            sequence.Add(4);
            return ValueTask.CompletedTask;
        });

        Assert.Equal([1, 2, 3, 4], sequence);
        Assert.Equal(1, maximumRunning);
    }
}
