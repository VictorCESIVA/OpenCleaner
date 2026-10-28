using System.Windows;
using OpenCleaner.Core.Tools;

namespace OpenCleaner.UI;

public partial class UpdatesWindow : Window
{
    private const string CheckArguments =
        "upgrade --accept-source-agreements --disable-interactivity";

    public UpdatesWindow() => InitializeComponent();

    private async void Check_Click(object sender, RoutedEventArgs e) => await RunAsync(upgrade: false);

    private async void Upgrade_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this,
                "Mettre à jour les applications dont winget connaît la version ?\n\nWindows demandera une autorisation administrateur. Certaines applications peuvent être fermées ou nécessiter un redémarrage.",
                "Mises à jour", MessageBoxButton.YesNo, MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes) return;
        await RunAsync(upgrade: true);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => UiOperation.CancelCurrent();

    private async Task RunAsync(bool upgrade)
    {
        if (!UiOperation.TryBegin(this, out var operation)) return;
        using (operation)
        {
            SetBusy(true);
            Output.Text = "";
            OutputPlaceholder.Visibility = Visibility.Collapsed;
            StatusText.Text = upgrade ? "En attente de l'autorisation administrateur…" : "Recherche des mises à jour…";
            try
            {
                var progress = operation.CreateProgress<string>(line =>
                {
                    StatusText.Text = upgrade ? "Mise à jour des applications en cours…" : "Recherche des mises à jour…";
                    Output.AppendText(line + Environment.NewLine);
                    Output.ScrollToEnd();
                });
                WingetResult result = await Task.Run(() => upgrade
                    ? ElevatedWinget.RunAsync(progress, operation.Token)
                    : WingetService.RunAsync(CheckArguments, progress, operation.Token), operation.Token);
                StatusText.Text = result.ExitCode == 0
                    ? "Terminé."
                    : $"Terminé avec le code {result.ExitCode}. Consultez la sortie.";
                if (string.IsNullOrWhiteSpace(Output.Text)) Output.Text = "(aucune sortie)";
            }
            catch (OperationCanceledException)
            {
                StatusText.Text = "Opération annulée.";
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                StatusText.Text = "Autorisation administrateur refusée : aucune mise à jour n'a été lancée.";
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 2)
            {
                StatusText.Text = "winget est introuvable. Installez ou réparez le Programme d'installation d'applications Windows.";
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
