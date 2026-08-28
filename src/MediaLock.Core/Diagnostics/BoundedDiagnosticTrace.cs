using System.Diagnostics;

namespace MediaLock.Core.Diagnostics;

public static class BoundedDiagnosticTrace
{
    public static void WriteFailure(
        string eventName,
        Exception exception,
        string? problemCode = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        ArgumentNullException.ThrowIfNull(exception);
        var code = string.IsNullOrWhiteSpace(problemCode)
            ? string.Empty
            : $" ProblemCode={problemCode}.";
        Trace.TraceError(
            "{0} failed.{1} ExceptionType={2}",
            eventName,
            code,
            exception.GetType().FullName ?? exception.GetType().Name);
    }
}
