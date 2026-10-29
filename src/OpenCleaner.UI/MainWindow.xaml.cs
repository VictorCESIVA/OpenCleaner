using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using OpenCleaner.Contracts;
using OpenCleaner.Core;
using OpenCleaner.Core.Security;
using OpenCleaner.Core.Tools;
using OpenCleaner.Core.Transactions;
using OpenCleaner.Plugins.System.Plugins;
using OpenCleaner.UI.Localization;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace OpenCleaner.UI;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<CleanableItem> _foundItems = [];
    private List<CleanableItem> _allAnalyzedItems = [];
    private ICleanerPlugin? _currentPlugin;
    private string _selectedPluginTag = "system";
    private bool _isDarkTheme = true;
    private bool _loadingOptions = true;
    private SmartScanResult[]? _smartScanResults;
    private readonly Dictionary<string, UserControl> _toolViews = [];
    private Button? _activeToolButton;

    private static readonly Dictionary<string, (string TitleKey, string DescKey)> PluginMeta = new()
    {
        ["system"] = ("Plugin.System.Title", "Plugin.System.Desc"),
        ["windowsupdate"] = ("Plugin.WindowsUpdate.Title", "Plugin.WindowsUpdate.Desc"),
        ["thumbnails"] = ("Plugin.Thumbnails.Title", "Plugin.Thumbnails.Desc"),
        ["browser"] = ("Plugin.Browser.Title", "Plugin.Browser.Desc"),
        ["steam"] = ("Plugin.Steam.Title", "Plugin.Steam.Desc"),
        ["office"] = ("Plugin.Office.Title", "Plugin.Office.Desc"),
        ["appcaches"] = ("Plugin.AppCaches.Title", "Plugin.AppCaches.Desc"),
        ["registry"] = ("Plugin.Registry.Title", "Plugin.Registry.Desc"),
    };

    private static string T(string key, params object?[] args) => LocalizationService.Instance.Get(key, args);

    public MainWindow()
    {
        InitializeComponent();
        FilesListView.ItemsSource = _foundItems;
        ApplyTheme(App.Preferences.DarkTheme);
        UpdateSidebarSelection("system");
        UpdatePluginHeader("system");
        _loadingOptions = false;
        UiOperation.StateChanged += OnOperationStateChanged;
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        Closed += (_, _) =>
        {
            UiOperation.StateChanged -= OnOperationStateChanged;
            LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        };
        Closing += (_, e) =>
        {
            if (UiOperation.IsBusy)
            {
                e.Cancel = true;
                UiOperation.CancelCurrent();
                StatusText.Text = T("Main.Cancelling");
            }
        };
        Loaded += async (_, _) => await CheckLowSpaceAsync();
    }

    private async Task CheckLowSpaceAsync()
    {
        try
        {
            SystemHealthInfo info = await Task.Run(SystemInfoService.Get);
            DiskSpaceAlert? alert = DiskSpaceAlertService.FindLowSpace(info.Disks,
                App.Preferences.LowSpaceThresholdPercent / 100.0);
            if (alert == null)
            {
                LowSpaceBanner.Visibility = Visibility.Collapsed;
                return;
            }
            ShowLowSpace(alert);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            LowSpaceBanner.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowLowSpace(DiskSpaceAlert alert)
    {
        LowSpaceTitle.Text = T("Main.LowSpaceTitle", alert.Drive.TrimEnd('\\'));
        LowSpaceDetail.Text = T("Main.LowSpaceDetail", SizeFormatter.Format(alert.FreeBytes),
            SizeFormatter.Format(alert.TotalBytes), alert.FreePercent.ToString("0.#"));
        LowSpaceBanner.Visibility = Visibility.Visible;
    }

    internal Task RefreshLowSpaceAsync() => CheckLowSpaceAsync();

    internal void ShowLowSpaceForPreview(DiskSpaceAlert alert) => ShowLowSpace(alert);

    private void OnOperationStateChanged() => SetBusy(UiOperation.IsBusy);

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        UpdatePluginHeader(_selectedPluginTag);
        ThemeIcon.Text = T(_isDarkTheme ? "Nav.ThemeLight" : "Nav.ThemeDark");
        ApplyFilter();
        if (!UiOperation.IsBusy && _foundItems.Count == 0)
            StatusText.Text = T("Main.ReadyForScan");
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F)
        {
            if (DashboardScroll.Visibility != Visibility.Visible) return;
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
            return;
        }
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.E)
        {
            if (!ExportReportBtn.IsEnabled) return;
            ExportReport_Click(sender, e);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.F5 && DashboardScroll.Visibility == Visibility.Visible && !UiOperation.IsBusy)
        {
            Analyze_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && UiOperation.IsBusy)
        {
            CancelOperation_Click(sender, e);
            e.Handled = true;
        }
    }

    private void SidebarPlugin_Click(object sender, RoutedEventArgs e)
    {
        if (UiOperation.IsBusy || sender is not Button { Tag: string tag }) return;
        ShowCleaner(tag);
    }

    internal void ShowCleaner(string tag)
    {
        if (UiOperation.IsBusy || !PluginMeta.ContainsKey(tag)) return;
        if (_activeToolButton != null) _activeToolButton.Style = (Style)FindResource("SidebarBtnStyle");
        _activeToolButton = null;
        ToolContent.Visibility = Visibility.Collapsed;
        DashboardScroll.Visibility = Visibility.Visible;
        if (_selectedPluginTag != tag) InvalidateAnalysis();
        _selectedPluginTag = tag;
        UpdateSidebarSelection(tag);
        UpdatePluginHeader(tag);
    }

    private void InvalidateAnalysis()
    {
        _currentPlugin = null;
        _allAnalyzedItems.Clear();
        _foundItems.Clear();
        _smartScanResults = null;
        CleanBtn.IsEnabled = SmartCleanBtn.IsEnabled = ExportReportBtn.IsEnabled = RevealFileBtn.IsEnabled = false;
        SmartCleanResult.Visibility = Visibility.Collapsed;
        ResetStats();
        EmptyState.Visibility = Visibility.Visible;
        ActionStatus.Text = "";
    }

    private void UpdateSidebarSelection(string activeTag)
    {
        foreach (var button in SidebarPanel.Children.OfType<Button>())
            button.Style = (Style)FindResource((button.Tag as string) == activeTag
                ? "SidebarBtnActiveStyle" : "SidebarBtnStyle");
    }

    private void UpdatePluginHeader(string tag)
    {
        if (!PluginMeta.TryGetValue(tag, out var meta)) return;
        PluginTitle.Text = T(meta.TitleKey);
        PluginDescription.Text = T(meta.DescKey);
        StatusText.Text = T("Main.ReadyForScan");
        BrowserOptionsPanel.Visibility = tag == "browser" ? Visibility.Visible : Visibility.Collapsed;
        if (tag == "browser") LoadBrowserOptions();
    }

    private void LoadBrowserOptions()
    {
        _loadingOptions = true;
        OptCache.IsChecked = BrowserCleanOptions.IncludeCache;
        OptCodeCache.IsChecked = BrowserCleanOptions.IncludeCodeCache;
        OptGpuCache.IsChecked = BrowserCleanOptions.IncludeGpuCache;
        OptServiceWorkers.IsChecked = BrowserCleanOptions.IncludeServiceWorkers;
        OptCookies.IsChecked = BrowserCleanOptions.IncludeCookies;
        OptHistory.IsChecked = BrowserCleanOptions.IncludeHistory;
        OptFavicons.IsChecked = BrowserCleanOptions.IncludeFavicons;
        _loadingOptions = false;
    }

    private void BrowserOpt_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingOptions) return;
        if (UiOperation.IsBusy) { LoadBrowserOptions(); return; }
        BrowserCleanOptions.IncludeCache = OptCache.IsChecked == true;
        BrowserCleanOptions.IncludeCodeCache = OptCodeCache.IsChecked == true;
        BrowserCleanOptions.IncludeGpuCache = OptGpuCache.IsChecked == true;
        BrowserCleanOptions.IncludeServiceWorkers = OptServiceWorkers.IsChecked == true;
        BrowserCleanOptions.IncludeCookies = OptCookies.IsChecked == true;
        BrowserCleanOptions.IncludeHistory = OptHistory.IsChecked == true;
        BrowserCleanOptions.IncludeFavicons = OptFavicons.IsChecked == true;
        InvalidateAnalysis();
        StatusText.Text = T("Main.OptionsChanged");
    }

    private ICleanerPlugin? CreatePlugin(string tag)
    {
        var guardian = App.Services.GetRequiredService<IFileGuardian>();
        return tag switch
        {
            "system" => new SystemTempPlugin(guardian, GetLogger<SystemTempPlugin>()),
            "windowsupdate" => new WindowsUpdatePlugin(guardian, GetLogger<WindowsUpdatePlugin>()),
            "thumbnails" => new ThumbnailCachePlugin(guardian, GetLogger<ThumbnailCachePlugin>()),
            "browser" => new ChromeCleanerPlugin(guardian, GetLogger<ChromeCleanerPlugin>()),
            "steam" => new SteamCleanerPlugin(guardian, GetLogger<SteamCleanerPlugin>()),
            "office" => new OfficeCleanerPlugin(guardian, GetLogger<OfficeCleanerPlugin>()),
            "appcaches" => new AppCacheCleanerPlugin(guardian, GetLogger<AppCacheCleanerPlugin>()),
            "registry" => new RegistryCleanerPlugin(App.Services.GetRequiredService<IRegistryGuardian>(),
                GetLogger<RegistryCleanerPlugin>()),
            _ => null
        };
    }

    private void SetBusy(bool busy)
    {
        NavigationScroll.IsEnabled = SettingsNav.IsEnabled = BrowserOptionsPanel.IsEnabled = !busy;
        ToolCancelBtn.Visibility = busy && ToolContent.Visibility == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
        ToolCancelBtn.IsEnabled = busy;
        ToolCancelBtn.Content = T("Nav.CancelOperation");
        AnalyzeBtn.IsEnabled = SmartScanBtn.IsEnabled = !busy;
        SearchBox.IsEnabled = RiskFilterCombo.IsEnabled = !busy;
        CleanBtn.IsEnabled = !busy && _currentPlugin != null && _foundItems.Count > 0;
        ExportReportBtn.IsEnabled = !busy && _allAnalyzedItems.Count > 0;
        RevealFileBtn.IsEnabled = !busy && FilesListView.SelectedItem is CleanableItem;
        SmartCleanBtn.IsEnabled = !busy && _smartScanResults?.Any(r => r.Items.Count > 0) == true;
    }

    private void ShowLoading(string text, string? sub = null)
    {
        sub ??= T("Main.CancelDetail");
        SetBusy(true);
        LoadingText.Text = text;
        LoadingSubText.Text = sub;
        LoadingOverlay.Visibility = Visibility.Visible;
        ProgressBar.Visibility = Visibility.Visible;
        ProgressBar.IsIndeterminate = true;
        SpinnerRotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360,
            new Duration(TimeSpan.FromSeconds(1))) { RepeatBehavior = RepeatBehavior.Forever });
    }

    private void HideLoading()
    {
        SpinnerRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        LoadingOverlay.Visibility = ProgressBar.Visibility = Visibility.Collapsed;
        ProgressBar.IsIndeterminate = false;
        SetBusy(false);
    }

    private void CancelOperation_Click(object sender, RoutedEventArgs e)
    {
        UiOperation.CancelCurrent();
        ToolCancelBtn.IsEnabled = false;
        ToolCancelBtn.Content = T("Main.CancellingShort");
        LoadingText.Text = T("Main.CancellingShort");
        LoadingSubText.Text = T("Main.CancellingSub");
    }

    private async void Analyze_Click(object sender, RoutedEventArgs e)
    {
        if (!UiOperation.TryBegin(this, out var operation)) return;
        using (operation)
        {
            InvalidateAnalysis();
            EmptyState.Visibility = Visibility.Collapsed;
            ShowLoading(T("Main.LoadingAnalyze"));
            StatusText.Text = T("Main.LoadingAnalyze");
            try
            {
                var plugin = CreatePlugin(_selectedPluginTag);
                if (plugin == null) return;
                _currentPlugin = plugin;
                var progress = CreateProgress();
                var items = await Task.Run(() => plugin.AnalyzeAsync(progress, operation.Token), operation.Token);
                _allAnalyzedItems = items.ToList();
                ApplyFilter();
                if (items.Count > 0 && TryFindResource("FadeInStory") is Storyboard story) story.Begin();
                UpdateStats(_allAnalyzedItems);
                StatusText.Text = items.Count == 0
                    ? T("Main.NothingToClean")
                    : T("Main.ItemsFound", items.Count, SizeFormatter.Format(items.Sum(i => i.Size)));
                if (_selectedPluginTag == "browser" && BrowsersRunning())
                    ActionStatus.Text = T("Main.CloseBrowsers");
            }
            catch (OperationCanceledException)
            {
                _currentPlugin = null;
                StatusText.Text = T("Main.ScanCancelled");
                EmptyState.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                _currentPlugin = null;
                StatusText.Text = T("Main.ScanFailed");
                ActionStatus.Text = ex.Message;
                EmptyState.Visibility = Visibility.Visible;
            }
            finally { HideLoading(); }
        }
    }

    private static bool BrowsersRunning() => ChromeCleanerPlugin.IsAnyBrowserRunning();

    private void Filter_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loadingOptions) return;
        ApplyFilter();
    }

    private void RiskFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingOptions) return;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (FilesListView == null) return;
        var query = SearchBox?.Text?.Trim() ?? "";
        var riskTag = (RiskFilterCombo?.SelectedItem as ComboBoxItem)?.Tag as string ?? "All";

        IEnumerable<CleanableItem> filtered = _allAnalyzedItems;
        if (!string.IsNullOrEmpty(query))
        {
            filtered = filtered.Where(i =>
                i.Path.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                i.Description.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        filtered = riskTag switch
        {
            "Safe" => filtered.Where(i => i.RiskLevel == RiskLevel.Safe),
            "Recommended" => filtered.Where(i => i.RiskLevel == RiskLevel.Recommended),
            "ExpertOnly" => filtered.Where(i => i.RiskLevel == RiskLevel.ExpertOnly),
            _ => filtered
        };

        _foundItems.Clear();
        foreach (var item in filtered) _foundItems.Add(item);
        if (EmptyState != null)
            EmptyState.Visibility = _foundItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (CleanBtn != null && !UiOperation.IsBusy)
            CleanBtn.IsEnabled = _currentPlugin != null && _foundItems.Count > 0;
        if (ExportReportBtn != null && !UiOperation.IsBusy)
            ExportReportBtn.IsEnabled = _allAnalyzedItems.Count > 0;
    }

    private void FilesListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RevealFileBtn != null)
            RevealFileBtn.IsEnabled = !UiOperation.IsBusy && FilesListView.SelectedItem is CleanableItem;
        var selected = FilesListView.SelectedItems.Cast<CleanableItem>().ToList();
        if (selected.Count > 0)
            ActionStatus.Text = T("Main.SelectedCount", selected.Count,
                SizeFormatter.Format(selected.Sum(i => i.Size)));
    }

    private void FilesListView_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
        RevealSelected_Click(sender, e);

    private void RevealSelected_Click(object sender, RoutedEventArgs e)
    {
        if (FilesListView.SelectedItem is CleanableItem item && item.Type is ItemType.File or ItemType.Directory)
            ExplorerHelper.RevealInExplorer(item.Path);
    }

    private async void ExportReport_Click(object sender, RoutedEventArgs e)
    {
        if (_allAnalyzedItems.Count == 0 || UiOperation.IsBusy) return;
        var dlg = new SaveFileDialog
        {
            Title = T("Main.ExportReportTitle"),
            Filter = T("Main.ExportFilter"),
            FileName = $"OpenCleaner-Report-{_selectedPluginTag}-{DateTime.Now:yyyyMMdd-HHmm}.json"
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var payload = new
            {
                ExportedAt = DateTime.Now,
                Category = _selectedPluginTag,
                TotalItems = _allAnalyzedItems.Count,
                TotalBytes = _allAnalyzedItems.Sum(i => i.Size),
                TotalFormatted = SizeFormatter.Format(_allAnalyzedItems.Sum(i => i.Size)),
                Items = _allAnalyzedItems.Select(i => new
                {
                    i.Path,
                    i.Size,
                    SizeFormatted = SizeFormatter.Format(i.Size),
                    i.Description,
                    RiskLevel = i.RiskLevel.ToString(),
                    Type = i.Type.ToString(),
                    i.LastAccessTime
                })
            };
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(dlg.FileName, json);
            ActionStatus.Text = T("Main.ReportExported", Path.GetFileName(dlg.FileName));
        }
        catch (Exception ex)
        {
            ActionStatus.Text = T("Main.ExportFailed", ex.Message);
        }
    }

    private IProgress<double> CreateProgress()
    {
        var throttle = Stopwatch.StartNew();
        return new Progress<double>(p =>
        {
            if (throttle.ElapsedMilliseconds < 80 || LoadingOverlay.Visibility != Visibility.Visible) return;
            throttle.Restart();
            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = Math.Clamp(p, 0, 1) * 100;
        });
    }

    private async void Clean_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPlugin == null || _foundItems.Count == 0 || UiOperation.IsBusy) return;
        if (_selectedPluginTag == "browser" && BrowsersRunning())
        {
            MessageBox.Show(this, T("Main.CloseAllBrowsers"),
                T("Main.BrowsersOpenTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var selected = FilesListView.SelectedItems.Cast<CleanableItem>().ToList();
        var items = selected.Count > 0 ? selected : _foundItems.ToList();
        var warning = _selectedPluginTag == "registry"
            ? T("Main.RegistryWarning")
            : T("Main.BackupWarning");
        if (MessageBox.Show(this, T("Main.ConfirmClean", items.Count, warning),
            T("Main.ConfirmCleanTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning,
            MessageBoxResult.No) != MessageBoxResult.Yes) return;
        if (!UiOperation.TryBegin(this, out var operation)) return;
        using (operation)
        {
            var plugin = _currentPlugin;
            ShowLoading(T("Main.Cleaning"), T("Main.CleaningSub"));
            try
            {
                var backup = App.Services.GetRequiredService<IBackupManager>();
                var progress = CreateProgress();
                OperationResult result;
                await using (var transaction = new CleanupTransactionScope(
                                 App.Services.GetRequiredService<ITransactionManager>()))
                {
                    result = await Task.Run(() => plugin.CleanAsync(items, backup, progress, operation.Token),
                        operation.Token);
                    if (!result.Success) throw new InvalidOperationException(result.Message);
                    await transaction.CommitAsync(operation.Token);
                }
                ActionStatus.Text = result.Message;
                StatusText.Text = result.Success ? T("Main.CleanDone") : T("Main.CleanDoneSkipped");
                // Re-analyze registry paths; File.Exists cannot represent a registry key.
                if (_selectedPluginTag == "registry")
                {
                    var remaining = await Task.Run(() => plugin.AnalyzeAsync(ct: operation.Token), operation.Token);
                    _allAnalyzedItems = remaining.ToList();
                }
                else
                {
                    var removedIds = await Task.Run(() => items
                        .Where(i => !File.Exists(i.Path) && !Directory.Exists(i.Path))
                        .Select(i => i.Id)
                        .ToHashSet(StringComparer.Ordinal), operation.Token);
                    if (removedIds.Count > 0)
                        _allAnalyzedItems = _allAnalyzedItems.Where(i => !removedIds.Contains(i.Id)).ToList();
                }
                ApplyFilter();
                UpdateStats(_allAnalyzedItems);
                _smartScanResults = null;
                SmartCleanResult.Visibility = Visibility.Collapsed;
            }
            catch (OperationCanceledException)
            {
                InvalidateAnalysis();
                StatusText.Text = T("Main.CleanCancelled");
                if (_selectedPluginTag == "registry")
                    MessageBox.Show(this, T("Main.RegistryManual"),
                        T("Main.RegistryDialogTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                StatusText.Text = T("Main.CleanFailed");
                ActionStatus.Text = ex.Message + (_selectedPluginTag == "registry"
                    ? T("Main.RegistryErrorSuffix")
                    : "");
            }
            finally { HideLoading(); }
        }
    }

    private void UpdateStats(IReadOnlyList<CleanableItem> items)
    {
        StatFiles.Text = items.Count.ToString("N0");
        StatSize.Text = SizeFormatter.Format(items.Sum(i => i.Size));
        var risk = items.Count > 0 ? items.Max(i => i.RiskLevel) : RiskLevel.Safe;
        StatRisk.Text = risk switch
        {
            RiskLevel.Safe => T("Risk.Low"),
            RiskLevel.Recommended => T("Risk.Review"),
            RiskLevel.ExpertOnly => T("Risk.Expert"),
            _ => T("Main.RiskUnknown")
        };
        StatRisk.SetResourceReference(TextBlock.ForegroundProperty, risk switch
        {
            RiskLevel.Safe => "Accent",
            RiskLevel.Recommended => "Warning",
            _ => "Danger"
        });
    }

    private void ResetStats() => StatFiles.Text = StatSize.Text = StatRisk.Text = "—";

    private void ApplyTheme(bool dark)
    {
        _isDarkTheme = dark;
        var palette = dark ? new Dictionary<string, string>
        {
            ["BgMain"]="#15191F", ["BgSidebar"]="#101419", ["BgCard"]="#1D232B",
            ["BgHover"]="#29323C", ["BgSelected"]="#263C38", ["Accent"]="#8BC9AA",
            ["AccentHover"]="#A8DCC1", ["AccentForeground"]="#0D211A",
            ["TextPrimary"]="#F1F3F4", ["TextSecond"]="#B0BAC4", ["Border"]="#343E49",
            ["Warning"]="#F2C078", ["Danger"]="#FF929C"
        } : new Dictionary<string, string>
        {
            ["BgMain"]="#F4F5F6", ["BgSidebar"]="#EAEDEF", ["BgCard"]="#FFFFFF",
            ["BgHover"]="#E6E9EB", ["BgSelected"]="#DCEBE3", ["Accent"]="#176649",
            ["AccentHover"]="#105139", ["AccentForeground"]="#FFFFFF",
            ["TextPrimary"]="#202830", ["TextSecond"]="#55616D", ["Border"]="#D2D8DD",
            ["Warning"]="#805211", ["Danger"]="#B7263B"
        };
        foreach (var (key, color) in palette)
            SetThemeColor(key, color);
        var additional = new Dictionary<string, string>
        {
            ["RiskSafe"] = dark ? "#85E0B4" : "#176649",
            ["RiskRecommended"] = dark ? "#F4C27A" : "#805211",
            ["RiskExpert"] = dark ? "#F5A0A4" : "#B7263B",
            ["SizeHuge"] = dark ? "#F5A0A4" : "#B7263B",
            ["SizeLarge"] = dark ? "#F4C27A" : "#805211",
            ["SizeMed"] = dark ? "#E7D88A" : "#746418",
            ["SizeSmall"] = dark ? "#85E0B4" : "#176649",
            ["WarnColor"] = dark ? "#F4C27A" : "#805211"
        };
        foreach (var (key, color) in additional)
            SetThemeColor(key, color);
        foreach (var view in _toolViews.Values.OfType<IThemeAware>())
            view.ApplyTheme(dark);
        ThemeIcon.Text = T(dark ? "Nav.ThemeLight" : "Nav.ThemeDark");
    }

    private static void SetThemeColor(string key, string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex);
        if (Application.Current.Resources[key] is SolidColorBrush { IsFrozen: false } brush)
            brush.Color = color;
        else Application.Current.Resources[key] = new SolidColorBrush(color);
    }

    private void ThemeToggle_Click(object sender, RoutedEventArgs e)
    {
        ApplyTheme(!_isDarkTheme);
        App.SetPreferences(App.Preferences with { DarkTheme = _isDarkTheme });
    }
    internal void ShowTool(string key, Button? button = null)
    {
        if (UiOperation.IsBusy) return;
        if (!_toolViews.TryGetValue(key, out var view))
        {
            view = key switch
            {
                "restore" => new RestoreWindow(), "duplicates" => new DuplicateWindow(),
                "settings" => new SettingsWindow(), "disk" => new DiskSpaceWindow(),
                "privacy" => new PrivacyWindow(), "dev" => new DevCleanerWindow(),
                "health" => new HealthWindow(), "uninstall" => new UninstallerWindow(),
                "startup" => new StartupWindow(), "updates" => new UpdatesWindow(),
                "shredder" => new ShredderWindow(), _ => throw new ArgumentException("Unknown tool", nameof(key))
            };
            _toolViews.Add(key, view);
            if (view is IThemeAware themeAware) themeAware.ApplyTheme(_isDarkTheme);
        }
        if (_activeToolButton != null) _activeToolButton.Style = (Style)FindResource("SidebarBtnStyle");
        _activeToolButton = button;
        if (button != null) button.Style = (Style)FindResource("SidebarBtnActiveStyle");
        UpdateSidebarSelection("");
        DashboardScroll.Visibility = Visibility.Collapsed;
        ToolContent.Content = view;
        ToolContent.Visibility = Visibility.Visible;
    }

    private void OpenRestore_Click(object sender, RoutedEventArgs e) => ShowTool("restore", sender as Button);
    private void OpenDuplicates_Click(object sender, RoutedEventArgs e) => ShowTool("duplicates", sender as Button);
    private void OpenSettings_Click(object sender, RoutedEventArgs e) => ShowTool("settings", sender as Button);
    private void OpenDiskAnalyzer_Click(object sender, RoutedEventArgs e) => ShowTool("disk", sender as Button);
    private void OpenPrivacy_Click(object sender, RoutedEventArgs e) => ShowTool("privacy", sender as Button);
    private void OpenDevCleaner_Click(object sender, RoutedEventArgs e) => ShowTool("dev", sender as Button);
    private void OpenHealth_Click(object sender, RoutedEventArgs e) => ShowTool("health", sender as Button);
    private void OpenUninstaller_Click(object sender, RoutedEventArgs e) => ShowTool("uninstall", sender as Button);
    private void OpenStartup_Click(object sender, RoutedEventArgs e) => ShowTool("startup", sender as Button);
    private void OpenUpdates_Click(object sender, RoutedEventArgs e) => ShowTool("updates", sender as Button);
    private void OpenShredder_Click(object sender, RoutedEventArgs e) => ShowTool("shredder", sender as Button);

    internal void ShowHealthTool(string key)
    {
        Button? button = key switch { "startup" => StartupNav, "updates" => UpdatesNav, "disk" => DiskNav, _ => null };
        if (button != null) ShowTool(key, button);
    }

    private async void SmartScan_Click(object sender, RoutedEventArgs e)
    {
        if (!UiOperation.TryBegin(this, out var operation)) return;
        using (operation)
        {
            _smartScanResults = null;
            SmartCleanResult.Visibility = Visibility.Collapsed;
            ShowLoading(T("Main.SmartScanning"));
            SmartCleanDesc.Text = T("Main.SmartScanDesc");
            try
            {
                ICleanerPlugin[] plugins = new[] { "system", "thumbnails", "browser" }
                    .Select(tag => CreatePlugin(tag)!).ToArray();
                var service = new SmartCleanService(App.Services.GetRequiredService<IBackupManager>());
                _smartScanResults = await Task.Run(() => service.QuickScanAsync(plugins, operation.Token), operation.Token);
                var total = _smartScanResults.Sum(r => r.TotalSize);
                var failed = _smartScanResults.Count(r => r.Error != null);
                SmartCleanDesc.Text = T("Main.SmartCategories",
                    _smartScanResults.Length - failed, _smartScanResults.Length);
                if (failed > 0) SmartCleanDesc.Text += T("Main.SmartIncomplete", failed);
                SmartCleanResult.Text = total == 0
                    ? failed > 0 ? T("Main.SmartPartial") : T("Main.SmartNone")
                    : T("Main.SmartDetected", SizeFormatter.Format(total));
                SmartCleanResult.ToolTip = string.Join(Environment.NewLine, _smartScanResults.Where(r => r.Error != null)
                    .Select(r => r.Plugin.Name + " : " + r.Error));
                SmartCleanResult.Visibility = Visibility.Visible;
            }
            catch (OperationCanceledException) { SmartCleanDesc.Text = T("Main.SmartCancelled"); }
            catch (Exception ex) { SmartCleanDesc.Text = ex.Message; }
            finally { HideLoading(); }
        }
    }

    private async void SmartClean_Click(object sender, RoutedEventArgs e)
    {
        if (_smartScanResults == null || UiOperation.IsBusy) return;
        if (BrowsersRunning() && _smartScanResults.Any(r => r.Plugin.Id == "browser.chrome" && r.Items.Count > 0))
        {
            MessageBox.Show(this, T("Main.CloseBrowsersQuick"),
                T("Main.BrowsersOpenTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show(this, T("Main.ConfirmSmart", _smartScanResults.Sum(r => r.Items.Count)),
            T("Main.ConfirmSmartTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question,
            MessageBoxResult.No) != MessageBoxResult.Yes) return;
        if (!UiOperation.TryBegin(this, out var operation)) return;
        using (operation)
        {
            ShowLoading(T("Main.SmartCleaning"));
            try
            {
                var scans = _smartScanResults;
                var service = new SmartCleanService(App.Services.GetRequiredService<IBackupManager>());
                var progress = new Progress<(int current, int total, string plugin)>(p =>
                    SmartCleanDesc.Text = T("Main.SmartCleaningStep", p.plugin, p.current, p.total));
                SmartCleanResult result;
                await using (var transaction = new CleanupTransactionScope(
                                 App.Services.GetRequiredService<ITransactionManager>()))
                {
                    result = await Task.Run(() => service.ExecuteSmartCleanAsync(scans, progress, operation.Token),
                        operation.Token);
                    if (!result.Success) throw new InvalidOperationException(string.Join("\n", result.Errors));
                    await transaction.CommitAsync(operation.Token);
                }
                SmartCleanDesc.Text = T("Main.SmartResult", result.ItemsCleaned,
                    SizeFormatter.Format(result.BytesFreed));
                if (!result.Success) SmartCleanDesc.Text += T("Main.SmartErrors", result.Errors.Count);
                _smartScanResults = null;
                _currentPlugin = null;
                _foundItems.Clear();
                ResetStats();
                EmptyState.Visibility = Visibility.Visible;
                SmartCleanResult.Visibility = Visibility.Collapsed;
            }
            catch (OperationCanceledException) { SmartCleanDesc.Text = T("Main.SmartInterrupted"); _smartScanResults = null; }
            catch (Exception ex) { SmartCleanDesc.Text = ex.Message; _smartScanResults = null; }
            finally { HideLoading(); }
        }
    }

    internal void NotifyUpdateAvailable() => ActionStatus.Text = T("Main.UpdateAvailable");
    private static ILogger<T> GetLogger<T>() => App.Services.GetRequiredService<ILogger<T>>();
}
