using System.Globalization;
using System.Windows.Data;
using OpenCleaner.UI.Localization;

namespace OpenCleaner.UI;

/// <summary>
/// Traduit les descriptions produites par les plugins. Les plugins restent indépendants
/// de la langue ; l'interface applique les clés au moment de l'affichage.
/// </summary>
public static class PluginDescriptionLocalizer
{
    private static readonly Dictionary<string, string> Exact = new(StringComparer.Ordinal)
    {
        ["Fichier temporaire"] = "PluginItem.TempFile",
        ["Rapport d'erreur Windows archivé"] = "PluginItem.WerArchived",
        ["Rapport d'erreur Windows en attente"] = "PluginItem.WerQueued",
        ["Vidage mémoire d'application (CrashDump)"] = "PluginItem.CrashDump",
        ["Cache de shaders DirectX"] = "PluginItem.DirectXShaders",
        ["Temporaire Windows"] = "PluginItem.WindowsTemp",
        ["Prefetch Windows"] = "PluginItem.Prefetch",
        ["Cache npm"] = "PluginItem.NpmCache",
        ["Journal npm"] = "PluginItem.NpmLog",
        ["Cache Yarn"] = "PluginItem.YarnCache",
        ["Cache HTTP NuGet"] = "PluginItem.NuGetHttp",
        ["Cache HTTP pip"] = "PluginItem.PipHttp",
        ["Journal démon Gradle"] = "PluginItem.GradleDaemonLog",
        ["Cache de build Gradle"] = "PluginItem.GradleBuildCache",
        ["Cache VS Code"] = "PluginItem.VsCodeCache",
        ["Cache Discord"] = "PluginItem.DiscordCache",
        ["Cache Spotify"] = "PluginItem.SpotifyCache",
        ["Cache Teams"] = "PluginItem.TeamsCache",
        ["Journaux Teams"] = "PluginItem.TeamsLogs",
        ["Journaux JetBrains"] = "PluginItem.JetBrainsLogs",
        ["Cache ChatGPT"] = "PluginItem.ChatGpt",
        ["Cache Cursor"] = "PluginItem.Cursor",
        ["Cache HTTP Steam"] = "PluginItem.SteamHttp",
        ["Journal Steam"] = "PluginItem.SteamLog",
        ["Images Steam"] = "PluginItem.SteamImages",
        ["Cache shaders Steam"] = "PluginItem.SteamShader",
        ["Journal Office"] = "PluginItem.OfficeLog",
        ["Cache Thunderbird"] = "PluginItem.ThunderbirdCache",
        ["Cache de démarrage Thunderbird"] = "PluginItem.ThunderbirdStartup",
        ["Miniatures Thunderbird"] = "PluginItem.ThunderbirdThumbnails",
        ["Miniatures Explorer"] = "PluginItem.ExplorerThumbnails",
        ["Icônes Explorer"] = "PluginItem.ExplorerIcons",
        ["Cache Slack"] = "PluginItem.SlackCache",
        ["Journaux Slack"] = "PluginItem.SlackLogs",
        ["Cache Notion"] = "PluginItem.NotionCache",
        ["Journaux Notion"] = "PluginItem.NotionLogs",
        ["Cache WhatsApp"] = "PluginItem.WhatsAppCache",
        ["Journaux WhatsApp"] = "PluginItem.WhatsAppLogs",
        ["Cache Signal"] = "PluginItem.SignalCache",
        ["Journaux Signal"] = "PluginItem.SignalLogs",
        ["Journaux Zoom"] = "PluginItem.ZoomLogs",
        ["Cache du Microsoft Store"] = "PluginItem.StoreCache",
        ["Journaux OneDrive"] = "PluginItem.OneDriveLogs",
        ["Journaux d'installation OneDrive"] = "PluginItem.OneDriveSetupLogs",
        ["Rapport de plantage d'application"] = "PluginItem.AppCrashReport",
        ["Motif d'identifiant sensible détecté — vérifier avant toute suppression"] = "PluginItem.PrivacyContent",
        ["Cookies du navigateur"] = "PluginItem.BrowserCookies",
        ["Clé à vérifier"] = "PluginItem.RegistryKey",
        ["Fichiers temporaires"] = "PluginName.Temp",
        ["Navigateurs (Chrome, Edge, Brave, Firefox, Opera, Vivaldi)"] = "PluginName.Browsers",
        ["Miniatures et icônes"] = "PluginName.Thumbnails",
        ["Caches Steam"] = "PluginName.Steam",
        ["Office et Thunderbird"] = "PluginName.Office",
        ["Caches d'applications"] = "PluginName.AppCaches",
        ["Historiques du registre"] = "PluginName.Registry",
        ["Caches des développeurs"] = "PluginName.Dev",
        ["Analyse de confidentialité"] = "PluginName.Privacy",
        ["Détecteur de doublons"] = "PluginName.Duplicates",
        ["Analyseur d'espace disque"] = "PluginName.Disk"
    };

    private static readonly Dictionary<string, string> Suffixes = new(StringComparer.Ordinal)
    {
        ["cache"] = "cache",
        ["cache démarrage"] = "startup cache",
        ["miniatures"] = "thumbnails",
        ["cache GPU"] = "GPU cache",
        ["cookies"] = "cookies",
        ["icônes des sites"] = "site icons",
        ["cache JS"] = "JS cache",
        ["cache hors connexion"] = "offline cache",
        ["historique"] = "history"
    };

    private static string T(string key, params object?[] args) => LocalizationService.Instance.Get(key, args);

    public static string Localize(string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return description ?? "";
        if (Exact.TryGetValue(description, out string? key)) return T(key);

        if (description.StartsWith("Identique à « ", StringComparison.Ordinal) &&
            description.LastIndexOf(" » — copie conservée : ", StringComparison.Ordinal) is int copySeparator and > 0)
        {
            string original = description["Identique à « ".Length..copySeparator];
            string kept = description[(copySeparator + " » — copie conservée : ".Length)..];
            return T("PluginItem.Duplicate", original, kept);
        }
        if (description.StartsWith("Nom à vérifier — « ", StringComparison.Ordinal) &&
            description.EndsWith(" »", StringComparison.Ordinal))
        {
            return T("PluginItem.PrivacyName", description["Nom à vérifier — « ".Length..^2]);
        }
        if (description.StartsWith("Historique utilisateur — ", StringComparison.Ordinal))
            return T("PluginItem.RegistryHistory", description["Historique utilisateur — ".Length..]);

        if (description.StartsWith("Cache ", StringComparison.Ordinal) &&
            !description.StartsWith("Cache de ", StringComparison.Ordinal) &&
            !description.StartsWith("Cache HTTP ", StringComparison.Ordinal) &&
            !description.StartsWith("Cache du ", StringComparison.Ordinal))
            return description[6..] + " cache";
        if (description.StartsWith("Journaux ", StringComparison.Ordinal))
            return description[9..] + " logs";

        int separator = description.LastIndexOf(" — ", StringComparison.Ordinal);
        if (separator > 0 &&
            Suffixes.TryGetValue(description[(separator + 3)..], out string? localizedSuffix))
            return description[..separator] + " — " + localizedSuffix;
        return description;
    }
}

/// <summary>Convertit une description de plugin pour l'affichage courant.</summary>
public sealed class DescriptionConverter : IValueConverter
{
    public static readonly DescriptionConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        PluginDescriptionLocalizer.Localize(value as string);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
