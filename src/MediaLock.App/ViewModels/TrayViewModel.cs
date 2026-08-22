using System.ComponentModel;
using System.Runtime.CompilerServices;
using MediaLock.Application;
using MediaLock.Core.Media;
using MediaLock.Core.Routing;

namespace MediaLock.App.ViewModels;

public sealed class TrayViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IMediaLockApplication application;
    private readonly SynchronizationContext? synchronizationContext;
    private string statusText;
    private string? errorMessage;
    private bool disposed;

    public TrayViewModel(
        IMediaLockApplication application,
        Action show,
        Action exit,
        SynchronizationContext? synchronizationContext = null,
        Action? showSettings = null)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(show);
        ArgumentNullException.ThrowIfNull(exit);
        this.application = application;
        this.synchronizationContext = synchronizationContext;
        statusText = Describe(application.State);
        ShowCommand = new AsyncCommand(_ =>
        {
            show();
            return Task.CompletedTask;
        });
        ExitCommand = new AsyncCommand(_ =>
        {
            exit();
            return Task.CompletedTask;
        });
        SettingsCommand = new AsyncCommand(_ =>
        {
            showSettings?.Invoke();
            return Task.CompletedTask;
        });
        TogglePlayPauseCommand = Route(MediaCommand.TogglePlayPause);
        PreviousCommand = Route(MediaCommand.Previous);
        NextCommand = Route(MediaCommand.Next);
        WindowsAutoCommand = new AsyncCommand(_ => DispatchAsync(
            new ApplicationIntent.UseWindowsAuto()));
        application.StateChanged += OnApplicationStateChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string StatusText
    {
        get => statusText;
        private set
        {
            if (statusText == value)
            {
                return;
            }

            statusText = value;
            OnPropertyChanged();
        }
    }

    public IAsyncCommand ShowCommand { get; }

    public IAsyncCommand SettingsCommand { get; }

    public IAsyncCommand TogglePlayPauseCommand { get; }

    public IAsyncCommand PreviousCommand { get; }

    public IAsyncCommand NextCommand { get; }

    public IAsyncCommand WindowsAutoCommand { get; }

    public IAsyncCommand ExitCommand { get; }

    public string? ErrorMessage
    {
        get => errorMessage;
        private set
        {
            if (errorMessage == value)
            {
                return;
            }

            errorMessage = value;
            OnPropertyChanged();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        application.StateChanged -= OnApplicationStateChanged;
        disposed = true;
    }

    private AsyncCommand Route(MediaCommand command) => new(
        _ => DispatchAsync(new ApplicationIntent.Route(command)));

    private async Task DispatchAsync(ApplicationIntent intent)
    {
        try
        {
            await application.DispatchAsync(intent, CancellationToken.None);
            ErrorMessage = null;
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    private void OnApplicationStateChanged(
        object? sender,
        MediaLockApplicationStateChangedEventArgs args)
    {
        var next = Describe(args.State);
        if (synchronizationContext is not null &&
            SynchronizationContext.Current != synchronizationContext)
        {
            synchronizationContext.Post(_ => StatusText = next, null);
            return;
        }

        StatusText = next;
    }

    private static string Describe(MediaLockApplicationState state) => state.CatalogStatus switch
    {
        MediaSessionCatalogStatus.Suspended => "Suspended",
        MediaSessionCatalogStatus.Reacquiring => "Reacquiring",
        MediaSessionCatalogStatus.Unavailable => "Unavailable",
        _ => DescribeRouter(state.Router),
    };

    private static string DescribeRouter(RouterState state) => state.Status switch
    {
        RouterStatus.Recovering => "Recovering",
        RouterStatus.Locked => "Locked",
        _ when state.Mode == RoutingMode.WindowsAuto => "Windows Auto",
        _ => state.Status.ToString(),
    };

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
