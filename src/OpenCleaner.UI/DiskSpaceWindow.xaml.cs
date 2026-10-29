using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using OpenCleaner.Contracts;
using OpenCleaner.Core.Security;
using OpenCleaner.Core.Tools;

namespace OpenCleaner.UI;

/// <summary>Ligne affichée dans la liste des enfants ou dans celle des plus gros fichiers.</summary>
public sealed class DiskEntry
{
    public DiskEntry(DiskUsageNode node, long maxSize, bool isAggregate = false)
    {
        Node = node;
        IsAggregate = isAggregate;
        SizeLabel = SizeFormatter.Format(node.Size);
        TypeIcon = isAggregate ? "🗂" : node.IsFile ? "📄" : "📁";
        Detail = isAggregate
            ? "Éléments non listés individuellement"
            : node.IsFile
                ? "Fichier"
                : $"{node.FileCount:N0} fichiers • {Math.Max(0, node.DirectoryCount - 1):N0} dossiers" +
                  (node.IsProtected ? " • protégé" : "");
        ModifiedLabel = node.LastWriteTime == DateTime.MinValue ? "—" : node.LastWriteTime.ToLocalTime().ToString("g");
        BarPercent = maxSize > 0 ? Math.Clamp(node.Size * 100.0 / maxSize, 0, 100) : 0;
        SizeColor = (Brush)Application.Current.FindResource(SizeColorKey(node.Size));
    }

    public DiskUsageNode Node { get; }
    public string Name => Node.Name;
    public string Path => Node.FullPath;
    public long Size => Node.Size;
    public string SizeLabel { get; }
    public string ModifiedLabel { get; }
    public string TypeIcon { get; }
    public string Detail { get; }
    public double BarPercent { get; }
    public Brush SizeColor { get; }
    public bool IsAggregate { get; }
    public bool CanDrill => !IsAggregate && !Node.IsFile;

    private static string SizeColorKey(long size) =>
        size >= 1_073_741_824 ? "SizeHuge" :
        size >= 104_857_600 ? "SizeLarge" :
        size >= 10_485_760 ? "SizeMed" : "SizeSmall";
}

public sealed class ApplicationSpaceRow
{
    public ApplicationSpaceRow(InstalledSpaceEntry entry)
    {
        Entry = entry;
        SizeLabel = SizeFormatter.Format(entry.Size);
        Publisher = string.IsNullOrWhiteSpace(entry.Publisher) ? "—" : entry.Publisher!;
        SourceLabel = entry.Measured ? "Mesuré" : "Estimé";
        SourceBrush = (Brush)Application.Current.FindResource(entry.Measured ? "TextSecond" : "WarnColor");
        SizeColor = (Brush)Application.Current.FindResource(SizeColorKey(entry.Size));
    }

    public InstalledSpaceEntry Entry { get; }
    public string Name => Entry.Name;
    public string Publisher { get; }
    public string Location => Entry.Location;
    public long Size => Entry.Size;
    public string SizeLabel { get; }
    public string SourceLabel { get; }
    public Brush SourceBrush { get; }
    public Brush SizeColor { get; }

    private static string SizeColorKey(long size) =>
        size >= 1_073_741_824 ? "SizeHuge" :
        size >= 104_857_600 ? "SizeLarge" :
        size >= 10_485_760 ? "SizeMed" : "SizeSmall";
}

public partial class DiskSpaceWindow : UserControl, IThemeAware
{
    private enum DiskView { Map, List, Files, Apps }

    private static readonly string DefaultHint = "Double-cliquez sur un dossier pour l'explorer.";

    private readonly ObservableCollection<DiskEntry> _entries = [];
    private readonly ObservableCollection<DiskEntry> _largest = [];
    private readonly ObservableCollection<ApplicationSpaceRow> _applications = [];
    private readonly Stack<DiskUsageNode> _navStack = new();
    private DiskUsageResult? _result;
    private DiskUsageNode? _current;
    private IReadOnlyList<InstalledSpaceEntry>? _previewApplications;
    private CancellationTokenSource? _appsCancellation;
    private DiskView _view = DiskView.Map;
    private bool _busy;
    private string _currentRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";

    public DiskSpaceWindow()
    {
        InitializeComponent();
        ResultList.ItemsSource = _entries;
        LargestList.ItemsSource = _largest;
        ApplicationsList.ItemsSource = _applications;
        Treemap.NodeActivated += OnTreemapActivated;
        Treemap.HoveredNodeChanged += OnTreemapHovered;
        LoadDrives();
        UpdateView();
        PathBreadcrumb.Text = _currentRoot;
    }

