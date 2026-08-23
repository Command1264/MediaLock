using System.Windows;
using System.ComponentModel;
using MediaLock.App.Tray;
using MediaLock.App.ViewModels;
using MediaLock.Core.Lifecycle;
using MediaLock.Core.Routing;
using MediaLock.Windows.Lifecycle;
using MediaLock.Windows.Persistence;
using MediaLock.Windows.Startup;
using MediaLock.Windows.Gsmtc;
using MediaLock.Windows.Diagnostics;
using MediaLock.App.Localization;
using MediaLock.App.Theming;
using MediaLock.Core.Configuration;
using Microsoft.Win32;

namespace MediaLock.App;

public partial class App : System.Windows.Application
{
    private MediaLock.Application.MediaLockApplication? mediaApplication;
    private MainWindowViewModel? mainWindowViewModel;
    private TrayViewModel? trayViewModel;
    private TrayIconHost? trayIconHost;
    private NamedPipeInstanceCoordinator? instanceCoordinator;
    private Window? mainWindow;
    private SettingsWindow? settingsWindow;
    private bool explicitExit;
    private bool shutdownStarted;
    private bool hideAnimationRunning;
    private JsonLinesDiagnosticLog? diagnosticLog;
    private SystemLifecycle? systemLifecycle;
    private string activeThemePreference = UiThemePreference.System;
    private bool systemThemeSubscribed;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        UiText.Apply(UiLanguagePreference.System);
        ApplyTheme(UiThemePreference.System);

