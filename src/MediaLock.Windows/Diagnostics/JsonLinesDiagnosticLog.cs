using System.Text.Json;
using MediaLock.Core.Diagnostics;

namespace MediaLock.Windows.Diagnostics;

public sealed class JsonLinesDiagnosticLog : IDiagnosticLog, IAsyncDisposable
{
    private const long DefaultMaxFileBytes = 1024 * 1024;
    private const int DefaultMaxFiles = 3;
    private readonly string directoryPath;
    private readonly long maxFileBytes;
    private readonly int maxFiles;
    private readonly TimeProvider timeProvider;
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool disposed;

    public JsonLinesDiagnosticLog()
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MediaLock",
                "logs"),
            DefaultMaxFileBytes,
            DefaultMaxFiles)
    {
    }

    internal JsonLinesDiagnosticLog(
        string directoryPath,
        long maxFileBytes,
        int maxFiles,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFileBytes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFiles, 1);
        this.directoryPath = directoryPath;
        this.maxFileBytes = maxFileBytes;
        this.maxFiles = maxFiles;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async ValueTask WriteAsync(
        DiagnosticEvent diagnosticEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(diagnosticEvent);
        ArgumentException.ThrowIfNullOrWhiteSpace(diagnosticEvent.Name);
        ObjectDisposedException.ThrowIf(disposed, this);
        await gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(directoryPath);
            var line = JsonSerializer.Serialize(new LogLine(
                timeProvider.GetUtcNow(),
                diagnosticEvent.Name,
                diagnosticEvent.Properties));
            var bytes = System.Text.Encoding.UTF8.GetByteCount(line + Environment.NewLine);
            RotateIfNeeded(bytes);
            await File.AppendAllTextAsync(
                CurrentPath,
                line + Environment.NewLine,
                cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        await gate.WaitAsync();
        disposed = true;
        gate.Release();
        gate.Dispose();
    }

    private string CurrentPath => Path.Combine(directoryPath, "medialock.jsonl");

    private void RotateIfNeeded(int incomingBytes)
    {
        if (!File.Exists(CurrentPath) ||
            new FileInfo(CurrentPath).Length + incomingBytes <= maxFileBytes)
        {
            return;
        }

        var oldest = Path.Combine(directoryPath, $"medialock.{maxFiles - 1}.jsonl");
        if (File.Exists(oldest))
        {
            File.Delete(oldest);
        }

        for (var index = maxFiles - 2; index >= 1; index--)
        {
            var source = Path.Combine(directoryPath, $"medialock.{index}.jsonl");
            var destination = Path.Combine(directoryPath, $"medialock.{index + 1}.jsonl");
            if (File.Exists(source))
            {
                File.Move(source, destination);
            }
        }

        if (maxFiles > 1)
        {
            File.Move(CurrentPath, Path.Combine(directoryPath, "medialock.1.jsonl"));
        }
        else
        {
            File.Delete(CurrentPath);
        }
    }

    private sealed record LogLine(
        DateTimeOffset Timestamp,
        string Event,
        IReadOnlyDictionary<string, string>? Properties);
}
