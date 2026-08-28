using MediaLock.Application;

namespace MediaLock.App.Presentation;

internal static class AppProblemFactory
{
    public static MediaLockProblem Startup(Exception exception) =>
        MediaLockProblem.Error(MediaLockProblemId.StartupFailed, exception);

    public static MediaLockProblem Shutdown(Exception exception) =>
        MediaLockProblem.Error(MediaLockProblemId.ShutdownFailed, exception);

    public static MediaLockProblem MediaInputStartup(Exception exception) =>
        MediaLockProblem.Warning(MediaLockProblemId.MediaInputStartupFailed, exception);

    public static MediaLockProblem MediaInputStopped(Exception exception) =>
        MediaLockProblem.Warning(MediaLockProblemId.MediaInputStopped, exception);
}
