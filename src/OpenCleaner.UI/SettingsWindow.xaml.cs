using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using OpenCleaner.Contracts;
using OpenCleaner.Core.Scheduler;
using OpenCleaner.Core.Security;
using OpenCleaner.UI.Localization;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace OpenCleaner.UI;

public partial class SettingsWindow : UserControl
{
    private readonly SchedulerService _scheduler;
    private readonly ObservableCollection<string> _exclusions = [];
    private bool _loadingPreferences;
    private bool _loadingLanguage;

    private static string T(string key, params object?[] args) => LocalizationService.Instance.Get(key, args);

    public SettingsWindow()
    {
        InitializeComponent();
        ExclusionsList.ItemsSource = _exclusions;
        LoadExclusions();
        LoadPreferences();
        LoadLanguage();
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
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
            SaveStatus.Text = T("Settings.ConfigUnreadable", configError);

        EnabledCheck.IsChecked = config.IsEnabled;
        ScheduledBackupCheck.IsChecked = config.CreateBackup;
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

        UpdateRunSummary(config);

        if (configError == null)
        {
            bool taskExists = _scheduler.GetScheduledTask() != null;
            if (config.IsEnabled != taskExists)
                SaveStatus.Text = config.IsEnabled
                    ? T("Settings.TaskMissing")
                    : T("Settings.TaskOrphan");
        }
    }

