using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using MediaLock.Application;
using MediaLock.Core.Routing;

namespace MediaLock.App.Tests;

internal static class WpfTestHost
{
    private static readonly Lazy<Dispatcher> UiDispatcher = new(CreateDispatcher);

    public static void Run(Action action) => UiDispatcher.Value.Invoke(action);

    public static IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static Dispatcher CreateDispatcher()
    {
        var ready = new TaskCompletionSource<Dispatcher>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var app = new App();
            app.InitializeComponent();
            ready.SetResult(Dispatcher.CurrentDispatcher);
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "MediaLock.App.Tests.WPF",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return ready.Task.GetAwaiter().GetResult();
    }
}

internal sealed class FakeMediaLockApplication : IMediaLockApplication
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
