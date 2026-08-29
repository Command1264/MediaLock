namespace MediaLock.Core.Diagnostics;

public sealed record DiagnosticEvent(
    string Name,
    IReadOnlyDictionary<string, string>? Properties = null,
    string? ProblemCode = null);

public interface IDiagnosticLog
{
    ValueTask WriteAsync(
        DiagnosticEvent diagnosticEvent,
        CancellationToken cancellationToken);
}
