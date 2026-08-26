using System.Text.Json;

namespace MediaLock.Phase16ABrowserDirectProbe;

internal static class NativeHostCommandDelay
{
    public static async Task ApplyAsync(
        ReadOnlyMemory<byte> response,
        int delayMilliseconds,
        Func<TimeSpan, CancellationToken, Task> delayAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delayAsync);
        if (delayMilliseconds is < 0 or > 10000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(delayMilliseconds),
                "The command response delay must be from 0 through 10000 milliseconds.");
        }
        if (delayMilliseconds == 0)
        {
            return;
        }

        using var document = JsonDocument.Parse(response);
        if (!document.RootElement.TryGetProperty("type", out var type)
            || type.ValueKind != JsonValueKind.String
            || !string.Equals(type.GetString(), "command", StringComparison.Ordinal))
        {
            return;
        }

        await delayAsync(
            TimeSpan.FromMilliseconds(delayMilliseconds),
            cancellationToken).ConfigureAwait(false);
    }
}
