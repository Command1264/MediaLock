using MediaLock.Core.Media;
using MediaLock.Core.Configuration;
using MediaLock.Core.Routing;

namespace MediaLock.Application;

public abstract record ApplicationIntent
{
    private ApplicationIntent()
    {
    }

    public sealed record LockSession(SessionKey Session) : ApplicationIntent;

    public sealed record LockApplication(string SourceAppUserModelId) : ApplicationIntent;

    public sealed record UsePriorityRules : ApplicationIntent;

    public sealed record UseWindowsAuto : ApplicationIntent;

    public sealed record Route(MediaCommand Command) : ApplicationIntent;

    public sealed record UpdateSettings(MediaLockSettings Settings) : ApplicationIntent;
}

public sealed record ApplicationResult(
    MediaLockApplicationState State,
    RouteDecision Decision);
