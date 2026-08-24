using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using MediaLock.Application;
using Microsoft.Win32;

namespace MediaLock.Windows;

public sealed class WindowsAppEnvironmentInfoProvider : IAppEnvironmentInfoProvider
{
    private const string CurrentVersionRegistryPath =
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
    private readonly Func<string> getAppVersion;
    private readonly Func<string, string?> readRegistryValue;
    private readonly Func<Architecture> getArchitecture;
    private readonly Func<bool> getIsSigned;

    public WindowsAppEnvironmentInfoProvider()
        : this(
            ReadAppVersion,
            ReadCurrentVersionRegistryValue,
            () => RuntimeInformation.OSArchitecture,
            HasEmbeddedAuthenticodeSignature)
    {
    }

    internal WindowsAppEnvironmentInfoProvider(
        Func<string> getAppVersion,
        Func<string, string?> readRegistryValue,
        Func<Architecture> getArchitecture,
        Func<bool> getIsSigned)
    {
        this.getAppVersion = getAppVersion;
        this.readRegistryValue = readRegistryValue;
        this.getArchitecture = getArchitecture;
        this.getIsSigned = getIsSigned;
    }

    public AppEnvironmentInfo GetCurrent()
    {
        var currentBuild = readRegistryValue("CurrentBuild") ??
            Environment.OSVersion.Version.Build.ToString(CultureInfo.InvariantCulture);
        var buildNumber = int.TryParse(
            currentBuild,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var parsedBuild)
                ? parsedBuild
                : 0;
        var productName = NormalizeProductName(
            readRegistryValue("ProductName") ?? "Windows",
            buildNumber);
        var ubr = readRegistryValue("UBR");
        var fullBuild = string.IsNullOrWhiteSpace(ubr)
            ? currentBuild
            : $"{currentBuild}.{ubr}";

        return new AppEnvironmentInfo(
            NormalizeVersion(getAppVersion()),
            productName,
            readRegistryValue("DisplayVersion") ?? string.Empty,
            fullBuild,
            getArchitecture().ToString(),
            getIsSigned());
    }

    private static string NormalizeProductName(string productName, int buildNumber) =>
        buildNumber >= 22000 &&
        productName.Contains("Windows 10", StringComparison.OrdinalIgnoreCase)
            ? productName.Replace(
                "Windows 10",
                "Windows 11",
                StringComparison.OrdinalIgnoreCase)
            : productName;

    private static string NormalizeVersion(string version)
    {
        var metadataSeparator = version.IndexOf('+', StringComparison.Ordinal);
        return metadataSeparator < 0 ? version : version[..metadataSeparator];
    }

    private static string ReadAppVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        return assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? assembly.GetName().Version?.ToString() ?? "Unknown";
    }

    private static string? ReadCurrentVersionRegistryValue(string name)
    {
        using var key = Registry.LocalMachine.OpenSubKey(CurrentVersionRegistryPath);
        return Convert.ToString(key?.GetValue(name), CultureInfo.InvariantCulture);
    }

    private static bool HasEmbeddedAuthenticodeSignature()
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        try
        {
#pragma warning disable SYSLIB0057
            _ = X509Certificate.CreateFromSignedFile(executablePath);
#pragma warning restore SYSLIB0057
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
