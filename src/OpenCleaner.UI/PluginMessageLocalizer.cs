using System.Text.RegularExpressions;
using OpenCleaner.UI.Localization;

namespace OpenCleaner.UI;

/// <summary>Traduit les messages renvoyés par les plugins et le service HKLM.</summary>
public static class PluginMessageLocalizer
{
    private static readonly Dictionary<string, string> Exact = new(StringComparer.Ordinal)
    {
        ["Fermez les applications concernées puis relancez l'analyse avant de nettoyer."] = "PluginResult.CloseApps",
        ["L'analyseur disque est en lecture seule."] = "PluginResult.ReadOnly",
        ["Demande absente."] = "MachineMsg.MissingRequest",
        ["Action inconnue."] = "MachineMsg.UnknownAction",
        ["Clé de démarrage inconnue."] = "MachineMsg.UnknownKey",
        ["Nom d'élément invalide."] = "MachineMsg.InvalidName",
        ["Valeur attendue absente ou trop longue."] = "MachineMsg.MissingExpected",
        ["Clé de démarrage introuvable."] = "MachineMsg.KeyNotFound",
        ["Cet élément n'existe plus."] = "MachineMsg.Gone",
        ["Type de valeur non pris en charge."] = "MachineMsg.UnsupportedValue",
        ["La commande a changé depuis l'analyse. Actualisez la liste."] = "MachineMsg.CommandChanged",
        ["Une sauvegarde existe déjà pour cet élément. Réactivez-le d'abord."] = "MachineMsg.BackupExists",
        ["La sauvegarde n'a pas pu être vérifiée."] = "MachineMsg.BackupUnverified",
        ["La valeur n'a pas pu être supprimée."] = "MachineMsg.DeleteFailed",
        ["Élément désactivé. Sauvegarde conservée dans le registre."] = "MachineMsg.Disabled",
        ["Aucune sauvegarde pour cet élément."] = "MachineMsg.NoBackup",
        ["Type de sauvegarde non pris en charge."] = "MachineMsg.UnsupportedBackup",
        ["Un élément du même nom existe déjà au démarrage. Supprimez-le ou renommez-le d'abord."] =
            "MachineMsg.DuplicateName",
        ["La réactivation n'a pas pu être vérifiée."] = "MachineMsg.EnableUnverified",
        ["Élément réactivé au démarrage."] = "MachineMsg.Enabled",
        ["Élément mis à jour."] = "MachineMsg.Updated",
        ["Opération refusée."] = "MachineMsg.Refused",
        ["Opération annulée."] = "MachineMsg.Cancelled",
        ["Exécutable introuvable."] = "Elevated.ExecutableMissing",
        ["Impossible de lancer le processus administrateur."] = "Elevated.AdminStartFailed",
        ["Le processus administrateur s'est arrêté avant de se connecter."] = "Elevated.AdminStopped",
        ["La connexion au processus administrateur a été interrompue."] = "Elevated.ConnectionLost",
        ["Impossible de démarrer winget en administrateur."] = "Elevated.WingetStartFailed",
        ["winget est introuvable pour le compte administrateur. Installez ou réparez le Programme d'installation d'applications Windows pour ce compte."] = "Elevated.WingetMissing"
    };

    private static readonly Regex FilesDeleted = new(
        @"^(\d+)/(\d+) fichiers supprimés \((.+?) d'espace net récupéré, sauvegardes conservées\)$",
        RegexOptions.Compiled);

    private static readonly Regex HistoriesDeleted = new(
        @"^(\d+)/(\d+) historiques supprimés$", RegexOptions.Compiled);

    private static readonly Regex AdminRequired = new(
        @"^Droits administrateur requis : (.+)$", RegexOptions.Compiled);

    private static string T(string key, params object?[] args) => LocalizationService.Instance.Get(key, args);

    public static string Localize(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return message ?? "";
        if (Exact.TryGetValue(message, out string? key)) return T(key);

        Match match = FilesDeleted.Match(message);
        if (match.Success)
            return T("PluginResult.FilesDeleted", match.Groups[1].Value, match.Groups[2].Value,
                match.Groups[3].Value);

        match = HistoriesDeleted.Match(message);
        if (match.Success)
            return T("PluginResult.RegistryDeleted", match.Groups[1].Value, match.Groups[2].Value);

        match = AdminRequired.Match(message);
        if (match.Success)
            return T("MachineMsg.AdminRequired", match.Groups[1].Value);

        return message;
    }
}
