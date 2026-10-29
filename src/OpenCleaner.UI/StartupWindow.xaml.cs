using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using OpenCleaner.Core.Tools;

namespace OpenCleaner.UI;

public partial class StartupWindow : UserControl
{
    private readonly ObservableCollection<StartupItemRow> _items = [];
    private IReadOnlyList<StartupItemRow> _all = [];
    private bool _busy;
    private bool _loaded;
    private bool _loadFailed;
    private string? _loadError;

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
        _loadFailed = false;
        _loadError = null;
        SetBusy(true);
        StatusText.Text = "Lecture des éléments de démarrage…";
        try
        {
            IReadOnlyList<StartupItem> items = await Task.Run(StartupItemService.GetItems);
            _all = items.Select(item => new StartupItemRow(item)).ToList();
            ApplyFilter();
            StatusText.Text = _all.Count == 0
                ? "Aucun élément de démarrage détecté."
                : "Sélectionnez un élément pour l'activer ou le désactiver.";
        }
        catch (Exception ex)
        {
            _loadFailed = true;
            _loadError = ex.Message;
            StatusText.Text = "Lecture impossible : " + ex.Message;
        }
        finally { SetBusy(false); }
        if (_all.Count > 0) _ = LoadIconsAsync(_all);
    }

    private static async Task LoadIconsAsync(IReadOnlyList<StartupItemRow> rows) =>
        await Task.WhenAll(rows.Select(row => row.LoadIconAsync()));

    private void ApplyFilter()
    {
        string query = FilterBox.Text.Trim();
        IEnumerable<StartupItemRow> filtered = _all;
        if (query.Length > 0)
            filtered = filtered.Where(row =>
                row.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                row.Command.Contains(query, StringComparison.CurrentCultureIgnoreCase));

        string sort = (SortCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "Name";
        var ordered = sort == "Impact"
            ? filtered.OrderByDescending(row => row.Item.Impact)
                .ThenBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase)
            : filtered.OrderBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase);

        _items.Clear();
        foreach (StartupItemRow item in ordered) _items.Add(item);
        int highImpact = _items.Count(row => row.Item.Impact == StartupImpact.High);
        CountText.Text = _items.Count == 1 ? "1 élément" : $"{_items.Count:N0} éléments";
        if (highImpact > 0) CountText.Text += $" • {highImpact} à impact élevé";
        UpdateEmptyState();
        UpdateSelection();
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        FilterBox.IsEnabled = RefreshBtn.IsEnabled = StartupList.IsEnabled = !busy;
        LoadingState.Visibility = busy && _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateEmptyState();
        UpdateSelection();
    }

    private void UpdateEmptyState()
    {
        if (_busy || _items.Count > 0)
        {
            EmptyState.Visibility = Visibility.Collapsed;
            return;
        }

        if (_loadFailed)
        {
            EmptyIcon.Text = "\uE783";
            EmptyTitle.Text = "Lecture impossible";
            EmptyDescription.Text = _loadError ?? "Les éléments de démarrage n'ont pas pu être lus.";
            EmptyState.Visibility = Visibility.Visible;
            return;
        }

        string query = FilterBox.Text.Trim();
        bool filtering = query.Length > 0 && _all.Count > 0;
        EmptyIcon.Text = filtering ? "\uE721" : "\uE768";
        EmptyTitle.Text = filtering ? "Aucun résultat" : "Aucun élément de démarrage détecté";
        EmptyDescription.Text = filtering
            ? $"Aucun élément ne correspond à « {query} »."
            : "Les programmes lancés à l'ouverture de session apparaîtront ici.";
        EmptyState.Visibility = Visibility.Visible;
    }

    private void UpdateSelection()
    {
        if (StartupList.SelectedItem is not StartupItemRow row)
        {
            DisableBtn.IsEnabled = EnableBtn.IsEnabled = false;
            return;
        }
        DisableBtn.IsEnabled = !_busy && row.IsEnabled && row.CanToggle;
        EnableBtn.IsEnabled = !_busy && !row.IsEnabled && row.CanToggle;
        if (!_busy && !row.CanToggle)
            StatusText.Text = "Cet élément nécessite les droits administrateur pour être modifié.";
    }

    private void Filter_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_loaded) return;
        ApplyFilter();
    }

    private void Sort_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded) return;
        ApplyFilter();
    }

    private void StartupList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSelection();

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    private async void Disable_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || StartupList.SelectedItem is not StartupItemRow row) return;
        if (MessageBox.Show(Window.GetWindow(this),
                $"Désactiver « {row.Name} » au démarrage ?\n\nL'élément sera conservé dans une sauvegarde OpenCleaner.",
                "Désactiver le démarrage", MessageBoxButton.YesNo, MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes) return;
        await ToggleAsync(() => StartupItemService.Disable(row.Item), "Élément désactivé.");
    }

    private async void Enable_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || StartupList.SelectedItem is not StartupItemRow row) return;
        await ToggleAsync(() => StartupItemService.Enable(row.Item), "Élément réactivé.");
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
