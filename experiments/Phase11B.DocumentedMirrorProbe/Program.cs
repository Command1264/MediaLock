using System.Runtime.InteropServices;
using MediaLock.Application;
using MediaLock.Core.Media;
using MediaLock.Core.Routing;
using MediaLock.Windows.Gsmtc;
using MediaLock.Windows.Lifecycle;
using Windows.Media;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace MediaLock.Phase11BMirrorProbe;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        System.Windows.Forms.Application.Run(new ProbeForm());
    }
}

internal sealed class ProbeForm : Form
{
    private readonly TextBox output = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Both,
        WordWrap = false,
        Font = new Font(FontFamily.GenericMonospace, 9),
    };
    private readonly ComboBox sessions = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 300,
    };
    private readonly Button lockButton = new() { Text = "Lock selected Session", AutoSize = true };
    private readonly Button windowsAutoButton = new() { Text = "Windows Auto", AutoSize = true };
    private readonly Button mirrorButton = new() { Text = "Enable mirror", AutoSize = true, Enabled = false };
    private readonly Button inspectButton = new() { Text = "Inspect Windows surface", AutoSize = true };
    private readonly Button requestToggleButton = new()
    {
        Text = "Request Current Play/Pause once",
        AutoSize = true,
    };
    private readonly Button copyButton = new() { Text = "Copy evidence", AutoSize = true };
    private readonly Button exitButton = new() { Text = "Disable and exit", AutoSize = true };

    private readonly CancellationTokenSource lifetime = new();
    private readonly string ownSourceApplicationId =
        Path.GetFileName(Environment.ProcessPath) ?? "MediaLock.Phase11BMirrorProbe.exe";
    private SystemLifecycle? systemLifecycle;
    private MediaLockApplication? mediaApplication;
    private DocumentedMirror? mirror;
    private bool shutdownStarted;

    public ProbeForm()
    {
        Text = "Media Lock Phase 11B — DOCUMENTED SMTC MIRROR PROBE";
        Width = 1080;
        Height = 700;
        StartPosition = FormStartPosition.CenterScreen;

        var heading = new Label
        {
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Text = "SUPPORTED SMTC PUBLICATION — SELECTION AND ORDER ARE OBSERVATIONS, NOT CONTRACTS",
            Padding = new Padding(8),
        };
        var controls = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true,
            Padding = new Padding(8),
        };
        controls.Controls.AddRange([
            sessions,
            lockButton,
            windowsAutoButton,
            mirrorButton,
            inspectButton,
            requestToggleButton,
            copyButton,
            exitButton,
        ]);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(heading, 0, 0);
        layout.Controls.Add(output, 0, 1);
        layout.Controls.Add(controls, 0, 2);
        Controls.Add(layout);

        Shown += OnShown;
        FormClosing += OnFormClosing;
        lockButton.Click += async (_, _) => await LockSelectedAsync();
        windowsAutoButton.Click += async (_, _) => await UseWindowsAutoAsync();
        mirrorButton.Click += (_, _) => ToggleMirror();
        inspectButton.Click += async (_, _) => await InspectWindowsSurfaceAsync("manual inspection");
        requestToggleButton.Click += async (_, _) => await RequestCurrentToggleOnceAsync();
        copyButton.Click += (_, _) => Clipboard.SetText(output.Text);
        exitButton.Click += (_, _) => Close();
    }

    private async void OnShown(object? sender, EventArgs args)
    {
        try
        {
            Log($"Timestamp: {DateTimeOffset.Now:O}");
            Log($"Windows: {RuntimeInformation.OSDescription}");
            Log($"Architecture: {RuntimeInformation.OSArchitecture}");
            Log($"Process: {Environment.ProcessId}; HWND: 0x{Handle.ToInt64():X}");
            Log($"Owned Source application ID excluded from catalog: {ownSourceApplicationId}");

            systemLifecycle = new SystemLifecycle();
            var adapter = new GsmtcMediaAdapter(systemLifecycle, [ownSourceApplicationId]);
            var router = new MediaRouter(adapter);
            mediaApplication = new MediaLockApplication(adapter, router);
            mediaApplication.StateChanged += OnApplicationStateChanged;
            await mediaApplication.StartAsync(lifetime.Token);

            mirror = new DocumentedMirror(
                Handle,
                mediaApplication,
                SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext(),
                Log);
            ApplyState(mediaApplication.State);
            await InspectWindowsSurfaceAsync("probe initialized; mirror disabled");
        }
        catch (Exception exception)
        {
            LogException("Probe startup failed", exception);
        }
    }

    private void OnApplicationStateChanged(
        object? sender,
        MediaLockApplicationStateChangedEventArgs args)
    {
        if (IsDisposed)
        {
            return;
        }

        BeginInvoke(() => ApplyState(args.State));
    }

    private void ApplyState(MediaLockApplicationState state)
    {
        var selected = sessions.SelectedItem as SessionChoice;
        var preferredKey = state.Router.Mode is RoutingMode.SessionLock or RoutingMode.AppLock
            ? state.Router.ActiveTarget
            : selected?.Key;
        sessions.BeginUpdate();
        sessions.Items.Clear();
        foreach (var session in state.Router.Sessions)
        {
            sessions.Items.Add(new SessionChoice(
                session.Key,
                session.SourceAppUserModelId,
                $"{session.SourceAppUserModelId} — {session.Metadata?.Title ?? "<no title>"}"));
        }

        sessions.SelectedItem = sessions.Items
            .Cast<SessionChoice>()
            .FirstOrDefault(item => item.Key == preferredKey) ??
            sessions.Items.Cast<SessionChoice>().FirstOrDefault(item =>
                string.Equals(item.SourceApplicationId, selected?.SourceApplicationId, StringComparison.Ordinal)) ??
            sessions.Items.Cast<SessionChoice>().FirstOrDefault();
        sessions.EndUpdate();

        var active = ResolveActiveSession(state);
        mirrorButton.Enabled = active is not null;
        mirror?.Update(state, active);
        Log($"STATE revision={state.Router.Revision}; mode={state.Router.Mode}; status={state.Router.Status}; " +
            $"active={active?.SourceAppUserModelId ?? "<none>"}; sessions={state.Router.Sessions.Length}");
    }

    private async Task LockSelectedAsync()
    {
        if (mediaApplication is null || sessions.SelectedItem is not SessionChoice selected)
        {
            return;
        }

        await mediaApplication.DispatchAsync(
            new ApplicationIntent.LockSession(selected.Key),
            lifetime.Token);
    }

    private async Task UseWindowsAutoAsync()
    {
        if (mediaApplication is null)
        {
            return;
        }

        mirror?.Disable();
        mirrorButton.Text = "Enable mirror";
        await mediaApplication.DispatchAsync(
            new ApplicationIntent.UseWindowsAutoForCurrentRun(),
            lifetime.Token);
    }

    private void ToggleMirror()
    {
        if (mirror is null || mediaApplication is null)
        {
            return;
        }

        if (mirror.IsEnabled)
        {
            mirror.Disable();
            mirrorButton.Text = "Enable mirror";
        }
        else
        {
            var state = mediaApplication.State;
            var active = ResolveActiveSession(state);
            if (active is null)
            {
                return;
            }

            mirror.Enable(state, active);
            mirrorButton.Text = "Disable mirror";
        }
    }

    private async Task InspectWindowsSurfaceAsync(string reason)
    {
        Log(string.Empty);
        Log($"=== {reason} ===");
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var current = manager.GetCurrentSession();
            Log($"Public GSMTC Current: {current?.SourceAppUserModelId ?? "<none>"}");
            if (current is not null)
            {
                var properties = await current.TryGetMediaPropertiesAsync();
                var playback = current.GetPlaybackInfo();
                var timeline = current.GetTimelineProperties();
                Log($"Current metadata: title={properties.Title}; artist={properties.Artist}; " +
                    $"album={properties.AlbumTitle}; type={properties.PlaybackType}");
                Log($"Current playback: status={playback?.PlaybackStatus}; " +
                    $"play={playback?.Controls.IsPlayEnabled}; pause={playback?.Controls.IsPauseEnabled}; " +
                    $"previous={playback?.Controls.IsPreviousEnabled}; next={playback?.Controls.IsNextEnabled}; " +
                    $"stop={playback?.Controls.IsStopEnabled}; seek={playback?.Controls.IsPlaybackPositionEnabled}");
                Log($"Current timeline: {timeline.Position} / {timeline.EndTime}; " +
                    $"updated={timeline.LastUpdatedTime:O}");
            }

            var available = manager.GetSessions();
            for (var index = 0; index < available.Count; index++)
            {
                Log($"Public GSMTC [{index}]: {available[index].SourceAppUserModelId}");
            }
        }
        catch (Exception exception)
        {
            LogException("Public GSMTC inspection failed", exception);
        }
    }

    private async Task RequestCurrentToggleOnceAsync()
    {
        Log(string.Empty);
        Log("=== ONE public Current Session Play/Pause request ===");
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var current = manager.GetCurrentSession();
            if (current is null)
            {
                Log("Public GSMTC request skipped: there is no Current Session.");
                return;
            }

            Log($"Request target before call: {current.SourceAppUserModelId}");
            var accepted = await current.TryTogglePlayPauseAsync();
            Log($"Public TryTogglePlayPauseAsync returned: {accepted}");
            await Task.Delay(750);
            await InspectWindowsSurfaceAsync("after one public Current Session request");
        }
        catch (Exception exception)
        {
            LogException("Public Current Session request failed", exception);
        }
    }

    private async void OnFormClosing(object? sender, FormClosingEventArgs args)
    {
        if (shutdownStarted)
        {
            return;
        }

        shutdownStarted = true;
        mirror?.Dispose();
        mirror = null;
        if (mediaApplication is not null)
        {
            mediaApplication.StateChanged -= OnApplicationStateChanged;
            await mediaApplication.DisposeAsync();
            mediaApplication = null;
        }

        systemLifecycle?.Dispose();
        systemLifecycle = null;
        lifetime.Cancel();
        lifetime.Dispose();
    }

    private static MediaSessionSnapshot? ResolveActiveSession(MediaLockApplicationState state) =>
        state.Router.ActiveTarget is { } key
            ? state.Router.Sessions.FirstOrDefault(session => session.Key == key)
            : null;

    private void Log(string message) => output.AppendText(message + Environment.NewLine);

    private void LogException(string message, Exception exception) =>
        Log($"{message}: {exception.GetType().FullName}: {exception.Message} (0x{exception.HResult:X8})");

    private sealed record SessionChoice(
        SessionKey Key,
        string SourceApplicationId,
        string Label)
    {
        public override string ToString() => Label;
    }
}

