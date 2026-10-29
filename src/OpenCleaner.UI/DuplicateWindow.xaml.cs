using Microsoft.Extensions.DependencyInjection;
using OpenCleaner.Contracts;
using OpenCleaner.Core.Security;
using OpenCleaner.Core.Transactions;
using OpenCleaner.Plugins.System.Plugins;
using OpenCleaner.UI.Localization;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace OpenCleaner.UI;

// ─── Modèles de vue pour le TreeView ───────────────────────────────────────

/// <summary>Nœud-groupe représentant un hash (x fichiers identiques).</summary>
public sealed class DuplicateGroup : INotifyPropertyChanged
{
    private static string T(string key, params object?[] args) => LocalizationService.Instance.Get(key, args);

    public string Hash      { get; }
    public long   Size      { get; }
    public int    Count     => Files.Count;
    public string Label     => T("Dup.GroupLabel", Count, SizeFormatter.Format(Size),
        Files.FirstOrDefault()?.Item.OriginalPath ?? "");
    public ObservableCollection<DuplicateFile> Files { get; } = [];

    public DuplicateGroup(string hash, long size) { Hash = hash; Size = size; }

    public event PropertyChangedEventHandler? PropertyChanged;

    internal void RefreshLocalization() =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label)));
}

/// <summary>Feuille représentant un fichier dans un groupe.</summary>
public sealed class DuplicateFile
{
    public CleanableItem Item     { get; }
    public bool          IsChecked { get; set; } = false;
    public string        Path     => Item.Path;
    public string        Label    => System.IO.Path.GetFileName(Item.Path);
    public string        FullLabel => (IsChecked ? "☑ " : "☐ ") + Label;
    public long          Size     => Item.Size;

    public DuplicateFile(CleanableItem item) { Item = item; }
}

// ─── Fenêtre ────────────────────────────────────────────────────────────────

public partial class DuplicateWindow : UserControl
{
    private static string T(string key, params object?[] args) => LocalizationService.Instance.Get(key, args);

    private readonly ObservableCollection<DuplicateGroup> _groups = [];
    private List<CleanableItem> _allItems = [];
    private DuplicateFinderPlugin? _plugin;
    private DuplicateFile? _selectedPreviewFile;
    private IProgress<string>? _hashingProgress;
    private readonly System.Diagnostics.Stopwatch _hashThrottle = new();

    private static readonly HashSet<string> ImageExts =
        new(["jpg", "jpeg", "png", "bmp", "gif", "webp"], StringComparer.OrdinalIgnoreCase);

    public DuplicateWindow()
    {
        InitializeComponent();
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        foreach (DuplicateGroup group in _groups) group.RefreshLocalization();
    }

    // ─── SCAN ───────────────────────────────────────────────────────────

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        if (!UiOperation.TryBegin(Window.GetWindow(this), out var operation)) return;
        using var scope = operation;
        ScanBtn.IsEnabled       = false;
        DeleteBtn.IsEnabled     = false;
        AutoSelectBtn.IsEnabled = false;
        UnselectAllBtn.IsEnabled = false;
        MinSizeCombo.IsEnabled  = false;
        DupTree.Items.Clear();
        _groups.Clear();
        _allItems.Clear();
        StatusText.Text = T("Dup.StatusCollecting");
        CurrentFileText.Text = "";
        DupGroups.Text = "—";
        DupFiles.Text  = "—";
        DupSize.Text   = "—";
        ClearPreview();

