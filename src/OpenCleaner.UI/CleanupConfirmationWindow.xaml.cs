using System.Windows;

namespace OpenCleaner.UI;

public partial class CleanupConfirmationWindow : Window
{
    internal bool CreateBackup { get; private set; } = true;

    internal CleanupConfirmationWindow(string context)
    {
        InitializeComponent();
        ContextText.Text = context;
    }

    internal static bool? Confirm(Window owner, string context)
    {
        var dialog = new CleanupConfirmationWindow(context) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.CreateBackup : null;
    }

    private void BackupAndDelete_Click(object sender, RoutedEventArgs e)
    {
        CreateBackup = true;
        DialogResult = true;
    }

    private void DeleteWithoutBackup_Click(object sender, RoutedEventArgs e)
    {
        CreateBackup = false;
        DialogResult = true;
    }
}
