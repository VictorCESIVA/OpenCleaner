using Microsoft.Extensions.DependencyInjection;
using OpenCleaner.Contracts;
using OpenCleaner.Core.Security;
using OpenCleaner.Plugins.System.Plugins;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace OpenCleaner.UI;

// ─── Modèles de vue pour le TreeView ───────────────────────────────────────

/// <summary>Nœud-groupe représentant un hash (x fichiers identiques).</summary>
public sealed class DuplicateGroup
{
    public string Hash      { get; }
    public long   Size      { get; }
    public int    Count     => Files.Count;
    public string Label     => $"📁 {Count} fichiers identiques — {FormatSize(Size)}";
    public ObservableCollection<DuplicateFile> Files { get; } = [];

    public DuplicateGroup(string hash, long size) { Hash = hash; Size = size; }

    private static string FormatSize(long b) => b switch
    {
        >= 1_073_741_824 => $"{b / 1_073_741_824.0:0.#} Go",
        >= 1_048_576     => $"{b / 1_048_576.0:0.#} Mo",
        >= 1_024         => $"{b / 1_024.0:0.#} Ko",
        _                => $"{b} o"
    };
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

public partial class DuplicateWindow : Window
{
    private readonly ObservableCollection<DuplicateGroup> _groups = [];
    private List<CleanableItem> _allItems = [];

    private static readonly HashSet<string> ImageExts =
        new(["jpg", "jpeg", "png", "bmp", "gif", "webp"], StringComparer.OrdinalIgnoreCase);

    public DuplicateWindow()
    {
        InitializeComponent();
    }

    // ─── SCAN ───────────────────────────────────────────────────────────

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        ScanBtn.IsEnabled       = false;
        DeleteBtn.IsEnabled     = false;
        AutoSelectBtn.IsEnabled = false;
        DupTree.Items.Clear();
        _groups.Clear();
        _allItems.Clear();
        StatusText.Text = "Analyse en cours…";
        DupGroups.Text = "—";
        DupFiles.Text  = "—";
        DupSize.Text   = "—";

        try
        {
            var fileGuardian = App.Services.GetRequiredService<IFileGuardian>();
            var logger       = App.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<DuplicateFinderPlugin>>();
            var plugin       = new DuplicateFinderPlugin(fileGuardian, logger);

            var progress = new Progress<double>(p =>
                ScanProgress.Text = $"{p:P0}");

            _allItems = (await plugin.AnalyzeAsync(progress)).ToList();

            // Regroupe par hash (contenu de la Description pour récupérer le hash est dans Item.Id)
            // On re-groupe en cherchant les doublons avec le même nom de fichier d'original
            BuildTree(_allItems);

            long totalSize = _allItems.Sum(i => i.Size);
            var groups     = _groups.Count;
            var files      = _allItems.Count;

            DupGroups.Text = groups.ToString();
            DupFiles.Text  = files.ToString();
            DupSize.Text   = FormatSize(totalSize);
            ScanProgress.Text = "";

            StatusText.Text = files == 0
                ? "✅ Aucun doublon détecté dans vos dossiers."
                : $"🔁 {files} copies trouvées dans {groups} groupes.";

            AutoSelectBtn.IsEnabled = files > 0;
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

    private void BuildTree(List<CleanableItem> items)
    {
        // Les items sont déjà groupables car Description commence par "Doublon de « X »"
        // On regroupe simplement par chemin du fichier original extrait de la description
        var byOriginal = items
            .GroupBy(i => ExtractOriginalName(i.Description))
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
                Foreground = System.Windows.Media.Brushes.White
            };

            foreach (var file in group.Files)
            {
                var cb = new CheckBox
                {
                    Content      = file.Label + "   " + file.Path,
                    Foreground   = System.Windows.Media.Brushes.Silver,
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

    private void FileCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        var checkedCount = CountChecked();
        DeleteBtn.IsEnabled = checkedCount > 0;
        StatusText.Text = checkedCount > 0
            ? $"{checkedCount} fichier(s) sélectionné(s) pour suppression."
            : "Sélectionnez des fichiers puis cliquez Supprimer.";
    }

    // ─── SUPPRESSION ────────────────────────────────────────────────────

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        var toDelete = GetCheckedItems();
        if (toDelete.Count == 0) return;

        var confirm = MessageBox.Show(
            $"Supprimer {toDelete.Count} fichier(s) ? Un backup sera créé automatiquement.",
            "Confirmer la suppression",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        DeleteBtn.IsEnabled = false;
        ScanBtn.IsEnabled   = false;
        StatusText.Text     = "Suppression en cours…";

        try
        {
            var fileGuardian  = App.Services.GetRequiredService<IFileGuardian>();
            var backupManager = App.Services.GetRequiredService<IBackupManager>();
            var logger        = App.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<DuplicateFinderPlugin>>();
            var plugin        = new DuplicateFinderPlugin(fileGuardian, logger);

            var result = await plugin.CleanAsync(
                toDelete, backupManager,
                new Progress<double>(p => StatusText.Text = $"Suppression… {p:P0}"));

            StatusText.Text = result.Success ? "✅ " + result.Message : "⚠️ " + result.Message;
            // Relancer l'analyse pour mettre à jour l'arbre
            if (result.Success) Scan_Click(sender, e);
        }
        catch (Exception ex)
        {
            StatusText.Text = "❌ " + ex.Message;
        }
        finally
        {
            DeleteBtn.IsEnabled = false;
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
        PreviewName.Text = System.IO.Path.GetFileName(file.Path);
        PreviewSize.Text = FormatSize(file.Size);
        PreviewImage.Source = null;

        var ext = System.IO.Path.GetExtension(file.Path).TrimStart('.');
        if (!ImageExts.Contains(ext)) return;

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource      = new Uri(file.Path);
            bmp.CacheOption    = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 196;
            bmp.EndInit();
            PreviewImage.Source = bmp;
        }
        catch { PreviewImage.Source = null; }
    }

    // ─── UTILITAIRES ────────────────────────────────────────────────────

    private int CountChecked()
    {
        int n = 0;
        foreach (TreeViewItem node in DupTree.Items)
            foreach (var child in node.Items)
                if (child is CheckBox { IsChecked: true }) n++;
        return n;
    }

    private List<CleanableItem> GetCheckedItems()
    {
        var list = new List<CleanableItem>();
        foreach (TreeViewItem node in DupTree.Items)
            foreach (var child in node.Items)
                if (child is CheckBox { IsChecked: true, Tag: DuplicateFile f })
                    list.Add(f.Item);
        return list;
    }

    private static string ExtractOriginalName(string description)
    {
        // Description = "Doublon de « X » (chemin)" → on retourne X
        var start = description.IndexOf('«');
        var end   = description.IndexOf('»');
        if (start >= 0 && end > start)
            return description.Substring(start + 1, end - start - 2).Trim();
        return description;
    }

    private static string FormatSize(long b) => b switch
    {
        >= 1_073_741_824 => $"{b / 1_073_741_824.0:0.#} Go",
        >= 1_048_576     => $"{b / 1_048_576.0:0.#} Mo",
        >= 1_024         => $"{b / 1_024.0:0.#} Ko",
        _                => $"{b} o"
    };
}