internal sealed class DocumentedMirror : IDisposable
{
    private readonly MediaLockApplication application;
    private readonly SynchronizationContext uiContext;
    private readonly Action<string> log;
    private readonly SystemMediaTransportControls controls;
    private readonly SemaphoreSlim requestGate = new(1, 1);
    private InMemoryRandomAccessStream? artworkStream;
    private bool disposed;
    private long requestSequence;

    public DocumentedMirror(
        nint windowHandle,
        MediaLockApplication application,
        SynchronizationContext uiContext,
        Action<string> log)
    {
        this.application = application;
        this.uiContext = uiContext;
        this.log = log;
        controls = SystemMediaTransportControlsInterop.GetForWindow(windowHandle);
        controls.ButtonPressed += OnButtonPressed;
        controls.PlaybackPositionChangeRequested += OnPlaybackPositionChangeRequested;
    }

    public bool IsEnabled { get; private set; }

    public void Enable(MediaLockApplicationState state, MediaSessionSnapshot target)
    {
        IsEnabled = true;
        Update(state, target);
        log($"MIRROR enabled for capture target {target.Key} ({target.SourceAppUserModelId}).");
    }

    public void Disable()
    {
        if (disposed)
        {
            return;
        }

        IsEnabled = false;
        controls.PlaybackStatus = MediaPlaybackStatus.Closed;
        controls.DisplayUpdater.ClearAll();
        controls.DisplayUpdater.Update();
        controls.IsEnabled = false;
        artworkStream?.Dispose();
        artworkStream = null;
        log("MIRROR disabled and cleared.");
    }

