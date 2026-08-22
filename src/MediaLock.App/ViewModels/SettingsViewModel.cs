using System.ComponentModel;
using System.Runtime.CompilerServices;
using MediaLock.Application;
using MediaLock.Core.Configuration;
using MediaLock.Core.Routing;

namespace MediaLock.App.ViewModels;

public sealed class SettingsViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IMediaLockApplication application;
    private readonly SynchronizationContext? synchronizationContext;
    private bool closeToTray;
    private bool startWithWindows;
    private RoutingMode defaultRoutingMode;
    private double recoveryTimeoutSeconds;
    private FallbackPolicy fallbackPolicy;
    private string? errorMessage;
    private MediaLockSettings? appliedSettings;
    private bool disposed;

    public SettingsViewModel(
        IMediaLockApplication application,
        SynchronizationContext? synchronizationContext = null)
    {
        ArgumentNullException.ThrowIfNull(application);
        this.application = application;
        this.synchronizationContext = synchronizationContext;
        SaveCommand = new AsyncCommand(_ => SaveAsync());
        application.StateChanged += OnApplicationStateChanged;
        Apply(application.State.Settings);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<RoutingMode> RoutingModes { get; } =
        [RoutingMode.WindowsAuto, RoutingMode.SessionLock];

    public IReadOnlyList<FallbackPolicy> FallbackPolicies { get; } =
        Enum.GetValues<FallbackPolicy>();

    public bool CloseToTray
    {
        get => closeToTray;
        set => SetField(ref closeToTray, value);
    }

    public bool StartWithWindows
    {
        get => startWithWindows;
        set => SetField(ref startWithWindows, value);
    }

    public RoutingMode DefaultRoutingMode
    {
        get => defaultRoutingMode;
        set => SetField(ref defaultRoutingMode, value);
    }

    public double RecoveryTimeoutSeconds
    {
        get => recoveryTimeoutSeconds;
        set => SetField(ref recoveryTimeoutSeconds, value);
    }

    public FallbackPolicy FallbackPolicy
    {
        get => fallbackPolicy;
        set => SetField(ref fallbackPolicy, value);
    }

    public IAsyncCommand SaveCommand { get; }

    public string? ErrorMessage
    {
        get => errorMessage;
        private set => SetField(ref errorMessage, value);
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

    private async Task SaveAsync()
    {
        try
        {
            var settings = new MediaLockSettings(
                MediaLockSettings.CurrentSchemaVersion,
                DefaultRoutingMode,
                new RecoverySettings(
                    TimeSpan.FromSeconds(RecoveryTimeoutSeconds),
                    FallbackPolicy),
                new DesktopSettings(CloseToTray, StartWithWindows));
            await application.DispatchAsync(
                new ApplicationIntent.UpdateSettings(settings),
                CancellationToken.None);
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
        if (synchronizationContext is not null &&
            SynchronizationContext.Current != synchronizationContext)
        {
            synchronizationContext.Post(_ => ApplyIfChanged(args.State.Settings), null);
            return;
        }

        ApplyIfChanged(args.State.Settings);
    }

    private void ApplyIfChanged(MediaLockSettings settings)
    {
        if (Equals(appliedSettings, settings))
        {
            return;
        }

        Apply(settings);
    }

    private void Apply(MediaLockSettings settings)
    {
        appliedSettings = settings;
        CloseToTray = settings.Desktop!.CloseToTray;
        StartWithWindows = settings.Desktop.StartWithWindows;
        DefaultRoutingMode = settings.DefaultRoutingMode;
        RecoveryTimeoutSeconds = settings.Recovery!.Timeout.TotalSeconds;
        FallbackPolicy = settings.Recovery.FallbackPolicy;
    }

    private void SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
