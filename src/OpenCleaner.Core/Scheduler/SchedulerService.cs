using Microsoft.Extensions.Logging;
using Microsoft.Win32.TaskScheduler;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;

namespace OpenCleaner.Core.Scheduler;

/// <summary>
/// Gère la création/suppression de la tâche Windows planifiée.
/// Utilise le wrapper Microsoft.Win32.TaskScheduler (NuGet TaskScheduler v2.11).
/// La tâche s'exécute dans le contexte de l'utilisateur courant — aucun droit admin requis.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SchedulerService
{
    private const string TaskFolder = "OpenCleaner";
    private const string TaskName   = "AutoClean";
    private const string FullPath   = @"\OpenCleaner\AutoClean";

    private readonly ILogger<SchedulerService> _logger;

    public SchedulerService(ILogger<SchedulerService> logger)
    {
        _logger = logger;
    }

    // ─────────────────────────────────────────────
    //  PERSISTANCE CONFIG JSON
    // ─────────────────────────────────────────────

    private static readonly JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };

    public static ScheduleConfig LoadConfig() => LoadConfig(out _);

    public static ScheduleConfig LoadConfig(out string? error)
    {
        error = null;
        if (!File.Exists(ScheduleConfig.ConfigPath))
            return new ScheduleConfig();

        try
        {
            var json = File.ReadAllText(ScheduleConfig.ConfigPath);
            var config = JsonSerializer.Deserialize<ScheduleConfig>(json) ?? new ScheduleConfig();
            config.Validate();
            return config;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or
            ArgumentException or NotSupportedException)
        {
            error = ex.Message;
            return new ScheduleConfig();
        }
    }

    public static void SaveConfig(ScheduleConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(ScheduleConfig.ConfigPath)!);
        string temporaryPath = ScheduleConfig.ConfigPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(config, _jsonOpts));
            File.Move(temporaryPath, ScheduleConfig.ConfigPath, overwrite: true);
        }
        finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
    }

    // ─────────────────────────────────────────────
    //  GESTION DE LA TÂCHE WINDOWS
    // ─────────────────────────────────────────────

    /// <summary>Crée ou remplace la tâche planifiée selon la config fournie.</summary>
    public void CreateOrUpdateScheduledTask(ScheduleConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();
        if (!config.IsEnabled)
        {
            DeleteScheduledTask();
            return;
        }

        try
        {
            var exePath   = GetExePath();
            var pluginArg = string.Join(",", config.PluginIds.Distinct(StringComparer.Ordinal));

            using var ts = new TaskService();

            // Créer le dossier \OpenCleaner\ s'il n'existe pas
            Microsoft.Win32.TaskScheduler.TaskFolder folder;
            try   { folder = ts.GetFolder(TaskFolder); }
            catch { folder = ts.RootFolder.CreateFolder(TaskFolder); }

            var td = ts.NewTask();
            td.RegistrationInfo.Description = "Nettoyage automatique OpenCleaner";
            td.RegistrationInfo.Author      = "OpenCleaner";
            td.Principal.LogonType          = TaskLogonType.InteractiveToken;
            td.Settings.ExecutionTimeLimit  = TimeSpan.FromMinutes(30);
            td.Settings.DisallowStartIfOnBatteries = false;
            td.Settings.StopIfGoingOnBatteries     = false;
            td.Settings.RunOnlyIfNetworkAvailable   = false;
            td.Settings.MultipleInstances = TaskInstancesPolicy.IgnoreNew;
            td.Settings.RestartCount = 3;
            td.Settings.RestartInterval = TimeSpan.FromMinutes(10);

            // Triggers : un par jour sélectionné
            foreach (var day in config.Days.Distinct())
            {
                var trigger = new WeeklyTrigger(TriggerHelper.ToDaysOfWeek(day))
                {
                    StartBoundary = DateTime.Today + config.TimeOfDay,
                    WeeksInterval = 1
                };
                td.Triggers.Add(trigger);
            }

            // Action : lancer OpenCleaner --background --plugins "system.temp,..."
            td.Actions.Add(new ExecAction(
                path:      exePath,
                arguments: $"--background --plugins \"{pluginArg}\"",
                workingDirectory: Path.GetDirectoryName(exePath)));

            folder!.RegisterTaskDefinition(TaskName, td);
            _logger.LogInformation("Tâche planifiée créée : {Path} à {Time}", FullPath, config.TimeOfDay);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec création tâche planifiée");
            throw;
        }
    }

    /// <summary>Supprime la tâche si elle existe.</summary>
    public void DeleteScheduledTask()
    {
        try
        {
            using var ts = new TaskService();
            var folder = ts.GetFolder(TaskFolder);
            folder?.DeleteTask(TaskName, false);
            _logger.LogInformation("Tâche planifiée supprimée");
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException ||
            ex is COMException { HResult: unchecked((int)0x80070002) or unchecked((int)0x80070003) })
        {
            _logger.LogDebug(ex, "Suppression tâche — ignorée si inexistante");
        }
    }

    /// <summary>Retourne la tâche existante ou null.</summary>
    public Microsoft.Win32.TaskScheduler.Task? GetScheduledTask()
    {
        try
        {
            using var ts = new TaskService();
            return ts.GetTask(FullPath);
        }
        catch { return null; }
    }

    // ─────────────────────────────────────────────
    //  UTILITAIRES
    // ─────────────────────────────────────────────

    private static string GetExePath()
    {
        string application = Path.Combine(AppContext.BaseDirectory, "OpenCleaner.UI.exe");
        if (File.Exists(application)) return application;
        string? process = Environment.ProcessPath;
        if (process != null && Path.GetFileName(process).Equals("OpenCleaner.UI.exe", StringComparison.OrdinalIgnoreCase) && File.Exists(process))
            return process;
        throw new FileNotFoundException("The OpenCleaner executable must be available before creating a scheduled task", application);
    }
}

/// <summary>Helper pour convertir DayOfWeek en DaysOfTheWeek (bitmask Task Scheduler).</summary>
internal static class TriggerHelper
{
    public static DaysOfTheWeek ToDaysOfWeek(DayOfWeek d) => d switch
    {
        DayOfWeek.Monday    => DaysOfTheWeek.Monday,
        DayOfWeek.Tuesday   => DaysOfTheWeek.Tuesday,
        DayOfWeek.Wednesday => DaysOfTheWeek.Wednesday,
        DayOfWeek.Thursday  => DaysOfTheWeek.Thursday,
        DayOfWeek.Friday    => DaysOfTheWeek.Friday,
        DayOfWeek.Saturday  => DaysOfTheWeek.Saturday,
        DayOfWeek.Sunday    => DaysOfTheWeek.Sunday,
        _                   => throw new ArgumentOutOfRangeException(nameof(d))
    };
}
