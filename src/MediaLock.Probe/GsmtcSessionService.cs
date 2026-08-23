using Windows.Media.Control;

namespace MediaLock.Probe;

internal sealed class GsmtcSessionService : IDisposable
{
    private static readonly TimeSpan RecoveryWindow = TimeSpan.FromSeconds(2);

    private readonly Lock sync = new();
    private readonly SerializedIntentQueue intentQueue;
    private readonly IGsmtcSessionManagerFactory managerFactory;
    private readonly ISystemLifecycle lifecycle;
    private readonly TimeProvider timeProvider;
    private readonly TransientSessionSelection<GlobalSystemMediaTransportControlsSession> selection;
    private IGsmtcSessionManager? manager;
    private IReadOnlyList<GlobalSystemMediaTransportControlsSession> sessions = [];
    private IReadOnlySet<MediaKeyCommand> routableCommands = new HashSet<MediaKeyCommand>();
    private CancellationTokenSource? recoveryExpiration;
    private bool lifecycleSubscribed;
    private bool disposed;

    internal GsmtcSessionService(
        SerializedIntentQueue intentQueue,
        IGsmtcSessionManagerFactory managerFactory,
        ISystemLifecycle lifecycle,
        TimeProvider timeProvider)
    {
        this.intentQueue = intentQueue;
        this.managerFactory = managerFactory;
        this.lifecycle = lifecycle;
        this.timeProvider = timeProvider;
        selection = new(
            session => session.SourceAppUserModelId,
            RecoveryWindow);
    }

    public event Action<string>? StateChanged;