    // ─── SAVE ────────────────────────────────────────────────────────────

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // Validation de l'heure
        if (!TimeSpan.TryParseExact(TimeBox.Text.Trim(), @"hh\:mm",
                null, out var time))
        {
            MessageBox.Show(T("Settings.InvalidTime"),
                T("Settings.Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
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
            MessageBox.Show(T("Settings.SelectDay"),
                T("Settings.Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
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
            MessageBox.Show(T("Settings.SelectPlugin"),
                T("Settings.Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var config = new ScheduleConfig
        {
            IsEnabled  = EnabledCheck.IsChecked == true,
            CreateBackup = ScheduledBackupCheck.IsChecked == true,
            TimeOfDay  = time,
            Days       = days,
            PluginIds  = pluginIds
        };

        try
        {
            var previous = SchedulerService.LoadConfig();
            if (config.IsEnabled && !config.CreateBackup && MessageBox.Show(Window.GetWindow(this),
                    T("Settings.ScheduleWithoutBackupConfirm"), T("Cleanup.Title"), MessageBoxButton.YesNo,
                    MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
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
                ? T("Settings.ScheduledAt", config.TimeOfDay.ToString(@"hh\:mm"),
                    string.Join(", ", config.Days))
                : T("Settings.SchedulerOff");
            UpdateRunSummary(config);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                T("Settings.SchedulerUpdateFailed", ex.Message),
                T("Settings.SchedulerErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
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
                RecoveryStatus.Text = T("Settings.CodeExists");
                return;
            }
            ShowRecoveryCode(code);
            RecoveryStatus.Text = T("Settings.CodeCopied");
        }
        catch (BackupKeyUnavailableException)
        {
            IBackupManager manager = App.Services.GetRequiredService<IBackupManager>();
            MessageBox.Show(Window.GetWindow(this),
                manager.HasRecoveryCode ? T("Settings.KeyMissing") : T("Settings.KeyAndCodeMissing"),
                T("Settings.BackupsLocked"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), ex.Message, T("Settings.RecoveryCodeTitle"),
                MessageBoxButton.OK, MessageBoxImage.Error);
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
                T("Settings.RegenerateConfirm"),
                T("Settings.RegenerateTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes) return;
        CreateRecoveryBtn.IsEnabled = RegenerateRecoveryBtn.IsEnabled = false;
        try
        {
            IBackupManager manager = App.Services.GetRequiredService<IBackupManager>();
            string code = await manager.RegenerateRecoveryCodeAsync();
            ShowRecoveryCode(code);
            RecoveryStatus.Text = T("Settings.NewCodeCopied");
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), ex.Message, T("Settings.RecoveryCodeTitle"),
                MessageBoxButton.OK, MessageBoxImage.Error);
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
                msg => MessageBoxResult.Yes == MessageBox.Show(msg, T("Settings.UpdateTitle"),
                    MessageBoxButton.YesNo, MessageBoxImage.Question));
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

    private void UpdateRunSummary(ScheduleConfig config)
    {
        ScheduledRunRecord? last = ScheduledRunJournal.ReadLatest(1).FirstOrDefault();
        LastRunText.Text = last == null
            ? T("Settings.NoRuns")
            : FormatLastRun(last);
        DateTime? next = config.GetNextRun(DateTime.Now);
        NextRunText.Text = next is DateTime date
            ? T("Settings.NextRun", date)
            : T("Settings.NextRunNone");
    }

    private static string FormatLastRun(ScheduledRunRecord record) =>
        T("Settings.LastRun", record.CompletedAt.ToLocalTime(), record.FilesCleaned,
            SizeFormatter.Format(record.BytesProcessed)) +
        (record.Failures > 0 ? T("Settings.LastRunErrors", record.Failures) : T("Settings.LastRunOk"));

    internal void ShowScheduleForPreview(ScheduledRunRecord lastRun, DateTime nextRun)
    {
        LastRunText.Text = FormatLastRun(lastRun);
        NextRunText.Text = T("Settings.NextRun", nextRun);
    }

    // ─── PRÉFÉRENCES ────────────────────────────────────────────────────

    private void LoadLanguage()
    {
        _loadingLanguage = true;
        string language = LocalizationService.NormalizeLanguage(LocalizationService.Instance.Language);
        foreach (ComboBoxItem item in LanguageCombo.Items)
        {
            if (string.Equals(item.Tag as string, language, StringComparison.OrdinalIgnoreCase))
            {
                LanguageCombo.SelectedItem = item;
                break;
            }
        }
        _loadingLanguage = false;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        LoadLanguage();
        UpdateExclusionsStatus(null);
        UpdateRunSummary(SchedulerService.LoadConfig());
        SaveStatus.Text = "";
    }

    private void LanguageCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingLanguage) return;
        if ((LanguageCombo.SelectedItem as ComboBoxItem)?.Tag is not string tag) return;
        string language = LocalizationService.NormalizeLanguage(tag);
        App.SetPreferences(App.Preferences with { Language = language });
        LocalizationService.Instance.SetLanguage(language);
    }

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
        PreferencesStatus.Text = T("Settings.ThresholdBanner", threshold);
    }

    private void LowSpaceCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingPreferences) return;
        if ((LowSpaceCombo.SelectedItem as ComboBoxItem)?.Tag is not string tag ||
            !int.TryParse(tag, out int threshold)) return;
        App.SetPreferences(App.Preferences with { LowSpaceThresholdPercent = threshold });
        PreferencesStatus.Text = T("Settings.ThresholdSaved", threshold);
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
            ? T("Settings.NoExclusions")
            : T("Settings.ExclusionCount", _exclusions.Count));

    private void ExclusionsList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        RemoveExclusionBtn.IsEnabled = ExclusionsList.SelectedItem is string;

    private void AddExclusion_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = T("Settings.AddFolderTitle"),
            Multiselect = false
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        if (!CleaningExclusions.Add(dialog.FolderName))
        {
            UpdateExclusionsStatus(T("Settings.ExclusionAddFailed"));
            return;
        }
        LoadExclusions();
        UpdateExclusionsStatus(T("Settings.ExclusionAdded", dialog.FolderName));
    }

    private void RemoveExclusion_Click(object sender, RoutedEventArgs e)
    {
        if (ExclusionsList.SelectedItem is not string path) return;
        if (!CleaningExclusions.Remove(path))
        {
            UpdateExclusionsStatus(T("Settings.ExclusionRemoveFailed"));
            return;
        }
        LoadExclusions();
        UpdateExclusionsStatus(T("Settings.ExclusionRemoved", path));
    }

    internal void ShowExclusionsForPreview(IEnumerable<string> paths)
    {
        _exclusions.Clear();
        foreach (string path in paths) _exclusions.Add(path);
        ExclusionsStatus.Text = T("Settings.PreviewOnly");
        RemoveExclusionBtn.IsEnabled = false;
    }
}
