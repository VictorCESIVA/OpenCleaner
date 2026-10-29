using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using OpenCleaner.Contracts;
using OpenCleaner.Core;
using OpenCleaner.Core.Health;
using OpenCleaner.Core.Security;
using OpenCleaner.Core.Tools;
using OpenCleaner.Core.Transactions;
using OpenCleaner.Plugins.System.Plugins;
using OpenCleaner.UI.Localization;

namespace OpenCleaner.UI;

/// <summary>Ligne de recommandation prête à lier, avec sa couleur et son libellé d'action.</summary>
public sealed class HealthFindingRow
{
    public HealthFindingRow(HealthFinding finding)
    {
        Finding = finding;
        SeverityBrush = (Brush)Application.Current.FindResource(finding.Severity switch
        {
            HealthSeverity.Critical => "Danger",
            HealthSeverity.Warning => "WarnColor",
            HealthSeverity.Advice => "Accent",
            _ => "SizeSmall"
        });
        ActionLabel = finding.Action switch
        {
            "disk" => HealthLocalization.T("Health.Action.Explore"),
            "startup" => HealthLocalization.T("Health.Action.Manage"),
            "updates" => HealthLocalization.T("Health.Action.View"),
            "cleanTemp" => HealthLocalization.T("Health.Action.Analyze"),
            "cleanBrowser" => HealthLocalization.T("Health.Action.Analyze"),
            "recycle" => HealthLocalization.T("Health.Action.Empty"),
            _ => null
        };
        Action = finding.Action;
        ActionVisibility = ActionLabel == null ? Visibility.Collapsed : Visibility.Visible;
        ReclaimLabel = finding.ReclaimableBytes is long bytes && bytes > 0
            ? "• " + SizeFormatter.Format(bytes)
            : "";
    }

    public HealthFinding Finding { get; }
    public string Title => HealthLocalization.Title(Finding);
    public string Detail => HealthLocalization.Detail(Finding);
    public string Recommendation => HealthLocalization.Recommendation(Finding);
    public Brush SeverityBrush { get; }
    public string? Action { get; }
    public string? ActionLabel { get; }
    public Visibility ActionVisibility { get; }
    public string ReclaimLabel { get; }
}

/// <summary>Traduit les constats du moteur ; les textes français restent le repli.</summary>
internal static class HealthLocalization
{
    public static string T(string key, params object?[] args) => LocalizationService.Instance.Get(key, args);

    public static string Title(HealthFinding finding) =>
        finding.TitleKey is { } key ? T(key, finding.Args ?? []) : finding.Title;

    public static string Detail(HealthFinding finding) =>
        finding.DetailKey is { } key ? T(key, finding.Args ?? []) : finding.Detail;

    public static string Recommendation(HealthFinding finding) =>
        finding.RecommendationKey is { } key ? T(key, finding.Args ?? []) : finding.Recommendation;

    public static string SectionTitle(HealthSection section)
    {
        string id = string.IsNullOrEmpty(section.Id)
            ? section.Id
            : char.ToUpperInvariant(section.Id[0]) + section.Id[1..];
        return T("Health.Section." + id);
    }

    public static string Grade(int score) => score switch
    {
        >= 90 => T("Health.Grade.Excellent"),
        >= 75 => T("Health.Grade.Good"),
        >= 55 => T("Health.Grade.Watch"),
        _ => T("Health.Grade.Critical")
    };

    public static HealthFinding Localize(HealthFinding finding) => finding with
    {
        Title = Title(finding),
        Detail = Detail(finding),
        Recommendation = Recommendation(finding)
    };
}

public partial class HealthWindow : UserControl, IThemeAware
{
    private bool _busy;
    private bool _loaded;
    private bool _hasRecycleItems;
    private HealthReport? _report;
    private HealthMetrics? _metrics;
    private SystemHealthInfo? _info;
    private CleanupHistorySummary? _history;
    private IReadOnlyList<SpaceTrendPoint> _trendPoints = [];
    private SpaceTrendResult? _trendResult;

