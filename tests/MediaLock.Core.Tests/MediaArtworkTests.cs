using MediaLock.Core.Media;
using Xunit;

namespace MediaLock.Core.Tests;

public sealed class MediaArtworkTests
{
    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0x01 }, "image/jpeg")]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, "image/png")]
    public void SupportedEncodedArtworkIsCopiedAndClassified(byte[] encoded, string expectedContentType)
    {
        Assert.True(MediaArtwork.TryCreate(encoded, out var artwork));

        encoded[0] = 0;

        Assert.Equal(expectedContentType, artwork!.ContentType);
        Assert.NotEqual(0, artwork.Bytes[0]);
    }

    [Fact]
    public void UnknownOrOversizedArtworkIsRejected()
    {
        Assert.False(MediaArtwork.TryCreate([0x47, 0x49, 0x46], out _));
        Assert.False(MediaArtwork.TryCreate(
            new byte[MediaArtwork.MaximumEncodedByteCount + 1],
            out _));
    }
}
