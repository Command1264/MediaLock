using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using MediaLock.App.Localization;
using MediaLock.Application;
using MediaLock.Core.Media;
using MediaLock.Core.Routing;

namespace MediaLock.App.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IMediaLockApplication application;
    private readonly SynchronizationContext? synchronizationContext;
    private readonly AsyncCommand lockCommand;
    private readonly AsyncCommand appLockCommand;
    private readonly AsyncCommand priorityRulesCommand;
    private readonly AsyncCommand windowsAutoCommand;
    private readonly AsyncCommand[] mediaCommands;
    private SessionItemViewModel? selectedSession;
    private RouterState routerState = RouterState.Initial;
    private MediaSessionCatalogStatus catalogStatus = MediaSessionCatalogStatus.Available;
    private string? errorMessage;
    private bool disposed;

    public MainWindowViewModel(
        IMediaLockApplication application,
        SynchronizationContext? synchronizationContext = null,
        Action? showSettings = null,
        Action? closeSettings = null,
        Action<string>? applyLanguage = null)
    {
        ArgumentNullException.ThrowIfNull(application);
        this.application = application;
        this.synchronizationContext = synchronizationContext;
        Settings = new SettingsViewModel(
            application,
            synchronizationContext,
            closeSettings,
            applyLanguage);
        SettingsCommand = new AsyncCommand(_ =>
        {
            showSettings?.Invoke();
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
            OnPropertyChanged();
            lockCommand.RaiseCanExecuteChanged();
            appLockCommand.RaiseCanExecuteChanged();
        }
    }

    public IAsyncCommand LockCommand { get; }

    public IAsyncCommand AppLockCommand { get; }

    public IAsyncCommand PriorityRulesCommand { get; }

    public IAsyncCommand WindowsAutoCommand { get; }

    public IAsyncCommand PlayCommand { get; }

    public IAsyncCommand PauseCommand { get; }

    public IAsyncCommand TogglePlayPauseCommand { get; }

    public IAsyncCommand PreviousCommand { get; }

    public IAsyncCommand NextCommand { get; }

    public IAsyncCommand StopCommand { get; }

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

    public bool HasError => errorMessage is not null;

    public string? ErrorMessage => errorMessage;

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        application.StateChanged -= OnApplicationStateChanged;
        UiText.CultureChanged -= OnCultureChanged;
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

    private async Task DispatchAsync(ApplicationIntent intent)
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
        }
        catch (Exception exception)
        {
            SetError(exception.Message);
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
        routerState = state.Router;
        catalogStatus = state.CatalogStatus;
        errorMessage = state.ErrorMessage ??
            (state.CatalogStatus == MediaSessionCatalogStatus.Unavailable
                ? state.CatalogStatusMessage
                : null);
        RefreshLocalizedProjection();
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(ErrorMessage));
        windowsAutoCommand.RaiseCanExecuteChanged();
        priorityRulesCommand.RaiseCanExecuteChanged();
        foreach (var command in mediaCommands)
        {
            command.RaiseCanExecuteChanged();
        }
    }

    private void RefreshLocalizedProjection()
    {
        var selectedKey = SelectedSession?.Key;
        Sessions.Clear();
        foreach (var session in routerState.Sessions)
        {
            Sessions.Add(SessionItemViewModel.From(session));
        }

        SelectedSession = selectedKey is { } key
            ? Sessions.FirstOrDefault(session => session.Key == key)
            : Sessions.FirstOrDefault();
        OnPropertyChanged(nameof(Sessions));
        OnPropertyChanged(nameof(HasSessions));
        OnPropertyChanged(nameof(EmptyStateText));
        OnPropertyChanged(nameof(RoutingStatus));
        OnPropertyChanged(nameof(RoutingStatusLine));
        OnPropertyChanged(nameof(TargetDescription));
    }

    private SessionItemViewModel? ResolveTarget()
    {
        return routerState.ActiveTarget is { } target
            ? Sessions.FirstOrDefault(session => session.Key == target)
            : null;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
