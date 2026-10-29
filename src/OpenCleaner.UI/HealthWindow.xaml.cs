using System.Windows.Controls;
using System.Diagnostics;
using System.Windows;
using OpenCleaner.Contracts;
using OpenCleaner.Core.Tools;

namespace OpenCleaner.UI;

public partial class HealthWindow : UserControl
{
    private bool _busy;
    private bool _loaded;
    private bool _hasRecycleItems;

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
        SetBusy(true);
        StatusText.Text = "Lecture des informations système…";
        try
        {
            SystemHealthInfo info = await Task.Run(SystemInfoService.Get);
            Populate(info);
            try
            {
                RecycleBinInfo bin = await Task.Run(RecycleBinService.Query);
                _hasRecycleItems = bin.ItemCount > 0;
                RecycleText.Text = $"Corbeille : {bin.ItemCount:N0} élément(s) • {SizeFormatter.Format(bin.SizeBytes)}";
            }
            catch (Exception ex)
            {
                _hasRecycleItems = false;
                RecycleText.Text = "Corbeille indisponible : " + ex.Message;
            }
            StatusText.Text = "Informations actualisées.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Impossible de lire les informations système : " + ex.Message;
        }
        finally { SetBusy(false); }
    }

    internal void Populate(SystemHealthInfo info)
    {
        DisksPanel.Children.Clear();
        int lowDisks = 0;
        int knownDisks = 0;
        foreach (var disk in info.Disks)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            panel.Children.Add(new TextBlock { Text = $"{disk.Name}  {disk.FileSystem}", FontWeight = FontWeights.SemiBold });
            if (disk.TotalBytes <= 0)
                panel.Children.Add(new TextBlock { Text = "Espace disponible non communiqué", Margin = new Thickness(0, 4, 0, 0) });
            else
            {
                knownDisks++;
                long free = Math.Clamp(disk.FreeBytes, 0, disk.TotalBytes);
                double percentUsed = 100d * (disk.TotalBytes - free) / disk.TotalBytes;
                bool low = free / (double)disk.TotalBytes < 0.10;
                if (low) lowDisks++;
                var detail = new TextBlock { Text = $"{SizeFormatter.Format(free)} libres sur {SizeFormatter.Format(disk.TotalBytes)}" + (low ? "  •  Espace faible" : ""), Margin = new Thickness(0, 4, 0, 6), TextWrapping = TextWrapping.Wrap };
                detail.SetResourceReference(TextBlock.ForegroundProperty, low ? "WarnColor" : "TextSecond");
                panel.Children.Add(detail);
                var bar = new ProgressBar { Minimum = 0, Maximum = 100, Value = percentUsed, Height = 6, Style = (Style)FindResource("ModernProgressBar") };
                bar.SetResourceReference(ProgressBar.ForegroundProperty, low ? "WarnColor" : "Accent");
                panel.Children.Add(bar);
            }
            DisksPanel.Children.Add(panel);
        }
        if (DisksPanel.Children.Count == 0) DisksPanel.Children.Add(new TextBlock { Text = "Aucun disque accessible détecté." });
        long total = (long)Math.Min(info.TotalMemoryBytes, long.MaxValue);
        long available = (long)Math.Min(info.AvailableMemoryBytes, (ulong)total);
        MemoryText.Text = total > 0 ? $"{SizeFormatter.Format(total - available)} utilisés" : "Indisponible";
        MemoryDetail.Text = total > 0 ? $"{SizeFormatter.Format(available)} disponibles sur {SizeFormatter.Format(total)}" : "Windows n'a pas communiqué cette information.";
        MemoryBar.Value = total > 0 ? 100d * (total - available) / total : 0;
        AdviceText.Text = knownDisks == 0 ? "Capacité des disques indisponible ; vérifiez l'espace dans Windows."
            : lowDisks > 0
            ? $"{(lowDisks == 1 ? "Un disque dispose" : $"{lowDisks} disques disposent")} de moins de 10 % d'espace libre. Explorez leur contenu avant de supprimer des fichiers."
            : "Aucun manque d'espace détecté sur les disques dont la capacité est connue.";
        UptimeText.Text = FormatUptime(info.Uptime);
        SystemText.Text = $"{info.OsName} {info.OsVersion} • build {info.Build}";
        HealthText.Text = $"{info.CpuName} • {info.LogicalProcessors} processeurs logiques";
    }

    private void Clean_Click(object sender, RoutedEventArgs e) => (Window.GetWindow(this) as MainWindow)?.ShowCleaner("system");
    private void Disk_Click(object sender, RoutedEventArgs e) => (Window.GetWindow(this) as MainWindow)?.ShowHealthTool("disk");
    private void Startup_Click(object sender, RoutedEventArgs e) => (Window.GetWindow(this) as MainWindow)?.ShowHealthTool("startup");
    private void Updates_Click(object sender, RoutedEventArgs e) => (Window.GetWindow(this) as MainWindow)?.ShowHealthTool("updates");

    private void SetBusy(bool busy)
    {
        _busy = busy;
        RefreshBtn.IsEnabled = FlushDnsBtn.IsEnabled = !busy;
        EmptyRecycleBtn.IsEnabled = !busy && _hasRecycleItems;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

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
                await LoadAsync();
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
            finally { SetBusy(false); }
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
        finally { SetBusy(false); }
    }

    private static string FormatUptime(TimeSpan uptime) =>
        uptime.TotalDays >= 1
            ? $"{(int)uptime.TotalDays} {(uptime.TotalDays < 2 ? "jour" : "jours")} et {uptime.Hours} h"
            : $"{uptime.Hours} h {uptime.Minutes} min";
}
