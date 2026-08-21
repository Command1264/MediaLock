using System.Windows;
using MediaLock.App.ViewModels;
using MediaLock.Core.Routing;
using MediaLock.Windows.Gsmtc;

namespace MediaLock.App;

public partial class App : System.Windows.Application
{
    private MediaLock.Application.MediaLockApplication? mediaApplication;
    private MainWindowViewModel? mainWindowViewModel;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var adapter = new GsmtcMediaAdapter();
        var router = new MediaRouter(adapter);
        mediaApplication = new MediaLock.Application.MediaLockApplication(adapter, router);

        try
        {
            await mediaApplication.StartAsync(CancellationToken.None);
            mainWindowViewModel = new MainWindowViewModel(
                mediaApplication,
                SynchronizationContext.Current);
            var window = new MainWindow(mainWindowViewModel);
            MainWindow = window;
            window.Closed += OnMainWindowClosed;
            window.Show();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"Media Lock could not start GSMTC.\n\n{exception.Message}",
                "Media Lock startup error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            await mediaApplication.DisposeAsync();
            mediaApplication = null;
            Shutdown(1);
        }
    }

    private async void OnMainWindowClosed(object? sender, EventArgs e)
    {
        if (sender is Window window)
        {
            window.Closed -= OnMainWindowClosed;
        }

        mainWindowViewModel?.Dispose();
        mainWindowViewModel = null;

        try
        {
            if (mediaApplication is not null)
            {
                await mediaApplication.DisposeAsync();
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"Media Lock could not shut down cleanly.\n\n{exception.Message}",
                "Media Lock shutdown error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            mediaApplication = null;
            Shutdown();
        }
    }
}
