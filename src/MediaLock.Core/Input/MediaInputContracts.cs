using MediaLock.Core.Media;

namespace MediaLock.Core.Input;

public delegate bool MediaInputHandler(MediaCommand command);

public interface IMediaInputSource : IAsyncDisposable
{
    event EventHandler<MediaInputSourceFaultedEventArgs>? Faulted;

    bool IsRunning { get; }

    ValueTask StartAsync(MediaInputHandler handler, CancellationToken cancellationToken);

    void Stop();
}

public sealed class MediaInputSourceFaultedEventArgs(Exception exception) : EventArgs
{
    public Exception Exception { get; } = exception;
}
