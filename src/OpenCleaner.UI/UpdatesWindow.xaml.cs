using System.Windows.Controls;
using System.Windows;
using OpenCleaner.Core.Tools;
using OpenCleaner.UI.Localization;

namespace OpenCleaner.UI;

public partial class UpdatesWindow : UserControl
{
    private static string T(string key, params object?[] args) => LocalizationService.Instance.Get(key, args);

    private const string CheckArguments =
        "upgrade --accept-source-agreements --disable-interactivity";

    public UpdatesWindow() => InitializeComponent();

    private async void Check_Click(object sender, RoutedEventArgs e) => await RunAsync(upgrade: false);

    private async void Upgrade_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(Window.GetWindow(this),
                T("Updates.Confirm"),
                T("Updates.DialogTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes) return;
        await RunAsync(upgrade: true);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => UiOperation.CancelCurrent();

    private async Task RunAsync(bool upgrade)
    {
        if (!UiOperation.TryBegin(Window.GetWindow(this), out var operation)) return;
        using (operation)
        {
            SetBusy(true);
            Output.Text = "";
            OutputPlaceholder.Visibility = Visibility.Collapsed;
            StatusText.Text = upgrade ? T("Updates.Status.WaitingElevation") : T("Updates.Status.Searching");
            try
            {
                var progress = operation.CreateProgress<string>(line =>
                {
                    StatusText.Text = upgrade
                        ? T("Updates.Status.Upgrading")
                        : T("Updates.Status.Searching");
                    Output.AppendText(line + Environment.NewLine);
                    Output.ScrollToEnd();
                });
                WingetResult result = await Task.Run(() => upgrade
                    ? ElevatedWinget.RunAsync(progress, operation.Token)
                    : WingetService.RunAsync(CheckArguments, progress, operation.Token), operation.Token);
                StatusText.Text = result.ExitCode == 0
                    ? T("Updates.Status.Done")
                    : T("Updates.Status.DoneCode", result.ExitCode);
                if (string.IsNullOrWhiteSpace(Output.Text)) Output.Text = T("Updates.NoOutput");
            }
            catch (OperationCanceledException)
            {
                StatusText.Text = T("Updates.Cancelled");
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                StatusText.Text = T("Updates.ElevationDenied");
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 2)
            {
                StatusText.Text = T("Updates.WingetMissing");
            }
            catch (Exception ex)
            {
                StatusText.Text = T("Updates.Failed", ex.Message);
            }
            finally
            {
                SetBusy(false);
                if (string.IsNullOrWhiteSpace(Output.Text))
                    OutputPlaceholder.Visibility = Visibility.Visible;
            }
        }
    }

    private void SetBusy(bool busy)
    {
        CheckBtn.IsEnabled = UpgradeBtn.IsEnabled = !busy;
        CancelBtn.IsEnabled = busy;
    }
}
