using System.Buffers.Binary;
using Xunit;

namespace MediaLock.Browser.Tests;

public sealed class BrowserNativeMessageFrameTests
{
    [Fact]
    public async Task OversizedLengthIsRejectedBeforePayloadAllocation()
    {
        var header = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(
            header,
            BrowserNativeMessageFrame.MaximumPayloadBytes + 1u);
        using var stream = new MemoryStream(header);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await BrowserNativeMessageFrame.ReadAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task PartialFrameMustCompleteWithinTheBoundedDeadline()
    {
        await using var stream = new OneByteThenWaitStream();

        var exception = await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await BrowserNativeMessageFrame.ReadAsync(
                stream,
                CancellationToken.None,
                TimeSpan.FromMilliseconds(20)));

        Assert.Contains("complete in time", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abcdefghijklmnopabcdefghijklmnop")]
    [InlineData("KGGFKKIIFNCLHHMIBDGLKBDFBACAKEMN")]
    public void AdapterRequiresAnExactFixedChromiumExtensionIdentity(string extensionId)
    {
        Assert.Throws<ArgumentException>(() => new BrowserMediaAdapterOptions(extensionId));
    }

    private sealed class OneByteThenWaitStream : Stream
    {
        private bool firstRead = true;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (firstRead)
            {
                firstRead = false;
                buffer.Span[0] = 1;
                return ValueTask.FromResult(1);
            }

            return new ValueTask<int>(WaitForCancellationAsync(cancellationToken));
        }

        private static async Task<int> WaitForCancellationAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
