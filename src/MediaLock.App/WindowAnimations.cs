using System.Windows;
using System.Windows.Media.Animation;

namespace MediaLock.App;

internal static class WindowAnimations
{
    private static readonly Duration FadeDuration = new(TimeSpan.FromMilliseconds(170));

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

        window.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, FadeDuration)
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            });
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
