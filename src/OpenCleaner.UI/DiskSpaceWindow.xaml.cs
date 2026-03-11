using Microsoft.Extensions.DependencyInjection;
using OpenCleaner.Contracts;
using OpenCleaner.Core.Security;
using OpenCleaner.Plugins.System.Plugins;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace OpenCleaner.UI;

// ──── ViewModel ────────────────────────────────────────────────────────────

public sealed class DiskEntry
{
    public string Path       { get; init; } = "";
    public long   Size       { get; init; }
    public string SizeLabel  { get; init; } = "";
    public string TypeIcon   { get; init; } = "📄";
    public double BarPercent { get; init; }
    public Brush  SizeColor  { get; init; } = Brushes.White;

    public static DiskEntry From(CleanableItem item, long maxSize)
    {
        var pct   = maxSize > 0 ? Math.Min(100.0, item.Size * 100.0 / maxSize) : 0;
        var color = item.Size >= 1_073_741_824 ? new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x1C)) :
                    item.Size >= 104_857_600    ? new SolidColorBrush(Color.FromRgb(0xFF, 0x8C, 0x00)) :
                    item.Size >= 10_485_760     ? new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00)) :
                                                  new SolidColorBrush(Color.FromRgb(0x10, 0x7C, 0x10));
        return new DiskEntry
        {
            Path       = item.Path,
            Size       = item.Size,
            SizeLabel  = SizeFormatter.Format(item.Size),
            TypeIcon   = item.Type == ItemType.Directory ? "📁" : "📄",
            BarPercent = pct,
            SizeColor  = color
        };
    }
}

// ──── Code-behind ───────────────────────────────────────────────────────────

public partial class DiskSpaceWindow : System.Windows.Window
{
    private readonly ObservableCollection<DiskEntry> _entries = [];
    private IReadOnlyList<CleanableItem> _allItems = [];
    private readonly Stack<string> _navStack = new();
    private string _currentRoot = @"C:\";

    public DiskSpaceWindow()
    {
        InitializeComponent();
        LoadDrives();
        ResultList.ItemsSource = _entries;
    }

    private void LoadDrives()
    {
        foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady))
            DriveCombo.Items.Add(d.RootDirectory.FullName);
        if (DriveCombo.Items.Count > 0) DriveCombo.SelectedIndex = 0;
    }

    private void DriveCombo_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        _currentRoot = (string?)DriveCombo.SelectedItem ?? @"C:\";
        _navStack.Clear();
        GoUpBtn.IsEnabled = false;
        PathBreadcrumb.Text = _currentRoot;
    }

    // ─── SCAN ───────────────────────────────────────────────────────────

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        ScanBtn.IsEnabled  = false;
        StatusText.Text    = "Analyse en cours… (peut prendre quelques secondes)";
        _entries.Clear();
        _allItems = [];
        ClearStats();

        try
        {
            var fileGuardian = App.Services.GetRequiredService<IFileGuardian>();
            var logger       = App.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<DiskAnalyzerPlugin>>();
            var plugin       = new DiskAnalyzerPlugin(fileGuardian, logger) { ScanRoot = _currentRoot };

            _allItems = await plugin.AnalyzeAsync(new Progress<double>(p =>
                StatusText.Text = $"Analyse… {p:P0}"));

            ShowItems(_allItems);
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

    // ─── AFFICHAGE ──────────────────────────────────────────────────────

    private void ShowItems(IEnumerable<CleanableItem> source)
    {
        _entries.Clear();
        var list    = source.ToList();
        long maxSz  = list.Count > 0 ? list.Max(i => i.Size) : 1;

        foreach (var item in list.Take(2000)) // limite 2000 pour perf UI
            _entries.Add(DiskEntry.From(item, maxSz));

        // Stats
        StatTotal.Text  = SizeFormatter.Format(list.Sum(i => i.Size));
        StatHuge.Text   = list.Count(i => i.Size >= 1_073_741_824).ToString();
        StatLarge.Text  = list.Count(i => i.Size >= 104_857_600).ToString();
        StatFiles.Text  = list.Count.ToString();
        StatusText.Text = $"{list.Count} entrées trouvées dans « {_currentRoot} »";
    }

    private void ClearStats()
    {
        StatTotal.Text = StatHuge.Text = StatLarge.Text = StatFiles.Text = "—";
    }

    // ─── FILTRE GROS FICHIERS ────────────────────────────────────────────

    private void FilterLarge_Click(object sender, RoutedEventArgs e)
    {
        if (!_allItems.Any()) return;
        var filtered = _allItems.Where(i => i.Size >= 500L * 1024 * 1024 && i.Type == ItemType.File);
        ShowItems(filtered);
        StatusText.Text = "Filtre actif : fichiers > 500 Mo";
    }

    // ─── DRILL-DOWN ─────────────────────────────────────────────────────

    private async void ResultList_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ResultList.SelectedItem is not DiskEntry entry) return;
        if (entry.TypeIcon != "📁") return;

        _navStack.Push(_currentRoot);
        _currentRoot      = entry.Path;
        PathBreadcrumb.Text = _currentRoot;
        GoUpBtn.IsEnabled = true;

        ScanBtn.IsEnabled = false;
        StatusText.Text   = $"Sous-scan de « {_currentRoot } »…";
        _entries.Clear();

        try
        {
            var fileGuardian = App.Services.GetRequiredService<IFileGuardian>();
            var logger       = App.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<DiskAnalyzerPlugin>>();
            var plugin       = new DiskAnalyzerPlugin(fileGuardian, logger) { ScanRoot = _currentRoot };

            _allItems = await plugin.AnalyzeAsync();
            ShowItems(_allItems);
        }
        catch (Exception ex) { StatusText.Text = "❌ " + ex.Message; }
        finally { ScanBtn.IsEnabled = true; }
    }

    private void GoUp_Click(object sender, RoutedEventArgs e)
    {
        if (_navStack.Count == 0) return;
        _currentRoot = _navStack.Pop();
        PathBreadcrumb.Text = _currentRoot;
        GoUpBtn.IsEnabled = _navStack.Count > 0;
        Scan_Click(sender, e);
    }
}
