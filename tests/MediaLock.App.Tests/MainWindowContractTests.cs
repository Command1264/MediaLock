using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Reflection;
using System.Windows.Data;
using System.Windows.Threading;
using System.Xml.Linq;
using MediaLock.Application;
using MediaLock.App.Localization;
using MediaLock.App.ViewModels;
using MediaLock.Core.Configuration;
using MediaLock.Core.Media;
using MediaLock.Core.Routing;
using Xunit;

namespace MediaLock.App.Tests;

[Collection("Localization")]
public sealed class MainWindowContractTests
{
    [Fact]
    public void NowPlayingArtworkAndTimelineExposeAnAccessibleSeekSlider()
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
                var slider = Assert.Single(WpfTestHost.FindVisualChildren<Slider>(window));
                Assert.True(slider.IsHitTestVisible);
                Assert.True(slider.Focusable);
                Assert.False(string.IsNullOrWhiteSpace(
                    System.Windows.Automation.AutomationProperties.GetName(slider)));
                Assert.Empty(WpfTestHost.FindVisualChildren<ProgressBar>(window));
                Assert.Single(WpfTestHost.FindVisualChildren<Image>(window));
                var routingModes = WpfTestHost.FindVisualChildren<ToggleButton>(window).ToArray();
                Assert.Equal(4, routingModes.Length);
                Assert.Single(routingModes, button => button.IsChecked is true);
                Assert.All(routingModes, button =>
                {
                    Assert.Equal(36, button.MinHeight);
                    Assert.False(string.IsNullOrWhiteSpace(
                        System.Windows.Automation.AutomationProperties.GetName(button)));
                });
                var dismissError = Assert.Single(
                    WpfTestHost.FindVisualChildren<Button>(window),
                    button => Equals(button.Content, "×"));
                Assert.NotNull(dismissError.Command);
                Assert.False(string.IsNullOrWhiteSpace(
                    System.Windows.Automation.AutomationProperties.GetName(dismissError)));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void MinimumWindowKeepsTheStopControlInsideTheVisibleContent()
    {
        WpfTestHost.Run(() =>
        {
            var originalLanguage = UiText.CurrentCulture.Name == "zh-TW"
                ? UiLanguagePreference.TraditionalChinese
                : UiLanguagePreference.EnglishUnitedStates;
            UiText.Apply(UiLanguagePreference.EnglishUnitedStates);
            using var viewModel = new MainWindowViewModel(
                new FakeMediaLockApplication(),
                synchronizationContext: null);
            var window = new MainWindow(viewModel)
            {
                Width = 720,
                Height = 560,
            };
            window.Show();
            try
            {
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                window.UpdateLayout();

                var content = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
                var stop = Assert.Single(
                    WpfTestHost.FindVisualChildren<Button>(window),
                    button => ReferenceEquals(button.Command, viewModel.StopCommand));
                var bounds = stop.TransformToAncestor(content).TransformBounds(
                    new Rect(stop.RenderSize));

                Assert.True(
                    bounds.Right <= content.ActualWidth,
                    $"Stop right edge {bounds.Right:F2} exceeds visible content width " +
                    $"{content.ActualWidth:F2}.");
                Assert.True(
                    stop.ActualWidth >= stop.DesiredSize.Width,
                    $"Stop actual width {stop.ActualWidth:F2} is smaller than its desired width " +
                    $"{stop.DesiredSize.Width:F2}.");
                Assert.True(
                    stop.Margin.Right >= 1,
                    "The final transport control needs a safe right inset so its border is not " +
                    "clipped at the minimum window width.");
            }
            finally
            {
                window.Close();
                UiText.Apply(originalLanguage);
            }
        });
    }

