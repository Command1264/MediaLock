using System.Text.Json;
using MediaLock.Phase16ABrowserDirectProbe;

namespace Phase16A.BrowserDirectProbe.Tests;

public sealed class NativeHostCommandDelayTests
{
    [Fact]
    public async Task ApplyAsync_DelaysOnlyCommandResponses()
    {
        var observedDelays = new List<TimeSpan>();
        var command = JsonSerializer.SerializeToUtf8Bytes(new { type = "command" });
        var hello = JsonSerializer.SerializeToUtf8Bytes(new { type = "helloAck" });

        await NativeHostCommandDelay.ApplyAsync(
            hello,
            3000,
            (delay, _) =>
            {
                observedDelays.Add(delay);
                return Task.CompletedTask;
            },
            CancellationToken.None);
        await NativeHostCommandDelay.ApplyAsync(
            command,
            3000,
            (delay, _) =>
            {
                observedDelays.Add(delay);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal([TimeSpan.FromSeconds(3)], observedDelays);
    }

    [Fact]
    public async Task ApplyAsync_SkipsAZeroDelay()
    {
        var delayCount = 0;
        var command = JsonSerializer.SerializeToUtf8Bytes(new { type = "command" });

        await NativeHostCommandDelay.ApplyAsync(
            command,
            0,
            (_, _) =>
            {
                delayCount++;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal(0, delayCount);
    }
}