    private void LoadDrives()
    {
        foreach (var drive in DriveInfo.GetDrives().Where(drive =>
                     drive.IsReady && drive.DriveType is DriveType.Fixed or DriveType.Removable))
            DriveCombo.Items.Add(drive.RootDirectory.FullName);
        if (DriveCombo.Items.Count > 0) DriveCombo.SelectedIndex = 0;
    }

    public void ApplyTheme(bool dark)
    {
        Treemap.RefreshTheme();
        if (_current != null) ShowNode(_current);
        if (_view == DiskView.Files) ReloadLargest();
    }

    /// <summary>Alimente la fenêtre avec un résultat synthétique (aperçus, tests de rendu).</summary>
    internal void ShowPreviewData(DiskUsageResult result, string view = "map",
        IReadOnlyList<InstalledSpaceEntry>? applications = null)
    {
        _result = result;
        _previewApplications = applications;
        _view = view switch
        {
            "list" => DiskView.List,
            "files" => DiskView.Files,
            "apps" => DiskView.Apps,
            _ => DiskView.Map
        };
        UpdateStats(result);
        ShowNode(result.Root);
        UpdateView();
        StatusText.Text = "Aperçu avec des données synthétiques — aucune analyse exécutée.";
        EmptyState.Visibility = Visibility.Collapsed;
    }

