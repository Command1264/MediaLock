using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MediaLock.App.Converters;
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
            var app = new System.Windows.Application
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown,
            };
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(
                    "pack://application:,,,/MediaLock;component/Themes/Light.xaml",
                    UriKind.Absolute),
            });
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(
                    "pack://application:,,,/MediaLock;component/Themes/Controls.xaml",
                    UriKind.Absolute),
            });
            app.Resources["BooleanToVisibilityConverter"] = new BooleanToVisibilityConverter();
            app.Resources["MediaArtworkImageConverter"] = new MediaArtworkImageConverter();
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
    public FakeMediaLockApplication(
        MediaLockApplicationState? state = null,
        RouteDecision? decision = null)
    {
        State = state ?? MediaLockApplicationState.Initial;
        Decision = decision ?? RouteDecision.StateUpdated;
    }

    public event EventHandler<MediaLockApplicationStateChangedEventArgs>? StateChanged;

    public List<ApplicationIntent> Intents { get; } = [];

    public RouteDecision Decision { get; }

    public MediaLockApplicationState State { get; private set; }

    public ValueTask StartAsync(CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    public ValueTask<ApplicationResult> DispatchAsync(
        ApplicationIntent intent,
        CancellationToken cancellationToken)
    {
        Intents.Add(intent);
        return ValueTask.FromResult(new ApplicationResult(State, Decision));
    }

    public void Publish(MediaLockApplicationState state)
    {
        State = state;
        StateChanged?.Invoke(this, new MediaLockApplicationStateChangedEventArgs(state));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
