using System.Globalization;
using System.Windows.Media.Imaging;
using MediaLock.App.Converters;
using MediaLock.Core.Media;
using Xunit;

namespace MediaLock.App.Tests;

public sealed class MediaArtworkImageConverterTests
{
    [Fact]
    public void ValidArtworkBecomesAFrozenSizeConstrainedImage()
    {
        var encoded = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
        Assert.True(MediaArtwork.TryCreate(encoded, out var artwork));

        var converted = new MediaArtworkImageConverter().Convert(
            artwork,
            typeof(BitmapSource),
            parameter: null,
            CultureInfo.InvariantCulture);

        var image = Assert.IsAssignableFrom<BitmapSource>(converted);
        Assert.True(image.IsFrozen);
        Assert.InRange(image.PixelWidth, 1, 192);
    }

    [Fact]
    public void MalformedArtworkFallsBackWithoutThrowing()
    {
        Assert.True(MediaArtwork.TryCreate(
            [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A],
            out var artwork));

        var converted = new MediaArtworkImageConverter().Convert(
            artwork,
            typeof(BitmapSource),
            parameter: null,
            CultureInfo.InvariantCulture);

        Assert.Null(converted);
    }
}
