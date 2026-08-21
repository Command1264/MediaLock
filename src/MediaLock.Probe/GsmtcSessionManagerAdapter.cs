using Windows.Media.Control;

namespace MediaLock.Probe;

internal interface IGsmtcSessionManagerFactory
{
    Task<IGsmtcSessionManager> CreateAsync();
}

internal interface IGsmtcSessionManager : IDisposable
{
    event Action SessionsChanged;

    IReadOnlyList<GlobalSystemMediaTransportControlsSession> GetSessions();
}

internal sealed class GsmtcSessionManagerFactory : IGsmtcSessionManagerFactory
{
    public async Task<IGsmtcSessionManager> CreateAsync()
    {
        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        return new GsmtcSessionManagerAdapter(manager);
    }
}

internal sealed class GsmtcSessionManagerAdapter : IGsmtcSessionManager
{
    private readonly GlobalSystemMediaTransportControlsSessionManager manager;
    private bool disposed;

    public GsmtcSessionManagerAdapter(GlobalSystemMediaTransportControlsSessionManager manager)
    {
        this.manager = manager;
        manager.SessionsChanged += OnSessionsChanged;
    }

    public event Action? SessionsChanged;

    public IReadOnlyList<GlobalSystemMediaTransportControlsSession> GetSessions() =>
        manager.GetSessions().ToArray();

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        manager.SessionsChanged -= OnSessionsChanged;
        disposed = true;
    }

    private void OnSessionsChanged(
        GlobalSystemMediaTransportControlsSessionManager sender,
        SessionsChangedEventArgs args) => SessionsChanged?.Invoke();
}
