using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using OpenCleaner.Contracts;
using OpenCleaner.Core.Tools;

namespace OpenCleaner.UI;

public sealed class ShredFile
{
    public required string Path { get; init; }
    public long Size { get; init; }
    public string SizeLabel => SizeFormatter.Format(Size);
}

public partial class ShredderWindow : Window
{
    private readonly ObservableCollection<ShredFile> _files = [];
    private bool _busy;

    public ShredderWindow()
    {
        InitializeComponent();
        FileList.ItemsSource = _files;
        FileList.SelectionChanged += (_, _) => UpdateSelection();
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        int selected = FileList.SelectedItems.Count;
        RemoveBtn.IsEnabled = !_busy && selected > 0;
        ShredBtn.IsEnabled = !_busy && _files.Count > 0;
        CountText.Text = $"{_files.Count:N0} fichier(s) • {SizeFormatter.Format(_files.Sum(f => f.Size))}";
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        AddBtn.IsEnabled = !busy;
        FileList.IsEnabled = !busy;
        UpdateSelection();
    }

    private void PruneMissingFiles()
    {
        foreach (ShredFile file in _files.Where(f => !File.Exists(f.Path)).ToList())
            _files.Remove(file);
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Multiselect = true, Title = "Choisir les fichiers à effacer" };
        if (dialog.ShowDialog(this) != true) return;
        foreach (string path in dialog.FileNames)
        {
            if (_files.Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase))) continue;
            try
            {
                var info = new FileInfo(path);
                _files.Add(new ShredFile { Path = path, Size = info.Length });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { StatusText.Text = $"Fichier ignoré : {path}"; }
        }
        UpdateSelection();
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        foreach (ShredFile file in FileList.SelectedItems.Cast<ShredFile>().ToList())
            _files.Remove(file);
        UpdateSelection();
    }

    private async void Shred_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _files.Count == 0) return;
        if (MessageBox.Show(this,
                $"Effacer définitivement {_files.Count:N0} fichier(s) ?\n\nAucune sauvegarde ne sera créée et l'opération est irréversible.",
                "Effacement sécurisé", MessageBoxButton.YesNo, MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes) return;
        if (!UiOperation.TryBegin(this, out var operation)) return;
        using (operation)
        {
            SetBusy(true);
            StatusText.Text = "Écriture des passes aléatoires…";
            try
            {
                var paths = _files.Select(f => f.Path).ToList();
                int deleted = await Task.Run(
                    () => SecureDeleteService.ShredAsync(paths, 3, ct: operation.Token), operation.Token);
                PruneMissingFiles();
                StatusText.Text = $"{deleted:N0} fichier(s) effacé(s) définitivement.";
            }
            catch (OperationCanceledException)
            {
                PruneMissingFiles();
                StatusText.Text = "Effacement annulé ; certains fichiers peuvent être partiellement écrasés.";
            }
            catch (Exception ex)
            {
                PruneMissingFiles();
                StatusText.Text = "Échec : " + ex.Message;
            }
            finally { SetBusy(false); }
        }
    }
}
