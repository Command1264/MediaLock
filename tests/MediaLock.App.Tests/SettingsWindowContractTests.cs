using System.Runtime.ExceptionServices;
using System.Windows;
using MediaLock.App.ViewModels;
using MediaLock.Application;
using MediaLock.Core.Routing;
using Xunit;

namespace MediaLock.App.Tests;

public sealed class SettingsWindowContractTests
{
    [Fact]
    public void SettingsUsesAFixedFramelessSurface()
    {
        RunOnStaThread(() =>
        {
            var app = new App();
            app.InitializeComponent();
            using var viewModel = new SettingsViewModel(new FakeApplication());
            var window = new SettingsWindow(viewModel);

            Assert.Equal(680, window.Width);
            Assert.Equal(720, window.Height);
            Assert.Equal(ResizeMode.NoResize, window.ResizeMode);
            Assert.Equal(WindowStyle.None, window.WindowStyle);
            Assert.True(window.AllowsTransparency);
            Assert.False(window.ShowInTaskbar);

            window.Close();
        });
    }

    private static void RunOnStaThread(Action action)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }

    private sealed class FakeApplication : IMediaLockApplication
    {
        public event EventHandler<MediaLockApplicationStateChangedEventArgs>? StateChanged
        {
            add { }
            remove { }
        }

        public MediaLockApplicationState State { get; } = MediaLockApplicationState.Initial;

        public ValueTask StartAsync(CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask<ApplicationResult> DispatchAsync(
            ApplicationIntent intent,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new ApplicationResult(State, RouteDecision.StateUpdated));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
