using System.Runtime.InteropServices;
using MediaLock.Core.Media;
using Windows.Media;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace MediaLock.Windows.Gsmtc;

internal sealed class GsmtcSessionManagerFactory : IGsmtcSessionManagerFactory
{
    public async ValueTask<IGsmtcSessionManager> CreateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return new WindowsGsmtcSessionManager(manager);
    }
}

internal sealed class WindowsGsmtcSessionManager : IGsmtcSessionManager
{
    private readonly GlobalSystemMediaTransportControlsSessionManager manager;
    private readonly Dictionary<GlobalSystemMediaTransportControlsSession, WindowsGsmtcSession> sessions =
        new(ReferenceEqualityComparer.Instance);
    private bool disposed;

    public WindowsGsmtcSessionManager(GlobalSystemMediaTransportControlsSessionManager manager)
    {
        this.manager = manager;
        manager.SessionsChanged += OnSessionsChanged;
    }

    public event EventHandler? SessionsChanged;

    public IReadOnlyList<IGsmtcSession> GetSessions()
    {
        var current = manager.GetSessions().ToArray();
        var present = current.ToHashSet(ReferenceEqualityComparer.Instance);
        foreach (var removed in sessions.Keys.Where(session => !present.Contains(session)).ToArray())
        {
            sessions[removed].Dispose();
            sessions.Remove(removed);
        }

        return current.Select(GetOrCreate).ToArray();
    }

    public IGsmtcSession? GetCurrentSession()
    {
        var current = manager.GetCurrentSession();
        return current is null ? null : GetOrCreate(current);
    }

    public ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return ValueTask.CompletedTask;
        }

        disposed = true;
        manager.SessionsChanged -= OnSessionsChanged;
        foreach (var session in sessions.Values)
        {
            session.Dispose();
        }

        sessions.Clear();
        return ValueTask.CompletedTask;
    }

    private WindowsGsmtcSession GetOrCreate(GlobalSystemMediaTransportControlsSession session)
    {
        if (!sessions.TryGetValue(session, out var adapter))
        {
            adapter = new WindowsGsmtcSession(session);
            sessions.Add(session, adapter);
        }

        return adapter;
    }

    private void OnSessionsChanged(
        GlobalSystemMediaTransportControlsSessionManager sender,
        SessionsChangedEventArgs args) => SessionsChanged?.Invoke(this, EventArgs.Empty);
}

internal sealed class WindowsGsmtcSession : IGsmtcSession, IDisposable
{
    private readonly GlobalSystemMediaTransportControlsSession session;
    private MediaArtwork? artwork;
    private bool artworkDirty = true;
    private bool disposed;

    public WindowsGsmtcSession(GlobalSystemMediaTransportControlsSession session)
    {
        this.session = session;
        session.MediaPropertiesChanged += OnMediaPropertiesChanged;
        session.PlaybackInfoChanged += OnPlaybackInfoChanged;
        session.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
    }

    public event EventHandler? Changed;

    public string SourceAppUserModelId => session.SourceAppUserModelId;

    public async ValueTask<MediaSessionSnapshot> ReadAsync(
        SessionKey key,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var playback = session.GetPlaybackInfo();
        var timeline = session.GetTimelineProperties();
        var properties = await session
            .TryGetMediaPropertiesAsync()
            .AsTask(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (artworkDirty)
        {
            artworkDirty = false;
            artwork = await TryReadArtworkAsync(properties.Thumbnail, cancellationToken);
        }

        return new MediaSessionSnapshot(
            key,
            SourceAppUserModelId,
            MapPlaybackStatus(playback?.PlaybackStatus),
            MapCapabilities(playback?.Controls),
            observedAt,
            Metadata: new MediaMetadata(
                properties.Title,
                properties.Artist,
                properties.AlbumTitle,
                properties.TrackNumber is 0 or > int.MaxValue ? null : (int)properties.TrackNumber),
            Timeline: new MediaTimeline(
                timeline.StartTime,
                timeline.EndTime,
                timeline.Position,
                timeline.LastUpdatedTime),
            PlaybackType: MapPlaybackType(properties.PlaybackType),
            Artwork: artwork);
    }

    public async ValueTask<MediaControlResult> TryExecuteAsync(
        MediaCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var accepted = command switch
        {
            MediaCommand.Play => await session.TryPlayAsync(),
            MediaCommand.Pause => await session.TryPauseAsync(),
            MediaCommand.TogglePlayPause => await session.TryTogglePlayPauseAsync(),
            MediaCommand.Previous => await session.TrySkipPreviousAsync(),
            MediaCommand.Next => await session.TrySkipNextAsync(),
            MediaCommand.Stop => await session.TryStopAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(command)),
        };
        return accepted ? MediaControlResult.Succeeded : MediaControlResult.Rejected;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
        session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
        session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
        disposed = true;
    }

