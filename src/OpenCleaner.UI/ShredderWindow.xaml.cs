using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using OpenCleaner.Contracts;
using OpenCleaner.Core.Tools;
using OpenCleaner.UI.Localization;

namespace OpenCleaner.UI;

public sealed class ShredFile
{
    public required string Path { get; init; }
    public long Size { get; init; }
    public string SizeLabel => SizeFormatter.Format(Size);
}

public partial class ShredderWindow : UserControl
{
    private static string T(string key, params object?[] args) => LocalizationService.Instance.Get(key, args);

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
        CountText.Text = T("Shred.Count", _files.Count.ToString("N0"),
            SizeFormatter.Format(_files.Sum(f => f.Size)));
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
        var dialog = new OpenFileDialog { Multiselect = true, Title = T("Shred.DialogTitle") };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        foreach (string path in dialog.FileNames)
        {
            if (_files.Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase))) continue;
            try
            {
                var info = new FileInfo(path);
                _files.Add(new ShredFile { Path = path, Size = info.Length });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { StatusText.Text = T("Shred.Ignored", path); }
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
        if (MessageBox.Show(Window.GetWindow(this),
                T("Shred.Confirm", _files.Count),
                T("Shred.ConfirmTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes) return;
        if (!UiOperation.TryBegin(Window.GetWindow(this), out var operation)) return;
        using (operation)
        {
            SetBusy(true);
            StatusText.Text = T("Shred.Writing");
            try
            {
                var paths = _files.Select(f => f.Path).ToList();
                int deleted = await Task.Run(
                    () => SecureDeleteService.ShredAsync(paths, 3, ct: operation.Token), operation.Token);
                PruneMissingFiles();
                StatusText.Text = T("Shred.Done", deleted.ToString("N0"));
            }
            catch (OperationCanceledException)
            {
                PruneMissingFiles();
                StatusText.Text = T("Shred.Cancelled");
            }
            catch (Exception ex)
            {
                PruneMissingFiles();
                StatusText.Text = T("Shred.Failed", ex.Message);
            }
            finally { SetBusy(false); }
        }
    }
}
