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
    public void MediaSourceGroupsShareOneBoundedVerticalScrollSurface()
    {
        var xaml = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "MediaLock.App", "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var scrollViewer = Assert.Single(
            xaml.Descendants(presentation + "ScrollViewer"),
            element => (string?)element.Attribute(x + "Name") == "MediaSourceScrollViewer");
        Assert.Equal("Auto", (string?)scrollViewer.Attribute("VerticalScrollBarVisibility"));
        Assert.Equal("Disabled", (string?)scrollViewer.Attribute("HorizontalScrollBarVisibility"));
        Assert.Equal("3", (string?)scrollViewer.Attribute("Grid.Row"));
        Assert.Contains(
            scrollViewer.Descendants(presentation + "ItemsControl"),
            element => (string?)element.Attribute("ItemsSource") == "{Binding MediaSourceGroups}");
        Assert.Contains(
            scrollViewer.Descendants(presentation + "ToggleButton"),
            element => (string?)element.Attribute("Command") ==
                "{Binding DataContext.SelectMediaSourceGroupCommand, RelativeSource={RelativeSource AncestorType=Window}}");
        Assert.Contains(
            scrollViewer.Descendants(presentation + "Button"),
            element => (string?)element.Attribute("Command") ==
                "{Binding DataContext.ToggleMediaSourceGroupCommand, RelativeSource={RelativeSource AncestorType=Window}}");
    }

    [Fact]
    public void BrowserTargetsUseSharedLockAndExposePerRowAuthorizationRevocation()
    {
        var xaml = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "MediaLock.App", "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var list = Assert.Single(
            xaml.Descendants(presentation + "ListBox"),
            element => (string?)element.Attribute("ItemsSource") == "{Binding BrowserTargets}");
        Assert.Equal(
            "{Binding DataContext.SelectedBrowserTarget, RelativeSource={RelativeSource AncestorType=Window}}",
            (string?)list.Attribute("SelectedItem"));
        var details = Assert.Single(
            list.Descendants(presentation + "TextBlock"),
            element => (string?)element.Attribute("Text") == "{Binding TargetDetails}");
        Assert.Equal("{Binding TargetDetails}",
            (string?)details.Attribute("AutomationProperties.HelpText"));
        Assert.DoesNotContain(
            xaml.Descendants(),
            element => ((string?)element.Attribute("Command"))?.Contains(
                "LockBrowserTargetCommand",
                StringComparison.Ordinal) is true);
        Assert.Contains(
            xaml.Descendants(),
            element => (string?)element.Attribute("Command") == "{Binding LockCommand}");

        var overflowButton = Assert.Single(
            list.Descendants(presentation + "Button"),
            element => (string?)element.Attribute("Content") == "…");
        Assert.Equal("2", (string?)overflowButton.Attribute("Grid.Column"));
        var revokeAction = Assert.Single(
            overflowButton.Descendants(presentation + "MenuItem"),
            element => ((string?)element.Attribute("Command"))?.Contains(
                "RevokeBrowserTargetAuthorizationCommand",
                StringComparison.Ordinal) is true);
        Assert.Equal(
            "{Binding PlacementTarget.DataContext, RelativeSource={RelativeSource AncestorType=ContextMenu}}",
            (string?)revokeAction.Attribute("CommandParameter"));
    }

    [Fact]
    public void BrowserAndWindowsRowsShareAlignedPlaybackStatusChrome()
    {
        var xaml = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "MediaLock.App", "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var lists = xaml.Descendants(presentation + "ListBox")
            .Where(element => (string?)element.Attribute("ItemsSource") is
                "{Binding BrowserTargets}" or "{Binding Sessions}")
            .ToArray();
        Assert.Equal(2, lists.Length);

        foreach (var list in lists)
        {
            Assert.Equal(
                "OnNestedMediaSourcePreviewMouseWheel",
                (string?)list.Attribute("PreviewMouseWheel"));
            var status = Assert.Single(
                list.Descendants(presentation + "Border"),
                element => (string?)element.Attribute("Style") ==
                    "{StaticResource MediaPlaybackStatusPillStyle}");
            Assert.Equal("3", (string?)status.Attribute("Grid.Column"));

            var row = status.Ancestors(presentation + "Grid").First();
            var columns = Assert.Single(row.Elements(presentation + "Grid.ColumnDefinitions"))
                .Elements(presentation + "ColumnDefinition")
                .ToArray();
            Assert.Equal(4, columns.Length);
            Assert.Equal("36", (string?)columns[2].Attribute("Width"));
        }
    }

    [Fact]
    public void BrowserTargetOverflowOwnsItsThemeTemplatesWithoutAnIconGutter()
    {
        var controls = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "MediaLock.App", "Themes", "Controls.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var buttonStyle = Assert.Single(
            controls.Descendants(presentation + "Style"),
            element => (string?)element.Attribute(x + "Key") == "OverflowButtonStyle");
        Assert.Contains(
            buttonStyle.Elements(presentation + "Setter"),
            element => (string?)element.Attribute("Property") == "Template");
        Assert.DoesNotContain(
            buttonStyle.Descendants(presentation + "Trigger")
                .Elements(presentation + "Setter"),
            element => (string?)element.Attribute("Property") is
                "Background" or "BorderBrush");
        Assert.Single(buttonStyle.Descendants(presentation + "DropShadowEffect"));

        var mainWindow = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "MediaLock.App", "MainWindow.xaml"));
        var overflowButton = Assert.Single(
            mainWindow.Descendants(presentation + "Button"),
            element => (string?)element.Attribute("Style") ==
                "{StaticResource OverflowButtonStyle}");
        Assert.Null(overflowButton.Attribute("ToolTip"));

        var contextMenuStyle = Assert.Single(
            controls.Descendants(presentation + "Style"),
            element => (string?)element.Attribute(x + "Key") == "BrowserTargetContextMenuStyle");
        Assert.Contains(
            contextMenuStyle.Elements(presentation + "Setter"),
            element => (string?)element.Attribute("Property") == "Template");

        var menuItemStyle = Assert.Single(
            controls.Descendants(presentation + "Style"),
            element => (string?)element.Attribute(x + "Key") == "BrowserTargetMenuItemStyle");
        var templateSetter = Assert.Single(
            menuItemStyle.Elements(presentation + "Setter"),
            element => (string?)element.Attribute("Property") == "Template");
        var header = Assert.Single(
            templateSetter.Descendants(presentation + "ContentPresenter"));
        Assert.Equal("Center", (string?)header.Attribute("VerticalAlignment"));
        Assert.DoesNotContain(
            templateSetter.Descendants(presentation + "ColumnDefinition"),
            element => (string?)element.Attribute("Width") is "Auto" or "16" or "20");
    }

    [Fact]
    public void PlaybackStatusPillsShareOneAdaptiveWidthAndCenterTheirText()
    {
        var mainWindow = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "MediaLock.App", "MainWindow.xaml"));
        var controls = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "MediaLock.App", "Themes", "Controls.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var groups = Assert.Single(
            mainWindow.Descendants(presentation + "ItemsControl"),
            element => (string?)element.Attribute("ItemsSource") == "{Binding MediaSourceGroups}");
        Assert.Equal("True", (string?)groups.Attribute("Grid.IsSharedSizeScope"));

        var pillStyle = Assert.Single(
            controls.Descendants(presentation + "Style"),
            element => (string?)element.Attribute(x + "Key") == "MediaPlaybackStatusPillStyle");
        Assert.DoesNotContain(
            pillStyle.Elements(presentation + "Setter"),
            element => (string?)element.Attribute("Property") is "Width" or "MinWidth");
        Assert.Contains(
            pillStyle.Elements(presentation + "Setter"),
            element => (string?)element.Attribute("Property") == "HorizontalAlignment" &&
                (string?)element.Attribute("Value") == "Stretch");

        var textStyle = Assert.Single(
            controls.Descendants(presentation + "Style"),
            element => (string?)element.Attribute(x + "Key") == "MediaPlaybackStatusTextStyle");
        Assert.Contains(
            textStyle.Elements(presentation + "Setter"),
            element => (string?)element.Attribute("Property") == "TextAlignment" &&
                (string?)element.Attribute("Value") == "Center");
        Assert.Equal(
            2,
            mainWindow.Descendants(presentation + "TextBlock").Count(
                element => (string?)element.Attribute("Style") ==
                    "{StaticResource MediaPlaybackStatusTextStyle}"));
    }

    [Fact]
    public void NestedMediaRowsForwardTheMouseWheelToTheSharedOuterScroller()
    {
        WpfTestHost.Run(() =>
        {
            var observedAt = DateTimeOffset.Parse("2026-08-28T00:00:00Z");
            var sessions = Enumerable.Range(1, 12)
                .Select(index => new MediaSessionSnapshot(
                    new SessionKey($"wheel-session-{index}"),
                    $"Wheel.App.{index}",
                    PlaybackStatus.Playing,
                    MediaCommandCapabilities.Play,
                    observedAt,
                    Metadata: new MediaMetadata($"Track {index}", null, null, null)))
                .ToArray();
            var application = new FakeMediaLockApplication(
                MediaLockApplicationState.Initial with
                {
                    Router = RouterState.Initial with
                    {
                        Sessions = [.. sessions],
                        Targets = [.. sessions.Select(MediaTargetSnapshot.FromGsmtc)],
                    },
                });
            using var viewModel = new MainWindowViewModel(
                application,
                synchronizationContext: SynchronizationContext.Current);
            var window = new MainWindow(viewModel)
            {
                Width = 720,
                Height = 560,
            };
            window.Show();

            try
            {
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var outerScroller = Assert.Single(
                    WpfTestHost.FindVisualChildren<ScrollViewer>(window),
                    candidate => candidate.Name == "MediaSourceScrollViewer");
                var nestedList = Assert.Single(
                    WpfTestHost.FindVisualChildren<ListBox>(window),
                    candidate => candidate.Items.Count == 1 &&
                        candidate.Items[0] is SessionItemViewModel item &&
                        item.Key == sessions[0].Key);
                Assert.True(outerScroller.ScrollableHeight > 0);

                var wheel = new MouseWheelEventArgs(
                    Mouse.PrimaryDevice,
                    Environment.TickCount,
                    -120)
                {
                    RoutedEvent = UIElement.PreviewMouseWheelEvent,
                    Source = nestedList,
                };
                nestedList.RaiseEvent(wheel);
                window.UpdateLayout();

                Assert.True(wheel.Handled);
                Assert.True(outerScroller.VerticalOffset > 0);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void SourceIdentityPresentationKeepsRawDetailsAccessible()
    {
        var xaml = XDocument.Load(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "MediaLock.App",
            "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var sessionSource = Assert.Single(
            xaml.Descendants(presentation + "TextBlock"),
            element => (string?)element.Attribute("Text") ==
                "{Binding SourceApplicationDisplayName}");
        Assert.Equal(
            "{Binding SourceApplicationDetails}",
            (string?)sessionSource.Attribute("ToolTip"));
        Assert.Equal(
            "{Binding SourceApplicationDetails}",
            (string?)sessionSource.Attribute("AutomationProperties.HelpText"));

        var target = Assert.Single(
            xaml.Descendants(presentation + "TextBlock"),
            element => (string?)element.Attribute("Text") == "{Binding TargetDescription}");
        Assert.Equal(
            "{Binding CurrentTargetSourceDetails}",
            (string?)target.Attribute("ToolTip"));
        Assert.Equal(
            "{Binding CurrentTargetSourceDetails}",
            (string?)target.Attribute("AutomationProperties.HelpText"));
    }

    [Fact]
    public void PlaybackStateLockUsesTwoContentSizedSelectionControlsOnTheCurrentTargetSurface()
    {
        var xaml = XDocument.Load(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "MediaLock.App",
            "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var panel = Assert.Single(
            xaml.Descendants(presentation + "Grid"),
            element => (string?)element.Attribute(x + "Name") ==
                "PlaybackStateLockPanel");
        var columns = Assert.Single(panel.Elements(presentation + "Grid.ColumnDefinitions"));
        Assert.Equal(2, columns.Elements(presentation + "ColumnDefinition").Count());
        var label = Assert.Single(
            panel.Elements(presentation + "TextBlock"),
            element => (string?)element.Attribute(x + "Name") ==
                "PlaybackStateLockLabel");
        Assert.Equal("14", (string?)label.Attribute("FontSize"));
        Assert.Equal("SemiBold", (string?)label.Attribute("FontWeight"));
        Assert.Equal("Center", (string?)label.Attribute("VerticalAlignment"));
        var controls = panel.Descendants(presentation + "ToggleButton")
            .Where(element =>
                ((string?)element.Attribute(x + "Name"))?.StartsWith(
                    "PlaybackStateLock",
                    StringComparison.Ordinal) is true)
            .ToArray();

        Assert.Equal(2, controls.Length);
        Assert.Single(panel.Elements(presentation + "WrapPanel"));
        Assert.Empty(panel.Elements(presentation + "UniformGrid"));
        Assert.All(controls, control =>
        {
            Assert.Equal("{StaticResource StateLockToggleStyle}",
                (string?)control.Attribute("Style"));
            Assert.Null(control.Attribute("MinWidth"));
            Assert.NotNull(control.Attribute("Command"));
            Assert.NotNull(control.Attribute("IsChecked"));
            Assert.NotNull(control.Attribute("AutomationProperties.Name"));
        });
        Assert.Contains(controls, control =>
            (string?)control.Attribute("Command") ==
                "{Binding PlaybackStateLockOffCommand}");
        Assert.Contains(controls, control =>
            (string?)control.Attribute("Command") == "{Binding KeepPlayingCommand}");

        var controlsXaml = XDocument.Load(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "MediaLock.App",
            "Themes",
            "Controls.xaml"));
        var style = Assert.Single(
            controlsXaml.Descendants(presentation + "Style"),
            element => (string?)element.Attribute(x + "Key") ==
                "StateLockToggleStyle");
        Assert.Null(style.Attribute("BasedOn"));
        var template = Assert.Single(style.Descendants(presentation + "ControlTemplate"));
        var content = Assert.Single(template.Descendants(presentation + "ContentPresenter"));
        Assert.Equal("1", (string?)content.Attribute("Grid.Column"));
        Assert.Equal("Right", (string?)content.Attribute("HorizontalAlignment"));
        var selectedMark = Assert.Single(
            template.Descendants(presentation + "TextBlock"),
            element => (string?)element.Attribute(x + "Name") == "SelectedMark");
        Assert.Null(selectedMark.Attribute("HorizontalAlignment"));
    }

    [Fact]
    public void PlaybackStateLockButtonsMeasureTheirLocalizedContentIndependently()
    {
        WpfTestHost.Run(() =>
        {
            var originalLanguage = UiText.CurrentCulture.Name == "zh-TW"
                ? UiLanguagePreference.TraditionalChinese
                : UiLanguagePreference.EnglishUnitedStates;
            UiText.Apply(UiLanguagePreference.EnglishUnitedStates);
            using var viewModel = new MainWindowViewModel(
                new FakeMediaLockApplication(),
                synchronizationContext: SynchronizationContext.Current);
            var window = new MainWindow(viewModel);
            window.Show();
            try
            {
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var buttons = WpfTestHost.FindVisualChildren<ToggleButton>(window).ToArray();
                var off = Assert.Single(buttons, button =>
                    ReferenceEquals(button.Command, viewModel.PlaybackStateLockOffCommand));
                var keepPlaying = Assert.Single(buttons, button =>
                    ReferenceEquals(button.Command, viewModel.KeepPlayingCommand));
                Assert.True(keepPlaying.ActualWidth > off.ActualWidth);

                UiText.Apply(UiLanguagePreference.TraditionalChinese);
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.True(keepPlaying.ActualWidth > off.ActualWidth);
            }
            finally
            {
                window.Close();
                UiText.Apply(originalLanguage);
            }
        });
    }

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
                var selectionControls = WpfTestHost.FindVisualChildren<ToggleButton>(window).ToArray();
                var routingModes = selectionControls.Where(button =>
                    ReferenceEquals(button.Command, viewModel.LockCommand) ||
                    ReferenceEquals(button.Command, viewModel.AppLockCommand) ||
                    ReferenceEquals(button.Command, viewModel.PriorityRulesCommand) ||
                    ReferenceEquals(button.Command, viewModel.WindowsAutoCommand)).ToArray();
                Assert.Equal(4, routingModes.Length);
                Assert.Single(routingModes, button => button.IsChecked is true);
                var playbackStateLocks = selectionControls.Where(button =>
                    ReferenceEquals(button.Command, viewModel.PlaybackStateLockOffCommand) ||
                    ReferenceEquals(button.Command, viewModel.KeepPlayingCommand)).ToArray();
                Assert.Equal(2, playbackStateLocks.Length);
                Assert.Single(playbackStateLocks, button => button.IsChecked is true);
                Assert.All(selectionControls, button =>
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
    public void TimelineTimerFollowsPlaybackRateAndReturnsToIdleCadenceWhenPaused()
    {
        WpfTestHost.Run(() =>
        {
            var observedAt = DateTimeOffset.Parse("2026-08-28T00:00:00Z");
            MediaTargetSnapshot Target(PlaybackStatus status, double rate) =>
                MediaTargetSnapshot.FromBrowserPageBinding(
                    "page-binding-window-refresh-cadence",
                    new MediaTargetPresentation(
                        "Big Buck Bunny",
                        status,
                        MediaCommandCapabilities.None,
                        observedAt,
                        Timeline: new MediaTimeline(
                            TimeSpan.Zero,
                            TimeSpan.FromMinutes(10),
                            TimeSpan.FromSeconds(30),
                            observedAt),
                        ReportedPlaybackRate: rate));
            var initial = Target(PlaybackStatus.Playing, 1);
            var application = new FakeMediaLockApplication(MediaLockApplicationState.Initial with
            {
                Router = RouterState.Initial with
                {
                    Mode = RoutingMode.SessionLock,
                    Status = RouterStatus.Locked,
                    Targets = [initial],
                    LockedMediaTarget = initial.Id,
                    Revision = 1,
                },
            });
            using var viewModel = new MainWindowViewModel(
                application,
                synchronizationContext: SynchronizationContext.Current);
            var window = new MainWindow(viewModel);
            window.Show();
            try
            {
                Assert.Equal(500, window.TimelineTimerInterval.TotalMilliseconds);

                application.Publish(application.State with
                {
                    Router = application.State.Router with
                    {
                        Targets = [Target(PlaybackStatus.Playing, 10)],
                        Revision = 2,
                    },
                });
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.Equal(100, window.TimelineTimerInterval.TotalMilliseconds);

                application.Publish(application.State with
                {
                    Router = application.State.Router with
                    {
                        Targets = [Target(PlaybackStatus.Paused, 10)],
                        Revision = 3,
                    },
                });
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.Equal(500, window.TimelineTimerInterval.TotalMilliseconds);
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
                var listBox = Assert.Single(
                    WpfTestHost.FindVisualChildren<ListBox>(window),
                    candidate => candidate.ItemsSource is IEnumerable<SessionItemViewModel> items &&
                        items.Any(item => item.Key == brave.Key));
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

                listBox = Assert.Single(
                    WpfTestHost.FindVisualChildren<ListBox>(window),
                    candidate => candidate.ItemsSource is IEnumerable<SessionItemViewModel> items &&
                        items.Any(item => item.Key == braveSuccessor.Key));
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
                    ControlOutcome: MediaCommandOutcome.Succeeded));
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
                    ControlOutcome: MediaCommandOutcome.Succeeded));
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
                var expectedPosition = TimeSpan.FromSeconds(viewModel.NowPlayingPositionSeconds);
                var actualPosition = Assert.IsType<TimeSpan>(route.Command.AbsolutePosition);
                Assert.InRange(
                    (expectedPosition - actualPosition).Duration(),
                    TimeSpan.Zero,
                    TimeSpan.FromTicks(1));
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
                    ControlOutcome: MediaCommandOutcome.Succeeded));
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