    public void Update(MediaLockApplicationState state, MediaSessionSnapshot? target)
    {
        if (!IsEnabled || target is null)
        {
            return;
        }

        controls.IsPlayEnabled = target.Capabilities.HasFlag(MediaCommandCapabilities.Play);
        controls.IsPauseEnabled = target.Capabilities.HasFlag(MediaCommandCapabilities.Pause);
        controls.IsPreviousEnabled = target.Capabilities.HasFlag(MediaCommandCapabilities.Previous);
        controls.IsNextEnabled = target.Capabilities.HasFlag(MediaCommandCapabilities.Next);
        controls.IsStopEnabled = target.Capabilities.HasFlag(MediaCommandCapabilities.Stop);
        controls.PlaybackStatus = target.PlaybackStatus switch
        {
            PlaybackStatus.Playing => MediaPlaybackStatus.Playing,
            PlaybackStatus.Paused => MediaPlaybackStatus.Paused,
            PlaybackStatus.Stopped => MediaPlaybackStatus.Stopped,
            PlaybackStatus.Changing => MediaPlaybackStatus.Changing,
            PlaybackStatus.Closed => MediaPlaybackStatus.Closed,
            _ => MediaPlaybackStatus.Stopped,
        };

        var updater = controls.DisplayUpdater;
        updater.Type = target.PlaybackType == MediaLock.Core.Media.MediaPlaybackType.Video
            ? global::Windows.Media.MediaPlaybackType.Video
            : global::Windows.Media.MediaPlaybackType.Music;
        var mirroredTitle = target.Metadata?.Title ?? target.SourceAppUserModelId;
        updater.MusicProperties.Title = $"[Media Lock Mirror] {mirroredTitle}";
        updater.MusicProperties.Artist = target.Metadata?.Artist ?? string.Empty;
        updater.MusicProperties.AlbumTitle = target.Metadata?.AlbumTitle ?? string.Empty;
        updater.Thumbnail = CreateArtworkReference(target.Artwork);
        updater.Update();

        if (target.Timeline is { } timeline && timeline.End >= timeline.Start)
        {
            controls.UpdateTimelineProperties(new SystemMediaTransportControlsTimelineProperties
            {
                StartTime = timeline.Start,
                MinSeekTime = timeline.Start,
                Position = timeline.Position,
                MaxSeekTime = timeline.End,
                EndTime = timeline.End,
            });
        }

        controls.IsEnabled = true;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        controls.ButtonPressed -= OnButtonPressed;
        controls.PlaybackPositionChangeRequested -= OnPlaybackPositionChangeRequested;
        Disable();
        requestGate.Dispose();
        disposed = true;
    }

