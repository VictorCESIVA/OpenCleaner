using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using OpenCleaner.Core.Tools;

namespace OpenCleaner.UI;

public partial class StartupWindow : Window
{
    private readonly ObservableCollection<StartupItem> _items = [];
    private IReadOnlyList<StartupItem> _all = [];
    private bool _busy;
    private bool _loaded;

    public StartupWindow()
    {
        InitializeComponent();
        StartupList.ItemsSource = _items;
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
        StatusText.Text = "Lecture des éléments de démarrage…";
        try
        {
            _all = await Task.Run(StartupItemService.GetItems);
            ApplyFilter();
            StatusText.Text = _all.Count == 0
                ? "Aucun élément de démarrage détecté."
                : "Sélectionnez un élément pour l'activer ou le désactiver.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Lecture impossible : " + ex.Message;
        }
        finally { SetBusy(false); }
    }

    private void ApplyFilter()
    {
        string query = FilterBox.Text.Trim();
        IEnumerable<StartupItem> filtered = _all;
        if (query.Length > 0)
            filtered = filtered.Where(i =>
                i.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                i.Command.Contains(query, StringComparison.CurrentCultureIgnoreCase));
        _items.Clear();
        foreach (StartupItem item in filtered) _items.Add(item);
        CountText.Text = $"{_items.Count:N0} élément(s)";
        UpdateSelection();
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        FilterBox.IsEnabled = RefreshBtn.IsEnabled = StartupList.IsEnabled = !busy;
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        if (StartupList.SelectedItem is not StartupItem item)
        {
            DisableBtn.IsEnabled = EnableBtn.IsEnabled = false;
            return;
        }
        DisableBtn.IsEnabled = !_busy && item.IsEnabled && item.CanToggle;
        EnableBtn.IsEnabled = !_busy && !item.IsEnabled && item.CanToggle;
    }

    private void Filter_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_loaded) return;
        ApplyFilter();
    }

    private void StartupList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSelection();

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    private async void Disable_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || StartupList.SelectedItem is not StartupItem item) return;
        if (MessageBox.Show(this,
                $"Désactiver « {item.Name} » au démarrage ?\n\nL'élément sera conservé dans une sauvegarde OpenCleaner.",
                "Désactiver le démarrage", MessageBoxButton.YesNo, MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes) return;
        await ToggleAsync(() => StartupItemService.Disable(item), "Élément désactivé.");
    }

    private async void Enable_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || StartupList.SelectedItem is not StartupItem item) return;
        await ToggleAsync(() => StartupItemService.Enable(item), "Élément réactivé.");
    }

    private async Task ToggleAsync(Func<bool> action, string successMessage)
    {
        SetBusy(true);
        try
        {
            bool success = await Task.Run(action);
            if (!success)
            {
                StatusText.Text = "L'opération a échoué (droits insuffisants ou élément déjà modifié).";
                return;
            }
            _loaded = false;
            await LoadAsync();
            _loaded = true;
            StatusText.Text = successMessage;
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
        finally { SetBusy(false); }
    }
}
