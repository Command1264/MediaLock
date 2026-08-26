using MediaLock.Phase16ABrowserDirectProbe;

namespace Phase16A.BrowserDirectProbe.Tests;

public sealed class NativeHostConfigurationTests
{
    private const string ExtensionId = "abcdefghijklmnopabcdefghijklmnop";

    [Fact]
    public void Load_AcceptsOneStrictExtensionIdField()
    {
        using var fixture = ConfigurationFixture.Create($$"""
            { "extensionId": "{{ExtensionId}}" }
            """);

        var configuration = NativeHostConfiguration.Load(fixture.ExecutablePath);

        Assert.Equal(ExtensionId, configuration.ExtensionId);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{ \"extensionId\": \"abc\" }")]
    [InlineData("{ \"extensionId\": \"abcdefghijklmnopabcdefghijklmnop\", \"allowAny\": true }")]
    public void Load_RejectsMissingMalformedOrUnknownConfiguration(string json)
    {
        using var fixture = ConfigurationFixture.Create(json);

        Assert.ThrowsAny<InvalidDataException>(() => NativeHostConfiguration.Load(fixture.ExecutablePath));
    }

    [Fact]
    public void Load_RejectsMissingConfiguration()
    {
        using var fixture = ConfigurationFixture.CreateWithoutConfiguration();

        Assert.Throws<FileNotFoundException>(() => NativeHostConfiguration.Load(fixture.ExecutablePath));
    }

    private sealed class ConfigurationFixture : IDisposable
    {
        private ConfigurationFixture(string directory)
        {
            Directory = directory;
            ExecutablePath = Path.Combine(directory, "probe.exe");
        }

        public string Directory { get; }

        public string ExecutablePath { get; }

        public static ConfigurationFixture Create(string json)
        {
            var fixture = CreateWithoutConfiguration();
            File.WriteAllText(Path.Combine(fixture.Directory, "phase16a-native-host.json"), json);
            return fixture;
        }

        public static ConfigurationFixture CreateWithoutConfiguration()
        {
            var directory = Path.Combine(Path.GetTempPath(), $"MediaLock-Phase16A-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(directory);
            return new ConfigurationFixture(directory);
        }

        public void Dispose()
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
    }
}
