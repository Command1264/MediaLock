namespace MediaLock.Phase16ABrowserDirectProbe;

public static class NativeHostOrigin
{
    private const int ChromeExtensionIdLength = 32;

    public static string Validate(string launchOrigin, string configuredExtensionId)
    {
        if (!IsValidExtensionId(configuredExtensionId))
        {
            throw new InvalidDataException(
                "The configured Extension ID must contain exactly 32 lowercase characters from 'a' through 'p'.");
        }

        var expectedOrigin = $"chrome-extension://{configuredExtensionId}/";
        if (!string.Equals(launchOrigin, expectedOrigin, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("The Native Messaging launch origin is not authorized.");
        }

        return expectedOrigin;
    }

    private static bool IsValidExtensionId(string value)
    {
        if (value.Length != ChromeExtensionIdLength)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (character is < 'a' or > 'p')
            {
                return false;
            }
        }

        return true;
    }
}
