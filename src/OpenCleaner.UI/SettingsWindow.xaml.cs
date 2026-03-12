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
        _scheduler = new SchedulerService(
            App.Services.GetRequiredService<ILogger<SchedulerService>>());
        LoadConfig();
    }

    // ─── CHARGEMENT ─────────────────────────────────────────────────────

    private void LoadConfig()
    {
        var config = SchedulerService.LoadConfig();

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
            cb.IsChecked = config.PluginIds.Contains((string)cb.Tag);
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
            .Where(cb => cb.IsChecked == true)
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
            SchedulerService.SaveConfig(config);
            _scheduler.CreateOrUpdateScheduledTask(config);

            SaveStatus.Text = config.IsEnabled
                ? $"✅ Tâche planifiée à {config.TimeOfDay:hh\\:mm} ({string.Join(", ", config.Days)})"
                : "✅ Planificateur désactivé.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Impossible de créer la tâche Windows :\n" + ex.Message +
                "\n\nLancez OpenCleaner en tant qu'administrateur pour les permissions.",
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
        PlugSystem.IsEnabled = PlugWinUpd.IsEnabled = PlugBrowser.IsEnabled =
        PlugThumb.IsEnabled  = PlugOffice.IsEnabled = PlugSteam.IsEnabled   = on;
    }
}