    private static PlaybackStatus MapPlaybackStatus(
        GlobalSystemMediaTransportControlsSessionPlaybackStatus? status) => status switch
        {
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed => PlaybackStatus.Closed,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Opened => PlaybackStatus.Opened,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Changing => PlaybackStatus.Changing,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped => PlaybackStatus.Stopped,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => PlaybackStatus.Playing,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => PlaybackStatus.Paused,
            _ => PlaybackStatus.Unknown,
        };

    private static MediaCommandCapabilities MapCapabilities(
        GlobalSystemMediaTransportControlsSessionPlaybackControls? controls)
    {
        if (controls is null)
        {
            return MediaCommandCapabilities.None;
        }

        var capabilities = MediaCommandCapabilities.None;
        capabilities |= controls.IsPlayEnabled ? MediaCommandCapabilities.Play : 0;
        capabilities |= controls.IsPauseEnabled ? MediaCommandCapabilities.Pause : 0;
        capabilities |= controls.IsPlayPauseToggleEnabled ? MediaCommandCapabilities.TogglePlayPause : 0;
        capabilities |= controls.IsPreviousEnabled ? MediaCommandCapabilities.Previous : 0;
        capabilities |= controls.IsNextEnabled ? MediaCommandCapabilities.Next : 0;
        capabilities |= controls.IsStopEnabled ? MediaCommandCapabilities.Stop : 0;
        return capabilities;
    }

    private static MediaLock.Core.Media.MediaPlaybackType MapPlaybackType(
        global::Windows.Media.MediaPlaybackType? playbackType) => playbackType switch
        {
            global::Windows.Media.MediaPlaybackType.Music => MediaLock.Core.Media.MediaPlaybackType.Music,
            global::Windows.Media.MediaPlaybackType.Video => MediaLock.Core.Media.MediaPlaybackType.Video,
            global::Windows.Media.MediaPlaybackType.Image => MediaLock.Core.Media.MediaPlaybackType.Image,
            _ => MediaLock.Core.Media.MediaPlaybackType.Unknown,
        };

    private static async ValueTask<MediaArtwork?> TryReadArtworkAsync(
        IRandomAccessStreamReference? reference,
        CancellationToken cancellationToken)
    {
        if (reference is null)
        {
            return null;
        }

        try
        {
            using var stream = await reference.OpenReadAsync().AsTask(cancellationToken);
            if (stream.Size is 0 or > MediaArtwork.MaximumEncodedByteCount)
            {
                return null;
            }

            var byteCount = checked((uint)stream.Size);
            using var input = stream.GetInputStreamAt(0);
            using var reader = new DataReader(input);
            reader.InputStreamOptions = InputStreamOptions.None;
            var loaded = await reader.LoadAsync(byteCount).AsTask(cancellationToken);
            if (loaded != byteCount)
            {
                return null;
            }

            var bytes = new byte[byteCount];
            reader.ReadBytes(bytes);
            return MediaArtwork.TryCreate(bytes, out var result) ? result : null;
        }
        catch (Exception exception) when (
            !cancellationToken.IsCancellationRequested &&
            exception is COMException or IOException or UnauthorizedAccessException)
        {
            // Known external thumbnail-stream failures degrade to absent presentation data.
            // Unexpected failures propagate into the adapter's observable recovery path.
            return null;
        }
    }

    private void OnMediaPropertiesChanged(
        GlobalSystemMediaTransportControlsSession sender,
        MediaPropertiesChangedEventArgs args)
    {
        artworkDirty = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnPlaybackInfoChanged(
        GlobalSystemMediaTransportControlsSession sender,
        PlaybackInfoChangedEventArgs args) => Changed?.Invoke(this, EventArgs.Empty);

    private void OnTimelinePropertiesChanged(
        GlobalSystemMediaTransportControlsSession sender,
        TimelinePropertiesChangedEventArgs args) => Changed?.Invoke(this, EventArgs.Empty);
}