    public HealthWindow()
    {
        InitializeComponent();
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        Loaded += async (_, _) =>
        {
            if (_loaded) return;
            _loaded = true;
            await LoadAsync();
        };
    }

    internal void InvalidateAfterCleanup() => _loaded = false;

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (_report == null || _metrics == null || _info == null) return;
        ShowReport(_report, _metrics, _info);
        if (_history != null) PopulateHistory(_history);
        if (_trendResult != null) PopulateTrend(_trendResult);
    }

    private async Task LoadAsync()
    {
        if (_busy) return;
        SetBusy(true);
        StatusText.Text = HealthLocalization.T("Health.Status.System");
        try
        {
            SystemHealthInfo info = await Task.Run(SystemInfoService.Get);
            RecycleBinInfo? bin = null;
            try { bin = await Task.Run(RecycleBinService.Query); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            IReadOnlyList<StartupItem> startup = await Task.Run(StartupItemService.GetItems);

            _hasRecycleItems = bin is { ItemCount: > 0 };
            RecycleText.Text = bin == null
                ? HealthLocalization.T("Health.Recycle.Unavailable")
                : HealthLocalization.T("Health.Recycle.Summary", bin.ItemCount.ToString("N0"),
                    SizeFormatter.Format(bin.SizeBytes));

            HealthMetrics baseMetrics = HealthCheckService.Collect(info, bin, startup);
            ShowReport(HealthScoreCalculator.Evaluate(baseMetrics), baseMetrics, info);
            StatusText.Text = HealthLocalization.T("Health.Status.Base");

            try
            {
                CleanupHistorySummary history = await Task.Run(
                    () => new CleanupHistoryService().GetSummary(TimeSpan.FromDays(30)));
                PopulateHistory(history);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                HistoryCaption.Text = HealthLocalization.T("Health.History.Unavailable", ex.Message);
            }

            try
            {
                string primaryDrive = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
                SpaceTrendResult trend = await Task.Run(() =>
                    new SpaceTrendService().RecordAndGet(info.Disks, DateTime.UtcNow, primaryDrive, 30));
                PopulateTrend(trend);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                TrendCaption.Text = HealthLocalization.T("Health.Trend.Unavailable", ex.Message);
            }

            StatusText.Text = HealthLocalization.T("Health.Status.Caches");

            CacheSnapshot cache = CacheSnapshot.Empty;
            string cacheNote = "";
            try
            {
                (cache, cacheNote) = await MeasureCachesAsync();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                cacheNote = HealthLocalization.T("Health.Caches.NotMeasured", ex.Message);
            }

            HealthMetrics metrics = HealthCheckService.Collect(info, bin, startup, cache);
            ShowReport(HealthScoreCalculator.Evaluate(metrics), metrics, info);
            StatusText.Text = cacheNote.Length > 0
                ? HealthLocalization.T("Health.Status.Partial", cacheNote)
                : HealthLocalization.T("Health.Status.Done");
        }
        catch (Exception ex)
        {
            StatusText.Text = HealthLocalization.T("Health.Status.SystemError", ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task<(CacheSnapshot Cache, string Note)> MeasureCachesAsync()
    {
        var guardian = App.Services.GetRequiredService<IFileGuardian>();
        ICleanerPlugin[] plugins =
        [
            new SystemTempPlugin(guardian, GetLogger<SystemTempPlugin>()),
            new ThumbnailCachePlugin(guardian, GetLogger<ThumbnailCachePlugin>()),
            new ChromeCleanerPlugin(guardian, GetLogger<ChromeCleanerPlugin>())
        ];
        var service = new SmartCleanService(App.Services.GetRequiredService<IBackupManager>());
        SmartScanResult[] results = await service.QuickScanAsync(plugins);

        long temporary = 0, browser = 0;
        int temporaryItems = 0, browserItems = 0;
        foreach (var result in results)
        {
            if (result.Plugin.Id == "browser.chrome")
            {
                browser += result.TotalSize;
                browserItems += result.Items.Count;
            }
            else
            {
                temporary += result.TotalSize;
                temporaryItems += result.Items.Count;
            }
        }

        int failed = results.Count(result => result.Error != null);
        string note = failed > 0
            ? HealthLocalization.T("Health.CachePartial", failed)
            : "";
        return (new CacheSnapshot(temporary, temporaryItems, browser, browserItems), note);
    }

    private void ShowReport(HealthReport report, HealthMetrics metrics, SystemHealthInfo info)
    {
        _report = report;
        _metrics = metrics;
        _info = info;

        ScoreRing.Score = report.Score;
        ScoreRing.MaxScore = report.MaxScore;
        ScoreValue.Text = report.Score.ToString();
        GradeText.Text = HealthLocalization.Grade(report.Score);

        bool reclaim = report.ReclaimableBytes > 0;
        GradeBadge.Visibility = reclaim ? Visibility.Visible : Visibility.Collapsed;
        if (reclaim)
        {
            var warn = ((SolidColorBrush)FindResource("WarnColor")).Color;
            var tint = new SolidColorBrush(Color.FromArgb(40, warn.R, warn.G, warn.B));
            tint.Freeze();
            GradeBadge.Background = tint;
            GradeBadgeText.Foreground = (Brush)FindResource("WarnColor");
            GradeBadgeText.Text = HealthLocalization.T("Health.Reclaimable",
                SizeFormatter.Format(report.ReclaimableBytes));
        }

        int toFix = report.Findings.Count(finding => finding.Severity is HealthSeverity.Warning or HealthSeverity.Critical);
        ScoreCaption.Text = toFix == 0
            ? HealthLocalization.T("Health.Report.None")
            : HealthLocalization.T("Health.Report.Alerts", toFix);

        RenderSections(report);
        RenderFindings(report);

        PopulateDisks(info);
        PopulateMemory(info);
        UptimeText.Text = FormatUptime(info.Uptime);
        SystemText.Text = $"{info.OsName} {info.OsVersion} • " +
                          HealthLocalization.T("Health.System.Build", info.Build);
        HealthText.Text = HealthLocalization.T("Health.System.Cpu", info.CpuName,
            info.LogicalProcessors);

        EmptyRecycleBtn.IsEnabled = !_busy && _hasRecycleItems;
    }

    private void RenderSections(HealthReport report)
    {
        SectionsPanel.Children.Clear();
        foreach (var section in report.Sections)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(104) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });

            var name = new TextBlock
            {
                Text = HealthLocalization.SectionTitle(section),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };
            name.SetResourceReference(TextBlock.ForegroundProperty, "TextSecond");

            var bar = new ProgressBar
            {
                Minimum = 0,
                Maximum = section.MaxScore <= 0 ? 1 : section.MaxScore,
                Value = section.Score,
                Height = 6,
                Style = (Style)FindResource("ModernProgressBar"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            double ratio = section.MaxScore <= 0 ? 0 : (double)section.Score / section.MaxScore;
            bar.SetResourceReference(ProgressBar.ForegroundProperty,
                ratio >= 0.8 ? "Accent" : ratio >= 0.5 ? "WarnColor" : "Danger");

            var score = new TextBlock
            {
                Text = section.Measured ? $"{section.Score}/{section.MaxScore}" : "n/d",
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            score.SetResourceReference(TextBlock.ForegroundProperty,
                section.Measured ? "TextPrimary" : "TextSecond");

            Grid.SetColumn(name, 0);
            Grid.SetColumn(bar, 1);
            Grid.SetColumn(score, 2);
            row.Children.Add(name);
            row.Children.Add(bar);
            row.Children.Add(score);
            SectionsPanel.Children.Add(row);
        }
    }

    private void RenderFindings(HealthReport report)    {
        var rows = report.Findings
            .Where(finding => finding.Severity != HealthSeverity.Ok)
            .Select(finding => new HealthFindingRow(finding))
            .ToList();
        FindingsList.ItemsSource = rows;
        FindingsList.Visibility = rows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        FindingsEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        FindingsSummary.Text = rows.Count == 0
            ? HealthLocalization.T("Health.Summary.Ok")
            : report.ReclaimableBytes > 0
                ? HealthLocalization.T("Health.Summary.Reclaimable",
                    SizeFormatter.Format(report.ReclaimableBytes))
                : HealthLocalization.T("Health.Summary.Points", rows.Count);
    }

    internal void PopulateTrend(SpaceTrendResult trend)
    {
        _trendResult = trend;
        _trendPoints = trend.Points;
        TrendTitle.Text = HealthLocalization.T("Health.Trend.FreeTitle", trend.Drive.TrimEnd('\\'));
        TrendStart.Text = trend.Points.Count > 0
            ? trend.Points[0].Date.ToString("d MMM")
            : "";
        TrendEnd.Text = trend.Points.Count > 0
            ? trend.Points[^1].Date.ToString("d MMM")
            : "";
        TrendEmpty.Visibility = trend.HasEnoughData ? Visibility.Collapsed : Visibility.Visible;

        if (!trend.HasEnoughData)
        {
            TrendDelta.Text = trend.LatestFreeBytes is long latest
                ? HealthLocalization.T("Health.Trend.Free", SizeFormatter.Format(latest))
                : "—";
            TrendDelta.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
            TrendCaption.Text = trend.LatestFreeBytes == null
                ? HealthLocalization.T("Health.Trend.NoData")
                : HealthLocalization.T("Health.Trend.First");
            RenderTrendChart();
            return;
        }

        long delta = trend.DeltaBytes ?? 0;
        if (delta == 0)
        {
            TrendDelta.Text = HealthLocalization.T("Health.Trend.Stable");
            TrendDelta.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
        }
        else
        {
            TrendDelta.Text = (delta > 0 ? "+" : "−") + SizeFormatter.Format(Math.Abs(delta));
            TrendDelta.SetResourceReference(TextBlock.ForegroundProperty, delta > 0 ? "Accent" : "Danger");
        }
        TrendCaption.Text = HealthLocalization.T("Health.Trend.Today",
            SizeFormatter.Format(trend.LatestFreeBytes ?? 0), trend.Points.Count);
        RenderTrendChart();
    }

    private void RenderTrendChart()
    {
        TrendLine.Points = [];
        TrendArea.Points = [];
        if (_trendPoints.Count < 2) return;

        double width = TrendChartHost.ActualWidth;
        double height = TrendChartHost.ActualHeight;
        if (width < 20 || height < 20) return;

        long minimum = _trendPoints.Min(point => point.FreeBytes);
        long maximum = _trendPoints.Max(point => point.FreeBytes);
        if (maximum <= minimum) maximum = minimum + 1;
        double top = 5;
        double bottom = Math.Max(top + 1, height - 5);

        var line = new PointCollection();
        for (int index = 0; index < _trendPoints.Count; index++)
        {
            double x = width * index / (_trendPoints.Count - 1);
            double ratio = (double)(_trendPoints[index].FreeBytes - minimum) / (maximum - minimum);
            line.Add(new Point(x, bottom - ratio * (bottom - top)));
        }
        var area = new PointCollection(line)
        {
            new Point(width, bottom),
            new Point(0, bottom)
        };
        TrendLine.Points = line;
        TrendArea.Points = area;
    }

    private void TrendChart_SizeChanged(object sender, SizeChangedEventArgs e) => RenderTrendChart();

    private void PopulateHistory(CleanupHistorySummary history)
    {
        _history = history;
        if (!history.HasData)
        {
            HistoryCleanups.Text = HistoryFiles.Text = HistoryBytes.Text = "0";
            HistoryCaption.Text = HealthLocalization.T("Health.History.NoData");
            return;
        }

        HistoryCleanups.Text = history.Cleanups.ToString("N0");
        HistoryFiles.Text = history.Files.ToString("N0");
        HistoryBytes.Text = SizeFormatter.Format(history.Bytes);
        string suffix = HealthLocalization.T("Health.History.Volume");
        HistoryCaption.Text = history.LastCleanupUtc is DateTime last
            ? HealthLocalization.T("Health.History.Last", last.ToLocalTime().ToString("g"), suffix)
            : suffix;
    }

    internal void Populate(SystemHealthInfo info)
    {
        HealthMetrics metrics = HealthCheckService.Collect(info, null, null);
        ShowReport(HealthScoreCalculator.Evaluate(metrics), metrics, info);
    }

    internal HealthReport? CurrentReport => _report;
    internal CleanupHistorySummary? CurrentHistory => _history;

    /// <summary>Sérialise le diagnostic courant pour les contrôles de rendu (aucun fichier écrit).</summary>
    internal string? BuildExportJsonForPreview()
    {
        if (_report == null || _metrics == null) return null;
        return HealthExportService.ToJson(HealthExportService.Create(
            LocalizedReport(), _metrics, _history, _trendResult, DateTime.UtcNow, "OpenCleaner"));
    }

    /// <summary>Exécute la collecte réelle et le rendu (validation locale, hors CI).</summary>
    internal Task RunLiveCheckAsync() => LoadAsync();

    /// <summary>Alimente la fenêtre avec un diagnostic déjà calculé (aperçus, tests de rendu).</summary>
    internal void PopulatePreview(SystemHealthInfo info, HealthMetrics metrics)
    {
        ShowReport(HealthScoreCalculator.Evaluate(metrics), metrics, info);
        RecycleText.Text = metrics.RecycleBinBytes > 0
            ? HealthLocalization.T("Health.Recycle.Summary", metrics.RecycleBinItems.ToString("N0"),
                SizeFormatter.Format(metrics.RecycleBinBytes))
            : HealthLocalization.T("Health.Recycle.Empty");
        _hasRecycleItems = metrics.RecycleBinItems > 0;
        EmptyRecycleBtn.IsEnabled = _hasRecycleItems;
        PopulateHistory(new CleanupHistorySummary(8, 12_400, 34L * 1024 * 1024 * 1024,
            DateTime.UtcNow.AddDays(-1), []));
        PopulateTrend(PreviewTrend());
        StatusText.Text = HealthLocalization.T("Health.Preview");
    }

    private static SpaceTrendResult PreviewTrend()
    {
        const long Gb = 1024L * 1024 * 1024;
        var points = new List<SpaceTrendPoint>();
        DateTime start = DateTime.UtcNow.Date.AddDays(-28);
        for (int index = 0; index < 15; index++)
            points.Add(new SpaceTrendPoint(start.AddDays(index * 2), (long)(52.0 * Gb - index * 0.65 * Gb)));
        return new SpaceTrendResult("C:\\", points[^1].FreeBytes,
            points[^1].FreeBytes - points[0].FreeBytes, points);
    }

    private void PopulateDisks(SystemHealthInfo info)
    {
        DisksPanel.Children.Clear();
        int lowDisks = 0;
        int knownDisks = 0;
        foreach (var disk in info.Disks)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 11) };
            var title = new TextBlock { Text = $"{disk.Name}  {disk.FileSystem}", FontWeight = FontWeights.SemiBold };
            panel.Children.Add(title);
            if (disk.TotalBytes <= 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = HealthLocalization.T("Health.Disk.NoCapacity"),
                    Margin = new Thickness(0, 3, 0, 0)
                });
            }
            else
            {
                knownDisks++;
                long free = Math.Clamp(disk.FreeBytes, 0, disk.TotalBytes);
                double percentUsed = 100d * (disk.TotalBytes - free) / disk.TotalBytes;
                bool low = free / (double)disk.TotalBytes < 0.10;
                if (low) lowDisks++;
                var detail = new TextBlock
                {
                    Text = HealthLocalization.T("Health.Disk.Free", SizeFormatter.Format(free),
                        SizeFormatter.Format(disk.TotalBytes)) +
                           (low ? HealthLocalization.T("Health.Disk.Low") : ""),
                    Margin = new Thickness(0, 3, 0, 6),
                    TextWrapping = TextWrapping.Wrap
                };
                detail.SetResourceReference(TextBlock.ForegroundProperty, low ? "WarnColor" : "TextSecond");
                panel.Children.Add(detail);

                var bar = new ProgressBar
                {
                    Minimum = 0,
                    Maximum = 100,
                    Value = percentUsed,
                    Height = 6,
                    Style = (Style)FindResource("ModernProgressBar")
                };
                bar.SetResourceReference(ProgressBar.ForegroundProperty, low ? "WarnColor" : "Accent");
                panel.Children.Add(bar);
            }
            DisksPanel.Children.Add(panel);
        }

        if (DisksPanel.Children.Count == 0)
            DisksPanel.Children.Add(new TextBlock { Text = HealthLocalization.T("Health.Disk.None") });

        AdviceText.Text = knownDisks == 0
            ? HealthLocalization.T("Health.Disk.CapacityUnknown")
            : lowDisks > 0
                ? lowDisks == 1
                    ? HealthLocalization.T("Health.Disk.LowOne")
                    : HealthLocalization.T("Health.Disk.LowMany", lowDisks)
                : HealthLocalization.T("Health.Disk.Ok");
    }

    private void PopulateMemory(SystemHealthInfo info)
    {
        long total = (long)Math.Min(info.TotalMemoryBytes, long.MaxValue);
        long available = (long)Math.Min(info.AvailableMemoryBytes, (ulong)Math.Max(0, total));
        MemoryText.Text = total > 0
            ? HealthLocalization.T("Health.Memory.Used", SizeFormatter.Format(total - available))
            : HealthLocalization.T("Health.Memory.Unavailable");
        MemoryDetail.Text = total > 0
            ? HealthLocalization.T("Health.Memory.Detail", SizeFormatter.Format(available),
                SizeFormatter.Format(total))
            : HealthLocalization.T("Health.Memory.NotReported");
        MemoryBar.Value = total > 0 ? 100d * (total - available) / total : 0;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        RefreshBtn.IsEnabled = FlushDnsBtn.IsEnabled = ExportHealthBtn.IsEnabled = !busy;
        EmptyRecycleBtn.IsEnabled = !busy && _hasRecycleItems;
        Cursor = busy ? System.Windows.Input.Cursors.Wait : System.Windows.Input.Cursors.Arrow;
    }

    public void ApplyTheme(bool dark)
    {
        ScoreRing.RefreshTheme();
        if (_report == null || _metrics == null) return;
        RenderSections(_report);
        RenderFindings(_report);
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = LoadAsync();

    private void Health_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.F5 || _busy) return;
        Refresh_Click(sender, e);
        e.Handled = true;
    }

    private async void ExportHealth_Click(object sender, RoutedEventArgs e)
    {
        if (_report == null || _metrics == null)
        {
            StatusText.Text = HealthLocalization.T("Health.Diagnostic.Unavailable");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = HealthLocalization.T("Health.Export.Title"),
            Filter = HealthLocalization.T("Health.Export.Filter"),
            FileName = $"OpenCleaner-Diagnostic-{DateTime.Now:yyyyMMdd-HHmm}.json"
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            string version = typeof(HealthWindow).Assembly.GetName().Version?.ToString(3) ?? "";
            string json = HealthExportService.ToJson(HealthExportService.Create(
                LocalizedReport(), _metrics, _history, _trendResult, DateTime.UtcNow, "OpenCleaner " + version));
            await File.WriteAllTextAsync(dialog.FileName, json);
            StatusText.Text = HealthLocalization.T("Health.Export.Done", Path.GetFileName(dialog.FileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = HealthLocalization.T("Health.Export.Failed", ex.Message);
        }
    }

    private void FindingAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string action }) return;
        switch (action)
        {
            case "disk": Disk_Click(sender, e); break;
            case "startup": Startup_Click(sender, e); break;
            case "updates": Updates_Click(sender, e); break;
            case "cleanTemp": (Window.GetWindow(this) as MainWindow)?.ShowCleaner("system"); break;
            case "cleanBrowser": (Window.GetWindow(this) as MainWindow)?.ShowCleaner("browser"); break;
            case "recycle": EmptyRecycle_Click(sender, e); break;
        }
    }

    private void Clean_Click(object sender, RoutedEventArgs e) => (Window.GetWindow(this) as MainWindow)?.ShowCleaner("system");
    private void Disk_Click(object sender, RoutedEventArgs e) => (Window.GetWindow(this) as MainWindow)?.ShowHealthTool("disk");
    private void Startup_Click(object sender, RoutedEventArgs e) => (Window.GetWindow(this) as MainWindow)?.ShowHealthTool("startup");
    private void Updates_Click(object sender, RoutedEventArgs e) => (Window.GetWindow(this) as MainWindow)?.ShowHealthTool("updates");

    private async void EmptyRecycle_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (MessageBox.Show(Window.GetWindow(this),
                HealthLocalization.T("Health.Recycle.Confirm"),
                HealthLocalization.T("Health.Recycle.Title"), MessageBoxButton.YesNo, MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes) return;
        if (!UiOperation.TryBegin(Window.GetWindow(this), out var operation)) return;
        using (operation)
        {
            SetBusy(true);
            try
            {
                await Task.Run(RecycleBinService.Empty, operation.Token);
                _hasRecycleItems = false;
                EmptyRecycleBtn.IsEnabled = false;
                RecycleText.Text = HealthLocalization.T("Health.Recycle.Empty");
                StatusText.Text = HealthLocalization.T("Health.Recycle.Emptied");
            }
            catch (OperationCanceledException)
            {
                StatusText.Text = HealthLocalization.T("Health.Operation.Cancelled");
            }
            catch (Exception ex)
            {
                MessageBox.Show(Window.GetWindow(this), ex.Message,
                    HealthLocalization.T("Health.Recycle.Title"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetBusy(false);
            }
        }
    }

    private async void FlushDns_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        SetBusy(true);
        try
        {
            var start = new ProcessStartInfo("ipconfig", "/flushdns")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var process = Process.Start(start) ??
                throw new InvalidOperationException(HealthLocalization.T("Health.Dns.NotFound"));
            await process.WaitForExitAsync();
            StatusText.Text = process.ExitCode == 0
                ? HealthLocalization.T("Health.Dns.Flushed")
                : HealthLocalization.T("Health.Dns.ExitCode", process.ExitCode);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), ex.Message,
                HealthLocalization.T("Health.Dns.Title"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private static ILogger<T> GetLogger<T>() => App.Services.GetRequiredService<ILogger<T>>();

    private static string FormatUptime(TimeSpan uptime) =>
        uptime.TotalDays >= 2
            ? HealthLocalization.T("Health.Uptime.Days", (int)uptime.TotalDays, uptime.Hours)
            : uptime.TotalDays >= 1
                ? HealthLocalization.T("Health.Uptime.DaysOne", uptime.Hours)
                : HealthLocalization.T("Health.Uptime.Hours", uptime.Hours, uptime.Minutes);

    private HealthReport LocalizedReport()
    {
        HealthReport report = _report!;
        return report with
        {
            Grade = HealthLocalization.Grade(report.Score),
            Sections = report.Sections.Select(section => section with
            {
                Title = HealthLocalization.SectionTitle(section),
                Findings = section.Findings.Select(HealthLocalization.Localize).ToList()
            }).ToList(),
            Findings = report.Findings.Select(HealthLocalization.Localize).ToList()
        };
    }
}
