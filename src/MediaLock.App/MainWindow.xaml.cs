using System.Windows;
using MediaLock.App.Theming;
using MediaLock.App.ViewModels;

namespace MediaLock.App;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
        UiTheme.ThemeChanged += OnThemeChanged;
    }

    private void OnSourceInitialized(object? sender, EventArgs args) => ApplyFrameTheme();

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
        SourceInitialized -= OnSourceInitialized;
        Closed -= OnClosed;
        UiTheme.ThemeChanged -= OnThemeChanged;
    }
}
