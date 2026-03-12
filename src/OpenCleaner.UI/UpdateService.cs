using Velopack;
using Velopack.Sources;

namespace OpenCleaner.UI;

/// <summary>
/// Service de mise à jour automatique via GitHub Releases.
/// </summary>
public static class UpdateService
{
    private const string RepoUrl = "https://github.com/VictorCESIVA/OpenCleaner";

    /// <summary>
    /// Vérifie les mises à jour au démarrage (appelé en arrière-plan, non bloquant).
    /// </summary>
    public static async Task CheckForUpdatesOnStartupAsync()
    {
        try
        {
            var mgr = new UpdateManager(new GithubSource(RepoUrl, null!, false));
            if (!mgr.IsInstalled)
                return; // App non installée via Velopack (ex: exe portable), pas de maj auto

            var newVersion = await mgr.CheckForUpdatesAsync();
            if (newVersion == null)
                return;

            // Mise à jour disponible : télécharger en arrière-plan
            await mgr.DownloadUpdatesAsync(newVersion);
            mgr.ApplyUpdatesAndRestart(newVersion);
        }
        catch
        {
            // Silencieux : ne pas bloquer le démarrage
        }
    }

    /// <summary>
    /// Vérifie manuellement les mises à jour (depuis le menu Paramètres).
    /// Retourne true si une mise à jour a été appliquée (app redémarrée).
    /// </summary>
    public static async Task<bool> CheckForUpdatesManualAsync(
        Action<string> onStatus,
        Func<string, bool> confirmRestart)
    {
        try
        {
            var mgr = new UpdateManager(new GithubSource(RepoUrl, null!, false));
            if (!mgr.IsInstalled)
            {
                onStatus("Mises à jour automatiques : installez OpenCleaner via Setup.exe pour les activer.");
                return false;
            }

            onStatus("Vérification en cours…");
            var newVersion = await mgr.CheckForUpdatesAsync();
            if (newVersion == null)
            {
                onStatus("Vous avez déjà la dernière version.");
                return false;
            }

            onStatus($"Téléchargement de la v{newVersion.TargetFullRelease?.Version}…");
            await mgr.DownloadUpdatesAsync(newVersion);

            if (!confirmRestart($"Une mise à jour (v{newVersion.TargetFullRelease?.Version}) est prête. Redémarrer maintenant ?"))
                return false;

            mgr.ApplyUpdatesAndRestart(newVersion);
            return true;
        }
        catch (Exception ex)
        {
            onStatus($"Erreur : {ex.Message}");
            return false;
        }
    }
}