        try
        {
            var fileGuardian = App.Services.GetRequiredService<IFileGuardian>();
            var logger       = App.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<DuplicateFinderPlugin>>();
            long minBytes    = long.TryParse((MinSizeCombo.SelectedItem as ComboBoxItem)?.Tag as string, out var parsed)
                ? parsed : 1024L * 1024L;
            var plugin       = _plugin = new DuplicateFinderPlugin(fileGuardian, logger) { MinFileSizeBytes = minBytes };

            var progress = operation.CreateProgress<double>(p =>
                ScanProgress.Text = T("Dup.Progress", p));

            _hashingProgress = operation.CreateProgress<string>(path => CurrentFileText.Text = path);
            _hashThrottle.Restart();
            plugin.OnFileHashing += Plugin_OnFileHashing;

            StatusText.Text = T("Dup.StatusHashing");

            _allItems = (await Task.Run(() => plugin.AnalyzeAsync(progress, operation.Token), operation.Token)).ToList();

            plugin.OnFileHashing -= Plugin_OnFileHashing;

            // Regroupe par hash (contenu de la Description pour récupérer le hash est dans Item.Id)
            // On re-groupe en cherchant les doublons avec le même nom de fichier d'original
            ShowResults();
            var groups     = _groups.Count;
            var files      = _allItems.Count;

            ScanProgress.Text = "";
            CurrentFileText.Text = "";

            StatusText.Text = files == 0
                ? T("Dup.NoDuplicates")
                : T("Dup.Found", files, groups);
        }
        catch (OperationCanceledException) { StatusText.Text = T("Dup.Cancelled"); _plugin = null; }
        catch (Exception ex)
        {
            StatusText.Text = "❌ " + ex.Message;
        }
        finally
        {
            if (_plugin != null) _plugin.OnFileHashing -= Plugin_OnFileHashing;
            _hashingProgress = null;
            ScanBtn.IsEnabled = true;
            MinSizeCombo.IsEnabled = true;
        }
    }

    private void ShowResults()
    {
        DupTree.Items.Clear();
        _groups.Clear();
        BuildTree(_allItems);
        DupGroups.Text = _groups.Count.ToString();
        DupFiles.Text = _allItems.Count.ToString();
        DupSize.Text = SizeFormatter.Format(_allItems.Sum(item => item.Size));
        AutoSelectBtn.IsEnabled = UnselectAllBtn.IsEnabled = _plugin != null && _allItems.Count > 0;
        DeleteBtn.IsEnabled = false;
        ClearPreview();
    }

    internal async Task RefreshCleanupResultsAsync()
    {
        _allItems = await Task.Run(() => CleanupResults.GetRemainingItems(_allItems));
        ShowResults();
        if (!IsVisible && _plugin != null)
            StatusText.Text = _allItems.Count == 0 ? T("Dup.NoDuplicates") : T("Dup.Found", _allItems.Count, _groups.Count);
    }

    private void Plugin_OnFileHashing(string path)
    {
        if (_hashThrottle.ElapsedMilliseconds < 80) return;
        _hashThrottle.Restart();
        _hashingProgress?.Report(path);
    }

    private void BuildTree(List<CleanableItem> items)
    {
        // Les items sont déjà groupables car Description commence par "Doublon de « X »"
        // On regroupe simplement par chemin du fichier original extrait de la description
        var byOriginal = items
            .GroupBy(i => i.DuplicateGroupId ?? i.OriginalPath ?? i.Id)
            .ToList();

        foreach (var grp in byOriginal)
        {
            var group = new DuplicateGroup(grp.Key, grp.First().Size);
            foreach (var item in grp)
                group.Files.Add(new DuplicateFile(item));

            _groups.Add(group);

            // Construire le TreeViewItem manuellement pour avoir les CheckBox
            var groupNode = new TreeViewItem
            {
                Header   = group.Label,
                IsExpanded = true,
                Foreground = (System.Windows.Media.Brush)FindResource("TextPrimary")
            };

            foreach (var file in group.Files)
            {
                var cb = new CheckBox
                {
                    Content      = file.Label + "   " + file.Path,
                    Foreground   = (System.Windows.Media.Brush)FindResource("TextSecond"),
                    IsChecked    = false,
                    Tag          = file,
                    FontSize     = 12
                };
                cb.Checked   += FileCheckBox_Changed;
                cb.Unchecked += FileCheckBox_Changed;
                groupNode.Items.Add(cb);
            }

            DupTree.Items.Add(groupNode);
        }
    }

    // ─── SÉLECTION AUTO ─────────────────────────────────────────────────

    private void AutoSelect_Click(object sender, RoutedEventArgs e)
    {
        // Coche toutes les copies (les cases dans le TreeView)
        foreach (TreeViewItem node in DupTree.Items)
        {
            foreach (var child in node.Items)
            {
                if (child is CheckBox cb) cb.IsChecked = true;
            }
        }
    }

    private void UnselectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (TreeViewItem node in DupTree.Items)
        {
            foreach (var child in node.Items)
            {
                if (child is CheckBox cb) cb.IsChecked = false;
            }
        }
    }

    private void FileCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        var checkedItems = GetCheckedItems();
        DeleteBtn.IsEnabled = checkedItems.Count > 0;
        StatusText.Text = checkedItems.Count > 0
            ? T("Dup.Selected", checkedItems.Count,
                SizeFormatter.Format(checkedItems.Sum(i => i.Size)))
            : T("Dup.SelectPrompt");
    }

    // ─── SUPPRESSION ────────────────────────────────────────────────────

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        var toDelete = GetCheckedItems();
        if (toDelete.Count == 0) return;

        bool? createBackup = CleanupConfirmationWindow.Confirm(Window.GetWindow(this),
            T("Dup.ConfirmDelete", toDelete.Count));
        if (createBackup == null) return;

        if (_plugin == null || !UiOperation.TryBegin(Window.GetWindow(this), out var operation)) return;
        using var scope = operation;
        AutoSelectBtn.IsEnabled = false;
        DeleteBtn.IsEnabled = false;
        ScanBtn.IsEnabled   = false;
        StatusText.Text     = T("Dup.Deleting");

        try
        {
            var fileGuardian  = App.Services.GetRequiredService<IFileGuardian>();
            var backupManager = App.Services.GetRequiredService<IBackupManager>();
            var logger        = App.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<DuplicateFinderPlugin>>();
            var plugin        = _plugin;

            var progress = operation.CreateProgress<double>(p => StatusText.Text = T("Dup.DeleteProgress", p));
            OperationResult result;
            await using (var transaction = new CleanupTransactionScope(
                             App.Services.GetRequiredService<ITransactionManager>(), createBackup.Value))
            {
                result = await Task.Run(() => plugin.CleanAsync(toDelete, backupManager, progress, operation.Token),
                    operation.Token);
                if (!result.Success) throw new InvalidOperationException(result.Message);
                await transaction.CommitAsync(operation.Token);
            }

            StatusText.Text = result.Success
                ? "✅ " + PluginMessageLocalizer.Localize(result.Message)
                : "⚠️ " + PluginMessageLocalizer.Localize(result.Message);
        }
        catch (OperationCanceledException) { StatusText.Text = T(createBackup.Value ? "Dup.Cancelled" : "Cleanup.PermanentInterrupted"); }
        catch (Exception ex)
        {
            StatusText.Text = "❌ " + ex.Message;
        }
        finally
        {
            if (Window.GetWindow(this) is MainWindow main) await main.RefreshCleanupResultsAsync();
            else await RefreshCleanupResultsAsync();
            ScanBtn.IsEnabled   = true;
        }
    }

    // ─── PREVIEW IMAGE ──────────────────────────────────────────────────

    private void DupTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        // Quand on sélectionne un CheckBox, on tente le preview
        if (e.NewValue is CheckBox cb && cb.Tag is DuplicateFile file)
        {
            LoadPreview(file);
        }
    }

    private void LoadPreview(DuplicateFile file)
    {
        _selectedPreviewFile = file;
        RevealDupBtn.IsEnabled = true;
        PreviewName.Text = System.IO.Path.GetFileName(file.Path);
        PreviewSize.Text = SizeFormatter.Format(file.Size);
        PreviewImage.Source = null;
        PreviewPlaceholder.Visibility = Visibility.Visible;

        var ext = System.IO.Path.GetExtension(file.Path).TrimStart('.');
        if (!ImageExts.Contains(ext))
        {
            PreviewPlaceholder.Text = T("Dup.PreviewNone");
            PreviewPlaceholder.FontSize = 12;
            return;
        }

        PreviewPlaceholder.Text = T("Dup.PreviewLoading");
        PreviewPlaceholder.FontSize = 12;

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource      = new Uri(file.Path);
            bmp.CacheOption    = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 196;
            bmp.EndInit();
            bmp.Freeze();
            PreviewImage.Source = bmp;
            PreviewPlaceholder.Visibility = Visibility.Collapsed;
        }
        catch
        {
            PreviewImage.Source = null;
            PreviewPlaceholder.Text = T("Dup.PreviewError");
            PreviewPlaceholder.Visibility = Visibility.Visible;
        }
    }

    private void ClearPreview()
    {
        _selectedPreviewFile = null;
        RevealDupBtn.IsEnabled = false;
        PreviewName.Text = "—";
        PreviewSize.Text = "";
        PreviewImage.Source = null;
        PreviewPlaceholder.Text = T("Dup.PreviewHint");
        PreviewPlaceholder.Visibility = Visibility.Visible;
    }

    private void RevealDup_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPreviewFile != null)
            ExplorerHelper.RevealInExplorer(_selectedPreviewFile.Path);
    }

    // ─── UTILITAIRES ────────────────────────────────────────────────────

    private List<CleanableItem> GetCheckedItems()
    {
        var list = new List<CleanableItem>();
        foreach (TreeViewItem node in DupTree.Items)
            foreach (var child in node.Items)
                if (child is CheckBox { IsChecked: true, Tag: DuplicateFile f })
                    list.Add(f.Item);
        return list;
    }
}
