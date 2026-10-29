using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenCleaner.Contracts;
using OpenCleaner.Core;
using OpenCleaner.Core.Health;
using OpenCleaner.Core.Security;
using OpenCleaner.Core.Tools;
using OpenCleaner.Core.Transactions;
using OpenCleaner.Plugins.System.Plugins;

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
            "disk" => "Explorer",
            "startup" => "Gérer",
            "updates" => "Voir",
            "cleanTemp" => "Analyser",
            "cleanBrowser" => "Analyser",
            "recycle" => "Vider",
            _ => null
        };
        Action = finding.Action;
        ActionVisibility = ActionLabel == null ? Visibility.Collapsed : Visibility.Visible;
        ReclaimLabel = finding.ReclaimableBytes is long bytes && bytes > 0
            ? "• " + SizeFormatter.Format(bytes)
            : "";
    }

    public HealthFinding Finding { get; }
    public string Title => Finding.Title;
    public string Detail => Finding.Detail;
    public string Recommendation => Finding.Recommendation;
    public Brush SeverityBrush { get; }
    public string? Action { get; }
    public string? ActionLabel { get; }
    public Visibility ActionVisibility { get; }
    public string ReclaimLabel { get; }
}

public partial class HealthWindow : UserControl, IThemeAware
{
    private bool _busy;
    private bool _loaded;
    private bool _hasRecycleItems;
    private HealthReport? _report;
    private HealthMetrics? _metrics;
    private CleanupHistorySummary? _history;