    private void DriveCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_busy) return;
        _currentRoot = (string?)DriveCombo.SelectedItem ?? @"C:\";
        ResetScan();
        StatusText.Text = "Sélectionnez un lecteur et cliquez Analyser.";
    }

    private void ResetScan()
    {
        _navStack.Clear();
        _result = null;
        _current = null;
        _entries.Clear();
        _largest.Clear();
        _applications.Clear();
        _previewApplications = null;
        _appsCancellation?.Cancel();
        Treemap.Node = null;
        PathBreadcrumb.Text = _currentRoot;
        GoUpBtn.IsEnabled = false;
        EmptyState.Visibility = Visibility.Visible;
        ClearStats();
        MapHint.Text = DefaultHint;
    }

    // ─── SCAN ───────────────────────────────────────────────────────────

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (!UiOperation.TryBegin(Window.GetWindow(this), out var operation)) return;
        using (operation)
        {
            await ScanAsync(_currentRoot, operation.Token);
        }
    }

    /// <summary>Analyse réelle réutilisable par la validation locale (aucune suppression).</summary>
    internal Task RunLiveScanAsync(string root, CancellationToken ct = default)
    {
        _currentRoot = root;
        return ScanAsync(root, ct);
    }

    internal DiskUsageResult? CurrentResult => _result;

    private async Task ScanAsync(string root, CancellationToken ct)
    {
        SetBusy(true);
        EmptyState.Visibility = Visibility.Collapsed;
        _entries.Clear();
        _largest.Clear();
        _applications.Clear();
        _previewApplications = null;
        _appsCancellation?.Cancel();
        Treemap.Node = null;
        ClearStats();
        StatusText.Text = "Analyse en cours… les dossiers protégés sont uniquement mesurés.";
        try
        {
            var guardian = App.Services.GetRequiredService<IFileGuardian>();
            var service = new DiskUsageService();
            var options = new DiskUsageOptions { IsProtected = guardian.IsSystemCriticalPath };
            var progress = new Progress<double>(value =>
            {
                if (_busy) StatusText.Text = $"Analyse… {value:P0}";
            });
            DiskUsageResult result = await Task.Run(
                () => service.Scan(root, options, progress, ct), ct);

            _result = result;
            _currentRoot = root;
            _navStack.Clear();
            UpdateStats(result);
            ShowNode(result.Root);
            StatusText.Text = result.SkippedEntries > 0
                ? $"{result.TotalDirectories:N0} dossiers, {result.TotalFiles:N0} fichiers • " +
                  $"{result.SkippedEntries:N0} lien(s) ou élément(s) illisible(s) ignoré(s)"
                : $"{result.TotalDirectories:N0} dossiers, {result.TotalFiles:N0} fichiers accessibles";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Analyse annulée.";
            ResetScan();
        }
        catch (Exception ex)
        {
            StatusText.Text = "L'analyse a échoué : " + ex.Message;
            ResetScan();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        ScanBtn.IsEnabled = !busy;
        DriveCombo.IsEnabled = !busy;
        ResultList.IsEnabled = LargestList.IsEnabled = ApplicationsList.IsEnabled = Treemap.IsEnabled = !busy;
        RevealBtn.IsEnabled = !busy;
        GoUpBtn.IsEnabled = !busy && _navStack.Count > 0;
        ViewMapBtn.IsEnabled = ViewListBtn.IsEnabled = ViewFilesBtn.IsEnabled = ViewAppsBtn.IsEnabled = !busy;
    }

    // ─── AFFICHAGE ──────────────────────────────────────────────────────

    private void UpdateStats(DiskUsageResult result)
    {
        StatTotal.Text = SizeFormatter.Format(result.TotalSize);
        StatFiles.Text = result.TotalFiles.ToString("N0");
        StatDirs.Text = result.TotalDirectories.ToString("N0");
        StatLargest.Text = result.LargestFiles.Count > 0
            ? SizeFormatter.Format(result.LargestFiles[0].Size)
            : "—";
    }

    private void ClearStats() => StatTotal.Text = StatFiles.Text = StatDirs.Text = StatLargest.Text = "—";

    private void ShowNode(DiskUsageNode node)
    {
        _current = node;
        PathBreadcrumb.Text = node.FullPath;
        GoUpBtn.IsEnabled = !_busy && _navStack.Count > 0;
        Treemap.Node = node;
        EmptyState.Visibility = Visibility.Collapsed;

        var children = new List<DiskUsageNode>(node.Directories.Count + node.Files.Count);
        children.AddRange(node.Directories);
        children.AddRange(node.Files);
        children.Sort((left, right) => right.Size.CompareTo(left.Size));

        long maxSize = children.Count > 0 ? children[0].Size : 1;
        _entries.Clear();
        long listed = 0;
        foreach (var child in children)
        {
            _entries.Add(new DiskEntry(child, maxSize));
            listed += child.Size;
        }

        long remaining = Math.Max(0, node.Size - listed);
        if (remaining > 0)
            _entries.Add(new DiskEntry(
                new DiskUsageNode("Autres éléments", node.FullPath, isFile: false, size: remaining,
                    isProtected: node.IsProtected),
                maxSize, isAggregate: true));

        MapHint.Text = node.Size > 0
            ? $"{SizeFormatter.Format(node.Size)} • {node.FileCount:N0} fichiers • double-cliquez pour explorer"
            : DefaultHint;
    }

    private void ReloadLargest()
    {
        _largest.Clear();
        if (_result == null) return;
        long maxSize = _result.LargestFiles.Count > 0 ? _result.LargestFiles[0].Size : 1;
        foreach (var file in _result.LargestFiles)
            _largest.Add(new DiskEntry(file, maxSize));
    }

    private void UpdateView()
    {
        ResultList.Visibility = _view == DiskView.List ? Visibility.Visible : Visibility.Collapsed;
        MapHost.Visibility = _view == DiskView.Map ? Visibility.Visible : Visibility.Collapsed;
        LargestList.Visibility = _view == DiskView.Files ? Visibility.Visible : Visibility.Collapsed;
        ApplicationsList.Visibility = _view == DiskView.Apps ? Visibility.Visible : Visibility.Collapsed;

        ViewMapBtn.Style = (Style)FindResource(_view == DiskView.Map ? "AccentBtnStyle" : "SecondBtnStyle");
        ViewListBtn.Style = (Style)FindResource(_view == DiskView.List ? "AccentBtnStyle" : "SecondBtnStyle");
        ViewFilesBtn.Style = (Style)FindResource(_view == DiskView.Files ? "AccentBtnStyle" : "SecondBtnStyle");
        ViewAppsBtn.Style = (Style)FindResource(_view == DiskView.Apps ? "AccentBtnStyle" : "SecondBtnStyle");

        if (_view == DiskView.Files)
        {
            ReloadLargest();
            MapHint.Text = _result == null
                ? "Lancez une analyse pour lister les plus gros fichiers."
                : $"{_result.LargestFiles.Count:N0} fichier(s) les plus volumineux de l'analyse.";
        }
        else if (_view == DiskView.Apps)
        {
            if (_previewApplications != null)
                PopulateApplications(_previewApplications);
            else
                _ = LoadApplicationsAsync();
        }
        else if (_current != null)
        {
            MapHint.Text = _view == DiskView.Map
                ? $"{SizeFormatter.Format(_current.Size)} • double-cliquez sur un dossier pour l'explorer"
                : "Double-cliquez sur un dossier pour l'explorer.";
        }
    }

    private async Task LoadApplicationsAsync()
    {
        DiskUsageResult? result = _result;
        if (result == null)
        {
            _applications.Clear();
            MapHint.Text = "Lancez une analyse pour associer les applications aux dossiers.";
            return;
        }

        _appsCancellation?.Cancel();
        _appsCancellation?.Dispose();
        var cancellation = _appsCancellation = new CancellationTokenSource();
        MapHint.Text = "Association des applications aux dossiers analysés…";
        try
        {
            var entries = await Task.Run(() =>
                InstalledSpaceMapper.Map(result.Root, InstalledProgramService.GetInstalledPrograms()),
                cancellation.Token);
            if (cancellation.IsCancellationRequested || !ReferenceEquals(_result, result) ||
                _view != DiskView.Apps) return;
            PopulateApplications(entries);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            MapHint.Text = "Lecture des applications impossible : " + ex.Message;
        }
    }

    private void PopulateApplications(IReadOnlyList<InstalledSpaceEntry> entries)
    {
        _applications.Clear();
        foreach (var entry in entries) _applications.Add(new ApplicationSpaceRow(entry));
        MapHint.Text = entries.Count == 0
            ? "Aucune application installée ne correspond aux dossiers analysés."
            : $"{entries.Count:N0} application(s) • tailles mesurées ou estimées depuis le registre.";
    }

    private void ViewMap_Click(object sender, RoutedEventArgs e) => SwitchView(DiskView.Map);
    private void ViewList_Click(object sender, RoutedEventArgs e) => SwitchView(DiskView.List);
    private void ViewFiles_Click(object sender, RoutedEventArgs e) => SwitchView(DiskView.Files);
    private void ViewApps_Click(object sender, RoutedEventArgs e) => SwitchView(DiskView.Apps);

    private void SwitchView(DiskView view)
    {
        if (_busy) return;
        if (_view == DiskView.Apps && view != DiskView.Apps) _appsCancellation?.Cancel();
        _view = view;
        UpdateView();
    }

    // ─── NAVIGATION ─────────────────────────────────────────────────────

    private void Drill(DiskUsageNode node)
    {
        if (_current != null) _navStack.Push(_current);
        ShowNode(node);
        StatusText.Text = $"{node.FullPath} • {SizeFormatter.Format(node.Size)} • {node.FileCount:N0} fichiers";
    }

    private void OnTreemapActivated(object? sender, DiskUsageNode node)
    {
        if (node.IsFile)
        {
            ExplorerHelper.RevealInExplorer(node.FullPath);
            return;
        }
        if (_current != null && string.Equals(node.FullPath, _current.FullPath, StringComparison.OrdinalIgnoreCase)) return;
        Drill(node);
    }

    private void OnTreemapHovered(object? sender, DiskUsageNode? node)
    {
        MapHint.Text = node == null
            ? _current != null
                ? $"{SizeFormatter.Format(_current.Size)} • double-cliquez sur un dossier pour l'explorer"
                : DefaultHint
            : $"{node.Name} • {SizeFormatter.Format(node.Size)}" +
              (node.IsFile ? "" : $" • {node.FileCount:N0} fichiers") +
              (node.IsProtected ? " • protégé" : "");
    }

    private void ResultList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_busy || ResultList.SelectedItem is not DiskEntry entry || entry.IsAggregate) return;
        if (entry.Node.IsFile) ExplorerHelper.RevealInExplorer(entry.Path);
        else Drill(entry.Node);
    }

    private void LargestList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (!_busy && LargestList.SelectedItem is DiskEntry entry)
            ExplorerHelper.RevealInExplorer(entry.Path);
    }

    private void ApplicationsList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (!_busy && ApplicationsList.SelectedItem is ApplicationSpaceRow row)
            ExplorerHelper.RevealInExplorer(row.Location);
    }

    private void GoUp_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _navStack.Count == 0) return;
        ShowNode(_navStack.Pop());
        StatusText.Text = _current?.FullPath ?? _currentRoot;
    }

    private void Reveal_Click(object sender, RoutedEventArgs e)
    {
        if (ResultList.SelectedItem is DiskEntry entry) ExplorerHelper.RevealInExplorer(entry.Path);
        else if (LargestList.SelectedItem is DiskEntry file) ExplorerHelper.RevealInExplorer(file.Path);
        else if (ApplicationsList.SelectedItem is ApplicationSpaceRow application)
            ExplorerHelper.RevealInExplorer(application.Location);
        else ExplorerHelper.RevealInExplorer(_current?.FullPath ?? _currentRoot);
    }
}
