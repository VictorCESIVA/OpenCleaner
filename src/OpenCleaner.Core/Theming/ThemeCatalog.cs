namespace OpenCleaner.Core.Theming;

public sealed record ThemePreset(string Id, string DisplayNameKey, string DarkAccent, string LightAccent);

public static class ThemeCatalog
{
    public const string DefaultId = "mint";
    public const string CustomId = "custom";

    public static IReadOnlyList<ThemePreset> Presets { get; } = Array.AsReadOnly(new[]
    {
        new ThemePreset("mint", "Theme.Mint", "#9BE7C4", "#176649"),
        new ThemePreset("azure", "Theme.Azure", "#8EC5FF", "#145B9C"),
        new ThemePreset("violet", "Theme.Violet", "#C3B1FF", "#5B3FA6"),
        new ThemePreset("teal", "Theme.Teal", "#7EDCD6", "#0C5F60"),
        new ThemePreset("rose", "Theme.Rose", "#FFB0C8", "#9E2A52"),
        new ThemePreset("graphite", "Theme.Graphite", "#B8C4D4", "#44556B")
    });

    public static ThemePreset Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return Presets[0];

        string trimmed = id.Trim();
        foreach (ThemePreset preset in Presets)
        {
            if (string.Equals(preset.Id, trimmed, StringComparison.OrdinalIgnoreCase)) return preset;
        }

        return Presets[0];
    }

    public static bool IsKnown(string? id) =>
        !string.IsNullOrWhiteSpace(id) &&
        Presets.Any(preset => string.Equals(preset.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));
}
