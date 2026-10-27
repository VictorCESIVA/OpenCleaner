using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using OpenCleaner.Core.Tools;

namespace OpenCleaner.UI;

public partial class UninstallerWindow : Window
{
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
        StatusText.Text = "Lecture des applications installées…";
        try
        {
            IReadOnlyList<InstalledProgram> programs =
                await Task.Run(InstalledProgramService.GetInstalledPrograms);
            _all = programs.Select(program => new InstalledProgramRow(program)).ToList();
            ApplyFilter();
            StatusText.Text = _all.Count == 0
                ? "Aucune application détectée."
                : "Sélectionnez une application pour lancer son désinstalleur.";
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
        CountText.Text = _items.Count == 1 ? "1 application" : $"{_items.Count:N0} applications";
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
            EmptyTitle.Text = "Lecture impossible";
            EmptyDescription.Text = _loadError ?? "La liste des programmes n'a pas pu être lue.";
            EmptyState.Visibility = Visibility.Visible;
            return;
        }

        string query = FilterBox.Text.Trim();
        bool filtering = query.Length > 0 && _all.Count > 0;
        EmptyIcon.Text = filtering ? "\uE721" : "\uE71D";
        EmptyTitle.Text = filtering ? "Aucun résultat" : "Aucune application détectée";
        EmptyDescription.Text = filtering
            ? $"Aucune application ne correspond à « {query} »."
            : "La liste des programmes installés apparaîtra ici.";
        EmptyState.Visibility = Visibility.Visible;
    }

    private void UpdateSelection()
    {
        InstalledProgramRow? selected = ProgramsList.SelectedItem as InstalledProgramRow;
        UninstallBtn.IsEnabled = !_busy && selected is { CanUninstall: true };
        if (!_busy && selected is { CanUninstall: false })
            StatusText.Text = "Aucune commande de désinstallation n'est disponible pour cette application.";
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
        string prompt = $"Lancer le désinstalleur de « {program.Name} » ?\n\n" +
            (program.Publisher is { Length: > 0 } publisher ? publisher + "\n\n" : "") +
            "Le programme de désinstallation de l'éditeur sera exécuté. OpenCleaner ne peut pas annuler cette action.";
        if (MessageBox.Show(this, prompt, "Désinstaller", MessageBoxButton.YesNo, MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes) return;
        try
        {
            if (InstalledProgramService.LaunchUninstall(program))
                StatusText.Text = $"Désinstalleur lancé pour « {program.Name} ». Actualisez la liste après la fin.";
            else
                MessageBox.Show(this, "Aucune commande de désinstallation n'est disponible pour ce programme.",
                    "Désinstaller", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Désinstaller", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
