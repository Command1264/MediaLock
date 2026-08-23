using MediaLock.Core.Diagnostics;
using Xunit;

namespace MediaLock.App.Tests;

public sealed class AppDiagnosticTests
{
    [Fact]
    public async Task InputHookStartedDiagnosticFailureIsContained()
    {
        var exception = await Record.ExceptionAsync(async () =>
            await App.TryWriteInputHookStartedDiagnosticAsync(
                new ThrowingDiagnosticLog(),
                enabled: true));

        Assert.Null(exception);
    }

    private sealed class ThrowingDiagnosticLog : IDiagnosticLog
    {
        public ValueTask WriteAsync(
            DiagnosticEvent diagnosticEvent,
            CancellationToken cancellationToken) =>
            ValueTask.FromException(new IOException("diagnostic unavailable"));
    }
}
