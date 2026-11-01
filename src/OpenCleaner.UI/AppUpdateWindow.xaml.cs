using OpenCleaner.UI.Localization;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;

namespace OpenCleaner.UI;

public partial class AppUpdateWindow : Window
{
    private UpdateSnapshot _snapshot = UpdateSnapshot.Initial(UpdateService.CurrentVersion);
    private UpdateState? _lastState;
    private bool _preview;
    private bool _attached;
    private bool _closed;
    private bool _applying;
    private string? _transient;

    private static string T(string key, params object?[] args) =>
        LocalizationService.Instance.Get(key, args);

    public AppUpdateWindow()
    {
        InitializeComponent();
        _snapshot = UpdateService.State;
        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += OnClosed;
        ApplySnapshot(_snapshot);
    }

    internal void ShowPreviewState(UpdateSnapshot snapshot)
    {
        _preview = true;
        ApplySnapshot(snapshot);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_attached || _preview) return;
        _attached = true;
        UpdateService.StateChanged += OnServiceStateChanged;
        ApplySnapshot(UpdateService.State);
        if (_snapshot.State is UpdateState.Idle or UpdateState.UpToDate or UpdateState.Error)
            _ = RunCheckAsync();
    }

    private async Task RunCheckAsync()
    {
        try
        {
            await UpdateService.CheckNowAsync();
        }
        catch (Exception)
        {
            SetTransient(T("UpdateDialog.Error.Unknown"));
        }
    }

    private void OnServiceStateChanged(UpdateSnapshot snapshot)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => ApplySnapshot(snapshot)));
            return;
        }
        ApplySnapshot(snapshot);
    }

    private void ApplySnapshot(UpdateSnapshot snapshot)
    {
        if (_closed) return;
        if (_lastState != snapshot.State) _transient = null;
        _lastState = snapshot.State;
        _snapshot = snapshot;

        string current = snapshot.CurrentVersion ?? "—";
        string target = snapshot.TargetVersion ?? "—";
        CurrentVersionText.Text = current;
        TargetVersionText.Text = target;

        string notes = UpdateNotesFormatter.Format(snapshot.Notes);
        string notesTitle = T("UpdateDialog.NotesTitle");
        if (notes.Equals(notesTitle, StringComparison.CurrentCultureIgnoreCase))
            notes = "";
        else if (notes.StartsWith(notesTitle + "\n", StringComparison.CurrentCultureIgnoreCase))
            notes = notes[(notesTitle.Length + 1)..].TrimStart();
        UpdateNotesText.Text = notes.Length == 0 ? T("UpdateDialog.NotesEmpty") : notes;
        AutomationProperties.SetName(UpdateNotesText, snapshot.TargetVersion is null
            ? T("UpdateDialog.NotesTitle")
            : T("UpdateDialog.NotesAutomation", snapshot.TargetVersion));

        string title;
        string detail;
        switch (snapshot.State)
        {
            case UpdateState.Checking:
                title = T("UpdateDialog.Checking.Title");
                detail = T("UpdateDialog.Checking.Detail");
                break;
            case UpdateState.UpToDate:
                title = T("UpdateDialog.UpToDate.Title");
                detail = T("UpdateDialog.UpToDate.Detail", current);
                break;
            case UpdateState.Available:
                title = T("UpdateDialog.Available.Title");
                detail = T("UpdateDialog.Available.Detail", target);
                break;
            case UpdateState.Downloading:
                title = T("UpdateDialog.Downloading.Title", target);
                detail = T("UpdateDialog.Downloading.Detail", snapshot.Percent);
                break;
            case UpdateState.Ready:
                title = T("UpdateDialog.Ready.Title");
                detail = T("UpdateDialog.Ready.Detail", target);
                break;
            case UpdateState.Applying:
                title = T("UpdateDialog.Applying.Title");
                detail = T("UpdateDialog.Applying.Detail");
                break;
            case UpdateState.Error:
                title = T("UpdateDialog.Error.Title");
                detail = T("UpdateDialog.Error." + snapshot.ErrorCode);
                break;
            case UpdateState.NotInstalled:
                title = T("UpdateDialog.NotInstalled.Title");
                detail = T("UpdateDialog.NotInstalled.Detail");
                break;
            default:
                title = T("UpdateDialog.Checking.Title");
                detail = T("UpdateDialog.Checking.Detail");
                break;
        }

        StateTitleText.Text = title;
        StateDetailText.Text = detail;
        AutomationProperties.SetName(StateTitleText, title);
        AutomationProperties.SetName(StateDetailText, detail);

        bool indeterminate = snapshot.State == UpdateState.Checking;
        bool showProgress = snapshot.State is UpdateState.Checking or UpdateState.Downloading;
        UpdateProgressBar.Visibility = showProgress ? Visibility.Visible : Visibility.Collapsed;
        UpdateProgressBar.IsIndeterminate = indeterminate;
        UpdateProgressBar.Value = indeterminate ? 0 : snapshot.Percent;
        AutomationProperties.SetName(UpdateProgressBar, indeterminate
            ? T("UpdateDialog.ProgressAutomation")
            : T("UpdateDialog.ProgressAutomation") + " " + snapshot.Percent + " %");

        ConfigureButtons(snapshot.State);
        UpdateTransient();
        if (snapshot.State == UpdateState.Applying) _applying = true;
    }

    private void ConfigureButtons(UpdateState state)
    {
        PrimaryButton.Visibility = Visibility.Visible;
        PrimaryButton.IsEnabled = true;
        PrimaryButton.Style = (Style)FindResource("AccentBtnStyle");
        CloseButton.Visibility = Visibility.Visible;
        CloseButton.IsEnabled = true;
        CloseButton.Content = T("UpdateDialog.Close");
        CloseButton.IsCancel = true;
        CaptionCloseButton.IsEnabled = true;

        switch (state)
        {
            case UpdateState.Available:
                SetPrimary("UpdateDialog.Download", primary: true);
                CloseButton.Content = T("UpdateDialog.Later");
                break;
            case UpdateState.Downloading:
                SetPrimary("UpdateDialog.CancelDownload", primary: false);
                break;
            case UpdateState.Ready:
                SetPrimary("UpdateDialog.Restart", primary: true);
                CloseButton.Content = T("UpdateDialog.Later");
                break;
            case UpdateState.Error:
                SetPrimary("UpdateDialog.Retry", primary: true);
                break;
            case UpdateState.Applying:
                PrimaryButton.Visibility = Visibility.Collapsed;
                CloseButton.Visibility = Visibility.Collapsed;
                CloseButton.IsCancel = false;
                CaptionCloseButton.IsEnabled = false;
                break;
            default:
                PrimaryButton.Visibility = Visibility.Collapsed;
                break;
        }
        AutomationProperties.SetName(CloseButton, (string)CloseButton.Content);
    }

    private void SetPrimary(string key, bool primary)
    {
        string label = T(key);
        PrimaryButton.Content = label;
        PrimaryButton.Style = (Style)FindResource(primary ? "AccentBtnStyle" : "SecondBtnStyle");
        AutomationProperties.SetName(PrimaryButton, label);
    }

    private async void Primary_Click(object sender, RoutedEventArgs e)
    {
        if (_applying) return;
        switch (_snapshot.State)
        {
            case UpdateState.Available:
                PrimaryButton.IsEnabled = false;
                await UpdateService.DownloadAsync();
                break;
            case UpdateState.Downloading:
                UpdateService.CancelDownload();
                break;
            case UpdateState.Ready:
                _applying = true;
                if (!UpdateService.ApplyAndRestart())
                {
                    _applying = false;
                    SetTransient(T("UpdateDialog.Busy"));
                }
                break;
            case UpdateState.Error:
                await RunCheckAsync();
                break;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        UpdateSnapshot live = UpdateService.State;
        if ((_applying || live.State == UpdateState.Applying) && !UpdateService.IsShuttingDown)
        {
            e.Cancel = true;
            return;
        }
        if (!_preview && live.State == UpdateState.Downloading
            && !App.Preferences.BackgroundUpdateDownload)
            UpdateService.CancelDownload();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _closed = true;
        if (_attached) UpdateService.StateChanged -= OnServiceStateChanged;
    }

    private void SetTransient(string message)
    {
        _transient = message;
        UpdateTransient();
    }

    private void UpdateTransient()
    {
        string message = _transient ?? (_snapshot.State == UpdateState.Downloading
            && App.Preferences.BackgroundUpdateDownload ? T("UpdateDialog.BackgroundHint") : "");
        TransientText.Text = message;
        AutomationProperties.SetName(TransientText, message.Length == 0
            ? T("UpdateDialog.StatusAutomation")
            : message);
    }
}
