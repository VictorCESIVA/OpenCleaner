using Microsoft.Extensions.DependencyInjection;
using OpenCleaner.Contracts;
using OpenCleaner.Core.Security;
using OpenCleaner.Plugins.System.Plugins;
using System.Collections.ObjectModel;
using System.Windows;

namespace OpenCleaner.UI;

// ──── ViewModel ─────────────────────────────────────────────────────────────

public sealed class DevItem : System.ComponentModel.INotifyPropertyChanged
{
    private bool _isSelected = true;

    public CleanableItem Item      { get; init; } = null!;
    public string        Category  { get; init; } = "";
    public string        SizeLabel { get; init; } = "";
    public string        PathLabel => Item.Description + "\n" + Item.Path;
    public long          Size      => Item.Size;

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public static DevItem From(CleanableItem item) => new()
    {
        Item      = item,
        Category  = item.Description.Split('—')[0].Trim(),
        SizeLabel = SizeFormatter.Format(item.Size)
    };

}

// ──── Code-behind ────────────────────────────────────────────────────────────

public partial class DevCleanerWindow : System.Windows.Window
{
    private readonly ObservableCollection<DevItem> _items = [];
    private List<CleanableItem> _allItems = [];

    public DevCleanerWindow()
    {
        InitializeComponent();
        ResultList.ItemsSource = _items;
    }

    // ─── SCAN ───────────────────────────────────────────────────────────

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        ScanBtn.IsEnabled    = false;
        CleanBtn.IsEnabled   = false;
        SelectAllBtn.IsEnabled = false;
        _items.Clear();
        _allItems.Clear();
        EstimateText.Text   = "—";
        EstimateDetail.Text = "";
        StatusText.Text     = "Analyse en cours…";

        try
        {
            var fileGuardian = App.Services.GetRequiredService<IFileGuardian>();
            var logger       = App.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<DevCleanerPlugin>>();
            var plugin       = new DevCleanerPlugin(fileGuardian, logger)
            {
                ScanNodeModules = CbNode.IsChecked    == true,
                ScanNpmCache    = CbNpm.IsChecked     == true,
                ScanYarnCache   = CbYarn.IsChecked    == true,
                ScanVsCode      = CbVsCode.IsChecked  == true,
                ScanDiscord     = CbDiscord.IsChecked == true,
                ScanSpotify     = CbSpotify.IsChecked == true,
                ScanJetBrains   = CbJetBrains.IsChecked == true,
                ScanDocker      = CbDocker.IsChecked  == true,
            };

            _allItems = (await plugin.AnalyzeAsync(
                new Progress<double>(p => StatusText.Text = $"Scan… {p:P0}"))).ToList();

            foreach (var item in _allItems)
                _items.Add(DevItem.From(item));

            long total = _allItems.Sum(i => i.Size);
            EstimateText.Text = SizeFormatter.Format(total);

            var byCategory = _allItems
                .GroupBy(i => i.Description.Split('—')[0].Trim())
                .Select(g => $"{g.Key}: {SizeFormatter.Format(g.Sum(i => i.Size))}")
                .ToList();
            EstimateDetail.Text = string.Join("\n", byCategory.Take(6));

            StatusText.Text = _allItems.Count == 0
                ? "✅ Aucun artefact dev détecté."
                : $"{_allItems.Count} fichiers / dossiers — {SizeFormatter.Format(total)} libérables.";

            CleanBtn.IsEnabled    = _allItems.Count > 0;
            SelectAllBtn.IsEnabled = _allItems.Count > 0;
        }
        catch (Exception ex) { StatusText.Text = "❌ " + ex.Message; }
        finally { ScanBtn.IsEnabled = true; }
    }

    // ─── SÉLECTION ──────────────────────────────────────────────────────

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _items) item.IsSelected = true;
    }

    // ─── NETTOYAGE ──────────────────────────────────────────────────────

    private async void Clean_Click(object sender, RoutedEventArgs e)
    {
        var toClean = _items.Where(i => i.IsSelected).Select(i => i.Item).ToList();
        if (toClean.Count == 0) { StatusText.Text = "Aucun élément sélectionné."; return; }

        var confirm = MessageBox.Show(
            $"Supprimer/nettoyer {toClean.Count} élément(s) ?\n" +
            "Les fichiers individuels auront un backup. Les dossiers (node_modules) seront supprimés directement.",
            "Confirmer le nettoyage Dev",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        CleanBtn.IsEnabled = ScanBtn.IsEnabled = false;
        StatusText.Text    = "Nettoyage en cours…";

        try
        {
            var fileGuardian  = App.Services.GetRequiredService<IFileGuardian>();
            var backupManager = App.Services.GetRequiredService<IBackupManager>();
            var logger        = App.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<DevCleanerPlugin>>();
            var plugin        = new DevCleanerPlugin(fileGuardian, logger);

            var result = await plugin.CleanAsync(toClean, backupManager,
                new Progress<double>(p => StatusText.Text = $"Nettoyage… {p:P0}"));

            StatusText.Text = (result.Success ? "✅ " : "⚠️ ") + result.Message;
            if (result.Success) Scan_Click(sender, e);
        }
        catch (Exception ex) { StatusText.Text = "❌ " + ex.Message; }
        finally { ScanBtn.IsEnabled = true; }
    }
}