    public HealthWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (_loaded) return;
            _loaded = true;
            await LoadAsync();
        };
    }

    private async Task LoadAsync()
    {
        if (_busy) return;
        SetBusy(true);
        StatusText.Text = "Lecture des informations système…";
        try
        {
            SystemHealthInfo info = await Task.Run(SystemInfoService.Get);
            RecycleBinInfo? bin = null;
            try { bin = await Task.Run(RecycleBinService.Query); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            IReadOnlyList<StartupItem> startup = await Task.Run(StartupItemService.GetItems);

            _hasRecycleItems = bin is { ItemCount: > 0 };
            RecycleText.Text = bin == null
                ? "Corbeille : information indisponible."
                : $"Corbeille : {bin.ItemCount:N0} élément(s) • {SizeFormatter.Format(bin.SizeBytes)}";

            HealthMetrics baseMetrics = HealthCheckService.Collect(info, bin, startup);
            ShowReport(HealthScoreCalculator.Evaluate(baseMetrics), baseMetrics, info);
            StatusText.Text = "Indicateurs de base actualisés. Lecture de l'historique…";

            try
            {
                CleanupHistorySummary history = await Task.Run(
                    () => new CleanupHistoryService().GetSummary(TimeSpan.FromDays(30)));
                PopulateHistory(history);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                HistoryCaption.Text = "Historique indisponible : " + ex.Message;
            }

            StatusText.Text = "Indicateurs de base actualisés. Mesure des caches à faible risque…";

            CacheSnapshot cache = CacheSnapshot.Empty;
            string cacheNote = "";
            try
            {
                (cache, cacheNote) = await MeasureCachesAsync();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                cacheNote = "Caches non mesurés : " + ex.Message;
            }

            HealthMetrics metrics = HealthCheckService.Collect(info, bin, startup, cache);
            ShowReport(HealthScoreCalculator.Evaluate(metrics), metrics, info);
            StatusText.Text = cacheNote.Length > 0
                ? "Diagnostic partiel. " + cacheNote
                : "Diagnostic terminé. Aucune donnée n'a quitté votre PC.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Impossible de lire les informations système : " + ex.Message;
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
            ? $"{failed} analyse(s) de cache incomplète(s) : les tailles affichées sont minimales."
            : "";
        return (new CacheSnapshot(temporary, temporaryItems, browser, browserItems), note);
    }

    private void ShowReport(HealthReport report, HealthMetrics metrics, SystemHealthInfo info)
    {
        _report = report;
        _metrics = metrics;

        ScoreRing.Score = report.Score;
        ScoreRing.MaxScore = report.MaxScore;
        ScoreValue.Text = report.Score.ToString();
        GradeText.Text = report.Grade;

        bool reclaim = report.ReclaimableBytes > 0;
        GradeBadge.Visibility = reclaim ? Visibility.Visible : Visibility.Collapsed;
        if (reclaim)
        {
            var warn = ((SolidColorBrush)FindResource("WarnColor")).Color;
            var tint = new SolidColorBrush(Color.FromArgb(40, warn.R, warn.G, warn.B));
            tint.Freeze();
            GradeBadge.Background = tint;
            GradeBadgeText.Foreground = (Brush)FindResource("WarnColor");
            GradeBadgeText.Text = $"récupérable : {SizeFormatter.Format(report.ReclaimableBytes)}";
        }

        int toFix = report.Findings.Count(finding => finding.Severity is HealthSeverity.Warning or HealthSeverity.Critical);
        ScoreCaption.Text = toFix == 0
            ? "Aucune alerte prioritaire. Les détails ci-dessous restent vérifiables."
            : $"{toFix} alerte(s) prioritaire(s) sur les indicateurs mesurés localement.";

        RenderSections(report);
        RenderFindings(report);

        PopulateDisks(info);
        PopulateMemory(info);
        UptimeText.Text = FormatUptime(info.Uptime);
        SystemText.Text = $"{info.OsName} {info.OsVersion} • build {info.Build}";
        HealthText.Text = $"{info.CpuName} • {info.LogicalProcessors} processeurs logiques";

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
                Text = section.Title,
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
            ? "Tout est en ordre"
            : report.ReclaimableBytes > 0
                ? $"{SizeFormatter.Format(report.ReclaimableBytes)} récupérables"
                : $"{rows.Count} point(s) à vérifier";
    }

    private void PopulateHistory(CleanupHistorySummary history)
    {
        _history = history;
        if (!history.HasData)
        {
            HistoryCleanups.Text = HistoryFiles.Text = HistoryBytes.Text = "0";
            HistoryCaption.Text = "Aucun nettoyage enregistré sur la période. " +
                                  "Cette carte se remplit après un nettoyage confirmé.";
            return;
        }

        HistoryCleanups.Text = history.Cleanups.ToString("N0");
        HistoryFiles.Text = history.Files.ToString("N0");
        HistoryBytes.Text = SizeFormatter.Format(history.Bytes);
        string suffix = "Volume des fichiers supprimés ; les sauvegardes conservées occupent encore de l'espace.";
        HistoryCaption.Text = history.LastCleanupUtc is DateTime last
            ? $"Dernier nettoyage : {last.ToLocalTime():g}. {suffix}"
            : suffix;
    }

    internal void Populate(SystemHealthInfo info)
    {
        HealthMetrics metrics = HealthCheckService.Collect(info, null, null);
        ShowReport(HealthScoreCalculator.Evaluate(metrics), metrics, info);
    }

    internal HealthReport? CurrentReport => _report;
    internal CleanupHistorySummary? CurrentHistory => _history;

    /// <summary>Exécute la collecte réelle et le rendu (validation locale, hors CI).</summary>
    internal Task RunLiveCheckAsync() => LoadAsync();

    /// <summary>Alimente la fenêtre avec un diagnostic déjà calculé (aperçus, tests de rendu).</summary>
    internal void PopulatePreview(SystemHealthInfo info, HealthMetrics metrics)
    {
        ShowReport(HealthScoreCalculator.Evaluate(metrics), metrics, info);
        RecycleText.Text = metrics.RecycleBinBytes > 0
            ? $"Corbeille : {metrics.RecycleBinItems:N0} élément(s) • {SizeFormatter.Format(metrics.RecycleBinBytes)}"
            : "Corbeille : vide.";
        _hasRecycleItems = metrics.RecycleBinItems > 0;
        EmptyRecycleBtn.IsEnabled = _hasRecycleItems;
        PopulateHistory(new CleanupHistorySummary(8, 12_400, 34L * 1024 * 1024 * 1024,
            DateTime.UtcNow.AddDays(-1), []));
        StatusText.Text = "Aperçu du diagnostic. Aucune analyse ni suppression exécutée.";
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
                    Text = "Espace disponible non communiqué",
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
                    Text = $"{SizeFormatter.Format(free)} libres sur {SizeFormatter.Format(disk.TotalBytes)}" +
                           (low ? "  •  Espace faible" : ""),
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
            DisksPanel.Children.Add(new TextBlock { Text = "Aucun disque accessible détecté." });

        AdviceText.Text = knownDisks == 0
            ? "Capacité des disques indisponible ; vérifiez l'espace dans Windows."
            : lowDisks > 0
                ? $"{(lowDisks == 1 ? "Un disque dispose" : $"{lowDisks} disques disposent")} de moins de 10 % " +
                  "d'espace libre. Explorez leur contenu avant de supprimer des fichiers."
                : "Aucun manque d'espace détecté sur les disques dont la capacité est connue.";
    }

    private void PopulateMemory(SystemHealthInfo info)
    {
        long total = (long)Math.Min(info.TotalMemoryBytes, long.MaxValue);
        long available = (long)Math.Min(info.AvailableMemoryBytes, (ulong)Math.Max(0, total));
        MemoryText.Text = total > 0 ? $"{SizeFormatter.Format(total - available)} utilisés" : "Indisponible";
        MemoryDetail.Text = total > 0
            ? $"{SizeFormatter.Format(available)} disponibles sur {SizeFormatter.Format(total)}"
            : "Windows n'a pas communiqué cette information.";
        MemoryBar.Value = total > 0 ? 100d * (total - available) / total : 0;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        RefreshBtn.IsEnabled = FlushDnsBtn.IsEnabled = !busy;
        EmptyRecycleBtn.IsEnabled = !busy && _hasRecycleItems;
        Cursor = busy ? System.Windows.Input.Cursors.Wait : null;
    }

    public void ApplyTheme(bool dark)
    {
        ScoreRing.RefreshTheme();
        if (_report == null || _metrics == null) return;
        RenderSections(_report);
        RenderFindings(_report);
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = LoadAsync();

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
                "Vider la corbeille supprime définitivement son contenu. OpenCleaner ne peut pas le restaurer.\n\nContinuer ?",
                "Vider la corbeille", MessageBoxButton.YesNo, MessageBoxImage.Warning,
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
                RecycleText.Text = "Corbeille : vide.";
                StatusText.Text = "Corbeille vidée.";
            }
            catch (OperationCanceledException)
            {
                StatusText.Text = "Opération annulée.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(Window.GetWindow(this), ex.Message, "Corbeille", MessageBoxButton.OK, MessageBoxImage.Error);
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
            using var process = Process.Start(start) ?? throw new InvalidOperationException("ipconfig est introuvable");
            await process.WaitForExitAsync();
            StatusText.Text = process.ExitCode == 0
                ? "Cache DNS vidé."
                : "La commande a retourné le code " + process.ExitCode + ".";
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), ex.Message, "Cache DNS", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private static ILogger<T> GetLogger<T>() => App.Services.GetRequiredService<ILogger<T>>();

    private static string FormatUptime(TimeSpan uptime) =>
        uptime.TotalDays >= 1
            ? $"{(int)uptime.TotalDays} {(uptime.TotalDays < 2 ? "jour" : "jours")} et {uptime.Hours} h"
            : $"{uptime.Hours} h {uptime.Minutes} min";
}
