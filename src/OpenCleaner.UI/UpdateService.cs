using OpenCleaner.UI.Localization;
using Velopack;
using Velopack.Sources;

namespace OpenCleaner.UI;

/// <summary>
/// Service de mise à jour automatique via GitHub Releases.
/// </summary>
public static class UpdateService
{
    private static string T(string key, params object?[] args) => LocalizationService.Instance.Get(key, args);

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
                onStatus(T("Update.Busy"));
                return false;
            }
            var mgr = new UpdateManager(new GithubSource(RepoUrl, null!, false));
            if (!mgr.IsInstalled)
            {
                onStatus(T("Update.NotInstalled"));
                return false;
            }

            onStatus(T("Update.Checking"));
            var newVersion = await mgr.CheckForUpdatesAsync();
            if (newVersion == null)
            {
                onStatus(T("Update.Latest"));
                return false;
            }

            onStatus(T("Update.Downloading", newVersion.TargetFullRelease?.Version));
            await mgr.DownloadUpdatesAsync(newVersion);

            if (UiOperation.IsBusy)
            {
                onStatus(T("Update.ReadyBusy"));
                return false;
            }
            if (!confirmRestart(T("Update.RestartConfirm", newVersion.TargetFullRelease?.Version)))
                return false;

            if (UiOperation.IsBusy)
            {
                onStatus(T("Update.Started"));
                return false;
            }
            mgr.ApplyUpdatesAndRestart(newVersion);
            return true;
        }
        catch (Exception ex)
        {
            onStatus(T("Update.Error", ex.Message));
            return false;
        }
    }
}
