using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using MediaLock.App.Localization;
using MediaLock.Application;
using MediaLock.Core.Media;
using MediaLock.Core.Playback;
using MediaLock.Core.Routing;

namespace MediaLock.App.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged, IDisposable
{
    private static readonly TimeSpan SeekConfirmationTolerance = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SeekConfirmationTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DefaultPlaybackStateLockNoticeDuration = TimeSpan.FromSeconds(5);

    private readonly IMediaLockApplication application;
    private readonly SynchronizationContext? synchronizationContext;
    private readonly AsyncCommand lockCommand;
    private readonly AsyncCommand appLockCommand;
    private readonly AsyncCommand priorityRulesCommand;
    private readonly AsyncCommand windowsAutoCommand;
    private readonly AsyncCommand playbackStateLockOffCommand;
    private readonly AsyncCommand keepPlayingCommand;
    private readonly AsyncCommand[] mediaCommands;
    private readonly TimeProvider timeProvider;
    private readonly IPlaybackStateLockFeedback? playbackStateLockFeedback;
    private readonly TimeSpan playbackStateLockNoticeDuration;
    private SessionItemViewModel? selectedSession;
    private RouterState routerState = RouterState.Initial;
    private PlaybackStateLockState playbackStateLock = PlaybackStateLockState.Off;
    private MediaSessionCatalogStatus catalogStatus = MediaSessionCatalogStatus.Available;
    private SeekPreview? seekPreview;
    private PendingSeek? pendingSeek;
    private SelectionBookmark? selectionBookmark;
    private TimeSpan selectionBookmarkTimeout = TimeSpan.FromSeconds(15);
    private bool selectionInitialized;
    private bool selectionRecoveryPending;
    private bool projectingSelection;
    private string? errorMessage;
    private bool releasedPlaybackStateLockNoticeVisible;
    private CancellationTokenSource? playbackStateLockNoticeCancellation;
    private bool disposed;