    public async Task InitializeAsync()
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        SubscribeLifecycle();
        await AcquireManagerAsync();
    }

    public async Task ReacquireAsync()
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        ReleaseManager();
        StateChanged?.Invoke("Reacquiring GSMTC manager after system resume.");

        try
        {
            await AcquireManagerAsync();
        }
        catch (Exception exception)
        {
            StateChanged?.Invoke($"GSMTC manager remains unavailable after resume: {exception.Message}");
        }
    }

    public IReadOnlyList<GlobalSystemMediaTransportControlsSession> Refresh()
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        IReadOnlyList<GlobalSystemMediaTransportControlsSession> snapshot;
        string? selectionMessage;
        lock (sync)
        {
            sessions = manager?.GetSessions().ToArray() ?? [];
            selectionMessage = ObserveSessionsLocked();
            snapshot = sessions;
        }

        if (selectionMessage is not null)
        {
            StateChanged?.Invoke(selectionMessage);
        }

        return snapshot;
    }

    public bool Select(int zeroBasedIndex, out string message)
    {
        lock (sync)
        {
            if (zeroBasedIndex < 0 || zeroBasedIndex >= sessions.Count)
            {
                message = $"Session index must be between 1 and {sessions.Count}.";
                return false;
            }

            CancelRecoveryExpirationLocked();
            DetachCurrentSessionEventsLocked();

            var selected = sessions[zeroBasedIndex];
            selection.Select(selected);
            AttachSessionEventsLocked(selected);
            RefreshRoutableCommandsLocked();
            message = $"Selected [{zeroBasedIndex + 1}] {selected.SourceAppUserModelId}.";
            return true;
        }
    }

    public void ClearSelection()
    {
        lock (sync)
        {
            CancelRecoveryExpirationLocked();
            DetachCurrentSessionEventsLocked();
            selection.Clear();
            routableCommands = new HashSet<MediaKeyCommand>();
        }

        StateChanged?.Invoke("Selection cleared.");
    }

    public bool TryPrepareRoute(MediaKeyCommand command, out PreparedMediaRoute? route, out string reason)
    {
        lock (sync)
        {
            var selected = selection.Current;
            if (selected is null)
            {
                route = null;
                reason = selection.Status switch
                {
                    TransientSelectionStatus.Recovering => "selected session is recovering",
                    TransientSelectionStatus.Unavailable => "locked target is unavailable",
                    _ => "no selected session",
                };
                return false;
            }

            if (!routableCommands.Contains(command))
            {
                route = null;
                reason = $"{command} is disabled by the selected session";
                return false;
            }

            route = new PreparedMediaRoute(command, selected);
            reason = "routable";
            return true;
        }
    }

    public static async Task<(bool Success, string Message)> RouteAsync(PreparedMediaRoute route)
    {
        var target = route.Target;

        try
        {
            var success = route.Command switch
            {
                MediaKeyCommand.PlayPause => await target.TryTogglePlayPauseAsync(),
                MediaKeyCommand.Next => await target.TrySkipNextAsync(),
                MediaKeyCommand.Previous => await target.TrySkipPreviousAsync(),
                MediaKeyCommand.Stop => await target.TryStopAsync(),
                _ => false,
            };

            return (success, $"{route.Command} -> {target.SourceAppUserModelId}: {(success ? "accepted" : "rejected")}");
        }
        catch (Exception exception)
        {
            return (false, $"{route.Command} -> {target.SourceAppUserModelId}: {exception.Message}");
        }
    }

    public async Task<(bool Success, string Message)> ExecuteAsync(string operation)
    {
        GlobalSystemMediaTransportControlsSession? target;
        TransientSelectionStatus selectionStatus;
        lock (sync)
        {
            target = selection.Current;
            selectionStatus = selection.Status;
        }

        if (target is null)
        {
            var reason = selectionStatus switch
            {
                TransientSelectionStatus.Recovering => "selected session is recovering",
                TransientSelectionStatus.Unavailable => "locked target is unavailable",
                _ => "no selected session",
            };
            return (false, $"Command skipped: {reason}.");
        }

        try
        {
            var success = operation switch
            {
                "play" => await target.TryPlayAsync(),
                "pause" => await target.TryPauseAsync(),
                "toggle" => await target.TryTogglePlayPauseAsync(),
                "next" => await target.TrySkipNextAsync(),
                "previous" => await target.TrySkipPreviousAsync(),
                "stop" => await target.TryStopAsync(),
                _ => false,
            };

            return (success, $"{operation} -> {target.SourceAppUserModelId}: {(success ? "accepted" : "rejected")}");
        }
        catch (Exception exception)
        {
            return (false, $"{operation} -> {target.SourceAppUserModelId}: {exception.Message}");
        }
    }

    public async Task<(bool Success, string Message)> ExecuteSeekAsync(SeekProbeRequest request)
    {
        GlobalSystemMediaTransportControlsSession? target;
        TransientSelectionStatus selectionStatus;
        lock (sync)
        {
            target = selection.Current;
            selectionStatus = selection.Status;
        }

        if (target is null)
        {
            var reason = selectionStatus switch
            {
                TransientSelectionStatus.Recovering => "selected session is recovering",
                TransientSelectionStatus.Unavailable => "locked target is unavailable",
                _ => "no selected session",
            };
            return (false, $"seek skipped: {reason}.");
        }

        try
        {
            var source = target.SourceAppUserModelId;
            var controls = target.GetPlaybackInfo()?.Controls;
            if (controls?.IsPlaybackPositionEnabled is not true)
            {
                return (false, $"seek -> {source}: skipped; playback-position capability is disabled.");
            }

            var before = target.GetTimelineProperties();
            if (!request.TryValidateTimeline(before.StartTime, before.EndTime, out var error))
            {
                return (false, $"seek -> {source}: skipped; {error}");
            }

            var accepted = await target.TryChangePlaybackPositionAsync(request.RequestedTicks);
            var observed = target.GetTimelineProperties().Position;
            var observedDelta = observed - request.Position;

            return (
                accepted,
                $"seek -> {source}: capability=enabled; API={(accepted ? "accepted" : "rejected")}; " +
                $"requested={request.Position:c}; before={before.Position:c}; " +
                $"observed={observed:c}; observed-delta={observedDelta:c}");
        }
        catch (Exception exception)
        {
            return (false, $"seek -> {target.SourceAppUserModelId}: {exception.Message}");
        }
    }

    public async Task PrintSessionsAsync()
    {
        var snapshot = Refresh();
        if (snapshot.Count == 0)
        {
            ConsoleLog.Info("No GSMTC sessions found. Start media playback and run 'refresh'.");
            return;
        }

        GlobalSystemMediaTransportControlsSession? selectedSnapshot;
        lock (sync)
        {
            selectedSnapshot = selection.Current;
        }

        for (var index = 0; index < snapshot.Count; index++)
        {
            var session = snapshot[index];
            var marker = ReferenceEquals(session, selectedSnapshot) ? "*" : " ";
            var playback = session.GetPlaybackInfo();
            var timeline = session.GetTimelineProperties();

            string title = "";
            string artist = "";
            try
            {
                var media = await session.TryGetMediaPropertiesAsync();
                title = media.Title;
                artist = media.Artist;
            }
            catch (Exception exception)
            {
                title = $"<unavailable: {exception.Message}>";
            }

            ConsoleLog.Info($"{marker}[{index + 1}] {session.SourceAppUserModelId}");
            ConsoleLog.Info($"    media={title} | artist={artist} | status={playback?.PlaybackStatus}");
            ConsoleLog.Info($"    timeline={timeline.Position:c} / {timeline.EndTime:c} | {DescribeControls(playback?.Controls)}");
        }
    }

    public void PrintSelectedStatus()
    {
        lock (sync)
        {
            var selected = selection.Current;
            if (selected is null)
            {
                var status = selection.Status switch
                {
                    TransientSelectionStatus.Recovering => $"recovering {selection.SelectedSource}; commands pass through",
                    TransientSelectionStatus.Unavailable => $"unavailable {selection.SelectedSource}; commands pass through",
                    _ => "none",
                };
                ConsoleLog.Info($"Selected session: {status}");
                return;
            }

            var playback = selected.GetPlaybackInfo();
            ConsoleLog.Info($"Selected session: {selected.SourceAppUserModelId}; status={playback?.PlaybackStatus}; {DescribeControls(playback?.Controls)}");
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        lock (sync)
        {
            CancelRecoveryExpirationLocked();
            DetachCurrentSessionEventsLocked();
            selection.Clear();
            ReleaseManagerLocked();
            sessions = [];
            disposed = true;
        }

        if (lifecycleSubscribed)
        {
            lifecycle.Suspending -= OnSuspending;
            lifecycle.Resumed -= OnResumed;
            lifecycleSubscribed = false;
        }
    }

    private static string DescribeControls(GlobalSystemMediaTransportControlsSessionPlaybackControls? controls)
    {
        if (controls is null)
        {
            return "controls=unavailable";
        }

        return $"controls(toggle={controls.IsPlayPauseToggleEnabled}, next={controls.IsNextEnabled}, previous={controls.IsPreviousEnabled}, stop={controls.IsStopEnabled}, seek={controls.IsPlaybackPositionEnabled})";
    }

    private void SubscribeLifecycle()
    {
        if (lifecycleSubscribed)
        {
            return;
        }

        lifecycle.Suspending += OnSuspending;
        lifecycle.Resumed += OnResumed;
        lifecycleSubscribed = true;
    }

    private async Task AcquireManagerAsync()
    {
        var acquiredManager = await managerFactory.CreateAsync();
        try
        {
            acquiredManager.SessionsChanged += OnSessionsChanged;
            var acquiredSessions = acquiredManager.GetSessions();
            string message;
            lock (sync)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                manager = acquiredManager;
                sessions = acquiredSessions;
                message = ObserveSessionsLocked() ?? $"GSMTC manager ready; {sessions.Count} session(s) discovered.";
            }

            StateChanged?.Invoke(message);
        }
        catch
        {
            acquiredManager.SessionsChanged -= OnSessionsChanged;
            acquiredManager.Dispose();
            throw;
        }
    }

    private void ReleaseManager()
    {
        string? selectionMessage;
        lock (sync)
        {
            ReleaseManagerLocked();
            sessions = [];
            selectionMessage = ObserveSessionsLocked();
        }

        StateChanged?.Invoke(selectionMessage ?? "GSMTC catalog is unavailable; waiting for system resume.");
    }

    private void ReleaseManagerLocked()
    {
        if (manager is null)
        {
            return;
        }

        manager.SessionsChanged -= OnSessionsChanged;
        manager.Dispose();
        manager = null;
    }

    private void OnSuspending()
    {
        _ = intentQueue.TryPost(() =>
        {
            if (!disposed)
            {
                ReleaseManager();
            }

            return ValueTask.CompletedTask;
        });
    }

    private void OnResumed()
    {
        _ = intentQueue.TryPost(async () =>
        {
            if (!disposed)
            {
                await ReacquireAsync();
            }
        });
    }

    private void OnSessionsChanged()
    {
        _ = intentQueue.TryPost(() =>
        {
            string message;
            lock (sync)
            {
                sessions = manager?.GetSessions() ?? [];
                message = ObserveSessionsLocked() ?? $"Sessions changed; {sessions.Count} session(s) available.";
            }

            StateChanged?.Invoke(message);
            return ValueTask.CompletedTask;
        });
    }

    private void OnSelectedMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
    {
        _ = intentQueue.TryPost(() =>
        {
            lock (sync)
            {
                if (!ReferenceEquals(sender, selection.Current))
                {
                    return ValueTask.CompletedTask;
                }
            }

            StateChanged?.Invoke($"Selected media changed: {sender.SourceAppUserModelId}.");
            return ValueTask.CompletedTask;
        });
    }

    private void OnSelectedPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
    {
        _ = intentQueue.TryPost(() =>
        {
            GlobalSystemMediaTransportControlsSessionPlaybackStatus? status;
            lock (sync)
            {
                if (!ReferenceEquals(sender, selection.Current))
                {
                    return ValueTask.CompletedTask;
                }

                status = sender.GetPlaybackInfo()?.PlaybackStatus;
                RefreshRoutableCommandsLocked();
            }

            StateChanged?.Invoke($"Selected playback changed: {status}.");
            return ValueTask.CompletedTask;
        });
    }

    private void OnSelectedTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
    {
        _ = intentQueue.TryPost(() =>
        {
            TimeSpan position;
            lock (sync)
            {
                if (!ReferenceEquals(sender, selection.Current))
                {
                    return ValueTask.CompletedTask;
                }

                position = sender.GetTimelineProperties().Position;
            }

            StateChanged?.Invoke($"Selected timeline changed: {position:c}.");
            return ValueTask.CompletedTask;
        });
    }

    private string? ObserveSessionsLocked()
    {
        var previousStatus = selection.Status;
        var previousSession = selection.Current;
        var selectedSource = selection.SelectedSource;

        selection.Observe(sessions, timeProvider.GetUtcNow());

        var currentSession = selection.Current;
        if (!ReferenceEquals(previousSession, currentSession))
        {
            if (previousSession is not null)
            {
                DetachSessionEventsLocked(previousSession);
            }

            if (currentSession is not null)
            {
                AttachSessionEventsLocked(currentSession);
                RefreshRoutableCommandsLocked();
            }
            else
            {
                routableCommands = new HashSet<MediaKeyCommand>();
            }
        }

        if (previousStatus is not TransientSelectionStatus.Recovering &&
            selection.Status is TransientSelectionStatus.Recovering)
        {
            StartRecoveryExpirationLocked();
            return $"Selected session '{selectedSource}' was lost temporarily; recovering for up to {RecoveryWindow.TotalSeconds:0} seconds.";
        }

        if (previousStatus is TransientSelectionStatus.Recovering &&
            selection.Status is TransientSelectionStatus.Selected)
        {
            CancelRecoveryExpirationLocked();
            return $"Selected session '{selection.SelectedSource}' recovered.";
        }

        if (previousStatus is TransientSelectionStatus.Recovering &&
            selection.Status is TransientSelectionStatus.Unavailable)
        {
            CancelRecoveryExpirationLocked();
            return $"Recovery window expired for locked target '{selectedSource}'. Target remains unavailable.";
        }

        if (previousStatus is TransientSelectionStatus.Unavailable &&
            selection.Status is TransientSelectionStatus.Selected)
        {
            return $"Locked target '{selection.SelectedSource}' became available and was reselected.";
        }

        if (previousStatus is TransientSelectionStatus.Selected &&
            selection.Status is TransientSelectionStatus.Selected &&
            !ReferenceEquals(previousSession, currentSession))
        {
            return $"Selected session '{selection.SelectedSource}' replaced and recovered.";
        }

        return null;
    }

    private void StartRecoveryExpirationLocked()
    {
        CancelRecoveryExpirationLocked();
        var cancellation = new CancellationTokenSource();
        recoveryExpiration = cancellation;
        _ = ExpireRecoveryAsync(cancellation);
    }

    private async Task ExpireRecoveryAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(RecoveryWindow, timeProvider, cancellation.Token);
            if (!intentQueue.TryPost(() =>
            {
                try
                {
                    string? message = null;
                    lock (sync)
                    {
                        if (!disposed && ReferenceEquals(recoveryExpiration, cancellation))
                        {
                            recoveryExpiration = null;
                            var selectedSource = selection.SelectedSource;
                            if (selection.Expire(timeProvider.GetUtcNow()))
                            {
                                routableCommands = new HashSet<MediaKeyCommand>();
                                message = $"Recovery window expired for locked target '{selectedSource}'. Target remains unavailable.";
                            }
                        }
                    }

                    if (message is not null)
                    {
                        StateChanged?.Invoke(message);
                    }
                }
                finally
                {
                    cancellation.Dispose();
                }

                return ValueTask.CompletedTask;
            }))
            {
                cancellation.Dispose();
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            cancellation.Dispose();
        }
    }

    private void CancelRecoveryExpirationLocked()
    {
        var cancellation = recoveryExpiration;
        recoveryExpiration = null;
        cancellation?.Cancel();
    }

    private void AttachSessionEventsLocked(GlobalSystemMediaTransportControlsSession session)
    {
        session.MediaPropertiesChanged += OnSelectedMediaPropertiesChanged;
        session.PlaybackInfoChanged += OnSelectedPlaybackInfoChanged;
        session.TimelinePropertiesChanged += OnSelectedTimelinePropertiesChanged;
    }

    private void DetachCurrentSessionEventsLocked()
    {
        if (selection.Current is { } selected)
        {
            DetachSessionEventsLocked(selected);
        }
    }

    private void DetachSessionEventsLocked(GlobalSystemMediaTransportControlsSession session)
    {
        session.MediaPropertiesChanged -= OnSelectedMediaPropertiesChanged;
        session.PlaybackInfoChanged -= OnSelectedPlaybackInfoChanged;
        session.TimelinePropertiesChanged -= OnSelectedTimelinePropertiesChanged;
    }

    private void RefreshRoutableCommandsLocked()
    {
        var controls = selection.Current?.GetPlaybackInfo()?.Controls;
        var commands = new HashSet<MediaKeyCommand>();
        if (controls?.IsPlayPauseToggleEnabled is true)
        {
            commands.Add(MediaKeyCommand.PlayPause);
        }

        if (controls?.IsNextEnabled is true)
        {
            commands.Add(MediaKeyCommand.Next);
        }

        if (controls?.IsPreviousEnabled is true)
        {
            commands.Add(MediaKeyCommand.Previous);
        }

        if (controls?.IsStopEnabled is true)
        {
            commands.Add(MediaKeyCommand.Stop);
        }

        routableCommands = commands;
    }
}
