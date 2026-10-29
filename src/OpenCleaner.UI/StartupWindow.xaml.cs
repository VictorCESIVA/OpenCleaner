using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OpenCleaner.Core.Tools;
using OpenCleaner.UI.Localization;

namespace OpenCleaner.UI;

public partial class StartupWindow : UserControl
{
    private static string T(string key, params object?[] args) => LocalizationService.Instance.Get(key, args);

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
        foreach (StartupItemRow row in _all) row.RefreshLocalization();
        ApplyFilter();
    }

    private async Task LoadAsync()
    {
        _loadFailed = false;
        _loadError = null;
        SetBusy(true);
        StatusText.Text = T("Startup.Status.Loading");
        try
        {
            IReadOnlyList<StartupItem> items = await Task.Run(StartupItemService.GetItems);
            _all = items.Select(item => new StartupItemRow(item)).ToList();
            ApplyFilter();
            StatusText.Text = _all.Count == 0
                ? T("Startup.Status.None")
                : T("Startup.Status.Ready");
        }
        catch (Exception ex)
        {
            _loadFailed = true;
            _loadError = ex.Message;
            StatusText.Text = T("Startup.Status.LoadFailed", ex.Message);
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
        CountText.Text = _items.Count == 1
            ? T("Startup.Count.One")
            : T("Startup.Count.Many", _items.Count.ToString("N0"));
        if (highImpact > 0) CountText.Text += T("Startup.Count.HighImpact", highImpact);
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
            EmptyTitle.Text = T("Startup.Empty.LoadFailedTitle");
            EmptyDescription.Text = _loadError ?? T("Startup.Empty.LoadFailedDescription");
            EmptyState.Visibility = Visibility.Visible;
            return;
        }

        string query = FilterBox.Text.Trim();
        bool filtering = query.Length > 0 && _all.Count > 0;
        EmptyIcon.Text = filtering ? "\uE721" : "\uE768";
        EmptyTitle.Text = filtering ? T("Startup.Empty.NoResults") : T("Startup.EmptyTitle");
        EmptyDescription.Text = filtering
            ? T("Startup.Empty.NoMatch", query)
            : T("Startup.EmptyDescription");
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
            StatusText.Text = T("Startup.Status.NotEditable");
        else if (!_busy && row.IsMachine && !ElevatedStartup.IsElevated())
            StatusText.Text = T("Startup.Status.ElevationRequired");
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

    private async void Startup_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.F5 || _busy) return;
        e.Handled = true;
        await LoadAsync();
    }

    private async void Disable_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || StartupList.SelectedItem is not StartupItemRow row) return;
        if (row.IsMachine)
        {
            if (MessageBox.Show(Window.GetWindow(this),
                    T("Startup.ConfirmMachine", row.Name),
                    T("Startup.DisableTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning,
                    MessageBoxResult.No) != MessageBoxResult.Yes) return;
            await ToggleMachineAsync(row, disable: true);
            return;
        }
        if (MessageBox.Show(Window.GetWindow(this),
                T("Startup.ConfirmUser", row.Name),
                T("Startup.DisableTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes) return;
        await ToggleAsync(() => StartupItemService.Disable(row.Item), T("Startup.Disabled"));
    }

    private async void Enable_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || StartupList.SelectedItem is not StartupItemRow row) return;
        if (row.IsMachine)
        {
            await ToggleMachineAsync(row, disable: false);
            return;
        }
        await ToggleAsync(() => StartupItemService.Enable(row.Item), T("Startup.Enabled"));
    }

    private async Task ToggleMachineAsync(StartupItemRow row, bool disable)
    {
        if (string.IsNullOrWhiteSpace(row.Item.ValueName))
        {
            StatusText.Text = T("Startup.NoRegistryValue");
            return;
        }

        MachineStartupKey key = KeyFor(row.Item);
        var request = disable
            ? new MachineStartupRequest(MachineStartupAction.Disable, key, row.Item.ValueName, row.Item.Command)
            : new MachineStartupRequest(MachineStartupAction.Enable, key, row.Item.ValueName);
        SetBusy(true);
        StatusText.Text = disable
            ? T("Startup.DisableProgress")
            : T("Startup.EnableProgress");
        try
        {
            MachineStartupResult result = ElevatedStartup.IsElevated()
                ? await Task.Run(() => new MachineStartupService().Apply(request))
                : await ElevatedStartup.RunAsync(request);
            StatusText.Text = PluginMessageLocalizer.Localize(result.Message);
            if (!result.Success) return;
            _loaded = false;
            await LoadAsync();
            _loaded = true;
            StatusText.Text = PluginMessageLocalizer.Localize(result.Message);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            StatusText.Text = T("Startup.Cancelled");
        }
        catch (Exception ex)
        {
            StatusText.Text = T("Startup.Failed", ex.Message);
        }
        finally { SetBusy(false); }
    }

    private static MachineStartupKey KeyFor(StartupItem item) =>
        item.Location == StartupItemLocation.LocalMachineRunOnce ||
        (item.RegistryPath?.EndsWith("RunOnce", StringComparison.OrdinalIgnoreCase) ?? false)
            ? MachineStartupKey.RunOnce
            : MachineStartupKey.Run;

    private async Task ToggleAsync(Func<bool> action, string successMessage)
    {
        SetBusy(true);
        try
        {
            bool success = await Task.Run(action);
            if (!success)
            {
                StatusText.Text = T("Startup.FailedRights");
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
