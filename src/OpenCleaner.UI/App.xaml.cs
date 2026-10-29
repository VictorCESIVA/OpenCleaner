using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenCleaner.Contracts;
using OpenCleaner.Core.Scheduler;
using OpenCleaner.Core.Security;
using OpenCleaner.Core.Settings;
using OpenCleaner.Core.Transactions;
using OpenCleaner.Plugins.System;
using OpenCleaner.Plugins.System.Plugins;
using OpenCleaner.UI.Localization;
using System.Windows;

namespace OpenCleaner.UI;

public partial class App : System.Windows.Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    public static AppPreferences Preferences { get; private set; } = new();

    public static void SetPreferences(AppPreferences preferences)
    {
        Preferences = (preferences ?? new AppPreferences()).Normalize();
        PreferencesStore.Save(Preferences);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (s, args) =>
        {
            MessageBox.Show($"Une erreur inattendue est survenue :\n\n{args.Exception.Message}", "Erreur critique", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            args.SetObserved();
        };

        BuildServices();

        // ── Mode background (lancé par le planificateur Windows) ──────────
        if (e.Args.Contains("--background"))
        {
            RunBackgroundMode(e.Args);
            return;
        }

        // ── Mode normal UI ────────────────────────────────────────────────
        new MainWindow().Show();

        _ = InitializeAsync();
    }

    // ─────────────────────────────────────────────────────────────────────
    //  DI
    // ─────────────────────────────────────────────────────────────────────

    private static void BuildServices()
    {
        Preferences = PreferencesStore.Load();
        LocalizationService.Instance.SetLanguage(Preferences.Language);
        CleaningExclusions.Load();
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddDebug());
        services.AddSingleton<IBackupManager, BackupManager>();
        services.AddSingleton<ITransactionManager, TransactionManager>();
        services.AddSingleton<IFileGuardian, FileGuardian>();
        services.AddSingleton<IRegistryGuardian, RegistryGuardian>();



        // Plugins
        services.AddTransient<SystemTempPlugin>();
        services.AddTransient<ChromeCleanerPlugin>();
        services.AddTransient<RegistryCleanerPlugin>();
        services.AddTransient<SteamCleanerPlugin>();
        services.AddTransient<WindowsUpdatePlugin>();
        services.AddTransient<OfficeCleanerPlugin>();
        services.AddTransient<ThumbnailCachePlugin>();
        services.AddTransient<DuplicateFinderPlugin>();
        services.AddTransient<PrivacyScannerPlugin>();
        services.AddTransient<DevCleanerPlugin>();
        services.AddTransient<DiskAnalyzerPlugin>();

        Services = services.BuildServiceProvider();
    }

    // ─────────────────────────────────────────────────────────────────────
    //  MODE BACKGROUND (planificateur)
    // ─────────────────────────────────────────────────────────────────────

    private static async Task PurgeTransactionJournalAsync()
    {
        try
        {
            ITransactionManager transactions = Services.GetRequiredService<ITransactionManager>();
            await transactions.PurgeOldTransactionsAsync(TimeSpan.FromDays(30));
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Transaction purge failed: {ex}"); }
    }

    private static string T(string key, params object?[] args) => LocalizationService.Instance.Get(key, args);

    private static async Task InitializeAsync()
    {
        await UpgradeBackupStorageAsync();
        try
        {
            var transactions = Services.GetRequiredService<ITransactionManager>();
            var pending = await transactions.GetUnfinishedTransactionsAsync();
            if (pending.Count > 0 && MessageBox.Show(
                    T("App.Recovery.Pending", pending.Count),
                    T("App.Recovery.Title"), MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                if (UiOperation.TryBegin(Current.MainWindow, out var operation))
                    using (operation)
                        foreach (var id in pending) await transactions.RollbackAsync(id);
            }
            await PurgeTransactionJournalAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(T("App.Recovery.Incomplete", ex.Message),
                T("App.Recovery.ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        _ = UpdateService.CheckForUpdatesOnStartupAsync();
    }

    private static async Task UpgradeBackupStorageAsync()
    {
        try
        {
            IBackupManager manager = Services.GetRequiredService<IBackupManager>();
            BackupUpgradeResult upgrade = await manager.UpgradeLegacyBackupsAsync();
            if (upgrade.Quarantined > 0)
            {
                MessageBox.Show(
                    T("App.Backup.Corrupt", upgrade.Quarantined, manager.QuarantineRoot),
                    T("App.Backup.CorruptTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            string? recoveryCode;
            try { recoveryCode = await manager.EnsureRecoveryCodeAsync(); }
            catch (BackupKeyUnavailableException)
            {
                MessageBox.Show(
                    manager.HasRecoveryCode ? T("App.Backup.Locked") : T("App.Backup.KeyMissing"),
                    T("App.Backup.LockedTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (recoveryCode != null) ShowRecoveryCode(recoveryCode);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Backup upgrade failed: {ex}"); }
    }

    private static void ShowRecoveryCode(string code)
    {
        try { Clipboard.SetText(code); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Clipboard unavailable: {ex}"); }
        var window = new RecoveryKeyWindow(code, enterMode: false) { Owner = Current.MainWindow };
        window.ShowDialog();
    }

    private static async void RunBackgroundMode(string[] args)
    {
        // Récupère la liste des plugins à exécuter depuis --plugins "id1,id2"
        var pluginIds = GetArg(args, "--plugins")
            ?.Split(',', StringSplitOptions.RemoveEmptyEntries)
            ?? ["system.temp"];

        var fileGuardian  = Services.GetRequiredService<IFileGuardian>();
        var backupManager = Services.GetRequiredService<IBackupManager>();
        var logFactory    = Services.GetRequiredService<ILoggerFactory>();
        var logger        = logFactory.CreateLogger("BackgroundMode");

        int filesProcessed = 0;
        long bytesProcessed = 0;

        try
        {
            BackupUpgradeResult upgrade = await backupManager.UpgradeLegacyBackupsAsync();
            if (upgrade.Quarantined > 0)
                logger.LogWarning("{Count} sauvegarde(s) corrompue(s) isolée(s) dans {Path}",
                    upgrade.Quarantined, backupManager.QuarantineRoot);
        }
        catch (Exception ex) { logger.LogWarning(ex, "Impossible de migrer les anciennes sauvegardes"); }

        var failures = 0;
        if ((await Services.GetRequiredService<ITransactionManager>().GetUnfinishedTransactionsAsync()).Count > 0)
        {
            logger.LogWarning("Nettoyage automatique suspendu : récupération nécessaire dans OpenCleaner.");
            Current.Shutdown(1);
            return;
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(25));
        try
        {
            await using (var transaction = new CleanupTransactionScope(
                             Services.GetRequiredService<ITransactionManager>()))
            {
                foreach (var pluginId in pluginIds.Distinct(StringComparer.Ordinal))
                {
                    if (!ScheduleConfig.IsAutomaticPlugin(pluginId.Trim())) { failures++; continue; }
                    ICleanerPlugin? plugin = pluginId.Trim() switch
                    {
                        "system.temp"       => new SystemTempPlugin(fileGuardian,
                                                   logFactory.CreateLogger<SystemTempPlugin>()),
                        "system.windowsupdate"  => new WindowsUpdatePlugin(fileGuardian,
                                                   logFactory.CreateLogger<WindowsUpdatePlugin>()),
                        "system.thumbnailcache" => new ThumbnailCachePlugin(fileGuardian,
                                                   logFactory.CreateLogger<ThumbnailCachePlugin>()),
                        "browser.chrome"    => new ChromeCleanerPlugin(fileGuardian,
                                                   logFactory.CreateLogger<ChromeCleanerPlugin>()),
                        "applications.office"     => new OfficeCleanerPlugin(fileGuardian,
                                                   logFactory.CreateLogger<OfficeCleanerPlugin>()),
                        "games.steam"      => new SteamCleanerPlugin(fileGuardian,
                                                   logFactory.CreateLogger<SteamCleanerPlugin>()),
                        "applications.caches" => new AppCacheCleanerPlugin(fileGuardian,
                                                   logFactory.CreateLogger<AppCacheCleanerPlugin>()),
                        _                   => null
                    };

                    if (plugin == null || !plugin.IsAvailable) { failures++; continue; }

                    try
                    {
                        var analyzed = await Task.Run(() => plugin.AnalyzeAsync(ct: timeout.Token), timeout.Token);
                        var items = analyzed.Where(i => i.RiskLevel == RiskLevel.Safe && i.Type == ItemType.File).ToList();
                        var result = await Task.Run(() => plugin.CleanAsync(items, backupManager, ct: timeout.Token), timeout.Token);
                        filesProcessed += Math.Clamp(result.ItemsProcessed ?? 0, 0, items.Count);
                        bytesProcessed += items.Sum(item => item.Size);
                        if (!result.Success) failures++;
                    }
                    catch (OperationCanceledException) { failures++; break; }
                    catch (Exception ex)
                    {
                        failures++;
                        logger.LogError(ex, "Erreur plugin background {PluginId}", pluginId);
                    }
                }
                try
                {
                    if (failures == 0) await transaction.CommitAsync();
                    else await transaction.RollbackAsync();
                }
                catch (Exception ex)
                {
                    failures++;
                    logger.LogError(ex, "Impossible d'enregistrer la transaction de nettoyage");
                }
            }
            }
        catch (Exception ex)
        {
            failures++;
            logger.LogError(ex, "Récupération du nettoyage automatique incomplète");
        }

        // Log le résultat dans un fichier pour traçabilité
        ScheduledRunJournal.Append(new ScheduledRunRecord(DateTime.UtcNow, filesProcessed, bytesProcessed, failures));
        LogBackgroundResult(filesProcessed, bytesProcessed, failures, logger);
        Current.Shutdown(failures == 0 ? 0 : 1);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        (Services as IDisposable)?.Dispose();
        base.OnExit(e);
    }

    private static void LogBackgroundResult(int files, long bytes, int failures, ILogger logger)
    {
        try
        {
            var mb      = bytes / 1_048_576.0;
            var logDir  = System.IO.Path.GetDirectoryName(ScheduleConfig.ConfigPath)!;
            System.IO.Directory.CreateDirectory(logDir);
            var logPath = System.IO.Path.Combine(logDir, "background.log");
            var line    = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {files} fichier(s) — {mb:0.#} Mo traités, {failures} erreur(s).";
            System.IO.File.AppendAllText(logPath, line + Environment.NewLine);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Impossible d'écrire le log du mode background");
        }
    }

    private static string? GetArg(string[] args, string key)
    {
        var idx = Array.IndexOf(args, key);
        return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : null;
    }
}
