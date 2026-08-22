using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using MediaLock.App.ViewModels;

namespace MediaLock.App.Tray;

internal sealed class TrayIconHost : IDisposable
{
    private readonly TrayViewModel viewModel;
    private readonly NotifyIcon notifyIcon;
    private readonly ToolStripMenuItem statusItem;
    private bool disposed;

    public TrayIconHost(TrayViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        this.viewModel = viewModel;
        statusItem = new ToolStripMenuItem(viewModel.StatusText)
        {
            Enabled = false,
        };
        var menu = new ContextMenuStrip();
        menu.Items.Add(statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item("Show Media Lock", viewModel.ShowCommand));
        menu.Items.Add(Item("Settings…", viewModel.SettingsCommand));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item("Play/Pause", viewModel.TogglePlayPauseCommand));
        menu.Items.Add(Item("Previous", viewModel.PreviousCommand));
        menu.Items.Add(Item("Next", viewModel.NextCommand));
        menu.Items.Add(Item("Windows Auto", viewModel.WindowsAutoCommand));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item("Exit", viewModel.ExitCommand));
        notifyIcon = new NotifyIcon
        {
            Text = "Media Lock",
            Icon = SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true,
        };
        notifyIcon.DoubleClick += OnDoubleClick;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        notifyIcon.DoubleClick -= OnDoubleClick;
        notifyIcon.Visible = false;
        notifyIcon.ContextMenuStrip?.Dispose();
        notifyIcon.Dispose();
        disposed = true;
    }

    private static ToolStripMenuItem Item(string text, IAsyncCommand command)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += (_, _) => command.Execute(null);
        return item;
    }

    private void OnDoubleClick(object? sender, EventArgs e) =>
        viewModel.ShowCommand.Execute(null);

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(TrayViewModel.StatusText))
        {
            statusItem.Text = viewModel.StatusText;
            notifyIcon.Text = $"Media Lock — {viewModel.StatusText}";
        }
        else if (args.PropertyName == nameof(TrayViewModel.ErrorMessage) &&
            viewModel.ErrorMessage is { Length: > 0 } error)
        {
            notifyIcon.ShowBalloonTip(
                timeout: 5000,
                tipTitle: "Media Lock command failed",
                tipText: error,
                tipIcon: ToolTipIcon.Error);
        }
    }
}
