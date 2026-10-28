using Microsoft.Extensions.DependencyInjection;
using OpenCleaner.Contracts;
using OpenCleaner.Core.Security;
using OpenCleaner.Core.Transactions;
using OpenCleaner.Plugins.System.Plugins;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;

namespace OpenCleaner.UI;

// ──── ViewModel ─────────────────────────────────────────────────────────────

public sealed class PrivacyAlert
{
    public CleanableItem Item       { get; init; } = null!;
    public string        RiskIcon   { get; init; } = "🟢";
    public string        Alert      { get; init; } = "";
    public string        Path       => Item.Path;
    public Brush         AlertColor { get; init; } = Brushes.White;

    public static PrivacyAlert From(CleanableItem item) => new()
    {
        Item      = item,
        RiskIcon  = item.RiskLevel == RiskLevel.ExpertOnly ? "🔴" : "🟠",
        Alert     = item.Description,
        AlertColor = item.RiskLevel == RiskLevel.ExpertOnly
            ? (Brush)Application.Current.FindResource("RiskExpert")
            : (Brush)Application.Current.FindResource("RiskRecommended")
    };
}

// ──── Code-behind ────────────────────────────────────────────────────────────

public partial class PrivacyWindow : System.Windows.Window
{
    private readonly ObservableCollection<PrivacyAlert> _alerts = [];
    private List<CleanableItem> _currentItems = [];
    private PrivacyScannerPlugin? _plugin;

    public PrivacyWindow()
    {
        InitializeComponent();
        AlertList.ItemsSource = _alerts;
    }

    // ─── SCAN ───────────────────────────────────────────────────────────

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        if (!UiOperation.TryBegin(this, out var operation)) return;
        using var scope = operation;
        ScanBtn.IsEnabled   = false;
        SecureBtn.IsEnabled = false;
        RevealBtn.IsEnabled = false;
        _alerts.Clear();
        _currentItems.Clear();
        StatCritical.Text = StatMedium.Text = StatTotal.Text = "—";
        StatusText.Text   = "Analyse en cours…";

        try
        {
            var fileGuardian = App.Services.GetRequiredService<IFileGuardian>();
            var logger       = App.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PrivacyScannerPlugin>>();
            var plugin       = _plugin = new PrivacyScannerPlugin(fileGuardian, logger);

            var progress = operation.CreateProgress<double>(p => StatusText.Text = $"Scan… {p:P0}");
            _currentItems = (await Task.Run(() => plugin.AnalyzeAsync(progress, operation.Token), operation.Token)).ToList();

            foreach (var item in _currentItems)
                _alerts.Add(PrivacyAlert.From(item));

            int crit   = _currentItems.Count(i => i.RiskLevel == RiskLevel.ExpertOnly);
            int medium  = _currentItems.Count(i => i.RiskLevel == RiskLevel.Recommended);

            StatCritical.Text = crit.ToString();
            StatMedium.Text   = medium.ToString();
            StatTotal.Text    = _currentItems.Count.ToString();

            StatusText.Text = _currentItems.Count == 0
                ? "✅ Aucune alerte détectée."
                : $"⚠️ {_currentItems.Count} alerte(s) — sélectionnez et cliquez Sécuriser.";

            SecureBtn.IsEnabled = _currentItems.Count > 0;
        }
        catch (OperationCanceledException) { StatusText.Text = "Opération annulée — relancez lʼanalyse."; _plugin = null; }
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
            StatusText.Text = "Sélectionnez explicitement les fichiers à supprimer.";
            return;
        }

        var confirm = MessageBox.Show(
            $"Supprimer {selected.Count} fichier(s) sensible(s) ? Un backup sera créé.",
            "Confirmer la sécurisation",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        if (_plugin == null || !UiOperation.TryBegin(this, out var operation)) return;
        using var scope = operation;
        SecureBtn.IsEnabled = ScanBtn.IsEnabled = false;
        StatusText.Text     = "Suppression en cours…";

        try
        {
            var fileGuardian  = App.Services.GetRequiredService<IFileGuardian>();
            var backupManager = App.Services.GetRequiredService<IBackupManager>();
            var logger        = App.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PrivacyScannerPlugin>>();
            var plugin        = _plugin;

            var progress = operation.CreateProgress<double>(p => StatusText.Text = $"Suppression… {p:P0}");
            OperationResult result;
            await using (var transaction = new CleanupTransactionScope(
                             App.Services.GetRequiredService<ITransactionManager>()))
            {
                result = await Task.Run(() => plugin.CleanAsync(selected, backupManager, progress, operation.Token),
                    operation.Token);
                if (!result.Success) throw new InvalidOperationException(result.Message);
                await transaction.CommitAsync(operation.Token);
            }

            StatusText.Text = (result.Success ? "✅ " : "⚠️ ") + result.Message;
            _ = Dispatcher.BeginInvoke(new Action(() => Scan_Click(sender, e)));
        }
        catch (OperationCanceledException) { StatusText.Text = "Opération annulée — relancez lʼanalyse."; _plugin = null; }
        catch (Exception ex) { StatusText.Text = "❌ " + ex.Message; }
        finally { ScanBtn.IsEnabled = true; }
    }
}
