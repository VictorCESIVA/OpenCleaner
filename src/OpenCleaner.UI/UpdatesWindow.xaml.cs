using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using OpenCleaner.Core.Tools;
using OpenCleaner.UI.Localization;

namespace OpenCleaner.UI;

public partial class UpdatesWindow : UserControl
{
    private static string T(string key, params object?[] args) => LocalizationService.Instance.Get(key, args);

    private const int MaxLogLines = 600;
    private const int MaxLogChars = 64 * 1024;
    private const int MaxLogLineChars = 2000;

    private enum UpdatesState
    {
        None,
        Initial,
        Checking,
        NoUpdates,
        ParseFailed,
        CheckFailed,
        WingetMissing,
        ElevationDenied,
        Error,
        Cancelled
    }

    private readonly ObservableCollection<SoftwareUpdateRow> _rows = [];
    private readonly Dictionary<string, SoftwareUpdateRow> _rowsById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _logLines = [];
    private readonly HashSet<string> _completedIds = new(StringComparer.OrdinalIgnoreCase);

    private CancellationTokenSource? _cts;
    private bool _loaded;
    private bool _busy;
    private bool _upgrading;
    private int _logChars;
    private int _completed;
    private int _total;
    private UpdatesState _state = UpdatesState.Initial;
    private string _stateDetail = "";
    private string _statusKey = "Updates.Status.Initial";
    private object?[] _statusArgs = [];
    private string? _alertKey;
    private object?[] _alertArgs = [];

    public UpdatesWindow()
    {
        InitializeComponent();
        UpdatesList.ItemsSource = _rows;
        SetStatus("Updates.Status.Initial");
        UpdateStatePanel();
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        Loaded += OnLoaded;
        Unloaded += (_, _) => _cts?.Cancel();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;
        if (Application.Current is App { PreviewMode: true }) return;
        await CheckAsync();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        foreach (SoftwareUpdateRow row in _rows) row.RefreshLocalization();
        StatusText.Text = T(_statusKey, _statusArgs);
        if (_alertKey != null) AlertText.Text = T(_alertKey, _alertArgs);
        UpdateSelectionSummary();
        UpdateStatePanel();
    }

    private async void Check_Click(object sender, RoutedEventArgs e) => await CheckAsync();

    private void SelectAll_Click(object sender, RoutedEventArgs e) => SelectAllRows();

    private void SelectNone_Click(object sender, RoutedEventArgs e) => SelectNoneRows();

    private async void UpdateSelected_Click(object sender, RoutedEventArgs e) => await UpgradeAsync(all: false);

