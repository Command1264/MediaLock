using System.Windows;
using System.Windows.Input;
using MediaLock.App.ViewModels;

namespace MediaLock.App;

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
        Closed += OnClosed;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs args)
    {
        if (args.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private async void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs args)
    {
        if (args.Key != Key.Escape || DataContext is not SettingsViewModel viewModel)
        {
            return;
        }

        args.Handled = true;
        await viewModel.CancelCommand.ExecuteAsync(null);
    }

    private void OnClosed(object? sender, EventArgs args)
    {
        Closed -= OnClosed;
        PreviewKeyDown -= OnPreviewKeyDown;
        if (DataContext is SettingsViewModel viewModel)
        {
            viewModel.DiscardChanges();
        }
    }
}
