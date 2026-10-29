using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using OpenCleaner.Contracts;
using OpenCleaner.Core.Security;
using OpenCleaner.Core.Transactions;
using OpenCleaner.Plugins.System.Plugins;
using OpenCleaner.UI.Localization;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;

namespace OpenCleaner.UI;

// ──── ViewModel ─────────────────────────────────────────────────────────────

public sealed class PrivacyAlert : System.ComponentModel.INotifyPropertyChanged
{
    public CleanableItem Item       { get; init; } = null!;
    public string        RiskIcon   { get; init; } = "🟢";
    public string        Alert      => PluginDescriptionLocalizer.Localize(Item.Description);
    public string        Path       => Item.Path;
    public Brush         AlertColor { get; init; } = Brushes.White;

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    internal void RefreshLocalization() =>
        PropertyChanged?.Invoke(this, new(nameof(Alert)));

    public static PrivacyAlert From(CleanableItem item) => new()
    {
        Item      = item,
        RiskIcon  = item.RiskLevel == RiskLevel.ExpertOnly ? "🔴" : "🟠",
        AlertColor = item.RiskLevel == RiskLevel.ExpertOnly
            ? (Brush)Application.Current.FindResource("RiskExpert")
            : (Brush)Application.Current.FindResource("RiskRecommended")
    };
}

// ──── Code-behind ────────────────────────────────────────────────────────────

public partial class PrivacyWindow : UserControl
{
    private static string T(string key, params object?[] args) => LocalizationService.Instance.Get(key, args);

    private readonly ObservableCollection<PrivacyAlert> _alerts = [];
    private List<CleanableItem> _currentItems = [];
    private PrivacyScannerPlugin? _plugin;

    public PrivacyWindow()
    {
        InitializeComponent();
        AlertList.ItemsSource = _alerts;
        LocalizationService.Instance.LanguageChanged += (_, _) =>
        {
            foreach (PrivacyAlert alert in _alerts) alert.RefreshLocalization();
        };
    }

    // ─── SCAN ───────────────────────────────────────────────────────────

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        if (!UiOperation.TryBegin(Window.GetWindow(this), out var operation)) return;
        using var scope = operation;
        ScanBtn.IsEnabled   = false;
        SecureBtn.IsEnabled = false;
        RevealBtn.IsEnabled = false;
        _alerts.Clear();
        _currentItems.Clear();
        StatCritical.Text = StatMedium.Text = StatTotal.Text = "—";
        StatusText.Text   = T("Privacy.Scanning");

        try
        {
            var fileGuardian = App.Services.GetRequiredService<IFileGuardian>();
            var logger       = App.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PrivacyScannerPlugin>>();
            var plugin       = _plugin = new PrivacyScannerPlugin(fileGuardian, logger);

            var progress = operation.CreateProgress<double>(p => StatusText.Text = T("Privacy.Progress", p));
            _currentItems = (await Task.Run(() => plugin.AnalyzeAsync(progress, operation.Token), operation.Token)).ToList();

            foreach (var item in _currentItems)
                _alerts.Add(PrivacyAlert.From(item));

            int crit   = _currentItems.Count(i => i.RiskLevel == RiskLevel.ExpertOnly);
            int medium  = _currentItems.Count(i => i.RiskLevel == RiskLevel.Recommended);

            StatCritical.Text = crit.ToString();
            StatMedium.Text   = medium.ToString();
            StatTotal.Text    = _currentItems.Count.ToString();

            StatusText.Text = _currentItems.Count == 0
                ? T("Privacy.NoAlerts")
                : T("Privacy.Alerts", _currentItems.Count);

            SecureBtn.IsEnabled = _currentItems.Count > 0;
        }
        catch (OperationCanceledException) { StatusText.Text = T("Privacy.Cancelled"); _plugin = null; }
        catch (Exception ex)
        {
            StatusText.Text = "❌ " + ex.Message;
        }
        finally
        {
            ScanBtn.IsEnabled = true;
        }
    }

    // ─── SÉCURISER ──────────────────────────────────────────────────────

    private void AlertList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (RevealBtn != null)
            RevealBtn.IsEnabled = !UiOperation.IsBusy && AlertList.SelectedItem is PrivacyAlert;
    }

    private void AlertList_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
        Reveal_Click(sender, e);

    private void Reveal_Click(object sender, RoutedEventArgs e)
    {
        if (AlertList.SelectedItem is PrivacyAlert alert)
            ExplorerHelper.RevealInExplorer(alert.Path);
    }

    private async void SecureSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = AlertList.SelectedItems.Cast<PrivacyAlert>()
            .Select(a => a.Item)
            .ToList();

        if (selected.Count == 0)
        {
            StatusText.Text = T("Privacy.SelectExplicit");
            return;
        }

        bool? createBackup = CleanupConfirmationWindow.Confirm(Window.GetWindow(this),
            T("Privacy.ConfirmDelete", selected.Count));
        if (createBackup == null) return;

        if (_plugin == null || !UiOperation.TryBegin(Window.GetWindow(this), out var operation)) return;
        using var scope = operation;
        SecureBtn.IsEnabled = ScanBtn.IsEnabled = false;
        StatusText.Text     = T("Privacy.Deleting");

        try
        {
            var fileGuardian  = App.Services.GetRequiredService<IFileGuardian>();
            var backupManager = App.Services.GetRequiredService<IBackupManager>();
            var logger        = App.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PrivacyScannerPlugin>>();
            var plugin        = _plugin;

            var progress = operation.CreateProgress<double>(p => StatusText.Text = T("Privacy.DeleteProgress", p));
            OperationResult result;
            await using (var transaction = new CleanupTransactionScope(
                             App.Services.GetRequiredService<ITransactionManager>(), createBackup.Value))
            {
                result = await Task.Run(() => plugin.CleanAsync(selected, backupManager, progress, operation.Token),
                    operation.Token);
                if (!result.Success) throw new InvalidOperationException(result.Message);
                await transaction.CommitAsync(operation.Token);
            }

            StatusText.Text = (result.Success ? "✅ " : "⚠️ ") + PluginMessageLocalizer.Localize(result.Message);
            _ = Dispatcher.BeginInvoke(new Action(() => Scan_Click(sender, e)));
        }
        catch (OperationCanceledException) { StatusText.Text = T(createBackup.Value ? "Privacy.Cancelled" : "Cleanup.PermanentInterrupted"); _plugin = null; }
        catch (Exception ex) { StatusText.Text = "❌ " + ex.Message; }
        finally { ScanBtn.IsEnabled = true; }
    }
}
