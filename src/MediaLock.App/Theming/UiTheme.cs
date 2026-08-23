using System.Windows;
using MediaLock.Core.Configuration;
using Microsoft.Win32;

namespace MediaLock.App.Theming;

internal enum UiThemeKind
{
    Light,
    Dark,
}

internal static class UiTheme
{
    private const string ThemeMarker = "MediaLockThemeMarker";
    private const string PersonalizeRegistryPath =
        @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static event EventHandler? ThemeChanged;

    public static UiThemeKind Current { get; private set; } = UiThemeKind.Light;

    public static UiThemeKind Resolve(string preference, bool windowsUsesLightTheme) => preference switch
    {
        UiThemePreference.System => windowsUsesLightTheme ? UiThemeKind.Light : UiThemeKind.Dark,
        UiThemePreference.Light => UiThemeKind.Light,
        UiThemePreference.Dark => UiThemeKind.Dark,
        _ => throw new ArgumentOutOfRangeException(
            nameof(preference),
            preference,
            "Unsupported UI theme preference."),
    };

    public static void Apply(System.Windows.Application application, string preference)
    {
        ArgumentNullException.ThrowIfNull(application);
        var next = Resolve(preference, WindowsUsesLightTheme());
        var dictionaries = application.Resources.MergedDictionaries;
        var currentIndex = dictionaries
            .Select((dictionary, index) => new { dictionary, index })
            .Where(item => item.dictionary.Contains(ThemeMarker))
            .Select(item => item.index)
            .DefaultIfEmpty(-1)
            .First();
        if (currentIndex >= 0 && next == Current)
        {
            return;
        }

        var replacement = new ResourceDictionary
        {
            Source = new Uri(
                $"/MediaLock;component/Themes/{next}.xaml",
                UriKind.RelativeOrAbsolute),
        };
        if (currentIndex >= 0)
        {
            dictionaries[currentIndex] = replacement;
        }
        else
        {
            dictionaries.Add(replacement);
        }

        Current = next;
        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    private static bool WindowsUsesLightTheme()
    {
        var value = Registry.GetValue(PersonalizeRegistryPath, "AppsUseLightTheme", 1);
        return value is not int integer || integer != 0;
    }
}
