using MediaLock.Phase16ABrowserDirectProbe;

namespace Phase16A.BrowserDirectProbe.Tests;

public sealed class NativeHostOriginTests
{
    private const string ExtensionId = "abcdefghijklmnopabcdefghijklmnop";
    private const string ExpectedOrigin = "chrome-extension://abcdefghijklmnopabcdefghijklmnop/";

    [Fact]
    public void Validate_RequiresOneExactConfiguredExtensionOrigin()
    {
        Assert.Equal(ExpectedOrigin, NativeHostOrigin.Validate(ExpectedOrigin, ExtensionId));
    }

    [Theory]
    [InlineData("chrome-extension://ponmlkjihgfedcbaponmlkjihgfedcba/")]
    [InlineData("chrome-extension://abcdefghijklmnopabcdefghijklmnop")]
    [InlineData("chrome-extension://abcdefghijklmnopabcdefghijklmnop/?forged=true")]
    [InlineData("https://music.youtube.com/")]
    public void Validate_RejectsAnyNonExactOrigin(string launchOrigin)
    {
        Assert.Throws<UnauthorizedAccessException>(() => NativeHostOrigin.Validate(launchOrigin, ExtensionId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ123456")]
    [InlineData("abcdefghijklmnopabcdefghijklmn0p")]
    public void Validate_RejectsMalformedConfiguredExtensionIds(string extensionId)
    {
        Assert.Throws<InvalidDataException>(() => NativeHostOrigin.Validate(ExpectedOrigin, extensionId));
    }
}
