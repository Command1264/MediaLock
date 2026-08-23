using System.Windows;
using System.Windows.Threading;
using MediaLock.App.Theming;
using MediaLock.App.ViewModels;

namespace MediaLock.App;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer timelineTimer;

    public MainWindow(MainWindowViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
        timelineTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(500),
            DispatcherPriority.Background,
            (_, _) => viewModel.RefreshTimeline(),
            Dispatcher);
        Loaded += OnLoaded;
        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
        UiTheme.ThemeChanged += OnThemeChanged;
    }

    private void OnSourceInitialized(object? sender, EventArgs args) => ApplyFrameTheme();

    private void OnLoaded(object sender, RoutedEventArgs args) => timelineTimer.Start();

    private void OnThemeChanged(object? sender, EventArgs args)
    {
        if (Dispatcher.CheckAccess())
        {
            ApplyFrameTheme();
            return;
        }

        Dispatcher.InvokeAsync(ApplyFrameTheme);
    }

    private void ApplyFrameTheme() => WindowFrameTheme.TryApply(this, UiTheme.Current);

    private void OnClosed(object? sender, EventArgs args)
    {
        timelineTimer.Stop();
        Loaded -= OnLoaded;
        SourceInitialized -= OnSourceInitialized;
        Closed -= OnClosed;
        UiTheme.ThemeChanged -= OnThemeChanged;
    }
}
