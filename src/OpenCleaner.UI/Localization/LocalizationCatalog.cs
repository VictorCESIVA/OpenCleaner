using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OpenCleaner.UI.Localization;

/// <summary>Catalogue plat clé → texte pour une langue.</summary>
public sealed class LocalizationCatalog
{
    private static readonly Regex PlaceholderPattern = new(@"\{(\d+)\}", RegexOptions.Compiled);
    private readonly Dictionary<string, string> _values;

    public LocalizationCatalog(string language, Dictionary<string, string> values)
    {
        Language = language;
        _values = values;
    }

    public string Language { get; }
    public IReadOnlyDictionary<string, string> Values => _values;
    public int Count => _values.Count;

    public static LocalizationCatalog Parse(string language, string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        ArgumentNullException.ThrowIfNull(json);
        Dictionary<string, string>? values = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        if (values == null) throw new JsonException("Catalogue de traduction vide ou invalide.");
        return new LocalizationCatalog(language, values);
    }

    public bool TryGet(string key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? value) =>
        _values.TryGetValue(key, out value) && !string.IsNullOrWhiteSpace(value);

    public string Format(string key, params object?[] args)
    {
        if (!_values.TryGetValue(key, out string? value)) return key;
        if (args.Length == 0) return value;
        try
        {
            return string.Format(CultureInfo.GetCultureInfo(Language), value, args);
        }
        catch (FormatException)
        {
            return value;
        }
    }

    /// <summary>Vérifie la présence des clés et la cohérence des paramètres {0}, {1}…</summary>
    public static IReadOnlyList<string> ValidatePair(LocalizationCatalog reference, LocalizationCatalog translation)
    {
        var errors = new List<string>();
        foreach (var (key, value) in reference.Values)
        {
            if (!translation.Values.TryGetValue(key, out string? translated))
            {
                errors.Add($"Clé absente de {translation.Language} : {key}");
                continue;
            }
            if (string.IsNullOrWhiteSpace(translated)) errors.Add($"Traduction vide : {key}");
            string expected = string.Join(',', Places(value));
            string actual = string.Join(',', Places(translated));
            if (expected != actual)
                errors.Add($"Paramètres différents pour {key} ({reference.Language} : {expected} ; {translation.Language} : {actual})");
        }
        foreach (string key in translation.Values.Keys.Except(reference.Values.Keys, StringComparer.Ordinal))
            errors.Add($"Clé superflue dans {translation.Language} : {key}");
        return errors;
    }

    private static IEnumerable<string> Places(string value)
        => PlaceholderPattern.Matches(value).Select(match => match.Groups[1].Value).Distinct().Order();
}
