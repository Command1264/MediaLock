using System.Buffers.Binary;

namespace MediaLock.Browser;

public static class BrowserNativeMessageFrame
{
    public const int MaximumPayloadBytes = 64 * 1024;
    public static readonly TimeSpan DefaultCompletionTimeout = TimeSpan.FromSeconds(5);

    public static async ValueTask<byte[]> ReadAsync(
        Stream stream,
        CancellationToken cancellationToken,
        TimeSpan? completionTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var timeout = completionTimeout ?? DefaultCompletionTimeout;
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromSeconds(30))
        {
            throw new ArgumentOutOfRangeException(nameof(completionTimeout));
        }

        var lengthBytes = new byte[sizeof(uint)];
        await ReadExactlyAsync(stream, lengthBytes.AsMemory(0, 1), cancellationToken);
        using var frameCompletion = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        frameCompletion.CancelAfter(timeout);
        try
        {
            await ReadExactlyAsync(stream, lengthBytes.AsMemory(1), frameCompletion.Token);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidDataException("Browser Native Messaging frame did not complete in time.", exception);
        }
        var length = BinaryPrimitives.ReadUInt32LittleEndian(lengthBytes);
        if (length == 0 || length > MaximumPayloadBytes)
        {
            throw new InvalidDataException(
                $"Browser Native Messaging payload length must be from 1 through {MaximumPayloadBytes} bytes.");
        }

        var payload = new byte[length];
        try
        {
            await ReadExactlyAsync(stream, payload, frameCompletion.Token);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidDataException("Browser Native Messaging frame did not complete in time.", exception);
        }
        return payload;
    }

    public static async ValueTask WriteAsync(
        Stream stream,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (payload.IsEmpty || payload.Length > MaximumPayloadBytes)
        {
            throw new InvalidDataException(
                $"Browser Native Messaging payload length must be from 1 through {MaximumPayloadBytes} bytes.");
        }

        var lengthBytes = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(lengthBytes, (uint)payload.Length);
        await stream.WriteAsync(lengthBytes, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async ValueTask ReadExactlyAsync(
        Stream stream,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[offset..], cancellationToken);
            if (read == 0)
            {
                throw new EndOfStreamException("Browser Native Messaging connection closed mid-frame.");
            }

            offset += read;
        }
    }
}