    [Fact]
    public void LockedTargetSelectionSurvivesTheRealListBoxRecoveryProjection()
    {
        WpfTestHost.Run(() =>
        {
            var observedAt = DateTimeOffset.Parse("2026-08-23T10:00:00Z");
            var brave = new MediaSessionSnapshot(
                new SessionKey("video"),
                "Brave",
                PlaybackStatus.Paused,
                MediaCommandCapabilities.All,
                observedAt);
            var music = new MediaSessionSnapshot(
                new SessionKey("music-old"),
                "Brave._crx_music",
                PlaybackStatus.Playing,
                MediaCommandCapabilities.All,
                observedAt);
            var fingerprint = SessionFingerprint.From(music);
            var application = new FakeMediaLockApplication(new MediaLockApplicationState(
                RouterState.Initial with
                {
                    Mode = RoutingMode.AppLock,
                    Status = RouterStatus.Locked,
                    Sessions = [brave, music],
                    LockedTarget = new LockedTarget(fingerprint, music.Key),
                    Revision = 1,
                }));
            using var viewModel = new MainWindowViewModel(
                application,
                synchronizationContext: null);
            var listBox = new ListBox
            {
                DataContext = viewModel,
                ItemsSource = viewModel.Sessions,
            };
            listBox.SetBinding(
                Selector.SelectedItemProperty,
                new Binding(nameof(MainWindowViewModel.SelectedSession))
                {
                    Mode = BindingMode.TwoWay,
                });
            listBox.SelectedItem = Assert.Single(
                viewModel.Sessions,
                session => session.Key == music.Key);

            application.Publish(application.State with
            {
                Router = application.State.Router with
                {
                    Status = RouterStatus.Recovering,
                    Sessions = [brave],
                    LockedTarget = new LockedTarget(fingerprint, ResolvedSession: null),
                    RecoveryEpoch = 2,
                    Revision = 2,
                },
            });

            Assert.Null(listBox.SelectedItem);
            Assert.Null(viewModel.SelectedSession);

            var recovered = music with { Key = new SessionKey("music-new") };
            application.Publish(application.State with
            {
                Router = application.State.Router with
                {
                    Status = RouterStatus.Locked,
                    Sessions = [brave, recovered],
                    LockedTarget = new LockedTarget(fingerprint, recovered.Key),
                    RecoveryEpoch = null,
                    Revision = 3,
                },
            });

            Assert.Equal(recovered.Key,
                Assert.IsType<SessionItemViewModel>(listBox.SelectedItem).Key);
            Assert.Equal(recovered.Key, viewModel.SelectedSession?.Key);
        });
    }

