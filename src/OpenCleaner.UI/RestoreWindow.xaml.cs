using Microsoft.Extensions.DependencyInjection;
using OpenCleaner.Contracts;
using OpenCleaner.Core.Security;
using OpenCleaner.UI.Localization;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;

namespace OpenCleaner.UI;

public partial class RestoreWindow : UserControl
{
    private static string T(string key, params object?[] args) => LocalizationService.Instance.Get(key, args);

    private bool _isBusy;
    private IReadOnlyList<BackupInfo> _allBackups = [];

    public RestoreWindow()
    {
        InitializeComponent();
        LocalizationService.Instance.LanguageChanged += (_, _) => _ = LoadBackupsAsync();
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
                ? T("Restore.SummaryNone")
                : T("Restore.Summary", _allBackups.Count.ToString("N0"), SizeFormatter.Format(totalBytes));
            int quarantined = 0;
            try
            {
                if (Directory.Exists(manager.QuarantineRoot))
                    quarantined = Directory.EnumerateDirectories(manager.QuarantineRoot).Count();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { System.Diagnostics.Debug.WriteLine($"Quarantine listing failed: {ex}"); }
            if (quarantined > 0)
                BackupSummaryText.Text += T("Restore.QuarantinedSuffix", quarantined.ToString("N0"));
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
                MessageBox.Show(Window.GetWindow(this), T("Restore.ReadFailed", ex.Message),
                    T("Restore.Title"), MessageBoxButton.OK, MessageBoxImage.Error);
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
            ? T("Restore.ConfirmOne", selected[0].OriginalPath)
            : T("Restore.ConfirmMany", selected.Count);
        if (MessageBox.Show(Window.GetWindow(this), prompt,
            T("Restore.ConfirmTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question,
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
                    ? (allOk ? T("Restore.RestoredOne") : T("Restore.RestoreFailedOne"))
                    : T("Restore.RestoredMany", restoredCount, selected.Count);
                MessageBox.Show(Window.GetWindow(this), msg,
                    T("Restore.Title"), MessageBoxButton.OK, allOk ? MessageBoxImage.Information : MessageBoxImage.Warning);
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
                MessageBox.Show(Window.GetWindow(this), T("Restore.InvalidCode"),
                    T("Restore.BackupsTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            await LoadBackupsAsync();
            MessageBox.Show(Window.GetWindow(this), T("Restore.Unlocked"), T("Restore.BackupsTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (BackupKeyUnavailableException)
        {
            MessageBox.Show(Window.GetWindow(this), T("Restore.NoCode"),
                T("Restore.BackupsTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            MessageBox.Show(Window.GetWindow(this), T("Restore.InvalidCode"), T("Restore.BackupsTitle"),
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
        if (MessageBox.Show(Window.GetWindow(this), T("Restore.ConfirmDelete", backups.Count),
            T("Restore.DeleteTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning,
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
                MessageBox.Show(Window.GetWindow(this), T("Restore.Deleted", deleted, backups.Count),
                    T("Restore.BackupsTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
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
        if (MessageBox.Show(Window.GetWindow(this), T("Restore.DeleteOldConfirm"),
            T("Restore.DeleteOldTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning,
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

    private void ShowError(Exception ex) => MessageBox.Show(Window.GetWindow(this), ex.Message, T("Restore.ErrorTitle"),
        MessageBoxButton.OK, MessageBoxImage.Error);
}
