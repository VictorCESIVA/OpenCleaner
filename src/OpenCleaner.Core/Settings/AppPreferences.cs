using System.Text.Json;

namespace OpenCleaner.Core.Settings;

public sealed record AppPreferences
{
    public const int MinimumLowSpacePercent = 5;
    public const int MaximumLowSpacePercent = 25;
    public const int DefaultLowSpacePercent = 10;

    public bool DarkTheme { get; init; } = true;
    public int LowSpaceThresholdPercent { get; init; } = DefaultLowSpacePercent;

    public AppPreferences Normalize() => this with
    {
        LowSpaceThresholdPercent = Math.Clamp(LowSpaceThresholdPercent,
            MinimumLowSpacePercent, MaximumLowSpacePercent)
    };
}

/// <summary>Préférences locales (thème, seuil d'alerte). Aucune donnée personnelle.</summary>
public static class PreferencesStore
{
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OpenCleaner", "preferences.json");

    public static AppPreferences Load(string? path = null)
    {
        try
        {
            string file = path ?? DefaultPath;
            if (!File.Exists(file)) return new AppPreferences().Normalize();
            AppPreferences? preferences = JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(file));
            return (preferences ?? new AppPreferences()).Normalize();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                                       or NotSupportedException)
        {
            return new AppPreferences().Normalize();
        }
    }

    public static bool Save(AppPreferences preferences, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        try
        {
            string file = path ?? DefaultPath;
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            string temporary = file + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(preferences.Normalize()));
            File.Move(temporary, file, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }
}
