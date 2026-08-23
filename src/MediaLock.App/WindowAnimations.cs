using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Media;

namespace MediaLock.App;

internal static class WindowAnimations
{
    private static readonly Duration FadeDuration = new(TimeSpan.FromMilliseconds(170));

    public static bool? ShowDialogWithFade(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.BeginAnimation(UIElement.OpacityProperty, null);
        if (!SystemParameters.ClientAreaAnimation)
        {
            window.Opacity = 1;
            return window.ShowDialog();
        }

        window.Opacity = 0;
        RoutedEventHandler? onLoaded = null;
        onLoaded = (_, _) =>
        {
            window.Loaded -= onLoaded;
            BeginShowAnimations(window);
        };
        window.Loaded += onLoaded;
        return window.ShowDialog();
    }

    public static void ShowWithFade(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.BeginAnimation(UIElement.OpacityProperty, null);
        if (!SystemParameters.ClientAreaAnimation)
        {
            window.Opacity = 1;
            if (!window.IsVisible)
            {
                window.Show();
            }

            return;
        }

        window.Opacity = 0;
        if (!window.IsVisible)
        {
            window.Show();
        }

        BeginShowAnimations(window);
    }

    private static void BeginShowAnimations(Window window)
    {
        window.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, FadeDuration)
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            });
        if (window.Content is UIElement content)
        {
            var translate = content.RenderTransform as TranslateTransform ?? new TranslateTransform();
            content.RenderTransform = translate;
            translate.BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation(8, 0, FadeDuration)
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                });
        }
    }

    public static void HideWithFade(Window window, Action completed)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(completed);
        window.BeginAnimation(UIElement.OpacityProperty, null);
        if (!SystemParameters.ClientAreaAnimation || !window.IsVisible)
        {
            window.Hide();
            window.Opacity = 1;
            completed();
            return;
        }

        var animation = new DoubleAnimation(window.Opacity, 0, FadeDuration)
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
        };
        animation.Completed += (_, _) =>
        {
            if (window.IsLoaded)
            {
                window.Hide();
                window.BeginAnimation(UIElement.OpacityProperty, null);
                window.Opacity = 1;
            }

            completed();
        };
        window.BeginAnimation(UIElement.OpacityProperty, animation);
    }
}
