using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using MediaLock.App.ViewModels;
using MediaLock.App.Localization;

namespace MediaLock.App.Tray;

internal sealed class TrayIconHost : IDisposable
{
    private readonly TrayViewModel viewModel;
    private readonly NotifyIcon notifyIcon;
    private readonly ToolStripMenuItem statusItem;
    private readonly ToolStripMenuItem showItem;
    private readonly ToolStripMenuItem settingsItem;
    private readonly ToolStripMenuItem toggleItem;
    private readonly ToolStripMenuItem previousItem;
    private readonly ToolStripMenuItem nextItem;
    private readonly ToolStripMenuItem windowsAutoItem;
    private readonly ToolStripMenuItem exitItem;
    private bool disposed;

    public TrayIconHost(TrayViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        this.viewModel = viewModel;
        statusItem = new ToolStripMenuItem(viewModel.StatusText)
        {
            Enabled = false,
        };
        showItem = Item(UiText.Get("Tray_Show"), viewModel.ShowCommand);
        settingsItem = Item(UiText.Get("Tray_Settings"), viewModel.SettingsCommand);
        toggleItem = Item(UiText.Get("Command_Toggle"), viewModel.TogglePlayPauseCommand);
        previousItem = Item(UiText.Get("Command_Previous"), viewModel.PreviousCommand);
        nextItem = Item(UiText.Get("Command_Next"), viewModel.NextCommand);
        windowsAutoItem = Item(UiText.Get("Mode_WindowsAuto"), viewModel.WindowsAutoCommand);
        exitItem = Item(UiText.Get("Tray_Exit"), viewModel.ExitCommand);
        var menu = new ContextMenuStrip();
        menu.Items.Add(statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(showItem);
        menu.Items.Add(settingsItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(toggleItem);
        menu.Items.Add(previousItem);
        menu.Items.Add(nextItem);
        menu.Items.Add(windowsAutoItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);
        notifyIcon = new NotifyIcon
        {
            Text = "Media Lock",
            Icon = SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true,
        };
        notifyIcon.DoubleClick += OnDoubleClick;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        UiText.CultureChanged += OnCultureChanged;
        ApplyLocalizedText();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        UiText.CultureChanged -= OnCultureChanged;
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

    private void OnCultureChanged(object? sender, EventArgs args) => ApplyLocalizedText();

    private void ApplyLocalizedText()
    {
        showItem.Text = UiText.Get("Tray_Show");
        settingsItem.Text = UiText.Get("Tray_Settings");
        toggleItem.Text = UiText.Get("Command_Toggle");
        previousItem.Text = UiText.Get("Command_Previous");
        nextItem.Text = UiText.Get("Command_Next");
        windowsAutoItem.Text = UiText.Get("Mode_WindowsAuto");
        exitItem.Text = UiText.Get("Tray_Exit");
        notifyIcon.Text = $"Media Lock — {viewModel.StatusText}";
    }

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
                tipTitle: UiText.Get("Tray_CommandFailed"),
                tipText: error,
                tipIcon: ToolTipIcon.Error);
        }
    }
}
