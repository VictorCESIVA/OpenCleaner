using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using OpenCleaner.Contracts;
using OpenCleaner.Core.Scheduler;
using OpenCleaner.Core.Security;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace OpenCleaner.UI;

public partial class SettingsWindow : UserControl
{
    private readonly SchedulerService _scheduler;
    private readonly ObservableCollection<string> _exclusions = [];
    private bool _loadingPreferences;

    public SettingsWindow()
    {
        InitializeComponent();
        ExclusionsList.ItemsSource = _exclusions;
        LoadExclusions();
        LoadPreferences();
        var version = typeof(App).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion.Split('+')[0];
        ApplicationVersionText.Text = "OpenCleaner " + (version ?? typeof(App).Assembly.GetName().Version?.ToString(3));
        _scheduler = new SchedulerService(
            App.Services.GetRequiredService<ILogger<SchedulerService>>());
        LoadConfig();
        RegenerateRecoveryBtn.IsEnabled =
            App.Services.GetRequiredService<IBackupManager>().IsBackupKeyAvailable;
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
        var plugBoxes = new[] { PlugSystem, PlugWinUpd, PlugBrowser, PlugThumb, PlugOffice, PlugSteam, PlugAppCaches };
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
        var plugBoxes = new[] { PlugSystem, PlugWinUpd, PlugBrowser, PlugThumb, PlugOffice, PlugSteam, PlugAppCaches };
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

    // ─── CODE DE RÉCUPÉRATION ────────────────────────────────────────────

    private async void CreateRecovery_Click(object sender, RoutedEventArgs e)
    {
        CreateRecoveryBtn.IsEnabled = RegenerateRecoveryBtn.IsEnabled = false;
        try
        {
            IBackupManager manager = App.Services.GetRequiredService<IBackupManager>();
            string? code = await manager.EnsureRecoveryCodeAsync();
            if (code == null)
            {
                RecoveryStatus.Text = "Un code existe déjà. Utilisez « Régénérer » pour le remplacer.";
                return;
            }
            ShowRecoveryCode(code);
            RecoveryStatus.Text = "Code de récupération copié dans le presse-papiers. Conservez-le hors de cet ordinateur.";
        }
        catch (BackupKeyUnavailableException)
        {
            IBackupManager manager = App.Services.GetRequiredService<IBackupManager>();
            MessageBox.Show(Window.GetWindow(this),
                manager.HasRecoveryCode
                    ? "La clé de chiffrement est absente. Utilisez Restauration > Code de récupération pour saisir votre code."
                    : "La clé et le code de récupération sont absents. Il n'est pas possible de déverrouiller les sauvegardes existantes.",
                "Sauvegardes verrouillées", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), ex.Message, "Code de récupération", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            CreateRecoveryBtn.IsEnabled = true;
            RegenerateRecoveryBtn.IsEnabled = App.Services.GetRequiredService<IBackupManager>().IsBackupKeyAvailable;
        }
    }

    private async void RegenerateRecovery_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(Window.GetWindow(this),
                "Régénérer le code invalide le précédent. Les sauvegardes restent accessibles avec la clé actuelle. Continuer ?",
                "Régénérer le code", MessageBoxButton.YesNo, MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes) return;
        CreateRecoveryBtn.IsEnabled = RegenerateRecoveryBtn.IsEnabled = false;
        try
        {
            IBackupManager manager = App.Services.GetRequiredService<IBackupManager>();
            string code = await manager.RegenerateRecoveryCodeAsync();
            ShowRecoveryCode(code);
            RecoveryStatus.Text = "Nouveau code copié dans le presse-papiers. L'ancien code est invalide.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), ex.Message, "Code de récupération", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            CreateRecoveryBtn.IsEnabled = true;
            RegenerateRecoveryBtn.IsEnabled = App.Services.GetRequiredService<IBackupManager>().IsBackupKeyAvailable;
        }
    }

