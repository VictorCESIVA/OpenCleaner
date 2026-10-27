using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using OpenCleaner.Core.Tools;

namespace OpenCleaner.UI;

public partial class UninstallerWindow : Window
{
    private readonly ObservableCollection<InstalledProgram> _items = [];
    private IReadOnlyList<InstalledProgram> _all = [];
    private bool _busy;
    private bool _loaded;

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
        SetBusy(true);
        StatusText.Text = "Lecture des applications installées…";
        try
        {
            _all = await Task.Run(InstalledProgramService.GetInstalledPrograms);
            ApplyFilter();
            StatusText.Text = _all.Count == 0
                ? "Aucune application détectée."
                : "Sélectionnez une application puis cliquez Désinstaller.";
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
        IEnumerable<InstalledProgram> filtered = _all;
        if (query.Length > 0)
            filtered = filtered.Where(p =>
                p.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                (p.Publisher?.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false));
        _items.Clear();
        foreach (InstalledProgram program in filtered) _items.Add(program);
        CountText.Text = $"{_items.Count:N0} application(s)";
        UpdateSelection();
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        FilterBox.IsEnabled = RefreshBtn.IsEnabled = ProgramsList.IsEnabled = !busy;
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        UninstallBtn.IsEnabled = !_busy && ProgramsList.SelectedItem is InstalledProgram { CanUninstall: true };
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
        if (_busy || ProgramsList.SelectedItem is not InstalledProgram program) return;
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
