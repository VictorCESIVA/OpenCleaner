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

            // Never restart while a cleanup or a restoration is running.
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                if (System.Windows.Application.Current.MainWindow is MainWindow window)
                    window.NotifyUpdateAvailable();
            });
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
            if (UiOperation.IsBusy)
            {
                onStatus("Terminez l'opération en cours avant de mettre à jour OpenCleaner.");
                return false;
            }
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

            if (UiOperation.IsBusy)
            {
                onStatus("Mise à jour téléchargée. Terminez l'opération en cours avant de redémarrer.");
                return false;
            }
            if (!confirmRestart($"Une mise à jour (v{newVersion.TargetFullRelease?.Version}) est prête. Redémarrer maintenant ?"))
                return false;

            if (UiOperation.IsBusy)
            {
                onStatus("Une opération a démarré. La mise à jour reste téléchargée ; vous pourrez redémarrer ensuite.");
                return false;
            }
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
