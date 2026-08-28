using MediaLock.Application;

namespace MediaLock.App.Presentation;

internal enum AppFailureKind
{
    Startup,
    Shutdown,
    MediaInputStartup,
    MediaInputStopped,
}

internal static class AppProblemFactory
{
    public static MediaLockProblem Create(AppFailureKind kind, Exception exception) => kind switch
    {
        AppFailureKind.Startup => MediaLockProblem.Error(
            MediaLockProblemId.StartupFailed,
            exception),
        AppFailureKind.Shutdown => MediaLockProblem.Error(
            MediaLockProblemId.ShutdownFailed,
            exception),
        AppFailureKind.MediaInputStartup => MediaLockProblem.Warning(
            MediaLockProblemId.MediaInputStartupFailed,
            exception),
        AppFailureKind.MediaInputStopped => MediaLockProblem.Warning(
            MediaLockProblemId.MediaInputStopped,
            exception),
        _ => MediaLockProblem.Error(MediaLockProblemId.Unknown, exception),
    };
}