    [Fact]
    public void UnlockedListSelectionRemainsSelectedWhenAnotherTargetEntersRecovery()
    {
        WpfTestHost.Run(() =>
        {
            var observedAt = DateTimeOffset.Parse("2026-08-23T10:00:00Z");
            var brave = new MediaSessionSnapshot(
                new SessionKey("video"),
                "Brave",
                PlaybackStatus.Paused,
                MediaCommandCapabilities.All,
                observedAt);
            var music = new MediaSessionSnapshot(
                new SessionKey("music"),
                "Brave._crx_music",
                PlaybackStatus.Playing,
                MediaCommandCapabilities.All,
                observedAt);
            var fingerprint = SessionFingerprint.From(music);
            var application = new FakeMediaLockApplication(new MediaLockApplicationState(
                RouterState.Initial with
                {
                    Mode = RoutingMode.AppLock,
                    Status = RouterStatus.Locked,
                    Sessions = [brave, music],
                    LockedTarget = new LockedTarget(fingerprint, music.Key),
                    Revision = 1,
                }));
            using var viewModel = new MainWindowViewModel(
                application,
                synchronizationContext: null);
            var window = new MainWindow(viewModel);
            window.Show();
            try
            {
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var listBox = Assert.Single(WpfTestHost.FindVisualChildren<ListBox>(window));
                listBox.SelectedItem = Assert.Single(
                    viewModel.Sessions,
                    session => session.Key == brave.Key);

                var braveSuccessor = brave with { Key = new SessionKey("video-new") };
                application.Publish(application.State with
                {
                    Router = application.State.Router with
                    {
                        Status = RouterStatus.Recovering,
                        Sessions = [braveSuccessor],
                        LockedTarget = new LockedTarget(fingerprint, ResolvedSession: null),
                        RecoveryEpoch = 2,
                        Revision = 2,
                    },
                });
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

                Assert.Equal(braveSuccessor.Key,
                    Assert.IsType<SessionItemViewModel>(listBox.SelectedItem).Key);
                Assert.Equal(braveSuccessor.Key, viewModel.SelectedSession?.Key);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void RepeatedKeyboardSeekCommitsExactlyOneRoutedCommandOnKeyUp()
    {
        WpfTestHost.Run(() =>
        {
            var observedAt = DateTimeOffset.Parse("2026-08-23T06:00:00Z");
            var session = new MediaSessionSnapshot(
                new SessionKey("music"),
                "Brave.Music",
                PlaybackStatus.Paused,
                MediaCommandCapabilities.All,
                observedAt,
                Timeline: new MediaTimeline(
                    TimeSpan.Zero,
                    TimeSpan.FromMinutes(3),
                    TimeSpan.FromSeconds(30),
                    observedAt));
            var state = new MediaLockApplicationState(
                RouterState.Initial with
                {
                    Sessions = [session],
                    WindowsCurrentSession = session.Key,
                    Revision = 1,
                });
            var application = new FakeMediaLockApplication(
                state,
                new RouteDecision(
                    RouteDecisionKind.Routed,
                    RouteReason.WindowsCurrentSession,
                    Target: session.Key,
                    ControlResult: MediaControlResult.Succeeded));
            using var viewModel = new MainWindowViewModel(
                application,
                synchronizationContext: null);
            var window = new MainWindow(viewModel);
            window.Show();
            try
            {
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var slider = Assert.Single(WpfTestHost.FindVisualChildren<Slider>(window));

                RaiseKey(slider, Keyboard.PreviewKeyDownEvent, Key.Right);
                slider.Value = 45;
                RaiseKey(slider, Keyboard.PreviewKeyDownEvent, Key.Right);
                slider.Value = 50;
                RaiseKey(slider, Keyboard.PreviewKeyUpEvent, Key.Right);
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

                var route = Assert.IsType<ApplicationIntent.Route>(
                    Assert.Single(application.Intents));
                Assert.Equal(MediaCommandKind.SeekAbsolute, route.Command.Kind);
                Assert.Equal(TimeSpan.FromSeconds(50), route.Command.AbsolutePosition);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void DirectTrackClickSurvivesKeyboardFocusLossAndCommitsOnPointerRelease()
    {
        WpfTestHost.Run(() =>
        {
            var observedAt = DateTimeOffset.Parse("2026-08-23T06:00:00Z");
            var session = new MediaSessionSnapshot(
                new SessionKey("music"),
                "Brave.Music",
                PlaybackStatus.Paused,
                MediaCommandCapabilities.All,
                observedAt,
                Timeline: new MediaTimeline(
                    TimeSpan.Zero,
                    TimeSpan.FromMinutes(3),
                    TimeSpan.FromSeconds(30),
                    observedAt));
            var state = new MediaLockApplicationState(
                RouterState.Initial with
                {
                    Sessions = [session],
                    WindowsCurrentSession = session.Key,
                    Revision = 1,
                });
            var application = new FakeMediaLockApplication(
                state,
                new RouteDecision(
                    RouteDecisionKind.Routed,
                    RouteReason.WindowsCurrentSession,
                    Target: session.Key,
                    ControlResult: MediaControlResult.Succeeded));
            using var viewModel = new MainWindowViewModel(
                application,
                synchronizationContext: null);
            var window = new MainWindow(viewModel);
            window.Show();
            try
            {
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var slider = Assert.Single(WpfTestHost.FindVisualChildren<Slider>(window));
                Assert.True(viewModel.CanSeek);

                InvokeHandler(
                    window,
                    "OnPlaybackSeekPointerDown",
                    slider,
                    MouseArgs(UIElement.PreviewMouseLeftButtonDownEvent));
                slider.Value = 90;
                Assert.Equal("1:30", viewModel.NowPlayingElapsed);
                InvokeHandler(
                    window,
                    "OnPlaybackSeekLostKeyboardFocus",
                    slider,
                    new KeyboardFocusChangedEventArgs(
                        Keyboard.PrimaryDevice,
                        Environment.TickCount,
                        slider,
                        window)
                    {
                        RoutedEvent = Keyboard.LostKeyboardFocusEvent,
                    });
                Assert.Equal("1:30", viewModel.NowPlayingElapsed);
                InvokeHandler(
                    window,
                    "OnPlaybackSeekPointerUp",
                    slider,
                    MouseArgs(UIElement.PreviewMouseLeftButtonUpEvent));
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

                var route = Assert.IsType<ApplicationIntent.Route>(
                    Assert.Single(application.Intents));
                Assert.Equal(
                    TimeSpan.FromSeconds(viewModel.NowPlayingPositionSeconds),
                    route.Command.AbsolutePosition);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ThumbCaptureLossAllowsTheNextSeekGestureToCommit()
    {
        WpfTestHost.Run(() =>
        {
            var observedAt = DateTimeOffset.Parse("2026-08-23T06:00:00Z");
            var session = new MediaSessionSnapshot(
                new SessionKey("music"),
                "Brave.Music",
                PlaybackStatus.Paused,
                MediaCommandCapabilities.All,
                observedAt,
                Timeline: new MediaTimeline(
                    TimeSpan.Zero,
                    TimeSpan.FromMinutes(3),
                    TimeSpan.FromSeconds(30),
                    observedAt));
            var state = new MediaLockApplicationState(
                RouterState.Initial with
                {
                    Sessions = [session],
                    WindowsCurrentSession = session.Key,
                    Revision = 1,
                });
            var application = new FakeMediaLockApplication(
                state,
                new RouteDecision(
                    RouteDecisionKind.Routed,
                    RouteReason.WindowsCurrentSession,
                    Target: session.Key,
                    ControlResult: MediaControlResult.Succeeded));
            using var viewModel = new MainWindowViewModel(
                application,
                synchronizationContext: null);
            var window = new MainWindow(viewModel);
            window.Show();
            try
            {
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var slider = Assert.Single(WpfTestHost.FindVisualChildren<Slider>(window));
                var thumb = Assert.Single(WpfTestHost.FindVisualChildren<Thumb>(slider));
                var inputSurface = Assert.IsAssignableFrom<UIElement>(
                    System.Windows.Media.VisualTreeHelper.GetParent(slider));

                var pointerDown = MouseArgs(UIElement.PreviewMouseLeftButtonDownEvent);
                pointerDown.Source = thumb;
                inputSurface.RaiseEvent(pointerDown);
                slider.Value = 90;
                Assert.Equal("1:30", viewModel.NowPlayingElapsed);
                slider.RaiseEvent(new System.Windows.Input.MouseEventArgs(
                    Mouse.PrimaryDevice,
                    Environment.TickCount)
                {
                    RoutedEvent = Mouse.LostMouseCaptureEvent,
                });
                RaiseKey(slider, Keyboard.PreviewKeyDownEvent, Key.Right);
                slider.Value = 60;
                RaiseKey(slider, Keyboard.PreviewKeyUpEvent, Key.Right);
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

                Assert.Single(application.Intents);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void PointerGestureStartsOnTheSliderParentBeforeTheSliderClassChangesItsValue()
    {
        var xaml = XDocument.Load(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "MediaLock.App",
            "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var slider = Assert.Single(
            xaml.Descendants(presentation + "Slider"),
            element => (string?)element.Attribute(x + "Name") == "PlaybackSeekSlider");
        var inputSurface = Assert.IsType<XElement>(slider.Parent);

        Assert.Equal("Grid", inputSurface.Name.LocalName);
        Assert.Equal("OnPlaybackSeekPointerDown",
            (string?)inputSurface.Attribute("PreviewMouseLeftButtonDown"));
        Assert.Equal("OnPlaybackSeekPointerUp",
            (string?)inputSurface.Attribute("PreviewMouseLeftButtonUp"));
        Assert.Equal("OnPlaybackSeekPointerMove",
            (string?)inputSurface.Attribute("PreviewMouseMove"));
        Assert.Equal("OnPlaybackSeekMouseCaptureLost",
            (string?)inputSurface.Attribute("LostMouseCapture"));
        Assert.Equal("OnPlaybackSeekTouchMove",
            (string?)inputSurface.Attribute("PreviewTouchMove"));
        Assert.Equal("OnPlaybackSeekTouchCaptureLost",
            (string?)inputSurface.Attribute("LostTouchCapture"));
        Assert.Null(slider.Attribute("PreviewMouseLeftButtonDown"));
        Assert.Null(slider.Attribute("PreviewMouseLeftButtonUp"));
    }

    [Theory]
    [InlineData(-20, 200, 10, 190, 10)]
    [InlineData(0, 200, 10, 190, 10)]
    [InlineData(50, 200, 10, 190, 55)]
    [InlineData(100, 200, 10, 190, 100)]
    [InlineData(200, 200, 10, 190, 190)]
    [InlineData(250, 200, 10, 190, 190)]
    public void TrackScrubMapsAndClampsHorizontalPosition(
        double horizontalPosition,
        double width,
        double minimum,
        double maximum,
        double expected)
    {
        Assert.Equal(
            expected,
            MainWindow.MapSeekValue(horizontalPosition, width, minimum, maximum));
    }

    private static void RaiseKey(UIElement element, RoutedEvent routedEvent, Key key)
    {
        var source = PresentationSource.FromVisual(element) ??
            throw new InvalidOperationException("The Slider must be connected to a presentation source.");
        element.RaiseEvent(new KeyEventArgs(
            Keyboard.PrimaryDevice,
            source,
            Environment.TickCount,
            key)
        {
            RoutedEvent = routedEvent,
        });
    }

    private static MouseButtonEventArgs MouseArgs(RoutedEvent routedEvent) => new(
        Mouse.PrimaryDevice,
        Environment.TickCount,
        MouseButton.Left)
    {
        RoutedEvent = routedEvent,
    };

    private static void InvokeHandler(
        MainWindow window,
        string methodName,
        object sender,
        RoutedEventArgs args)
    {
        var method = typeof(MainWindow).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic) ??
            throw new InvalidOperationException($"Handler '{methodName}' was not found.");
        method.Invoke(window, [sender, args]);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
            !File.Exists(Path.Combine(directory.FullName, "MediaLock.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ??
            throw new DirectoryNotFoundException("Could not locate the MediaLock repository root.");
    }
}