        try
        {
            instanceCoordinator = new NamedPipeInstanceCoordinator();
            instanceCoordinator.ActivationRequested += OnActivationRequested;
            var instance = await instanceCoordinator.StartAsync(CancellationToken.None);
            if (instance != InstanceStartResult.Primary)
            {
                await instanceCoordinator.DisposeAsync();
                instanceCoordinator = null;
                Shutdown();
                return;
            }

            systemLifecycle = new SystemLifecycle();
            var adapter = new GsmtcMediaAdapter(systemLifecycle);
            var router = new MediaRouter(adapter);
            diagnosticLog = new JsonLinesDiagnosticLog();
            mediaApplication = new MediaLock.Application.MediaLockApplication(
                adapter,
                router,
                new JsonSettingsRepository(),
                new RegistryLoginStartupManager(),
                new JsonRuntimeStateRepository(),
                diagnosticLog);
            await mediaApplication.StartAsync(CancellationToken.None);
            UiText.Apply(mediaApplication.State.Settings.Desktop!.Language);
            ApplyTheme(mediaApplication.State.Settings.Desktop.Theme);
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            systemThemeSubscribed = true;
            mainWindowViewModel = new MainWindowViewModel(
                mediaApplication,
                SynchronizationContext.Current,
                ShowSettingsWindow,
                CloseSettingsWindow,
                UiText.Apply,
                ApplyTheme);
            var window = new MainWindow(mainWindowViewModel);
            mainWindow = window;
            MainWindow = window;
            window.Closing += OnMainWindowClosing;
            window.Closed += OnMainWindowClosed;
            trayViewModel = new TrayViewModel(
                mediaApplication,
                ShowMainWindow,
                ExitApplication,
                SynchronizationContext.Current,
                ShowSettingsWindow);
            trayIconHost = new TrayIconHost(trayViewModel);
            if (!e.Args.Contains("--startup", StringComparer.OrdinalIgnoreCase))
            {
                ShowMainWindow();
            }
        }
        catch (Exception exception)
        {
            var details = exception.Message;
            try
            {
                await ShutdownAsync();
            }
            catch (Exception cleanupException)
            {
                details += $"\n\n{UiText.Format("App_CleanupAlsoFailed", cleanupException.Message)}";
            }

            System.Windows.MessageBox.Show(
                $"{UiText.Get("App_StartupFailed")}\n\n{details}",
                UiText.Get("App_StartupErrorTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void OnMainWindowClosing(object? sender, CancelEventArgs e)
    {
        if (!explicitExit &&
            mediaApplication?.State.Settings.Desktop?.CloseToTray == true)
        {
            e.Cancel = true;
            settingsWindow?.Hide();
            if (mainWindow is not null && !hideAnimationRunning)
            {
                hideAnimationRunning = true;
                WindowAnimations.HideWithFade(
                    mainWindow,
                    () => hideAnimationRunning = false);
            }
        }
    }

    private async void OnMainWindowClosed(object? sender, EventArgs e)
    {
        if (sender is Window window)
        {
            window.Closing -= OnMainWindowClosing;
            window.Closed -= OnMainWindowClosed;
        }

        try
        {
            await ShutdownAsync();
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(
                $"{UiText.Get("App_ShutdownFailed")}\n\n{exception.Message}",
                UiText.Get("App_ShutdownErrorTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            Shutdown();
        }
    }

    private void ShowMainWindow()
    {
        if (mainWindow is null)
        {
            return;
        }

        if (hideAnimationRunning)
        {
            hideAnimationRunning = false;
            WindowAnimations.ShowWithFade(mainWindow);
        }
        else if (!mainWindow.IsVisible)
        {
            WindowAnimations.ShowWithFade(mainWindow);
        }

        if (mainWindow.WindowState == WindowState.Minimized)
        {
            mainWindow.WindowState = WindowState.Normal;
        }

        mainWindow.Activate();
    }

    private void ShowSettingsWindow()
    {
        if (mainWindowViewModel is null)
        {
            return;
        }

        ShowMainWindow();

        if (settingsWindow is not null)
        {
            settingsWindow.Activate();
            return;
        }

        var window = new SettingsWindow(mainWindowViewModel.Settings)
        {
            Owner = mainWindow,
        };
        settingsWindow = window;
        window.Closed += OnSettingsWindowClosed;
        try
        {
            WindowAnimations.ShowDialogWithFade(window);
        }
        finally
        {
            if (ReferenceEquals(settingsWindow, window))
            {
                window.Closed -= OnSettingsWindowClosed;
                settingsWindow = null;
            }

            if (!shutdownStarted)
            {
                ShowMainWindow();
            }
        }
    }

    private void OnSettingsWindowClosed(object? sender, EventArgs e)
    {
        if (sender is SettingsWindow window)
        {
            window.Closed -= OnSettingsWindowClosed;
        }

        settingsWindow = null;
    }

    private void CloseSettingsWindow() => settingsWindow?.Close();

    private void ExitApplication()
    {
        explicitExit = true;
        mainWindow?.Close();
    }

    private void OnActivationRequested(object? sender, EventArgs e) =>
        Dispatcher.InvokeAsync(ShowMainWindow);

    private void ApplyTheme(string preference)
    {
        activeThemePreference = preference;
        UiTheme.Apply(this, preference);
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs args)
    {
        if (activeThemePreference != UiThemePreference.System)
        {
            return;
        }

        Dispatcher.InvokeAsync(() => UiTheme.Apply(this, activeThemePreference));
    }

    private async ValueTask ShutdownAsync()
    {
        if (shutdownStarted)
        {
            return;
        }

        shutdownStarted = true;
        if (systemThemeSubscribed)
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            systemThemeSubscribed = false;
        }
        var failures = new List<Exception>();
        try
        {
            trayIconHost?.Dispose();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        trayIconHost = null;
        try
        {
            trayViewModel?.Dispose();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        trayViewModel = null;
        if (settingsWindow is not null)
        {
            try
            {
                settingsWindow.Closed -= OnSettingsWindowClosed;
                settingsWindow.Close();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            settingsWindow = null;
        }

        try
        {
            mainWindowViewModel?.Dispose();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        mainWindowViewModel = null;
        mainWindow = null;

        if (mediaApplication is not null)
        {
            try
            {
                await mediaApplication.DisposeAsync();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            mediaApplication = null;
        }

        if (diagnosticLog is not null)
        {
            try
            {
                await diagnosticLog.DisposeAsync();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            diagnosticLog = null;
        }

        try
        {
            systemLifecycle?.Dispose();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        systemLifecycle = null;

        if (instanceCoordinator is not null)
        {
            instanceCoordinator.ActivationRequested -= OnActivationRequested;
            try
            {
                await instanceCoordinator.DisposeAsync();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            instanceCoordinator = null;
        }

        if (failures.Count > 0)
        {
            throw new AggregateException(UiText.Get("App_ShutdownAggregate"), failures);
        }
    }
}
