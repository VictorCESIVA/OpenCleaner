using System.Windows;
using System.Windows.Markup;

namespace OpenCleaner.UI.Localization;

/// <summary>
/// Usage XAML : <c>Text="{loc:Translate Clé}"</c>. S'appuie sur DynamicResource :
/// le dictionnaire est remplacé à chaque changement de langue, sans recréer la vue.
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TranslateExtension : MarkupExtension
{
    public TranslateExtension()
    {
    }

    public TranslateExtension(string key) => Key = key;

    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (Application.Current == null) return LocalizationService.Instance.Get(Key);
        LocalizationService.Instance.InstallResourceDictionary();
        var resource = new DynamicResourceExtension(LocalizationService.ResourceKey(Key));
        return resource.ProvideValue(serviceProvider);
    }
}
