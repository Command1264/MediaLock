using System.Buffers.Binary;
using System.Text;
using MediaLock.Phase16ABrowserDirectProbe;

namespace Phase16A.BrowserDirectProbe.Tests;

public sealed class NativeMessagingFrameTests
{
    [Fact]
    public async Task ReadAsync_ReturnsOneBoundedUtf8Payload()
    {
        var payload = Encoding.UTF8.GetBytes("{\"type\":\"hello\"}");
        await using var stream = CreateFrame(payload);

        var result = await NativeMessagingFrame.ReadAsync(stream, 64 * 1024, CancellationToken.None);

        Assert.Equal(payload, result);
    }

    [Fact]
    public async Task TryReadAsync_ReturnsNullOnlyForCleanEndOfStream()
    {
        await using var cleanEndOfStream = new MemoryStream();

        var result = await NativeMessagingFrame.TryReadAsync(
            cleanEndOfStream,
            64 * 1024,
            CancellationToken.None);

        Assert.Null(result);

        await using var truncatedPrefix = new MemoryStream([1, 0]);
        await Assert.ThrowsAsync<EndOfStreamException>(
            () => NativeMessagingFrame.TryReadAsync(
                truncatedPrefix,
                64 * 1024,
                CancellationToken.None));
    }

    [Fact]
    public async Task ReadAsync_RejectsLengthBeforeAllocatingOversizedPayload()
    {
        await using var stream = new MemoryStream();
        var prefix = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(prefix, 64 * 1024 + 1);
        await stream.WriteAsync(prefix);
        stream.Position = 0;

        var error = await Assert.ThrowsAsync<InvalidDataException>(
            () => NativeMessagingFrame.ReadAsync(stream, 64 * 1024, CancellationToken.None));

        Assert.Contains("maximum", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(new byte[] { 1, 0 })]
    [InlineData(new byte[] { 5, 0, 0, 0, (byte)'{' })]
    public async Task ReadAsync_RejectsTruncatedFrames(byte[] frame)
    {
        await using var stream = new MemoryStream(frame);

        await Assert.ThrowsAsync<EndOfStreamException>(
            () => NativeMessagingFrame.ReadAsync(stream, 64 * 1024, CancellationToken.None));
    }

    [Fact]
    public async Task WriteAsync_UsesLittleEndianPrefixAndRejectsOversizedPayload()
    {
        await using var stream = new MemoryStream();
        var payload = Encoding.UTF8.GetBytes("{\"ok\":true}");

        await NativeMessagingFrame.WriteAsync(stream, payload, payload.Length, CancellationToken.None);

        var written = stream.ToArray();
        Assert.Equal(payload.Length, BinaryPrimitives.ReadInt32LittleEndian(written.AsSpan(0, sizeof(uint))));
        Assert.Equal(payload, written[sizeof(uint)..]);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => NativeMessagingFrame.WriteAsync(stream, payload, payload.Length - 1, CancellationToken.None));
    }

    private static MemoryStream CreateFrame(byte[] payload)
    {
        var stream = new MemoryStream();
        var prefix = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(prefix, checked((uint)payload.Length));
        stream.Write(prefix);
        stream.Write(payload);
        stream.Position = 0;
        return stream;
    }
}
