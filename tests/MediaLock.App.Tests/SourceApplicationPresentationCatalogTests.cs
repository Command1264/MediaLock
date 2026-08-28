using MediaLock.App.Presentation;
using MediaLock.Application;
using Xunit;

namespace MediaLock.App.Tests;

public sealed class SourceApplicationPresentationCatalogTests
{
    [Fact]
    public void ResolveUsesTrustedDisplayAndHostNamesWithoutChangingIdentity()
    {
        var resolver = new FakeSourceApplicationMetadataResolver(new Dictionary<string, SourceApplicationMetadata>
        {
            ["Brave._crx_music"] = new("YouTube Music", "Brave Browser"),
            ["Brave"] = new("Brave"),
        });

        var presentations = SourceApplicationPresentationCatalog.Resolve(
            ["Brave._crx_music", "Brave"],
            resolver);

        Assert.Equal(
            "YouTube Music — Brave Browser",
            presentations["Brave._crx_music"].DisplayName);
        Assert.Equal(
            "Brave._crx_music",
            presentations["Brave._crx_music"].SourceAppUserModelId);
        Assert.Equal("Brave._crx_music", presentations["Brave._crx_music"].Details);
        Assert.Equal("Brave", presentations["Brave"].DisplayName);
    }

    [Fact]
    public void ResolveFallsBackToTheExactRawIdentity()
    {
        var presentations = SourceApplicationPresentationCatalog.Resolve(
            ["Unknown.Source"],
            new FakeSourceApplicationMetadataResolver(
                new Dictionary<string, SourceApplicationMetadata>()));

        var presentation = presentations["Unknown.Source"];
        Assert.Equal("Unknown.Source", presentation.DisplayName);
        Assert.Equal("Unknown.Source", presentation.Details);
    }

    [Fact]
    public void ResolveDisambiguatesDuplicateFriendlyNamesDeterministically()
    {
        var resolver = new FakeSourceApplicationMetadataResolver(new Dictionary<string, SourceApplicationMetadata>
        {
            ["Player.Alpha"] = new("Player"),
            ["Player.Beta"] = new("Player"),
        });

        var presentations = SourceApplicationPresentationCatalog.Resolve(
            ["Player.Beta", "Player.Alpha", "Player.Beta"],
            resolver);

        Assert.Equal(2, presentations.Count);
        Assert.Equal("Player — Player.Alpha", presentations["Player.Alpha"].DisplayName);
        Assert.Equal("Player — Player.Beta", presentations["Player.Beta"].DisplayName);
    }
}
