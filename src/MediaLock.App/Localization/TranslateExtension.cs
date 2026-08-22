using System.Windows.Markup;

namespace MediaLock.App.Localization;

[MarkupExtensionReturnType(typeof(string))]
public sealed class TranslateExtension : MarkupExtension
{
    public TranslateExtension(string key) => Key = key;

    public string Key { get; }

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new System.Windows.Data.Binding($"[{Key}]")
        {
            Mode = System.Windows.Data.BindingMode.OneWay,
            Source = UiText.BindingSource,
        }.ProvideValue(serviceProvider);
}