    private RandomAccessStreamReference? CreateArtworkReference(MediaArtwork? artwork)
    {
        artworkStream?.Dispose();
        artworkStream = null;
        if (artwork is null)
        {
            return null;
        }

        artworkStream = new InMemoryRandomAccessStream();
        using var writer = new DataWriter(artworkStream);
        writer.WriteBytes(artwork.Bytes.AsSpan().ToArray());
        writer.StoreAsync().AsTask().GetAwaiter().GetResult();
        artworkStream.Seek(0);
        return (RandomAccessStreamReference)RandomAccessStreamReference.CreateFromStream(artworkStream);
    }

    private void OnButtonPressed(
        SystemMediaTransportControls sender,
        SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        var command = args.Button switch
        {
            SystemMediaTransportControlsButton.Play => MediaCommand.Play,
            SystemMediaTransportControlsButton.Pause => MediaCommand.Pause,
            SystemMediaTransportControlsButton.Previous => MediaCommand.Previous,
            SystemMediaTransportControlsButton.Next => MediaCommand.Next,
            SystemMediaTransportControlsButton.Stop => MediaCommand.Stop,
            _ => (MediaCommand?)null,
        };
        if (command is { } supported)
        {
            QueueRequest(supported);
        }
    }

    private void OnPlaybackPositionChangeRequested(
        SystemMediaTransportControls sender,
        PlaybackPositionChangeRequestedEventArgs args) =>
        QueueRequest(MediaCommand.SeekAbsolute(args.RequestedPlaybackPosition));

    private void QueueRequest(MediaCommand command)
    {
        var expectedTarget = application.State.Router.ActiveTarget;
        var sequence = Interlocked.Increment(ref requestSequence);
        _ = DispatchOnceAsync(sequence, command, expectedTarget);
    }

    private async Task DispatchOnceAsync(long sequence, MediaCommand command, SessionKey? expectedTarget)
    {
        try
        {
            await requestGate.WaitAsync();
            if (disposed || !IsEnabled || expectedTarget is null)
            {
                PostLog($"SURFACE #{sequence} {command}: skipped; mirror disabled or no capture target.");
                return;
            }

            var result = await application.DispatchAsync(
                new ApplicationIntent.Route(command, expectedTarget),
                CancellationToken.None);
            PostLog($"SURFACE #{sequence} {command} -> {expectedTarget}: " +
                $"{result.Decision.Kind}/{result.Decision.Reason}.");
        }
        catch (Exception exception)
        {
            PostLog($"SURFACE #{sequence} {command} failed: {exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            if (!disposed)
            {
                requestGate.Release();
            }
        }
    }

    private void PostLog(string message) => uiContext.Post(_ => log(message), null);
}
