using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MediaLock.App.Theming;
using MediaLock.App.ViewModels;

namespace MediaLock.App;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel viewModel;
    private readonly DispatcherTimer timelineTimer;
    private SeekGestureInput? activeSeekInput;
    private bool trackPointerScrubActive;
    private bool releasingSeekMouseCapture;
    private TouchDevice? capturedSeekTouch;

    public MainWindow(MainWindowViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        this.viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        timelineTimer = new DispatcherTimer(
            viewModel.TimelineRefreshInterval,
            DispatcherPriority.Background,
            (_, _) => viewModel.RefreshTimeline(),
            Dispatcher);
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Loaded += OnLoaded;
        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
        UiTheme.ThemeChanged += OnThemeChanged;
    }

    private void OnSourceInitialized(object? sender, EventArgs args) => ApplyFrameTheme();

    internal TimeSpan TimelineTimerInterval => timelineTimer.Interval;

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

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(MainWindowViewModel.TimelineRefreshInterval))
        {
            return;
        }

        if (Dispatcher.CheckAccess())
        {
            timelineTimer.Interval = viewModel.TimelineRefreshInterval;
            return;
        }

        Dispatcher.InvokeAsync(
            () => timelineTimer.Interval = viewModel.TimelineRefreshInterval,
            DispatcherPriority.Background);
    }

    private void OnBrowserTargetActionsClick(object sender, RoutedEventArgs args)
    {
        if (sender is not System.Windows.Controls.Button { ContextMenu: { } menu } button)
        {
            return;
        }

        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
        args.Handled = true;
    }

    private void OnNestedMediaSourcePreviewMouseWheel(
        object sender,
        MouseWheelEventArgs args)
    {
        var requestedOffset = MediaSourceScrollViewer.VerticalOffset - args.Delta;
        MediaSourceScrollViewer.ScrollToVerticalOffset(Math.Clamp(
            requestedOffset,
            0,
            MediaSourceScrollViewer.ScrollableHeight));
        args.Handled = true;
    }

    private void OnPlaybackSeekPointerDown(object sender, MouseButtonEventArgs args)
    {
        if (!BeginSeekGesture(SeekGestureInput.Pointer))
        {
            return;
        }

        if (args.OriginalSource is DependencyObject source && IsThumbSource(source))
        {
            return;
        }

        trackPointerScrubActive = true;
        PreviewSeekAt(args.GetPosition(PlaybackSeekSlider).X);
        if (!PlaybackSeekSlider.CaptureMouse())
        {
            trackPointerScrubActive = false;
            CancelSeekGesture();
            return;
        }

        args.Handled = true;
    }

    private void OnPlaybackSeekPointerMove(object sender, System.Windows.Input.MouseEventArgs args)
    {
        if (activeSeekInput != SeekGestureInput.Pointer ||
            !trackPointerScrubActive ||
            args.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        PreviewSeekAt(args.GetPosition(PlaybackSeekSlider).X);
        args.Handled = true;
    }

    private async void OnPlaybackSeekPointerUp(object sender, MouseButtonEventArgs args)
    {
        if (activeSeekInput != SeekGestureInput.Pointer)
        {
            return;
        }

        if (trackPointerScrubActive)
        {
            PreviewSeekAt(args.GetPosition(PlaybackSeekSlider).X);
            trackPointerScrubActive = false;
            releasingSeekMouseCapture = true;
            try
            {
                PlaybackSeekSlider.ReleaseMouseCapture();
            }
            finally
            {
                releasingSeekMouseCapture = false;
            }

            args.Handled = true;
        }

        await CommitSeekGestureAsync(SeekGestureInput.Pointer);
    }

    private void OnPlaybackSeekMouseCaptureLost(
        object sender,
        System.Windows.Input.MouseEventArgs args)
    {
        if (activeSeekInput == SeekGestureInput.Pointer && !releasingSeekMouseCapture)
        {
            trackPointerScrubActive = false;
            CancelSeekGesture();
        }
    }

    private void OnPlaybackSeekTouchDown(object sender, TouchEventArgs args)
    {
        if (!BeginSeekGesture(SeekGestureInput.Touch))
        {
            return;
        }

        PreviewSeekAt(args.GetTouchPoint(PlaybackSeekSlider).Position.X);
        capturedSeekTouch = args.TouchDevice;
        if (!PlaybackSeekSlider.CaptureTouch(args.TouchDevice))
        {
            capturedSeekTouch = null;
            CancelSeekGesture();
            return;
        }

        args.Handled = true;
    }

    private void OnPlaybackSeekTouchMove(object sender, TouchEventArgs args)
    {
        if (activeSeekInput != SeekGestureInput.Touch ||
            capturedSeekTouch != args.TouchDevice)
        {
            return;
        }

        PreviewSeekAt(args.GetTouchPoint(PlaybackSeekSlider).Position.X);
        args.Handled = true;
    }

    private async void OnPlaybackSeekTouchUp(object sender, TouchEventArgs args)
    {
        if (activeSeekInput != SeekGestureInput.Touch ||
            capturedSeekTouch != args.TouchDevice)
        {
            return;
        }

        PreviewSeekAt(args.GetTouchPoint(PlaybackSeekSlider).Position.X);
        capturedSeekTouch = null;
        PlaybackSeekSlider.ReleaseTouchCapture(args.TouchDevice);
        args.Handled = true;
        await CommitSeekGestureAsync(SeekGestureInput.Touch);
    }

    private void OnPlaybackSeekTouchCaptureLost(object sender, TouchEventArgs args)
    {
        if (activeSeekInput == SeekGestureInput.Touch &&
            capturedSeekTouch == args.TouchDevice)
        {
            capturedSeekTouch = null;
            CancelSeekGesture();
        }
    }

    private void OnPlaybackSeekValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> args)
    {
        if (activeSeekInput is not null)
        {
            viewModel.PreviewSeek(TimeSpan.FromSeconds(args.NewValue));
        }
    }

    private void OnPlaybackSeekKeyDown(object sender, System.Windows.Input.KeyEventArgs args)
    {
        if (args.Key == Key.Escape && activeSeekInput is not null)
        {
            CancelSeekGesture();
            args.Handled = true;
            return;
        }

        if (IsSeekKey(args.Key))
        {
            BeginSeekGesture(SeekGestureInput.Keyboard);
        }
    }

    private async void OnPlaybackSeekKeyUp(object sender, System.Windows.Input.KeyEventArgs args)
    {
        if (IsSeekKey(args.Key))
        {
            await CommitSeekGestureAsync(SeekGestureInput.Keyboard);
        }
    }

    private void OnPlaybackSeekLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs args)
    {
        if (activeSeekInput == SeekGestureInput.Keyboard)
        {
            CancelSeekGesture();
        }
    }

    private bool BeginSeekGesture(SeekGestureInput input)
    {
        if (activeSeekInput is not null || !viewModel.CanSeek)
        {
            return false;
        }

        activeSeekInput = input;
        viewModel.BeginSeekPreview();
        return true;
    }

    private async Task CommitSeekGestureAsync(SeekGestureInput input)
    {
        if (activeSeekInput != input)
        {
            return;
        }

        activeSeekInput = null;
        await viewModel.CommitSeekPreviewAsync();
    }

    private void CancelSeekGesture()
    {
        if (activeSeekInput is not { } input)
        {
            return;
        }

        activeSeekInput = null;
        if (input == SeekGestureInput.Pointer)
        {
            trackPointerScrubActive = false;
            if (PlaybackSeekSlider.IsMouseCaptureWithin)
            {
                Mouse.Capture(null);
            }
        }

        if (input == SeekGestureInput.Touch && capturedSeekTouch is { } touch)
        {
            capturedSeekTouch = null;
            PlaybackSeekSlider.ReleaseTouchCapture(touch);
        }

        viewModel.CancelSeekPreview();
    }

    private static bool IsSeekKey(Key key) => key is
        Key.Left or Key.Right or Key.Up or Key.Down or
        Key.PageUp or Key.PageDown or Key.Home or Key.End;

    internal static double MapSeekValue(
        double horizontalPosition,
        double width,
        double minimum,
        double maximum)
    {
        if (!double.IsFinite(horizontalPosition) ||
            !double.IsFinite(width) ||
            !double.IsFinite(minimum) ||
            !double.IsFinite(maximum) ||
            width <= 0 ||
            maximum <= minimum)
        {
            return minimum;
        }

        var ratio = Math.Clamp(horizontalPosition / width, 0, 1);
        return minimum + ((maximum - minimum) * ratio);
    }

    private void PreviewSeekAt(double horizontalPosition)
    {
        var value = MapSeekValue(
            horizontalPosition,
            PlaybackSeekSlider.ActualWidth,
            PlaybackSeekSlider.Minimum,
            PlaybackSeekSlider.Maximum);
        PlaybackSeekSlider.SetCurrentValue(RangeBase.ValueProperty, value);
    }

    private bool IsThumbSource(DependencyObject source)
    {
        DependencyObject? current = source;
        while (current is not null && current != PlaybackSeekSlider)
        {
            if (current is Thumb)
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private void OnClosed(object? sender, EventArgs args)
    {
        CancelSeekGesture();
        timelineTimer.Stop();
        Loaded -= OnLoaded;
        SourceInitialized -= OnSourceInitialized;
        Closed -= OnClosed;
        UiTheme.ThemeChanged -= OnThemeChanged;
        viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private enum SeekGestureInput
    {
        Pointer,
        Touch,
        Keyboard,
    }
}
