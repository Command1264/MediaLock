using System.Threading.Channels;
using MediaLock.Probe;

namespace MediaLock.Probe.Tests;

public sealed class ProbeApplicationTests
{
    [Fact]
    public async Task PhysicalInputWaitsForEarlierApplicationIntent()
    {
        await using var intentQueue = new SerializedIntentQueue();
        var earlierIntentStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseEarlierIntent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Assert.True(intentQueue.TryPost(async () =>
        {
            earlierIntentStarted.SetResult();
            await releaseEarlierIntent.Task;
        }));
        await earlierIntentStarted.Task;

        var inputs = Channel.CreateUnbounded<MediaKeyInput>();
        await inputs.Writer.WriteAsync(new MediaKeyInput(MediaKeyCommand.Next, null, "test pass-through"));
        inputs.Writer.Complete();

        var processing = ProbeApplication.ProcessInputsAsync(inputs.Reader, intentQueue);

        await Task.Delay(50);
        Assert.False(processing.IsCompleted);

        releaseEarlierIntent.SetResult();
        await processing;
    }
}
