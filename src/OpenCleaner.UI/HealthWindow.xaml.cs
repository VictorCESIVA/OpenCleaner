using System.Diagnostics;
using System.Windows;
using OpenCleaner.Contracts;
using OpenCleaner.Core.Tools;

namespace OpenCleaner.UI;

public partial class HealthWindow : Window
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
        HealthPlaceholder.Visibility = Visibility.Visible;
        HealthText.Visibility = Visibility.Collapsed;
        try
        {
            SystemHealthInfo info = await Task.Run(SystemInfoService.Get);
            RecycleBinInfo bin = await Task.Run(RecycleBinService.Query);
            _hasRecycleItems = bin.ItemCount > 0;
            long totalMemory = (long)Math.Min(info.TotalMemoryBytes, long.MaxValue);
            long availableMemory = (long)Math.Min(info.AvailableMemoryBytes, long.MaxValue);
            var disks = string.Join(Environment.NewLine, info.Disks.Select(d =>
                $"  • {d.Name} {d.FileSystem} — {SizeFormatter.Format(d.TotalBytes)} (libre : {SizeFormatter.Format(d.FreeBytes)})"));
            HealthText.Text =
                $"{info.OsName} {info.OsVersion} (build {info.Build})" + Environment.NewLine +
                $"Processeur : {info.CpuName} — {info.LogicalProcessors} threads" + Environment.NewLine +
                $"Mémoire : {SizeFormatter.Format(totalMemory)} (libre : {SizeFormatter.Format(availableMemory)})" + Environment.NewLine +
                $"Démarrage : il y a {FormatUptime(info.Uptime)}" + Environment.NewLine +
                $"Runtime : {info.RuntimeVersion}" + Environment.NewLine + Environment.NewLine +
                "Disques :" + Environment.NewLine + disks;
            RecycleText.Text = $"{bin.ItemCount:N0} élément(s) dans la corbeille • {SizeFormatter.Format(bin.SizeBytes)}";
            HealthPlaceholder.Visibility = Visibility.Collapsed;
            HealthText.Visibility = Visibility.Visible;
            StatusText.Text = "Informations actualisées.";
        }
        catch (Exception ex)
        {
            HealthPlaceholder.Visibility = Visibility.Collapsed;
            HealthText.Visibility = Visibility.Visible;
            HealthText.Text = "Impossible de lire les informations système : " + ex.Message;
        }
        finally { SetBusy(false); }
    }

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
        if (MessageBox.Show(this,
                "Vider la corbeille supprime définitivement son contenu. OpenCleaner ne peut pas le restaurer.\n\nContinuer ?",
                "Vider la corbeille", MessageBoxButton.YesNo, MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes) return;
        if (!UiOperation.TryBegin(this, out var operation)) return;
        using (operation)
        {
            SetBusy(true);
            try
            {
                await Task.Run(RecycleBinService.Empty, operation.Token);
                await LoadAsync();
                StatusText.Text = "Corbeille vidée.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Corbeille", MessageBoxButton.OK, MessageBoxImage.Error);
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
            MessageBox.Show(this, ex.Message, "Cache DNS", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { SetBusy(false); }
    }

    private static string FormatUptime(TimeSpan uptime) =>
        uptime.TotalDays >= 1
            ? $"{(int)uptime.TotalDays} jour(s) et {uptime.Hours} h"
            : $"{uptime.Hours} h {uptime.Minutes} min";
}
