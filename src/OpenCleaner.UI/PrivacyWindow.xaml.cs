using Microsoft.Extensions.DependencyInjection;
using OpenCleaner.Contracts;
using OpenCleaner.Core.Security;
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
            ? new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x1C))
            : new SolidColorBrush(Color.FromRgb(0xFF, 0x8C, 0x00))
    };
}

// ──── Code-behind ────────────────────────────────────────────────────────────

public partial class PrivacyWindow : System.Windows.Window
{
    private readonly ObservableCollection<PrivacyAlert> _alerts = [];
    private List<CleanableItem> _currentItems = [];

    public PrivacyWindow()
    {
        InitializeComponent();
        AlertList.ItemsSource = _alerts;
    }

    // ─── SCAN ───────────────────────────────────────────────────────────

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        ScanBtn.IsEnabled   = false;
        SecureBtn.IsEnabled = false;
        _alerts.Clear();
        _currentItems.Clear();
        StatCritical.Text = StatMedium.Text = StatTotal.Text = "—";
        StatusText.Text   = "Analyse en cours…";

        try
        {
            var fileGuardian = App.Services.GetRequiredService<IFileGuardian>();
            var logger       = App.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PrivacyScannerPlugin>>();
            var plugin       = new PrivacyScannerPlugin(fileGuardian, logger);

            _currentItems = (await plugin.AnalyzeAsync(
                new Progress<double>(p => StatusText.Text = $"Scan… {p:P0}"))).ToList();

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

    private async void SecureSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = AlertList.SelectedItems.Cast<PrivacyAlert>()
            .Select(a => a.Item)
            .ToList();

        if (selected.Count == 0) selected = _currentItems;

        var confirm = MessageBox.Show(
            $"Supprimer {selected.Count} fichier(s) sensible(s) ? Un backup sera créé.",
            "Confirmer la sécurisation",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        SecureBtn.IsEnabled = ScanBtn.IsEnabled = false;
        StatusText.Text     = "Suppression en cours…";

        try
        {
            var fileGuardian  = App.Services.GetRequiredService<IFileGuardian>();
            var backupManager = App.Services.GetRequiredService<IBackupManager>();
            var logger        = App.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PrivacyScannerPlugin>>();
            var plugin        = new PrivacyScannerPlugin(fileGuardian, logger);

            var result = await plugin.CleanAsync(selected, backupManager,
                new Progress<double>(p => StatusText.Text = $"Suppression… {p:P0}"));

            StatusText.Text = (result.Success ? "✅ " : "⚠️ ") + result.Message;
            if (result.Success) Scan_Click(sender, e);
        }
        catch (Exception ex) { StatusText.Text = "❌ " + ex.Message; }
        finally { ScanBtn.IsEnabled = true; }
    }
}
