using Microsoft.Extensions.DependencyInjection;
using OpenCleaner.Contracts;
using OpenCleaner.Core.Security;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;

namespace OpenCleaner.UI;

public partial class RestoreWindow : UserControl
{
    private bool _isBusy;
    private IReadOnlyList<BackupInfo> _allBackups = [];

    public RestoreWindow()
    {
        InitializeComponent();
        _ = LoadBackupsAsync();
    }

    private async Task LoadBackupsAsync()
    {
        try
        {
            IBackupManager manager = App.Services.GetRequiredService<IBackupManager>();
            _allBackups = await manager.GetAllBackupsAsync();
            var totalBytes = _allBackups.Sum(b => b.Size);
            BackupSummaryText.Text = _allBackups.Count == 0
                ? "0 sauvegarde (0 o)"
                : $"{_allBackups.Count:N0} sauvegarde(s) • {SizeFormatter.Format(totalBytes)}";
            int quarantined = 0;
            try
            {
                if (Directory.Exists(manager.QuarantineRoot))
                    quarantined = Directory.EnumerateDirectories(manager.QuarantineRoot).Count();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { System.Diagnostics.Debug.WriteLine($"Quarantine listing failed: {ex}"); }
            if (quarantined > 0)
                BackupSummaryText.Text += $" • {quarantined:N0} corrompue(s) isolée(s)";
            ApplyFilter();
        }
        catch (ObjectDisposedException)
        {
            // Ignore when application services are disposed during shutdown
        }
        catch (Exception ex)
        {
            if (IsLoaded && IsVisible)
            {
                MessageBox.Show(Window.GetWindow(this), $"Impossible de lire les sauvegardes :\n{ex.Message}", "Restauration",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        if (BackupsListView == null) return;
        var query = SearchBox?.Text?.Trim() ?? "";
        BackupsListView.ItemsSource = string.IsNullOrEmpty(query)
            ? _allBackups
            : _allBackups.Where(b => b.OriginalPath.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        SetBusy(_isBusy);
    }

    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        BackupsListView.IsEnabled = SelectAllBtn.IsEnabled = RefreshBtn.IsEnabled = DeleteOldBtn.IsEnabled =
            SearchBox.IsEnabled = UnlockBtn.IsEnabled = !busy;
        RestoreBtn.IsEnabled = !busy && BackupsListView.SelectedItems.Count > 0;
        DeleteSelectedBtn.IsEnabled = !busy && BackupsListView.SelectedItems.Count > 0;
        OpenFolderBtn.IsEnabled = !busy && BackupsListView.SelectedItem is BackupInfo;
    }

    private void BackupsListView_SelectionChanged(object sender, SelectionChangedEventArgs e) => SetBusy(_isBusy);

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (BackupsListView.SelectedItem is BackupInfo backup)
            ExplorerHelper.RevealInExplorer(backup.OriginalPath);
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        var selected = BackupsListView.SelectedItems.Cast<BackupInfo>().ToList();
        if (selected.Count == 0 || UiOperation.IsBusy) return;
        var prompt = selected.Count == 1
            ? $"Restaurer ce fichier ?\n\n{selected[0].OriginalPath}\n\nLe fichier existant sera conservé puis sauvegardé."
            : $"Restaurer {selected.Count} fichiers sélectionnés ?\n\nTout fichier existant sera conservé puis sauvegardé.";
        if (MessageBox.Show(Window.GetWindow(this), prompt,
            "Confirmer la restauration", MessageBoxButton.YesNo, MessageBoxImage.Question,
            MessageBoxResult.No) != MessageBoxResult.Yes) return;
        if (!UiOperation.TryBegin(Window.GetWindow(this), out var operation)) return;
        using (operation)
        {
            SetBusy(true);
            try
            {
                var manager = App.Services.GetRequiredService<IBackupManager>();
                int restoredCount = 0;
                foreach (var backup in selected)
                {
                    operation.Token.ThrowIfCancellationRequested();
                    if (await manager.RestoreAsync(backup.BackupId, operation.Token))
                        restoredCount++;
                }
                var allOk = restoredCount == selected.Count;
                var msg = selected.Count == 1
                    ? (allOk ? "Fichier restauré." : "La restauration a échoué. La sauvegarde est conservée.")
                    : $"{restoredCount}/{selected.Count} fichier(s) restauré(s).";
                MessageBox.Show(Window.GetWindow(this), msg,
                    "Restauration", MessageBoxButton.OK, allOk ? MessageBoxImage.Information : MessageBoxImage.Warning);
                await LoadBackupsAsync();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { ShowError(ex); }
            finally { SetBusy(false); }
        }
    }

    private async void Unlock_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy) return;
        var dialog = new RecoveryKeyWindow(null, enterMode: true) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true) return;
        SetBusy(true);
        try
        {
            IBackupManager manager = App.Services.GetRequiredService<IBackupManager>();
            if (!await manager.RecoverKeyAsync(dialog.Code))
            {
                MessageBox.Show(Window.GetWindow(this), "Code de récupération invalide.", "Sauvegardes",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            await LoadBackupsAsync();
            MessageBox.Show(Window.GetWindow(this), "Sauvegardes déverrouillées.", "Sauvegardes",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (BackupKeyUnavailableException)
        {
            MessageBox.Show(Window.GetWindow(this), "Aucun code de récupération n'a encore été configuré.",
                "Sauvegardes", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            MessageBox.Show(Window.GetWindow(this), "Code de récupération invalide.", "Sauvegardes",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex) { ShowError(ex); }
        finally { SetBusy(false); }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy) return;
        await LoadBackupsAsync();
    }

    private async void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        var backups = BackupsListView.SelectedItems.Cast<BackupInfo>().ToList();
        if (backups.Count == 0 || UiOperation.IsBusy) return;
        if (MessageBox.Show(Window.GetWindow(this), $"Supprimer définitivement {backups.Count} sauvegarde(s) ?\n\n" +
            "Les fichiers correspondants ne pourront plus être restaurés avec ces sauvegardes.",
            "Supprimer les sauvegardes", MessageBoxButton.YesNo, MessageBoxImage.Warning,
            MessageBoxResult.No) != MessageBoxResult.Yes) return;
        if (!UiOperation.TryBegin(Window.GetWindow(this), out var operation)) return;
        using (operation)
        {
            SetBusy(true);
            try
            {
                var manager = App.Services.GetRequiredService<IBackupManager>();
                var deleted = 0;
                foreach (var backup in backups)
                {
                    operation.Token.ThrowIfCancellationRequested();
                    if (await manager.DeleteBackupAsync(backup.BackupId, operation.Token)) deleted++;
                }
                MessageBox.Show(Window.GetWindow(this), $"{deleted}/{backups.Count} sauvegarde(s) supprimée(s).", "Sauvegardes",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { ShowError(ex); }
            finally { await LoadBackupsAsync(); SetBusy(false); }
        }
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        if (!_isBusy) BackupsListView.SelectAll();
    }

    private async void DeleteOld_Click(object sender, RoutedEventArgs e)
    {
        if (UiOperation.IsBusy) return;
        if (MessageBox.Show(Window.GetWindow(this), "Supprimer définitivement les sauvegardes de plus de 30 jours ?",
            "Supprimer les anciennes sauvegardes", MessageBoxButton.YesNo, MessageBoxImage.Warning,
            MessageBoxResult.No) != MessageBoxResult.Yes) return;
        if (!UiOperation.TryBegin(Window.GetWindow(this), out var operation)) return;
        using (operation)
        {
            SetBusy(true);
            try { await App.Services.GetRequiredService<IBackupManager>().CleanupOldBackupsAsync(TimeSpan.FromDays(30), operation.Token); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { ShowError(ex); }
            finally { await LoadBackupsAsync(); SetBusy(false); }
        }
    }

    private void ShowError(Exception ex) => MessageBox.Show(Window.GetWindow(this), ex.Message, "Erreur",
        MessageBoxButton.OK, MessageBoxImage.Error);
}
