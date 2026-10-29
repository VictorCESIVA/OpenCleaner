using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using OpenCleaner.Core.Tools;
using OpenCleaner.UI.Localization;

namespace OpenCleaner.UI;

public partial class UninstallerWindow : UserControl
{
    private static string T(string key, params object?[] args) => LocalizationService.Instance.Get(key, args);

    private readonly ObservableCollection<InstalledProgramRow> _items = [];
    private IReadOnlyList<InstalledProgramRow> _all = [];
    private bool _busy;
    private bool _loaded;
    private bool _loadFailed;
    private string? _loadError;

    public UninstallerWindow()
    {
        InitializeComponent();
        ProgramsList.ItemsSource = _items;
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        Loaded += async (_, _) =>
        {
            if (_loaded) return;
            _loaded = true;
            await LoadAsync();
        };
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        foreach (InstalledProgramRow row in _all) row.RefreshLocalization();
        ApplyFilter();
    }

    private async Task LoadAsync()
    {
        _loadFailed = false;
        _loadError = null;
        SetBusy(true);
        StatusText.Text = T("Uninstall.Status.Loading");
        try
        {
            IReadOnlyList<InstalledProgram> programs =
                await Task.Run(InstalledProgramService.GetInstalledPrograms);
            _all = programs.Select(program => new InstalledProgramRow(program)).ToList();
            ApplyFilter();
            StatusText.Text = _all.Count == 0
                ? T("Uninstall.Status.None")
                : T("Uninstall.Status.Ready");
        }
        catch (Exception ex)
        {
            _loadFailed = true;
            _loadError = ex.Message;
            StatusText.Text = T("Uninstall.Status.LoadFailed", ex.Message);
        }
        finally { SetBusy(false); }
        if (_all.Count > 0) _ = LoadIconsAsync(_all);
    }

    private static async Task LoadIconsAsync(IReadOnlyList<InstalledProgramRow> rows) =>
        await Task.WhenAll(rows.Select(row => row.LoadIconAsync()));

    private void ApplyFilter()
    {
        string query = FilterBox.Text.Trim();
        IEnumerable<InstalledProgramRow> filtered = _all;
        if (query.Length > 0)
            filtered = filtered.Where(row =>
                row.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                (row.Program.Publisher?.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
                (row.Program.Version?.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false));
        _items.Clear();
        foreach (InstalledProgramRow program in filtered) _items.Add(program);
        CountText.Text = _items.Count == 1
            ? T("Uninstall.Count.One")
            : T("Uninstall.Count.Many", _items.Count.ToString("N0"));
        UpdateEmptyState();
        UpdateSelection();
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        FilterBox.IsEnabled = RefreshBtn.IsEnabled = ProgramsList.IsEnabled = !busy;
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
            EmptyTitle.Text = T("Uninstall.Empty.LoadFailedTitle");
            EmptyDescription.Text = _loadError ?? T("Uninstall.Empty.LoadFailedDescription");
            EmptyState.Visibility = Visibility.Visible;
            return;
        }

        string query = FilterBox.Text.Trim();
        bool filtering = query.Length > 0 && _all.Count > 0;
        EmptyIcon.Text = filtering ? "\uE721" : "\uE71D";
        EmptyTitle.Text = filtering ? T("Uninstall.Empty.NoResults") : T("Uninstall.EmptyTitle");
        EmptyDescription.Text = filtering
            ? T("Uninstall.Empty.NoMatch", query)
            : T("Uninstall.EmptyDescription");
        EmptyState.Visibility = Visibility.Visible;
    }

    private void UpdateSelection()
    {
        InstalledProgramRow? selected = ProgramsList.SelectedItem as InstalledProgramRow;
        UninstallBtn.IsEnabled = !_busy && selected is { CanUninstall: true };
        if (!_busy && selected is { CanUninstall: false })
            StatusText.Text = T("Uninstall.NoCommand");
    }

    private void Filter_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_loaded) return;
        ApplyFilter();
    }

    private void ProgramsList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSelection();

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    private void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || ProgramsList.SelectedItem is not InstalledProgramRow row) return;
        InstalledProgram program = row.Program;
        string publisherLine = program.Publisher is { Length: > 0 } publisher
            ? T("Uninstall.PublisherLine", publisher)
            : "";
        string prompt = T("Uninstall.Confirm", program.Name, publisherLine);
        if (MessageBox.Show(Window.GetWindow(this), prompt, T("Uninstall.DialogTitle"),
                MessageBoxButton.YesNo, MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes) return;
        try
        {
            if (InstalledProgramService.LaunchUninstall(program))
                StatusText.Text = T("Uninstall.Launched", program.Name);
            else
                MessageBox.Show(Window.GetWindow(this), T("Uninstall.NoCommandInfo"),
                    T("Uninstall.DialogTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), ex.Message, T("Uninstall.DialogTitle"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
