namespace MediaLock.Application;

public sealed record AppEnvironmentInfo(
    string AppVersion,
    string WindowsProductName,
    string WindowsDisplayVersion,
    string WindowsBuild,
    string Architecture,
    bool IsSigned)
{
    public bool IsPrerelease => AppVersion.Contains('-', StringComparison.Ordinal);
}

public interface IAppEnvironmentInfoProvider
{
    AppEnvironmentInfo GetCurrent();
}
