using System.Text.Json;
using MediaLock.BrowserHost;
using Xunit;

namespace MediaLock.Browser.Tests;

public sealed class BrowserHostConfigurationTests
{
    [Fact]
    public void FixedExtensionAndPipeConfigurationIsAccepted()
    {
        using var fixture = new ConfigurationFixture(
            BrowserMediaAdapterOptions.ProductionExtensionId,
            BrowserMediaBridgeServer.DefaultPipeName);

        var configuration = BrowserHostConfiguration.Load(fixture.ExecutablePath);

        configuration.ValidateLaunchOrigin(
            $"chrome-extension://{BrowserMediaAdapterOptions.ProductionExtensionId}/");
    }

    [Theory]
    [InlineData("abcdefghijklmnopabcdefghijklmnop", "Command1264.MediaLock.Browser.v1")]
    [InlineData("kggfkkiifnclhhmibdglkbdfbacakemn", "foreign-pipe")]
    public void ConfiguredIdentityCannotRedirectTheHost(string extensionId, string pipeName)
    {
        using var fixture = new ConfigurationFixture(extensionId, pipeName);

        Assert.Throws<InvalidDataException>(() =>
            BrowserHostConfiguration.Load(fixture.ExecutablePath));
    }

    private sealed class ConfigurationFixture : IDisposable
    {
        private readonly string directory = Path.Combine(
            Path.GetTempPath(),
            $"MediaLock-BrowserHostTests-{Guid.NewGuid():N}");

        public ConfigurationFixture(string extensionId, string pipeName)
        {
            Directory.CreateDirectory(directory);
            ExecutablePath = Path.Combine(directory, "MediaLock.BrowserHost.exe");
            File.WriteAllText(ExecutablePath, string.Empty);
            File.WriteAllText(
                Path.Combine(directory, "browser-host.json"),
                JsonSerializer.Serialize(new { extensionId, pipeName }));
        }

        public string ExecutablePath { get; }

        public void Dispose() => Directory.Delete(directory, recursive: true);
    }
}
