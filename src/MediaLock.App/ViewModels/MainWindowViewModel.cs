using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using MediaLock.Application;
using MediaLock.Core.Media;
using MediaLock.Core.Routing;

namespace MediaLock.App.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IMediaLockApplication application;
    private readonly SynchronizationContext? synchronizationContext;
    private readonly AsyncCommand lockCommand;
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
        Action? closeSettings = null)
    {
        ArgumentNullException.ThrowIfNull(application);
        this.application = application;
        this.synchronizationContext = synchronizationContext;
        Settings = new SettingsViewModel(application, synchronizationContext, closeSettings);
        SettingsCommand = new AsyncCommand(_ =>
        {
            showSettings?.Invoke();
            return Task.CompletedTask;
        });
        lockCommand = new AsyncCommand(
            LockSelectedAsync,
            _ => SelectedSession is not null);
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
        WindowsAutoCommand = windowsAutoCommand;
        application.StateChanged += OnApplicationStateChanged;
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
        }
    }

    public IAsyncCommand LockCommand { get; }

    public IAsyncCommand WindowsAutoCommand { get; }

    public IAsyncCommand PlayCommand { get; }

    public IAsyncCommand PauseCommand { get; }

    public IAsyncCommand TogglePlayPauseCommand { get; }

    public IAsyncCommand PreviousCommand { get; }

    public IAsyncCommand NextCommand { get; }

    public IAsyncCommand StopCommand { get; }

    public string RoutingStatus => catalogStatus switch
    {
        MediaSessionCatalogStatus.Available => routerState.Status.ToString(),
        var status => status.ToString(),
    };

    public bool HasSessions => Sessions.Count > 0;

    public string EmptyStateText => catalogStatus switch
    {
        MediaSessionCatalogStatus.Suspended => "Media sessions are suspended while Windows sleeps.",
        MediaSessionCatalogStatus.Reacquiring => "Reacquiring media sessions after Windows resumed.",
        MediaSessionCatalogStatus.Unavailable => "Media sessions are unavailable. Resume Windows or restart Media Lock to retry.",
        _ when routerState.Status == RouterStatus.Recovering =>
            "Waiting for the locked Media Session to return.",
        _ => "Start media playback in a supported application.",
    };

    public string TargetDescription
    {
        get
        {
            var target = ResolveTarget();
            return target is null
                ? routerState.Mode == RoutingMode.WindowsAuto
                    ? "Windows Current Session is unavailable."
                    : "Locked Target is unavailable."
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
                    $"Media command was not completed: {result.Decision.Reason}.");
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

    private void Apply(MediaLockApplicationState state)
    {
        routerState = state.Router;
        catalogStatus = state.CatalogStatus;
        errorMessage = state.ErrorMessage ??
            (state.CatalogStatus == MediaSessionCatalogStatus.Unavailable
                ? state.CatalogStatusMessage
                : null);
        var selectedKey = SelectedSession?.Key;
        Sessions.Clear();
        foreach (var session in state.Router.Sessions)
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
        OnPropertyChanged(nameof(TargetDescription));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(ErrorMessage));
        windowsAutoCommand.RaiseCanExecuteChanged();
        foreach (var command in mediaCommands)
        {
            command.RaiseCanExecuteChanged();
        }
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