    public MainWindowViewModel(
        IMediaLockApplication application,
        SynchronizationContext? synchronizationContext = null,
        Action? showSettings = null,
        Action? closeSettings = null,
        Action<string>? applyLanguage = null,
        Action<string>? applyTheme = null,
        TimeProvider? timeProvider = null,
        IAppEnvironmentInfoProvider? environmentInfoProvider = null,
        IDesktopSupportActions? desktopSupportActions = null,
        Func<bool>? isMediaInputRunning = null,
        IPlaybackStateLockFeedback? playbackStateLockFeedback = null,
        TimeSpan? playbackStateLockNoticeDuration = null)
    {
        ArgumentNullException.ThrowIfNull(application);
        this.application = application;
        this.synchronizationContext = synchronizationContext;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.playbackStateLockFeedback = playbackStateLockFeedback;
        this.playbackStateLockNoticeDuration = playbackStateLockNoticeDuration ??
            DefaultPlaybackStateLockNoticeDuration;
        Settings = new SettingsViewModel(
            application,
            synchronizationContext,
            closeSettings,
            applyLanguage,
            applyTheme,
            environmentInfoProvider,
            desktopSupportActions,
            isMediaInputRunning);
        SettingsCommand = new AsyncCommand(_ =>
        {
            showSettings?.Invoke();
            return Task.CompletedTask;
        });
        DismissErrorCommand = new AsyncCommand(_ =>
        {
            errorMessage = null;
            OnPropertyChanged(nameof(HasError));
            OnPropertyChanged(nameof(ErrorMessage));
            return Task.CompletedTask;
        });
        lockCommand = new AsyncCommand(
            LockSelectedAsync,
            _ => SelectedSession is not null);
        appLockCommand = new AsyncCommand(
            LockSelectedApplicationAsync,
            _ => SelectedSession is not null);
        priorityRulesCommand = new AsyncCommand(
            _ => DispatchAsync(new ApplicationIntent.UsePriorityRules()),
            _ => routerState.Mode != RoutingMode.PriorityRules);
        windowsAutoCommand = new AsyncCommand(
            _ => DispatchAsync(new ApplicationIntent.UseWindowsAuto()),
            _ => routerState.Mode != RoutingMode.WindowsAuto);
        playbackStateLockOffCommand = new AsyncCommand(
            _ => DispatchAsync(new ApplicationIntent.SetPlaybackStateLock(
                PlaybackStateLockMode.Off)),
            _ => playbackStateLock.Mode != PlaybackStateLockMode.Off);
        keepPlayingCommand = new AsyncCommand(
            _ => DispatchAsync(new ApplicationIntent.SetPlaybackStateLock(
                PlaybackStateLockMode.KeepPlaying)),
            _ => playbackStateLock.Mode != PlaybackStateLockMode.KeepPlaying &&
                catalogStatus == MediaSessionCatalogStatus.Available &&
                ResolveTarget()?.PlaybackState == PlaybackStatus.Playing);
        PlayCommand = MediaCommand(MediaLock.Core.Media.MediaCommand.Play);
        PauseCommand = MediaCommand(MediaLock.Core.Media.MediaCommand.Pause);
        TogglePlayPauseCommand = MediaCommand(MediaLock.Core.Media.MediaCommand.TogglePlayPause);
        PreviousCommand = MediaCommand(MediaLock.Core.Media.MediaCommand.Previous);
        NextCommand = MediaCommand(MediaLock.Core.Media.MediaCommand.Next);
        StopCommand = MediaCommand(MediaLock.Core.Media.MediaCommand.Stop);
        mediaCommands =
        [
            (AsyncCommand)PlayCommand,
            (AsyncCommand)PauseCommand,
            (AsyncCommand)TogglePlayPauseCommand,
            (AsyncCommand)PreviousCommand,
            (AsyncCommand)NextCommand,
            (AsyncCommand)StopCommand,
        ];
        LockCommand = lockCommand;
        AppLockCommand = appLockCommand;
        PriorityRulesCommand = priorityRulesCommand;
        WindowsAutoCommand = windowsAutoCommand;
        application.StateChanged += OnApplicationStateChanged;
        UiText.CultureChanged += OnCultureChanged;
        Apply(application.State);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<SessionItemViewModel> Sessions { get; } = [];

    public SettingsViewModel Settings { get; }

    public IAsyncCommand SettingsCommand { get; }

    public IAsyncCommand DismissErrorCommand { get; }

    public SessionItemViewModel? SelectedSession
    {
        get => selectedSession;
        set
        {
            if (Equals(selectedSession, value))
            {
                return;
            }

            selectedSession = value;
            if (!projectingSelection)
            {
                selectionRecoveryPending = false;
                selectionInitialized = true;
                selectionBookmark = value is null
                    ? null
                    : SelectionBookmark.From(value);
            }

            OnPropertyChanged();
            lockCommand.RaiseCanExecuteChanged();
            appLockCommand.RaiseCanExecuteChanged();
        }
    }

    public IAsyncCommand LockCommand { get; }

    public IAsyncCommand AppLockCommand { get; }

    public IAsyncCommand PriorityRulesCommand { get; }

    public IAsyncCommand WindowsAutoCommand { get; }

    public IAsyncCommand PlaybackStateLockOffCommand => playbackStateLockOffCommand;

    public IAsyncCommand KeepPlayingCommand => keepPlayingCommand;

    public bool IsPlaybackStateLockOff =>
        playbackStateLock.Mode == PlaybackStateLockMode.Off;

    public bool IsKeepPlaying =>
        playbackStateLock.Mode == PlaybackStateLockMode.KeepPlaying;

    public bool HasPlaybackStateLockNotice =>
        releasedPlaybackStateLockNoticeVisible ||
        playbackStateLock.Status is PlaybackStateLockStatus.Suspended or
            PlaybackStateLockStatus.Failed;

    public bool IsPlaybackStateLockFailed =>
        playbackStateLock.Status == PlaybackStateLockStatus.Failed;

    public string PlaybackStateLockNotice => playbackStateLock.Status switch
    {
        _ when releasedPlaybackStateLockNoticeVisible => UiText.Get("Main_KeepPlayingReleased"),
        PlaybackStateLockStatus.Suspended => UiText.Get("Main_KeepPlayingSuspended"),
        PlaybackStateLockStatus.Failed => UiText.Get("Main_KeepPlayingFailed"),
        _ => string.Empty,
    };

    public IAsyncCommand PlayCommand { get; }

    public IAsyncCommand PauseCommand { get; }

    public IAsyncCommand TogglePlayPauseCommand { get; }

    public IAsyncCommand PreviousCommand { get; }

    public IAsyncCommand NextCommand { get; }

    public IAsyncCommand StopCommand { get; }

    public bool IsWindowsAutoMode => routerState.Mode == RoutingMode.WindowsAuto;

    public bool IsPriorityRulesMode => routerState.Mode == RoutingMode.PriorityRules;

    public bool IsAppLockMode => routerState.Mode == RoutingMode.AppLock;

    public bool IsSessionLockMode => routerState.Mode == RoutingMode.SessionLock;

    public string RoutingStatus => catalogStatus switch
    {
        MediaSessionCatalogStatus.Available
            when routerState.Mode == RoutingMode.AppLock && routerState.Status == RouterStatus.Locked =>
                UiText.Get("Mode_AppLocked"),
        MediaSessionCatalogStatus.Available when routerState.Mode == RoutingMode.PriorityRules =>
            UiText.Get("Mode_PriorityRules"),
        MediaSessionCatalogStatus.Available => UiDescriptions.DescribeRouterStatus(routerState.Status),
        var status => UiDescriptions.DescribeCatalogStatus(status),
    };

    public string RoutingStatusLine => UiText.Format("Main_StatusFormat", RoutingStatus);

    public bool HasSessions => Sessions.Count > 0;

    public string EmptyStateText => catalogStatus switch
    {
        MediaSessionCatalogStatus.Suspended => UiText.Get("Empty_Suspended"),
        MediaSessionCatalogStatus.Reacquiring => UiText.Get("Empty_Reacquiring"),
        MediaSessionCatalogStatus.Unavailable => UiText.Get("Empty_Unavailable"),
        _ when routerState.Status == RouterStatus.Recovering =>
            UiText.Get("Empty_Recovering"),
        _ => UiText.Get("Empty_Default"),
    };

    public string TargetDescription
    {
        get
        {
            var target = ResolveTarget();
            return target is null
                ? routerState.Mode == RoutingMode.WindowsAuto
                    ? UiText.Get("Target_WindowsUnavailable")
                    : routerState.Mode == RoutingMode.PriorityRules
                        ? UiText.Get("Target_RulesUnavailable")
                        : UiText.Get("Target_LockedUnavailable")
                : $"{target.SourceApplication} — {target.Title}";
        }
    }

    public string NowPlayingTitle => ResolveTarget()?.Title ?? string.Empty;

    public string NowPlayingArtist => ResolveTarget()?.Artist ?? string.Empty;

    public MediaArtwork? NowPlayingArtwork => ResolveTarget()?.Artwork;

    public bool HasNowPlayingTimeline => ResolveTimeline() is not null;

    public bool CanSeek
    {
        get
        {
            var target = ResolveTarget();
            var timeline = target?.Timeline;
            return catalogStatus == MediaSessionCatalogStatus.Available &&
                routerState.Status is not RouterStatus.Recovering and not RouterStatus.Unavailable &&
                target is not null &&
                (target.Capabilities & MediaCommandCapabilities.SeekAbsolute) != 0 &&
                timeline is not null &&
                timeline.Start >= TimeSpan.Zero &&
                timeline.End > timeline.Start;
        }
    }

    public double NowPlayingPositionSeconds => ResolveTimeline()?.Position.TotalSeconds ?? 0;

    public double NowPlayingDurationSeconds => ResolveTimeline()?.Duration.TotalSeconds ?? 0;

    public double NowPlayingProgress
    {
        get
        {
            var timeline = ResolveTimeline();
            return timeline is null
                ? 0
                : timeline.Value.Position.TotalSeconds / timeline.Value.Duration.TotalSeconds;
        }
    }

    public string NowPlayingElapsed
    {
        get
        {
            var timeline = ResolveTimeline();
            return timeline is null ? string.Empty : FormatTime(timeline.Value.Position);
        }
    }

    public string NowPlayingDuration
    {
        get
        {
            var timeline = ResolveTimeline();
            return timeline is null ? string.Empty : FormatTime(timeline.Value.Duration);
        }
    }

    public bool HasError => errorMessage is not null;

    public string? ErrorMessage => errorMessage;

    public void RefreshTimeline()
    {
        ExpireSelectionBookmarkIfNeeded();
        if (pendingSeek is { } pending &&
            timeProvider.GetUtcNow() - pending.RequestedAt >= SeekConfirmationTimeout)
        {
            pendingSeek = null;
            SetError(UiText.Get("Error_SeekNotConfirmed"));
        }

        OnPropertyChanged(nameof(NowPlayingProgress));
        OnPropertyChanged(nameof(NowPlayingPositionSeconds));
        OnPropertyChanged(nameof(NowPlayingElapsed));
    }

    public void BeginSeekPreview()
    {
        if (!CanSeek)
        {
            return;
        }

        var target = ResolveTarget();
        var timeline = target?.Timeline;
        if (target is null || timeline is null)
        {
            return;
        }

        seekPreview = new SeekPreview(
            target.Key,
            timeline.Start,
            timeline.End - timeline.Start,
            TimeSpan.FromTicks(Math.Clamp(
                (timeline.Position - timeline.Start).Ticks,
                0,
                (timeline.End - timeline.Start).Ticks)));
        pendingSeek = null;
        RefreshTimeline();
    }

    public void PreviewSeek(TimeSpan elapsed)
    {
        if (seekPreview is not { } preview)
        {
            return;
        }

        seekPreview = preview with
        {
            Elapsed = TimeSpan.FromTicks(Math.Clamp(elapsed.Ticks, 0, preview.Duration.Ticks)),
        };
        RefreshTimeline();
    }

    public async Task CommitSeekPreviewAsync()
    {
        if (seekPreview is not { } preview)
        {
            return;
        }

        seekPreview = null;
        pendingSeek = new PendingSeek(
            preview.Target,
            preview.Start + preview.Elapsed,
            preview.Duration,
            preview.Elapsed,
            routerState.Revision,
            ResolveTarget()?.Timeline?.LastUpdatedAt,
            timeProvider.GetUtcNow());
        RefreshTimeline();
        var result = await DispatchAsync(new ApplicationIntent.Route(
            MediaLock.Core.Media.MediaCommand.SeekAbsolute(preview.Start + preview.Elapsed)));
        if (result?.Decision.Kind != RouteDecisionKind.Routed)
        {
            pendingSeek = null;
            if (result is
                {
                    Decision.Kind: RouteDecisionKind.Skipped,
                    Decision.Reason: not RouteReason.ControlRejected,
                })
            {
                SetError(result.Decision.Error ??
                    UiText.Format("Error_CommandNotCompleted", result.Decision.Reason));
            }

            RefreshTimeline();
        }
    }

    public void CancelSeekPreview()
    {
        if (seekPreview is null)
        {
            return;
        }

        seekPreview = null;
        RefreshTimeline();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        application.StateChanged -= OnApplicationStateChanged;
        UiText.CultureChanged -= OnCultureChanged;
        ClearPlaybackStateLockReleasedNotice();
        Settings.Dispose();
        disposed = true;
    }

    private async Task LockSelectedAsync(object? parameter)
    {
        if (SelectedSession is null)
        {
            return;
        }

        await DispatchAsync(new ApplicationIntent.LockSession(SelectedSession.Key));
    }

    private async Task LockSelectedApplicationAsync(object? parameter)
    {
        if (SelectedSession is null)
        {
            return;
        }

        await DispatchAsync(new ApplicationIntent.LockApplication(SelectedSession.SourceApplication));
    }

    private AsyncCommand MediaCommand(MediaLock.Core.Media.MediaCommand command) => new(
        _ => DispatchAsync(new ApplicationIntent.Route(command)),
        _ => ResolveTarget()?.Capabilities.Supports(command) is true &&
            routerState.Status is not RouterStatus.Recovering and not RouterStatus.Unavailable);

    private async Task<ApplicationResult?> DispatchAsync(ApplicationIntent intent)
    {
        try
        {
            var result = await application.DispatchAsync(intent, CancellationToken.None);
            if (result.Decision.Kind == RouteDecisionKind.Failed ||
                result.Decision.Reason == RouteReason.ControlRejected)
            {
                SetError(result.Decision.Error ??
                    UiText.Format("Error_CommandNotCompleted", result.Decision.Reason));
            }

            return result;
        }
        catch (Exception exception)
        {
            SetError(exception.Message);
            return null;
        }
    }

    private void SetError(string message)
    {
        errorMessage = message;
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(ErrorMessage));
    }

