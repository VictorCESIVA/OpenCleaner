using Microsoft.Extensions.DependencyInjection;
using OpenCleaner.Contracts;
using System.Windows;

namespace OpenCleaner.UI;

public partial class RestoreWindow : Window
{
    public RestoreWindow()
    {
        InitializeComponent();
        LoadBackups();
    }

    private async void LoadBackups()
    {
        var backupManager = App.Services.GetRequiredService<IBackupManager>();
        var backups = await backupManager.GetAllBackupsAsync();
        BackupsListView.ItemsSource = backups.OrderByDescending(b => b.CreatedAt);
    }

    private void BackupsListView_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        RestoreBtn.IsEnabled = BackupsListView.SelectedItem != null;
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (BackupsListView.SelectedItem is not BackupInfo backup) return;

        var result = MessageBox.Show(
            $"Restaurer :\n{backup.OriginalPath}\n\nVers sa localisation d'origine ?",
            "Confirmation",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            var backupManager = App.Services.GetRequiredService<IBackupManager>();
            var success = await backupManager.RestoreAsync(backup.BackupId);

            if (success)
            {
                MessageBox.Show("Fichier restauré avec succès !", "Succès", MessageBoxButton.OK, MessageBoxImage.Information);
                LoadBackups();
            }
            else
            {
                MessageBox.Show("Échec de la restauration.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erreur : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => LoadBackups();

    private async void DeleteOld_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Supprimer tous les backups de plus de 30 jours ?",
            "Nettoyage",
            MessageBoxButton.YesNo);

        if (result == MessageBoxResult.Yes)
        {
            var backupManager = App.Services.GetRequiredService<IBackupManager>();
            await backupManager.CleanupOldBackupsAsync(TimeSpan.FromDays(30));
            LoadBackups();
        }
    }
}
