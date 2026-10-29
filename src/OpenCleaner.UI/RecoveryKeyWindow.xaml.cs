using System.Windows;
using OpenCleaner.UI.Localization;

namespace OpenCleaner.UI;

public partial class RecoveryKeyWindow : Window
{
    private static string T(string key, params object?[] args) => LocalizationService.Instance.Get(key, args);

    private readonly bool _enterMode;

    public RecoveryKeyWindow(string? code, bool enterMode)
    {
        InitializeComponent();
        _enterMode = enterMode;
        if (enterMode)
        {
            Title = T("Recovery.UnlockTitle");
            HeaderText.Text = T("Recovery.EnterHeader");
            HelpText.Text = T("Recovery.EnterHelp");
            CopyBtn.Visibility = Visibility.Collapsed;
            OkBtn.Content = T("Recovery.UnlockButton");
        }
        else
        {
            HeaderText.Text = T("Recovery.KeepHeader");
            HelpText.Text = T("Recovery.KeepHelp");
            CodeBox.Text = code ?? string.Empty;
            CodeBox.IsReadOnly = true;
            OkBtn.Content = T("Recovery.KeepButton");
        }
    }

    public string Code => CodeBox.Text.Trim();

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(Code);
            MessageBox.Show(this, T("Recovery.Copied"), T("Recovery.CodeTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, T("Recovery.CopyFailed", ex.Message), T("Recovery.CodeTitle"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (_enterMode && string.IsNullOrWhiteSpace(Code))
        {
            MessageBox.Show(this, T("Recovery.EnterPrompt"), T("Recovery.CodeTitle"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