    private void OnApplicationStateChanged(
        object? sender,
        MediaLockApplicationStateChangedEventArgs args)
    {
        if (synchronizationContext is not null &&
            SynchronizationContext.Current != synchronizationContext)
        {
            synchronizationContext.Post(_ => Apply(args.State), null);
            return;
        }

        Apply(args.State);
    }

    private void OnCultureChanged(object? sender, EventArgs args)
    {
        if (synchronizationContext is not null &&
            SynchronizationContext.Current != synchronizationContext)
        {
            synchronizationContext.Post(_ => RefreshLocalizedProjection(), null);
            return;
        }

        RefreshLocalizedProjection();
    }

    private void Apply(MediaLockApplicationState state)
    {
        if (SelectedSession is { } selected)
        {
            RememberSelection(selected);
            if (routerState.Mode is RoutingMode.AppLock or RoutingMode.SessionLock &&
                state.Router.Mode == routerState.Mode &&
                routerState.LockedTarget?.ResolvedSession == selected.Key &&
                state.Router.ActiveTarget != selected.Key)
            {
                selectionRecoveryPending = true;
            }
        }

        routerState = state.Router;
        var previousPlaybackStateLockStatus = playbackStateLock.Status;
        playbackStateLock = state.PlaybackStateLock;
        if (playbackStateLock.Status == PlaybackStateLockStatus.Released &&
            previousPlaybackStateLockStatus != PlaybackStateLockStatus.Released)
        {
            ShowPlaybackStateLockReleasedNotice(state.Settings.PlaybackStateLock!.PlayOverrideSound);
        }
        else if (playbackStateLock.Status != PlaybackStateLockStatus.Released)
        {
            ClearPlaybackStateLockReleasedNotice();
        }
        catalogStatus = state.CatalogStatus;
        selectionBookmarkTimeout = state.Settings.Recovery?.Timeout ?? TimeSpan.FromSeconds(15);
        errorMessage = state.ErrorMessage ??
            (state.CatalogStatus == MediaSessionCatalogStatus.Unavailable
                ? state.CatalogStatusMessage
                : null);
        RefreshLocalizedProjection();
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(ErrorMessage));
        windowsAutoCommand.RaiseCanExecuteChanged();
        priorityRulesCommand.RaiseCanExecuteChanged();
        playbackStateLockOffCommand.RaiseCanExecuteChanged();
        keepPlayingCommand.RaiseCanExecuteChanged();
        foreach (var command in mediaCommands)
        {
            command.RaiseCanExecuteChanged();
        }
    }

    private void ShowPlaybackStateLockReleasedNotice(bool playSound)
    {
        ClearPlaybackStateLockReleasedNotice();
        releasedPlaybackStateLockNoticeVisible = true;
        OnPropertyChanged(nameof(HasPlaybackStateLockNotice));
        OnPropertyChanged(nameof(PlaybackStateLockNotice));
        if (playSound && playbackStateLockFeedback is not null)
        {
            try
            {
                playbackStateLockFeedback.PlayOverrideReleasedSound();
            }
            catch (Exception exception)
            {
                SetError(UiText.Format("Main_NotificationSoundFailed", exception.Message));
            }
        }

        var cancellation = new CancellationTokenSource();
        playbackStateLockNoticeCancellation = cancellation;
        _ = ClearPlaybackStateLockReleasedNoticeAfterDelayAsync(cancellation);
    }

    private void ClearPlaybackStateLockReleasedNotice()
    {
        var cancellation = playbackStateLockNoticeCancellation;
        playbackStateLockNoticeCancellation = null;
        cancellation?.Cancel();
        if (releasedPlaybackStateLockNoticeVisible)
        {
            releasedPlaybackStateLockNoticeVisible = false;
            OnPropertyChanged(nameof(HasPlaybackStateLockNotice));
            OnPropertyChanged(nameof(PlaybackStateLockNotice));
        }
    }

    private async Task ClearPlaybackStateLockReleasedNoticeAfterDelayAsync(
        CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(playbackStateLockNoticeDuration, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            if (ReferenceEquals(playbackStateLockNoticeCancellation, cancellation))
            {
                playbackStateLockNoticeCancellation = null;
                releasedPlaybackStateLockNoticeVisible = false;
                OnPropertyChanged(nameof(HasPlaybackStateLockNotice));
                OnPropertyChanged(nameof(PlaybackStateLockNotice));
            }

            cancellation.Dispose();
        }
    }

    private void RefreshLocalizedProjection()
    {
        projectingSelection = true;
        try
        {
            Sessions.Clear();
            foreach (var session in routerState.Sessions)
            {
                Sessions.Add(SessionItemViewModel.From(session));
            }

            var nextSelection = ResolveSelection();
            SelectedSession = nextSelection;
        }
        finally
        {
            projectingSelection = false;
        }

        ReconcileSeekState();
        OnPropertyChanged(nameof(Sessions));
        OnPropertyChanged(nameof(HasSessions));
        OnPropertyChanged(nameof(EmptyStateText));
        OnPropertyChanged(nameof(RoutingStatus));
        OnPropertyChanged(nameof(RoutingStatusLine));
        OnPropertyChanged(nameof(IsWindowsAutoMode));
        OnPropertyChanged(nameof(IsPriorityRulesMode));
        OnPropertyChanged(nameof(IsAppLockMode));
        OnPropertyChanged(nameof(IsSessionLockMode));
        OnPropertyChanged(nameof(IsPlaybackStateLockOff));
        OnPropertyChanged(nameof(IsKeepPlaying));
        OnPropertyChanged(nameof(HasPlaybackStateLockNotice));
        OnPropertyChanged(nameof(IsPlaybackStateLockFailed));
        OnPropertyChanged(nameof(PlaybackStateLockNotice));
        OnPropertyChanged(nameof(TargetDescription));
        OnPropertyChanged(nameof(NowPlayingTitle));
        OnPropertyChanged(nameof(NowPlayingArtist));
        OnPropertyChanged(nameof(NowPlayingArtwork));
        OnPropertyChanged(nameof(HasNowPlayingTimeline));
        OnPropertyChanged(nameof(CanSeek));
        OnPropertyChanged(nameof(NowPlayingProgress));
        OnPropertyChanged(nameof(NowPlayingPositionSeconds));
        OnPropertyChanged(nameof(NowPlayingDurationSeconds));
        OnPropertyChanged(nameof(NowPlayingElapsed));
        OnPropertyChanged(nameof(NowPlayingDuration));
    }

    private SessionItemViewModel? ResolveTarget()
    {
        return routerState.ActiveTarget is { } target
            ? Sessions.FirstOrDefault(session => session.Key == target)
            : null;
    }

    private (TimeSpan Position, TimeSpan Duration)? ResolveTimeline()
    {
        var target = ResolveTarget();
        if (target is not null &&
            seekPreview is { } preview &&
            preview.Target == target.Key)
        {
            return (preview.Elapsed, preview.Duration);
        }

        if (target is not null &&
            pendingSeek is { } pending &&
            pending.Target == target.Key)
        {
            return (pending.Elapsed, pending.Duration);
        }

        var timeline = target?.Timeline;
        if (timeline is null || timeline.End <= timeline.Start)
        {
            return null;
        }

        var duration = timeline.End - timeline.Start;
        var position = timeline.Position - timeline.Start;
        if (target!.PlaybackState == PlaybackStatus.Playing)
        {
            var elapsed = timeProvider.GetUtcNow() - timeline.LastUpdatedAt;
            if (elapsed > TimeSpan.Zero)
            {
                position += elapsed;
            }
        }

        return (TimeSpan.FromTicks(Math.Clamp(position.Ticks, 0, duration.Ticks)), duration);
    }

    private static string FormatTime(TimeSpan value) => value.TotalHours >= 1
        ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
        : $"{(int)value.TotalMinutes}:{value.Seconds:00}";

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private void ReconcileSeekState()
    {
        var target = ResolveTarget();
        if (target is null ||
            catalogStatus != MediaSessionCatalogStatus.Available ||
            routerState.Status is RouterStatus.Recovering or RouterStatus.Unavailable)
        {
            var seekWasInterrupted = seekPreview is not null || pendingSeek is not null;
            seekPreview = null;
            pendingSeek = null;
            if (seekWasInterrupted)
            {
                SetError(UiText.Get("Error_SeekInterrupted"));
            }

            return;
        }

        if (seekPreview is { } preview && preview.Target != target.Key)
        {
            seekPreview = null;
            SetError(UiText.Get("Error_SeekInterrupted"));
        }

        if (pendingSeek is not { } pending)
        {
            return;
        }

        if (pending.Target != target.Key)
        {
            pendingSeek = null;
            SetError(UiText.Get("Error_SeekInterrupted"));
            return;
        }

        var timeline = target.Timeline;
        if (routerState.Revision <= pending.BaselineRevision ||
            timeline is null ||
            (pending.BaselineTimelineUpdatedAt is { } baselineUpdatedAt &&
                timeline.LastUpdatedAt <= baselineUpdatedAt))
        {
            return;
        }

        if ((timeline.Position - pending.AbsolutePosition).Duration() <= SeekConfirmationTolerance)
        {
            pendingSeek = null;
        }
    }

    private SessionItemViewModel? ResolveSelection()
    {
        ExpireSelectionBookmarkIfNeeded();
        if (selectionBookmark is not { } bookmark)
        {
            if (selectionInitialized)
            {
                return null;
            }

            var initial = Sessions.FirstOrDefault();
            return initial is null ? null : RememberSelection(initial);
        }

        var existing = Sessions.FirstOrDefault(session => session.Key == bookmark.Key);
        if (existing is not null)
        {
            return RememberSelection(existing);
        }

        if (selectionRecoveryPending)
        {
            if (routerState.Mode is not RoutingMode.AppLock and not RoutingMode.SessionLock)
            {
                selectionRecoveryPending = false;
            }
            else
            {
                if (routerState.ActiveTarget is { } recoveredKey)
                {
                    var recovered = Sessions.FirstOrDefault(session => session.Key == recoveredKey);
                    if (recovered is not null)
                    {
                        selectionRecoveryPending = false;
                        return RememberSelection(recovered);
                    }
                }

                return MarkSelectionMissing(bookmark);
            }
        }

        var sameSource = Sessions
            .Where(session => string.Equals(
                session.SourceApplication,
                bookmark.SourceApplication,
                StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        return sameSource.Length == 1
            ? RememberSelection(sameSource[0])
            : MarkSelectionMissing(bookmark);
    }

    private SessionItemViewModel RememberSelection(SessionItemViewModel session)
    {
        selectionInitialized = true;
        selectionBookmark = SelectionBookmark.From(session);
        return session;
    }

    private SessionItemViewModel? MarkSelectionMissing(SelectionBookmark bookmark)
    {
        selectionBookmark = bookmark.MissingSince is null
            ? bookmark with { MissingSince = timeProvider.GetUtcNow() }
            : bookmark;
        ExpireSelectionBookmarkIfNeeded();
        return null;
    }

    private void ExpireSelectionBookmarkIfNeeded()
    {
        if (selectionBookmark is not { MissingSince: { } missingSince } ||
            timeProvider.GetUtcNow() - missingSince < selectionBookmarkTimeout)
        {
            return;
        }

        selectionBookmark = null;
        selectionRecoveryPending = false;
    }

    private sealed record SeekPreview(
        SessionKey Target,
        TimeSpan Start,
        TimeSpan Duration,
        TimeSpan Elapsed);

    private sealed record PendingSeek(
        SessionKey Target,
        TimeSpan AbsolutePosition,
        TimeSpan Duration,
        TimeSpan Elapsed,
        long BaselineRevision,
        DateTimeOffset? BaselineTimelineUpdatedAt,
        DateTimeOffset RequestedAt);

    private sealed record SelectionBookmark(
        SessionKey Key,
        string SourceApplication,
        DateTimeOffset? MissingSince)
    {
        public static SelectionBookmark From(SessionItemViewModel session) => new(
            session.Key,
            session.SourceApplication,
            MissingSince: null);
    }
}
