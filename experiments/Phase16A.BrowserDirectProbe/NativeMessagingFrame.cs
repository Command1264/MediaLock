using System.Buffers.Binary;

namespace MediaLock.Phase16ABrowserDirectProbe;

public static class NativeMessagingFrame
{
    private static readonly TimeSpan DefaultFrameCompletionTimeout = TimeSpan.FromSeconds(5);

    public static async Task<byte[]> ReadAsync(
        Stream input,
        int maximumPayloadBytes,
        CancellationToken cancellationToken)
    {
        return await TryReadAsync(
            input,
            maximumPayloadBytes,
            DefaultFrameCompletionTimeout,
            cancellationToken).ConfigureAwait(false)
            ?? throw new EndOfStreamException("Native Messaging input ended before a frame was available.");
    }

    public static async Task<byte[]?> TryReadAsync(
        Stream input,
        int maximumPayloadBytes,
        CancellationToken cancellationToken)
    {
        return await TryReadAsync(
            input,
            maximumPayloadBytes,
            DefaultFrameCompletionTimeout,
            cancellationToken).ConfigureAwait(false);
    }

    public static async Task<byte[]?> TryReadAsync(
        Stream input,
        int maximumPayloadBytes,
        TimeSpan frameCompletionTimeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ValidateMaximum(maximumPayloadBytes);
        if (frameCompletionTimeout <= TimeSpan.Zero || frameCompletionTimeout > TimeSpan.FromMinutes(1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(frameCompletionTimeout),
                frameCompletionTimeout,
                "The frame completion timeout must be positive and no greater than one minute.");
        }

        var lengthPrefix = new byte[sizeof(uint)];
        var initialRead = await input.ReadAsync(lengthPrefix.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
        if (initialRead == 0)
        {
            return null;
        }

        using var completion = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        completion.CancelAfter(frameCompletionTimeout);
        try
        {
            await ReadExactlyAsync(input, lengthPrefix.AsMemory(1), completion.Token).ConfigureAwait(false);
            var payloadLength = BinaryPrimitives.ReadUInt32LittleEndian(lengthPrefix);
            if (payloadLength == 0)
            {
                throw new InvalidDataException("Native Messaging payloads must not be empty.");
            }

            if (payloadLength > maximumPayloadBytes)
            {
                throw new InvalidDataException(
                    $"Native Messaging payload exceeds the configured maximum of {maximumPayloadBytes} bytes.");
            }

            var payload = new byte[checked((int)payloadLength)];
            await ReadExactlyAsync(input, payload, completion.Token).ConfigureAwait(false);
            return payload;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                "Native Messaging frame did not complete within the configured deadline.",
                exception);
        }
    }

    public static async Task WriteAsync(
        Stream output,
        ReadOnlyMemory<byte> payload,
        int maximumPayloadBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(output);
        ValidateMaximum(maximumPayloadBytes);
        if (payload.IsEmpty)
        {
            throw new InvalidDataException("Native Messaging payloads must not be empty.");
        }

        if (payload.Length > maximumPayloadBytes)
        {
            throw new InvalidDataException(
                $"Native Messaging payload exceeds the configured maximum of {maximumPayloadBytes} bytes.");
        }

        var lengthPrefix = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(lengthPrefix, checked((uint)payload.Length));
        await output.WriteAsync(lengthPrefix, cancellationToken).ConfigureAwait(false);
        await output.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ReadExactlyAsync(
        Stream input,
        Memory<byte> destination,
        CancellationToken cancellationToken)
    {
        var consumed = 0;
        while (consumed < destination.Length)
        {
            var read = await input.ReadAsync(destination[consumed..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException("Native Messaging frame ended before the declared payload was read.");
            }

            consumed += read;
        }
    }

    private static void ValidateMaximum(int maximumPayloadBytes)
    {
        if (maximumPayloadBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumPayloadBytes),
                maximumPayloadBytes,
                "The maximum payload size must be positive.");
        }
    }
}