    private void ShowRecoveryCode(string code)
    {
        try { Clipboard.SetText(code); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Clipboard unavailable: {ex}"); }
        new RecoveryKeyWindow(code, enterMode: false) { Owner = Window.GetWindow(this) }.ShowDialog();
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
        PlugThumb.IsEnabled  = PlugOffice.IsEnabled = PlugSteam.IsEnabled   =
        PlugAppCaches.IsEnabled = on;
    }

    // ─── PRÉFÉRENCES ────────────────────────────────────────────────────

    private void LoadPreferences()
    {
        _loadingPreferences = true;
        int threshold = App.Preferences.LowSpaceThresholdPercent;
        var candidates = LowSpaceCombo.Items.OfType<ComboBoxItem>()
            .Select(item => (Item: item, Value: int.TryParse(item.Tag as string, out int value) ? value : 0))
            .Where(candidate => candidate.Value > 0)
            .ToList();
        var selected = candidates.FirstOrDefault(candidate => candidate.Value == threshold);
        if (selected.Item == null)
        {
            selected = candidates.OrderBy(candidate => Math.Abs(candidate.Value - threshold)).First();
            threshold = selected.Value;
            App.SetPreferences(App.Preferences with { LowSpaceThresholdPercent = threshold });
        }
        LowSpaceCombo.SelectedItem = selected.Item;
        _loadingPreferences = false;
        PreferencesStatus.Text = $"Bandeau au démarrage sous {threshold} % d'espace libre.";
    }

    private void LowSpaceCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingPreferences) return;
        if ((LowSpaceCombo.SelectedItem as ComboBoxItem)?.Tag is not string tag ||
            !int.TryParse(tag, out int threshold)) return;
        App.SetPreferences(App.Preferences with { LowSpaceThresholdPercent = threshold });
        PreferencesStatus.Text = $"Seuil enregistré : {threshold} %.";
        if (Window.GetWindow(this) is MainWindow main) _ = main.RefreshLowSpaceAsync();
    }

    // ─── EXCLUSIONS ─────────────────────────────────────────────────────
    private void LoadExclusions()
    {
        _exclusions.Clear();
        foreach (string path in CleaningExclusions.Paths) _exclusions.Add(path);
        UpdateExclusionsStatus(null);
    }

    private void UpdateExclusionsStatus(string? message) =>
        ExclusionsStatus.Text = message ?? (_exclusions.Count == 0
            ? "Aucune exclusion : les protections par défaut s'appliquent."
            : $"{_exclusions.Count} dossier(s) exclu(s) du nettoyage.");

    private void ExclusionsList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        RemoveExclusionBtn.IsEnabled = ExclusionsList.SelectedItem is string;

    private void AddExclusion_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choisir un dossier à exclure",
            Multiselect = false
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        if (!CleaningExclusions.Add(dialog.FolderName))
        {
            UpdateExclusionsStatus("Impossible d'enregistrer cette exclusion. Vérifiez le chemin et les droits du dossier de configuration.");
            return;
        }
        LoadExclusions();
        UpdateExclusionsStatus("Dossier exclu : " + dialog.FolderName);
    }

    private void RemoveExclusion_Click(object sender, RoutedEventArgs e)
    {
        if (ExclusionsList.SelectedItem is not string path) return;
        if (!CleaningExclusions.Remove(path))
        {
            UpdateExclusionsStatus("Impossible de retirer cette exclusion.");
            return;
        }
        LoadExclusions();
        UpdateExclusionsStatus("Exclusion retirée : " + path);
    }

    internal void ShowExclusionsForPreview(IEnumerable<string> paths)
    {
        _exclusions.Clear();
        foreach (string path in paths) _exclusions.Add(path);
        ExclusionsStatus.Text = "Aperçu : aucune modification enregistrée.";
        RemoveExclusionBtn.IsEnabled = false;
    }
}
