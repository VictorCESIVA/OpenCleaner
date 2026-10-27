using System.Windows;
using OpenCleaner.Core.Tools;

namespace OpenCleaner.UI;

public partial class UpdatesWindow : Window
{
    private const string CheckArguments =
        "upgrade --include-unknown --accept-source-agreements --disable-interactivity";
    private const string UpgradeArguments =
        "upgrade --all --include-unknown --silent --accept-package-agreements --accept-source-agreements --disable-interactivity";

    public UpdatesWindow() => InitializeComponent();

    private async void Check_Click(object sender, RoutedEventArgs e) => await RunAsync(CheckArguments);

    private async void Upgrade_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this,
                "Mettre à jour toutes les applications détectées par winget ?\n\nCertaines opérations peuvent demander des droits administrateur ou redémarrer une application.",
                "Mises à jour", MessageBoxButton.YesNo, MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes) return;
        await RunAsync(UpgradeArguments);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => UiOperation.CancelCurrent();

    private async Task RunAsync(string arguments)
    {
        if (!UiOperation.TryBegin(this, out var operation)) return;
        using (operation)
        {
            SetBusy(true);
            Output.Text = "";
            OutputPlaceholder.Visibility = Visibility.Collapsed;
            StatusText.Text = "Vérification de winget…";
            try
            {
                bool available = await Task.Run(WingetService.IsAvailable, operation.Token);
                if (!available)
                {
                    StatusText.Text = "winget n'est pas disponible sur ce poste.";
                    return;
                }
                StatusText.Text = "Exécution de winget…";
                var progress = new Progress<string>(line =>
                {
                    Output.AppendText(line + Environment.NewLine);
                    Output.ScrollToEnd();
                });
                WingetResult result = await Task.Run(
                    () => WingetService.RunAsync(arguments, progress, operation.Token), operation.Token);
                StatusText.Text = result.ExitCode == 0
                    ? "Terminé."
                    : $"Terminé avec le code {result.ExitCode}. Consultez la sortie.";
                if (string.IsNullOrWhiteSpace(Output.Text)) Output.Text = "(aucune sortie)";
            }
            catch (OperationCanceledException)
            {
                StatusText.Text = "Opération annulée.";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Échec : " + ex.Message;
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
