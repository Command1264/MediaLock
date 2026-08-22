using System.Windows.Markup;

namespace MediaLock.App.Localization;

[MarkupExtensionReturnType(typeof(string))]
public sealed class TranslateExtension : MarkupExtension
{
    public TranslateExtension(string key) => Key = key;

    public string Key { get; }

    public override object ProvideValue(IServiceProvider serviceProvider) => UiText.Get(Key);
}
