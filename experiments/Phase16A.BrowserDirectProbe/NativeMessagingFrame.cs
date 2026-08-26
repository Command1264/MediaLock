using System.Buffers.Binary;

namespace MediaLock.Phase16ABrowserDirectProbe;

public static class NativeMessagingFrame
{
    public static async Task<byte[]> ReadAsync(
        Stream input,
        int maximumPayloadBytes,
        CancellationToken cancellationToken)
    {
        return await TryReadAsync(input, maximumPayloadBytes, cancellationToken).ConfigureAwait(false)
            ?? throw new EndOfStreamException("Native Messaging input ended before a frame was available.");
    }

    public static async Task<byte[]?> TryReadAsync(
        Stream input,
        int maximumPayloadBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ValidateMaximum(maximumPayloadBytes);

        var lengthPrefix = new byte[sizeof(uint)];
        var initialRead = await input.ReadAsync(lengthPrefix.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
        if (initialRead == 0)
        {
            return null;
        }

        await ReadExactlyAsync(input, lengthPrefix.AsMemory(1), cancellationToken).ConfigureAwait(false);
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
        await ReadExactlyAsync(input, payload, cancellationToken).ConfigureAwait(false);
        return payload;
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
