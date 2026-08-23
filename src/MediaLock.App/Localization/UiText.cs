using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Windows.Data;
using MediaLock.Core.Configuration;

namespace MediaLock.App.Localization;

internal static class UiText
{
    private static readonly ResourceManager Resources = new(
        "MediaLock.App.Resources.Strings",
        Assembly.GetExecutingAssembly());
    private static readonly CultureInfo WindowsUiCulture = CultureInfo.CurrentUICulture;
    private static readonly LocalizedTextBindingSource LocalizedBindingSource = new();
    private static bool cultureApplied;

    public static event EventHandler? CultureChanged;

    public static object BindingSource => LocalizedBindingSource;

    public static CultureInfo CurrentCulture { get; private set; } =
        CultureInfo.GetCultureInfo(UiLanguagePreference.EnglishUnitedStates);

    public static void Apply(string preference)
    {
        var culture = ResolveCulture(preference, WindowsUiCulture);
        var changed = !culture.Equals(CurrentCulture);
        CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        if (!changed && cultureApplied)
        {
            return;
        }

        cultureApplied = true;
        LocalizedBindingSource.NotifyChanged();
        CultureChanged?.Invoke(null, EventArgs.Empty);
    }

    public static CultureInfo ResolveCulture(string preference, CultureInfo systemCulture)
    {
        ArgumentNullException.ThrowIfNull(systemCulture);
        return preference switch
        {
            UiLanguagePreference.EnglishUnitedStates =>
                CultureInfo.GetCultureInfo(UiLanguagePreference.EnglishUnitedStates),
            UiLanguagePreference.TraditionalChinese =>
                CultureInfo.GetCultureInfo(UiLanguagePreference.TraditionalChinese),
            UiLanguagePreference.System when IsTraditionalChinese(systemCulture) =>
                CultureInfo.GetCultureInfo(UiLanguagePreference.TraditionalChinese),
            UiLanguagePreference.System =>
                CultureInfo.GetCultureInfo(UiLanguagePreference.EnglishUnitedStates),
            _ => throw new ArgumentOutOfRangeException(
                nameof(preference),
                preference,
                "Unsupported UI language preference."),
        };
    }

    private static bool IsTraditionalChinese(CultureInfo culture) =>
        culture.Name.Equals("zh-TW", StringComparison.OrdinalIgnoreCase) ||
        culture.Name.Equals("zh-HK", StringComparison.OrdinalIgnoreCase) ||
        culture.Name.Equals("zh-MO", StringComparison.OrdinalIgnoreCase) ||
        culture.Name.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase) ||
        culture.Parent.Name.Equals("zh-Hant", StringComparison.OrdinalIgnoreCase);

    public static string Get(string key) => Get(key, CurrentCulture);

    public static string Get(string key, CultureInfo culture)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(culture);
        return Resources.GetString(key, culture) ??
            throw new MissingManifestResourceException($"Missing UI text resource '{key}'.");
    }

    public static string Format(string key, params object?[] arguments) =>
        string.Format(CurrentCulture, Get(key), arguments);

    private sealed class LocalizedTextBindingSource : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public string this[string key] => Get(key);

        public void NotifyChanged() => PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(System.Windows.Data.Binding.IndexerName));
    }
}