    private async void UpdateAll_Click(object sender, RoutedEventArgs e) => await UpgradeAsync(all: true);

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (!_busy || _cts == null) return;
        SetStatus("Updates.Status.Cancelling");
        _cts.Cancel();
    }

    private void Updates_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!UpdatesList.IsKeyboardFocusWithin) return;

        if (e.Key == Key.A && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            if (!_busy)
            {
                SelectAllRows();
                e.Handled = true;
            }
            return;
        }
        if (e.Key == Key.Space && !_busy)
        {
            SoftwareUpdateRow? row = RowFromFocus();
            if (row is { IsSelectable: true })
            {
                row.IsSelected = !row.IsSelected;
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Enter && !_busy && _rows.Any(row => row.IsSelected && row.IsSelectable))
        {
            e.Handled = true;
            _ = UpgradeAsync(all: false);
        }
    }

    private SoftwareUpdateRow? RowFromFocus()
    {
        if (Keyboard.FocusedElement is DependencyObject focused &&
            ItemsControl.ContainerFromElement(UpdatesList, focused) is ListBoxItem container &&
            container.DataContext is SoftwareUpdateRow focusedRow)
            return focusedRow;
        return UpdatesList.SelectedItem as SoftwareUpdateRow;
    }

    private void SelectAllRows()
    {
        if (_busy) return;
        foreach (SoftwareUpdateRow row in _rows)
            if (row.IsSelectable && !row.HasUnknownVersion)
                row.IsSelected = true;
        UpdateUi();
    }

    private void SelectNoneRows()
    {
        if (_busy) return;
        foreach (SoftwareUpdateRow row in _rows) row.IsSelected = false;
        UpdateUi();
    }

    private async Task CheckAsync()
    {
        if (_busy) return;
        ClearAlert();
        ClearLog();
        _busy = true;
        _upgrading = false;
        SetState(_rows.Count == 0 ? UpdatesState.Checking : UpdatesState.None);
        SetStatus("Updates.Status.Searching");
        ShowIndeterminateProgress();
        UpdateUi();

        CancellationTokenSource cts = BeginOperation();
        try
        {
            IProgress<string> progress = new DispatcherProgress<string>(Dispatcher, AppendLog);
            WingetUpgradeList list = await WingetUpgradeService.ListAsync(cts.Token, progress);
            if (cts.IsCancellationRequested) throw new OperationCanceledException(cts.Token);
            ApplyList(list);
        }
        catch (OperationCanceledException)
        {
            if (_rows.Count == 0) SetState(UpdatesState.Cancelled);
            SetStatus("Updates.Cancelled");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 2)
        {
            if (_rows.Count == 0) SetState(UpdatesState.WingetMissing);
            else SetAlert("Updates.WingetMissing");
            SetStatus("Updates.WingetMissing");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            if (_rows.Count == 0) SetState(UpdatesState.ElevationDenied);
            else SetAlert("Updates.ElevationDenied");
            SetStatus("Updates.ElevationDenied");
        }
        catch (Exception ex)
        {
            AppendLog(ex.Message);
            if (_rows.Count == 0) SetState(UpdatesState.Error, ex.Message);
            else SetAlert("Updates.Failed", ex.Message);
            SetStatus("Updates.Failed", ex.Message);
        }
        finally
        {
            EndOperation(cts);
            HideProgress();
            _busy = false;
            _upgrading = false;
            UpdateUi();
        }
    }

    private void ApplyList(WingetUpgradeList list)
    {
        var previous = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (SoftwareUpdateRow row in _rows) previous[Identity(row.Id, row.Source)] = row.IsSelected;
        ClearRows();

        foreach (WingetUpgradeItem item in list.Items.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            string key = Identity(item.Id, item.Source);
            bool selected = previous.TryGetValue(key, out bool wasSelected)
                ? wasSelected
                : !item.HasUnknownVersion;
            AddRow(new SoftwareUpdateRow(item, selected));
        }

        foreach (string warning in list.Warnings) AppendLog(warning);
        if (list.Warnings.Count > 0) AppendLog(T("Updates.Warnings", list.Warnings.Count));

        _total = 0;
        _completed = 0;
        _completedIds.Clear();
        HideProgress();

        if (_rows.Count == 0)
        {
            if (list.ParseFailed)
            {
                SetState(UpdatesState.ParseFailed);
                SetStatus("Updates.State.ParseFailedTitle");
            }
            else if (list.ExitCode != 0)
            {
                string code = list.ExitCode.ToString(CultureInfo.InvariantCulture);
                SetState(UpdatesState.CheckFailed, code);
                SetStatus("Updates.Status.CheckFailed", list.ExitCode);
            }
            else
            {
                SetState(UpdatesState.NoUpdates);
                SetStatus("Updates.Status.UpToDate");
            }
            return;
        }

        SetState(UpdatesState.None);
        SetStatus("Updates.Status.Found", _rows.Count);
        UpdateUi();
    }

    private async Task UpgradeAsync(bool all)
    {
        if (_busy) return;
        List<SoftwareUpdateRow> targets = _rows
            .Where(row => row.IsSelectable && (all ? !row.HasUnknownVersion : row.IsSelected))
            .ToList();
        if (targets.Count == 0)
        {
            SetStatus("Updates.NoSelection");
            return;
        }
        if (targets.Count > ElevatedWinget.MaxTargets)
        {
            SetStatus("Updates.TooMany", ElevatedWinget.MaxTargets);
            SetAlert("Updates.TooMany", ElevatedWinget.MaxTargets);
            return;
        }

        string prompt = all ? T("Updates.Confirm") : T("Updates.ConfirmSelected", targets.Count);
        if (MessageBox.Show(Window.GetWindow(this), prompt, T("Updates.DialogTitle"),
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;

        _busy = true;
        _upgrading = true;
        ClearAlert();
        ClearLog();
        foreach (SoftwareUpdateRow row in _rows)
            if (row.IsSelectable)
                row.PrepareForUpgrade();
        foreach (SoftwareUpdateRow row in targets) row.IsSelected = true;

        _total = targets.Count;
        _completed = 0;
        _completedIds.Clear();
        SetState(UpdatesState.None);
        SetStatus("Updates.Status.WaitingElevation");
        ShowProgress(0, _total);
        UpdateUi();

        CancellationTokenSource cts = BeginOperation();
        try
        {
            var progress = new DispatcherProgress<WingetPackageUpdate>(Dispatcher, OnPackageUpdate);
            var request = targets
                .Select(row => new WingetUpgradeTarget(
                    row.Id, row.Source, WingetUpgradeService.DefaultScope, row.HasUnknownVersion))
                .ToList();
            WingetBatchResult result = await ElevatedWinget.RunAsync(request, progress, cts.Token);
            FinishBatch(result);
        }
        catch (OperationCanceledException)
        {
            MarkUnfinished(targets, cancelled: true);
            SetStatus("Updates.Cancelled");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            HideProgress();
            SetStatus("Updates.ElevationDenied");
            SetAlert("Updates.ElevationDenied");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 2)
        {
            HideProgress();
            SetStatus("Updates.WingetMissing");
            SetAlert("Updates.WingetMissing");
        }
        catch (Exception ex)
        {
            MarkUnfinished(targets, cancelled: false, ex.Message);
            AppendLog(ex.Message);
            SetStatus("Updates.Failed", ex.Message);
            SetAlert("Updates.Failed", ex.Message);
        }
        finally
        {
            EndOperation(cts);
            _busy = false;
            _upgrading = false;
            UpdateUi();
        }
    }

    private void OnPackageUpdate(WingetPackageUpdate update)
    {
        if (string.IsNullOrEmpty(update.Id))
        {
            if (!string.IsNullOrEmpty(update.Message)) AppendLog(update.Message);
            return;
        }
        if (!_rowsById.TryGetValue(update.Id, out SoftwareUpdateRow? row))
        {
            if (!string.IsNullOrEmpty(update.Message)) AppendLog(update.Message);
            return;
        }

        switch (update.Phase?.ToLowerInvariant())
        {
            case WingetUpgradePhase.Running:
                row.ApplyRunning();
                SetStatus("Updates.Status.UpgradingOne", row.Name);
                break;
            case WingetUpgradePhase.Output:
                if (!string.IsNullOrEmpty(update.Message)) AppendLog($"[{row.Id}] {update.Message}");
                break;
            case WingetUpgradePhase.Succeeded:
                row.ApplySucceeded();
                row.IsSelected = false;
                if (MarkCompleted(row.Id)) AppendLog($"[{row.Id}] {T("Updates.Row.Succeeded")}");
                break;
            case WingetUpgradePhase.Failed:
                row.ApplyFailed(update.ExitCode, update.Message);
                if (MarkCompleted(row.Id))
                    AppendLog($"[{row.Id}] {update.Message ?? T("Updates.Row.Failed")}");
                break;
            case WingetUpgradePhase.Cancelled:
                row.ApplyCancelled();
                if (MarkCompleted(row.Id)) AppendLog($"[{row.Id}] {T("Updates.Row.Cancelled")}");
                break;
            case WingetUpgradePhase.Skipped:
                row.ApplySkipped();
                if (MarkCompleted(row.Id)) AppendLog($"[{row.Id}] {T("Updates.Row.Skipped")}");
                break;
        }
    }

    private void FinishBatch(WingetBatchResult result)
    {
        foreach (WingetPackageUpdate update in result.Packages) OnPackageUpdate(update);
        int finished = result.Succeeded + result.Failed + result.Cancelled;
        if (finished > _completed) _completed = Math.Min(_total, finished);
        UpdateProgress();

        if (result.WasCancelled)
        {
            SetStatus("Updates.Cancelled");
        }
        else if (result.Failed > 0 && result.Succeeded > 0)
        {
            SetStatus("Updates.Status.Partial", result.Succeeded, result.Failed);
            SetAlert("Updates.Status.Partial", result.Succeeded, result.Failed);
        }
        else if (result.Failed > 0)
        {
            SetStatus("Updates.Status.FailedCount", result.Failed);
            SetAlert("Updates.Status.FailedCount", result.Failed);
        }
        else
        {
            SetStatus("Updates.Status.AllDone", result.Succeeded);
        }
    }

    private void MarkUnfinished(IReadOnlyList<SoftwareUpdateRow> targets, bool cancelled, string? message = null)
    {
        foreach (SoftwareUpdateRow row in targets)
        {
            if (_completedIds.Contains(row.Id)) continue;
            if (cancelled) row.ApplyCancelled();
            else row.ApplyFailed(-1, message);
            MarkCompleted(row.Id);
        }
        UpdateProgress();
    }

    private bool MarkCompleted(string id)
    {
        if (!_completedIds.Add(id)) return false;
        _completed = Math.Min(_total, _completed + 1);
        UpdateProgress();
        return true;
    }

    private void UpdateProgress()
    {
        if (!_upgrading && _completedIds.Count == 0) return;
        ShowProgress(_completed, _total);
    }

    private void ShowIndeterminateProgress()
    {
        OverallProgress.Visibility = Visibility.Visible;
        OverallProgress.IsIndeterminate = true;
        OverallProgress.Maximum = 1;
        OverallProgress.Value = 0;
        ProgressText.Text = "";
    }

    private void ShowProgress(int completed, int total)
    {
        OverallProgress.Visibility = Visibility.Visible;
        OverallProgress.IsIndeterminate = false;
        OverallProgress.Maximum = Math.Max(1, total);
        OverallProgress.Value = Math.Clamp(completed, 0, Math.Max(0, total));
        ProgressText.Text = T("Updates.Progress", completed, total);
    }

    private void HideProgress()
    {
        OverallProgress.Visibility = Visibility.Collapsed;
        OverallProgress.IsIndeterminate = false;
        OverallProgress.Value = 0;
        ProgressText.Text = "";
    }

    private void UpdateUi()
    {
        UpdateActionButtons();
        UpdateSelectionSummary();
        UpdateStatePanel();
    }

    private void UpdateActionButtons()
    {
        int selected = _rows.Count(row => row.IsSelected && row.IsSelectable);
        CheckBtn.IsEnabled = !_busy;
        SelectAllBtn.IsEnabled = !_busy && _rows.Any(row => row.IsSelectable && !row.HasUnknownVersion);
        SelectNoneBtn.IsEnabled = !_busy && _rows.Any(row => row.IsSelected);
        UpdateSelectedBtn.IsEnabled = !_busy && selected > 0;
        UpdateAllBtn.IsEnabled = !_busy && _rows.Any(row => row.IsSelectable && !row.HasUnknownVersion);
        CancelBtn.Visibility = _busy ? Visibility.Visible : Visibility.Collapsed;
        CancelBtn.IsEnabled = _busy;
        UpdatesList.IsEnabled = !_busy;
    }

    private void UpdateSelectionSummary()
    {
        SelectedCountText.Text = _rows.Count == 0
            ? T("Updates.Count.None")
            : T("Updates.Count.Selected", _rows.Count(row => row.IsSelected), _rows.Count);
    }

    private void SetStatus(string key, params object?[] args)
    {
        _statusKey = key;
        _statusArgs = args;
        StatusText.Text = T(key, args);
    }

    private void SetAlert(string key, params object?[] args)
    {
        _alertKey = key;
        _alertArgs = args;
        AlertText.Text = T(key, args);
        AlertBanner.Visibility = Visibility.Visible;
    }

    private void ClearAlert()
    {
        _alertKey = null;
        _alertArgs = [];
        AlertBanner.Visibility = Visibility.Collapsed;
    }

    private void SetState(UpdatesState state, string? detail = null)
    {
        _state = state;
        if (detail != null) _stateDetail = detail;
        UpdateStatePanel();
    }

    private void UpdateStatePanel()
    {
        if (_rows.Count > 0 || _state == UpdatesState.None)
        {
            StatePanel.Visibility = Visibility.Collapsed;
            return;
        }

        string icon;
        string titleKey;
        string description;
        bool showProgress = false;
        switch (_state)
        {
            case UpdatesState.Checking:
                icon = "\uE895";
                titleKey = "Updates.State.CheckingTitle";
                description = T("Updates.State.CheckingDescription");
                showProgress = true;
                break;
            case UpdatesState.NoUpdates:
                icon = "\uE73E";
                titleKey = "Updates.State.NoUpdatesTitle";
                description = T("Updates.State.NoUpdatesDescription");
                break;
            case UpdatesState.ParseFailed:
                icon = "\uE783";
                titleKey = "Updates.State.ParseFailedTitle";
                description = T("Updates.State.ParseFailedDescription");
                break;
            case UpdatesState.CheckFailed:
                icon = "\uE783";
                titleKey = "Updates.State.CheckFailedTitle";
                description = T("Updates.State.CheckFailedDescription", _stateDetail);
                break;
            case UpdatesState.WingetMissing:
                icon = "\uE783";
                titleKey = "Updates.State.WingetMissingTitle";
                description = T("Updates.WingetMissing");
                break;
            case UpdatesState.ElevationDenied:
                icon = "\uE7BA";
                titleKey = "Updates.State.ElevationDeniedTitle";
                description = T("Updates.ElevationDenied");
                break;
            case UpdatesState.Error:
                icon = "\uE783";
                titleKey = "Updates.State.ErrorTitle";
                description = _stateDetail;
                break;
            case UpdatesState.Cancelled:
                icon = "\uE711";
                titleKey = "Updates.State.CancelledTitle";
                description = T("Updates.State.CancelledDescription");
                break;
            default:
                icon = "\uE895";
                titleKey = "Updates.State.InitialTitle";
                description = T("Updates.State.InitialDescription");
                break;
        }

        StateIcon.Text = icon;
        StateTitle.Text = T(titleKey);
        StateDescription.Text = description;
        StateProgress.Visibility = showProgress ? Visibility.Visible : Visibility.Collapsed;
        StatePanel.Visibility = Visibility.Visible;
    }

    private void ClearRows()
    {
        foreach (SoftwareUpdateRow row in _rows) row.PropertyChanged -= OnRowPropertyChanged;
        _rows.Clear();
        _rowsById.Clear();
    }

    private void AddRow(SoftwareUpdateRow row)
    {
        _rows.Add(row);
        _rowsById[row.Id] = row;
        row.PropertyChanged += OnRowPropertyChanged;
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SoftwareUpdateRow.IsSelected) or nameof(SoftwareUpdateRow.IsSelectable))
        {
            UpdateSelectionSummary();
            UpdateActionButtons();
        }
    }

    private static string Identity(string id, string source) =>
        id.ToLowerInvariant() + "\0" + source.ToLowerInvariant();

    private CancellationTokenSource BeginOperation()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        var cts = new CancellationTokenSource();
        _cts = cts;
        return cts;
    }

    private void EndOperation(CancellationTokenSource cts)
    {
        if (ReferenceEquals(_cts, cts)) _cts = null;
        cts.Dispose();
    }

    private void AppendLog(string? line)
    {
        if (string.IsNullOrEmpty(line)) return;
        foreach (string part in line.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            string text = part.Length > MaxLogLineChars ? part[..MaxLogLineChars] : part;
            _logLines.Enqueue(text);
            _logChars += text.Length + 1;
        }
        while (_logLines.Count > MaxLogLines || _logChars > MaxLogChars)
        {
            string removed = _logLines.Dequeue();
            _logChars -= removed.Length + 1;
        }
        DetailsLog.Text = string.Join(Environment.NewLine, _logLines);
        DetailsPlaceholder.Visibility = Visibility.Collapsed;
        if (DetailsExpander.IsExpanded) DetailsLog.ScrollToEnd();
    }

    private void ClearLog()
    {
        _logLines.Clear();
        _logChars = 0;
        DetailsLog.Text = "";
        DetailsPlaceholder.Visibility = Visibility.Visible;
    }

    internal void ShowPreviewData(string state)
    {
        _loaded = true;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _busy = false;
        _upgrading = false;
        ClearAlert();
        ClearLog();
        ClearRows();

        switch (state)
        {
            case "empty":
                _total = 0;
                _completed = 0;
                _completedIds.Clear();
                SetState(UpdatesState.NoUpdates);
                SetStatus("Updates.Status.UpToDate");
                HideProgress();
                break;

            case "updating":
                AddPreviewRows();
                SoftwareUpdateRow chrome = _rows[0];
                SoftwareUpdateRow code = _rows[1];
                SoftwareUpdateRow powerToys = _rows[4];
                chrome.ApplySucceeded();
                chrome.IsSelected = false;
                code.ApplyRunning();
                powerToys.ApplyFailed(1, T("Updates.Preview.FailureDetail"));
                _total = 4;
                _completed = 2;
                _completedIds.Clear();
                _completedIds.Add(chrome.Id);
                _completedIds.Add(powerToys.Id);
                _busy = true;
                _upgrading = true;
                AppendLog("[Google.Chrome] " + T("Updates.Row.Succeeded"));
                AppendLog("[Microsoft.VisualStudioCode] Downloading installer...");
                AppendLog("[Microsoft.PowerToys] " + T("Updates.Preview.FailureDetail"));
                SetState(UpdatesState.None);
                SetStatus("Updates.Status.UpgradingOne", code.Name);
                ShowProgress(_completed, _total);
                break;

            default:
                AddPreviewRows();
                _total = 0;
                _completed = 0;
                _completedIds.Clear();
                AppendLog("Found Google Chrome [Google.Chrome] Version 125.0.6422.60");
                AppendLog("Found Microsoft Visual Studio Code [Microsoft.VisualStudioCode] Version 1.89.1");
                AppendLog("Found Mozilla.Firefox [Mozilla.Firefox] Version 126.0");
                SetState(UpdatesState.None);
                SetStatus("Updates.Status.Found", _rows.Count(row => row.IsSelectable));
                HideProgress();
                break;
        }

        UpdateUi();
    }

    private void AddPreviewRows()
    {
        AddRow(new SoftwareUpdateRow(
            new WingetUpgradeItem("Google.Chrome", "Google Chrome", "124.0.6367.60", "125.0.6422.60",
                WingetUpgradeService.WingetSource, false, false), true));
        AddRow(new SoftwareUpdateRow(
            new WingetUpgradeItem("Microsoft.VisualStudioCode", "Microsoft Visual Studio Code", "1.88.1", "1.89.1",
                WingetUpgradeService.WingetSource, false, false), true));
        AddRow(new SoftwareUpdateRow(
            new WingetUpgradeItem("Mozilla.Firefox", "Mozilla Firefox", "125.0.3", "126.0",
                WingetUpgradeService.WingetSource, false, false), true));
        AddRow(new SoftwareUpdateRow(
            new WingetUpgradeItem("Microsoft.WindowsTerminal", "Windows Terminal", "1.19.10821.0", "",
                WingetUpgradeService.WingetSource, true, false), false));
        AddRow(new SoftwareUpdateRow(
            new WingetUpgradeItem("Microsoft.PowerToys", "Microsoft PowerToys", "0.80.1", "0.81.0",
                WingetUpgradeService.WingetSource, false, false), true));
    }

    private sealed class DispatcherProgress<T>(Dispatcher dispatcher, Action<T> handler) : IProgress<T>
    {
        public void Report(T value)
        {
            if (dispatcher.CheckAccess()) handler(value);
            else dispatcher.Invoke(() => handler(value));
        }
    }
}
