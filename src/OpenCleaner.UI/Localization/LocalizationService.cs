using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using OpenCleaner.Contracts;

namespace OpenCleaner.UI.Localization;

/// <summary>
/// Service de traduction de l'interface. Les catalogues sont embarqués dans l'assembly ;
/// une bascule de langue met à jour les liaisons WPF et prévient le code des textes à reconstruire.
/// </summary>
public sealed class LocalizationService : INotifyPropertyChanged
{
    private static readonly Lazy<LocalizationService> LazyInstance = new(() => new LocalizationService());
    private readonly Dictionary<string, LocalizationCatalog> _catalogs = new(StringComparer.OrdinalIgnoreCase);
    private ResourceDictionary? _resourceDictionary;
    private string? _installedLanguage;
    private string _language = "fr";

    public static LocalizationService Instance => LazyInstance.Value;
    public static IReadOnlyList<string> SupportedLanguages { get; } = ["fr", "en"];
    public static string DefaultLanguage => "fr";

    public string Language => _language;
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? LanguageChanged;

    private LocalizationService()
    {
        LoadEmbeddedCatalog("fr");
        LoadEmbeddedCatalog("en");
        ApplyCulture(_language);
        ApplySizeUnits();
        InstallResourceDictionary();
    }

    /// <summary>Culture d'affichage associée à une langue (nombres, dates, devises).</summary>
    public static CultureInfo CultureFor(string language) =>
        CultureInfo.GetCultureInfo(NormalizeLanguage(language) == "en" ? "en-US" : "fr-FR");

    private static void ApplyCulture(string language)
    {
        CultureInfo culture = CultureFor(language);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    private void ApplySizeUnits() =>
        SizeFormatter.ConfigureUnits(Get("Size.Byte"), Get("Size.Kilobyte"), Get("Size.Megabyte"),
            Get("Size.Gigabyte"), Get("Size.Terabyte"));

    /// <summary>Clé de ressource WPF associée à une clé de catalogue.</summary>
    public static string ResourceKey(string key) => "Loc." + key;

    /// <summary>
    /// Publie le catalogue courant comme dictionnaire de ressources pour que les
    /// <c>{loc:Translate}</c> (DynamicResource) suivent la langue sans recréer les vues.
    /// </summary>
    public bool InstallResourceDictionary()
    {
        if (Application.Current == null) return false;
        if (_resourceDictionary != null && string.Equals(_installedLanguage, _language, StringComparison.Ordinal))
            return true;
        ResourceDictionary next = BuildResourceDictionary(_language);
        ResourceDictionary resources = Application.Current.Resources;
        if (_resourceDictionary != null) resources.MergedDictionaries.Remove(_resourceDictionary);
        _resourceDictionary = next;
        _installedLanguage = _language;
        resources.MergedDictionaries.Add(next);
        return true;
    }

    private ResourceDictionary BuildResourceDictionary(string language)
    {
        var dictionary = new ResourceDictionary();
        if (!_catalogs.TryGetValue(language, out LocalizationCatalog? catalog) &&
            !_catalogs.TryGetValue(DefaultLanguage, out catalog))
            return dictionary;
        foreach (var (key, value) in catalog.Values)
            dictionary[ResourceKey(key)] = value;
        return dictionary;
    }

    /// <summary>Texte traduit indexable depuis XAML : <c>{loc:T Clé}</c>.</summary>
    public string this[string key] => Get(key);

    public string Get(string key, params object?[] args)
    {
        if (string.IsNullOrEmpty(key)) return "";
        if (_catalogs.TryGetValue(_language, out LocalizationCatalog? current) && current.TryGet(key, out string? value))
            return Format(current, key, value, args);
        if (_catalogs.TryGetValue(DefaultLanguage, out LocalizationCatalog? fallback) &&
            fallback.TryGet(key, out string? fallbackValue))
            return Format(fallback, key, fallbackValue, args);
        return key;
    }

    public static string NormalizeLanguage(string? language) =>
        string.Equals(language?.Trim(), "en", StringComparison.OrdinalIgnoreCase) ? "en" : DefaultLanguage;

    public bool SetLanguage(string? language)
    {
        string normalized = NormalizeLanguage(language);
        if (string.Equals(_language, normalized, StringComparison.Ordinal)) return false;
        _language = normalized;
        ApplyCulture(normalized);
        ApplySizeUnits();
        InstallResourceDictionary();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public LocalizationCatalog Catalog(string language) =>
        _catalogs.TryGetValue(NormalizeLanguage(language), out LocalizationCatalog? catalog)
            ? catalog
            : new LocalizationCatalog(language, []);

    internal void LoadCatalogForTests(string language, string json) =>
        _catalogs[NormalizeLanguage(language)] = LocalizationCatalog.Parse(NormalizeLanguage(language), json);

    private static string Format(LocalizationCatalog catalog, string key, string value, object?[] args) =>
        args.Length == 0 ? value : catalog.Format(key, args);

    private void LoadEmbeddedCatalog(string language)
    {
        string suffix = $".Localization.strings-{language}.json";
        Assembly assembly = typeof(LocalizationService).Assembly;
        string? resource = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        if (resource == null) return;
        using Stream? stream = assembly.GetManifestResourceStream(resource);
        if (stream == null) return;
        using var reader = new StreamReader(stream);
        _catalogs[language] = LocalizationCatalog.Parse(language, reader.ReadToEnd());
    }
}
