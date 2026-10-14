using Microsoft.Extensions.DependencyInjection;
using OpenCleaner.Contracts;
using System.Windows;
using System.Windows.Controls;

namespace OpenCleaner.UI;

public partial class RestoreWindow : Window
{
    private bool _isBusy;
    public RestoreWindow()
    {
        InitializeComponent();
        _ = LoadBackupsAsync();
    }

    private async Task LoadBackupsAsync()
    {
        try
        {
            var backups = await App.Services.GetRequiredService<IBackupManager>().GetAllBackupsAsync();
            BackupsListView.ItemsSource = backups;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Impossible de lire les sauvegardes :\n{ex.Message}", "Restauration",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        BackupsListView.IsEnabled = SelectAllBtn.IsEnabled = RefreshBtn.IsEnabled = DeleteOldBtn.IsEnabled = !busy;
        RestoreBtn.IsEnabled = !busy && BackupsListView.SelectedItems.Count == 1;
        DeleteSelectedBtn.IsEnabled = !busy && BackupsListView.SelectedItems.Count > 0;
    }

    private void BackupsListView_SelectionChanged(object sender, SelectionChangedEventArgs e) => SetBusy(_isBusy);

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (BackupsListView.SelectedItem is not BackupInfo backup || UiOperation.IsBusy) return;
        if (MessageBox.Show(this, $"Restaurer ce fichier ?\n\n{backup.OriginalPath}\n\n" +
            "Le fichier existant sera sauvegardé avant remplacement.",
            "Confirmer la restauration", MessageBoxButton.YesNo, MessageBoxImage.Question,
            MessageBoxResult.No) != MessageBoxResult.Yes) return;
        if (!UiOperation.TryBegin(this, out var operation)) return;
        using (operation)
        {
            SetBusy(true);
            try
            {
                var success = await App.Services.GetRequiredService<IBackupManager>().RestoreAsync(backup.BackupId, operation.Token);
                MessageBox.Show(this, success ? "Fichier restauré." : "La restauration a échoué. La sauvegarde est conservée.",
                    "Restauration", MessageBoxButton.OK, success ? MessageBoxImage.Information : MessageBoxImage.Error);
                await LoadBackupsAsync();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { ShowError(ex); }
            finally { SetBusy(false); }
        }
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
        if (MessageBox.Show(this, $"Supprimer définitivement {backups.Count} sauvegarde(s) ?\n\n" +
            "Les fichiers correspondants ne pourront plus être restaurés avec ces sauvegardes.",
            "Supprimer les sauvegardes", MessageBoxButton.YesNo, MessageBoxImage.Warning,
            MessageBoxResult.No) != MessageBoxResult.Yes) return;
        if (!UiOperation.TryBegin(this, out var operation)) return;
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
                MessageBox.Show(this, $"{deleted}/{backups.Count} sauvegarde(s) supprimée(s).", "Sauvegardes",
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
        if (MessageBox.Show(this, "Supprimer définitivement les sauvegardes de plus de 30 jours ?",
            "Supprimer les anciennes sauvegardes", MessageBoxButton.YesNo, MessageBoxImage.Warning,
            MessageBoxResult.No) != MessageBoxResult.Yes) return;
        if (!UiOperation.TryBegin(this, out var operation)) return;
        using (operation)
        {
            SetBusy(true);
            try { await App.Services.GetRequiredService<IBackupManager>().CleanupOldBackupsAsync(TimeSpan.FromDays(30), operation.Token); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { ShowError(ex); }
            finally { await LoadBackupsAsync(); SetBusy(false); }
        }
    }

    private void ShowError(Exception ex) => MessageBox.Show(this, ex.Message, "Erreur",
        MessageBoxButton.OK, MessageBoxImage.Error);
}
