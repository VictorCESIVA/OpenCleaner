using System.Windows;

namespace OpenCleaner.UI;

public partial class RecoveryKeyWindow : Window
{
    private readonly bool _enterMode;

    public RecoveryKeyWindow(string? code, bool enterMode)
    {
        InitializeComponent();
        _enterMode = enterMode;
        if (enterMode)
        {
            Title = "Déverrouiller les sauvegardes";
            HeaderText.Text = "Saisissez votre code de récupération";
            HelpText.Text = "Le code a été affiché lors de sa création. Il est insensible à la casse et les tirets sont facultatifs.";
            CopyBtn.Visibility = Visibility.Collapsed;
            OkBtn.Content = "Déverrouiller";
        }
        else
        {
            HeaderText.Text = "Conservez ce code de récupération";
            HelpText.Text = "Il permet de retrouver l'accès aux sauvegardes si la clé protégée par Windows est perdue. Stockez-le hors de cet ordinateur ; il ne sera plus affiché.";
            CodeBox.Text = code ?? string.Empty;
            CodeBox.IsReadOnly = true;
            OkBtn.Content = "J'ai conservé le code";
        }
    }

    public string Code => CodeBox.Text.Trim();

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(Code);
            MessageBox.Show(this, "Code copié dans le presse-papiers.", "Code de récupération",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Impossible de copier le code : {ex.Message}", "Code de récupération",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (_enterMode && string.IsNullOrWhiteSpace(Code))
        {
            MessageBox.Show(this, "Saisissez le code de récupération.", "Code de récupération",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
