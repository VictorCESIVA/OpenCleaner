using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenCleaner.Core.Scheduler;
using System.Windows;
using System.Windows.Controls;

namespace OpenCleaner.UI;

public partial class SettingsWindow : Window
{
    private readonly SchedulerService _scheduler;

    public SettingsWindow()
    {
        InitializeComponent();
        var version = typeof(App).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion.Split('+')[0];
        ApplicationVersionText.Text = "OpenCleaner " + (version ?? typeof(App).Assembly.GetName().Version?.ToString(3));
        _scheduler = new SchedulerService(
            App.Services.GetRequiredService<ILogger<SchedulerService>>());
        LoadConfig();
    }

    // ─── CHARGEMENT ─────────────────────────────────────────────────────

    private void LoadConfig()
    {
        var config = SchedulerService.LoadConfig(out string? configError);
        if (configError != null)
            SaveStatus.Text = "Configuration illisible, valeurs par défaut utilisées : " + configError;

        EnabledCheck.IsChecked = config.IsEnabled;
        TimeBox.Text = config.TimeOfDay.ToString(@"hh\:mm");

        // Jours
        var dayBoxes = new[] { DayMon, DayTue, DayWed, DayThu, DayFri, DaySat, DaySun };
        foreach (var cb in dayBoxes)
        {
            if (Enum.TryParse<DayOfWeek>((string)cb.Tag, out var day))
                cb.IsChecked = config.Days.Contains(day);
        }

        // Plugins
        var plugBoxes = new[] { PlugSystem, PlugWinUpd, PlugBrowser, PlugThumb, PlugOffice, PlugSteam };
        foreach (var cb in plugBoxes)
            cb.IsChecked = cb != PlugWinUpd && config.PluginIds.Contains((string)cb.Tag);

        if (configError == null)
        {
            bool taskExists = _scheduler.GetScheduledTask() != null;
            if (config.IsEnabled != taskExists)
                SaveStatus.Text = config.IsEnabled
                    ? "Attention : la tâche planifiée est absente. Enregistrez pour la recréer."
                    : "Attention : une tâche planifiée existe encore. Enregistrez pour la supprimer.";
        }
    }

    // ─── SAVE ────────────────────────────────────────────────────────────

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // Validation de l'heure
        if (!TimeSpan.TryParseExact(TimeBox.Text.Trim(), @"hh\:mm",
                null, out var time))
        {
            MessageBox.Show("Format d'heure invalide. Utilisez HH:mm (ex : 03:00).",
                "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Jours cochés
        var dayBoxes = new[] { DayMon, DayTue, DayWed, DayThu, DayFri, DaySat, DaySun };
        var days = dayBoxes
            .Where(cb => cb.IsChecked == true)
            .Select(cb => Enum.Parse<DayOfWeek>((string)cb.Tag))
            .ToArray();

        if ((EnabledCheck.IsChecked == true) && days.Length == 0)
        {
            MessageBox.Show("Sélectionnez au moins un jour.",
                "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Plugins cochés
        var plugBoxes = new[] { PlugSystem, PlugWinUpd, PlugBrowser, PlugThumb, PlugOffice, PlugSteam };
        var pluginIds = plugBoxes
            .Where(cb => cb != PlugWinUpd && cb.IsChecked == true)
            .Select(cb => (string)cb.Tag)
            .ToArray();

        if ((EnabledCheck.IsChecked == true) && pluginIds.Length == 0)
        {
            MessageBox.Show("Sélectionnez au moins un plugin.",
                "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var config = new ScheduleConfig
        {
            IsEnabled  = EnabledCheck.IsChecked == true,
            TimeOfDay  = time,
            Days       = days,
            PluginIds  = pluginIds
        };

        try
        {
            var previous = SchedulerService.LoadConfig();
            SchedulerService.SaveConfig(config);
            try
            {
                _scheduler.CreateOrUpdateScheduledTask(config);
            }
            catch
            {
                try { SchedulerService.SaveConfig(previous); }
                catch { /* keep the original scheduler error */ }
                throw;
            }

            SaveStatus.Text = config.IsEnabled
                ? $"✅ Tâche planifiée à {config.TimeOfDay:hh\\:mm} ({string.Join(", ", config.Days)})"
                : "✅ Planificateur désactivé.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Impossible de mettre à jour la tâche Windows :\n" + ex.Message,
                "Erreur planificateur", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ─── MISES À JOUR ────────────────────────────────────────────────────

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdateBtn.IsEnabled = false;
        UpdateStatus.Text = "…";
        try
        {
            var applied = await UpdateService.CheckForUpdatesManualAsync(
                status => Dispatcher.Invoke(() => UpdateStatus.Text = status),
                msg => MessageBoxResult.Yes == MessageBox.Show(msg, "Mise à jour", MessageBoxButton.YesNo, MessageBoxImage.Question));
            if (applied)
                Application.Current.Shutdown();
        }
        finally
        {
            CheckUpdateBtn.IsEnabled = true;
        }
    }

    // ─── ENABLE TOGGLE ──────────────────────────────────────────────────

    private void EnabledCheck_Changed(object sender, RoutedEventArgs e)
    {
        bool on = EnabledCheck.IsChecked == true;
        TimeBox.IsEnabled = on;
        DayMon.IsEnabled = DayTue.IsEnabled = DayWed.IsEnabled =
        DayThu.IsEnabled = DayFri.IsEnabled = DaySat.IsEnabled = DaySun.IsEnabled = on;
        PlugWinUpd.IsEnabled = false;
        PlugSystem.IsEnabled = PlugBrowser.IsEnabled =
        PlugThumb.IsEnabled  = PlugOffice.IsEnabled = PlugSteam.IsEnabled   = on;
    }
}
