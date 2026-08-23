using System.Windows.Controls;
using System.Windows.Threading;
using MediaLock.App.ViewModels;
using Xunit;

namespace MediaLock.App.Tests;

[Collection("Localization")]
public sealed class MainWindowContractTests
{
    [Fact]
    public void NowPlayingArtworkAndProgressRemainPresentationOnly()
    {
        WpfTestHost.Run(() =>
        {
            using var viewModel = new MainWindowViewModel(
                new FakeMediaLockApplication(),
                synchronizationContext: null);
            var window = new MainWindow(viewModel);
            window.Show();
            try
            {
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var progress = Assert.Single(WpfTestHost.FindVisualChildren<ProgressBar>(window));
                Assert.False(progress.IsHitTestVisible);
                Assert.False(progress.Focusable);
                Assert.Single(WpfTestHost.FindVisualChildren<Image>(window));
            }
            finally
            {
                window.Close();
            }
        });
    }
}
