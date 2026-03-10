using Microsoft.Extensions.DependencyInjection;
using OpenCleaner.Contracts;
using OpenCleaner.Core.Security;
using OpenCleaner.Plugins.System;
using OpenCleaner.Plugins.System.Plugins;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace OpenCleaner.UI;

public partial class MainWindow : Window
{
    private ObservableCollection<CleanableItem> _foundItems = new();
    private ICleanerPlugin? _currentPlugin;

    public MainWindow()
    {
        InitializeComponent();
        FilesListView.ItemsSource = _foundItems;
    }

    private async void Analyze_Click(object sender, RoutedEventArgs e)
    {
        var selectedPlugin = ((ComboBoxItem)PluginComboBox.SelectedItem).Tag as string;
        _foundItems.Clear();
        AnalyzeBtn.IsEnabled = false;
        CleanBtn.IsEnabled = false;

        try
        {
            var fileGuardian = App.Services.GetRequiredService<IFileGuardian>();
            var registryGuardian = App.Services.GetRequiredService<IRegistryGuardian>();

            switch (selectedPlugin)
            {
                case "system":
                    StatusText.Text = "Analyse des fichiers temporaires...";
                    var tempLogger = App.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SystemTempPlugin>>();
                    _currentPlugin = new SystemTempPlugin(fileGuardian, tempLogger);
                    break;

                case "browser":
                    StatusText.Text = "Analyse des caches navigateurs...";
                    var chromeLogger = App.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ChromeCleanerPlugin>>();
                    _currentPlugin = new ChromeCleanerPlugin(fileGuardian, chromeLogger);
                    break;

                case "registry":
                    StatusText.Text = "Analyse du registre (mode expert)...";
                    var regLogger = App.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<RegistryCleanerPlugin>>();
                    _currentPlugin = new RegistryCleanerPlugin(registryGuardian, regLogger);
                    break;

                case "steam":
                    StatusText.Text = "Analyse de Steam...";
                    var steamLogger = App.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SteamCleanerPlugin>>();
                    _currentPlugin = new SteamCleanerPlugin(fileGuardian, steamLogger);
                    break;

                case "windowsupdate":
                    StatusText.Text = "Analyse de Windows Update...";
                    var wuLogger = App.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<WindowsUpdatePlugin>>();
                    _currentPlugin = new WindowsUpdatePlugin(fileGuardian, wuLogger);
                    break;

                case "office":
                    StatusText.Text = "Analyse de Microsoft Office...";
                    var officeLogger = App.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<OfficeCleanerPlugin>>();
                    _currentPlugin = new OfficeCleanerPlugin(fileGuardian, officeLogger);
                    break;

                case "thumbnails":
                    StatusText.Text = "Analyse du cache miniatures...";
                    var thumbLogger = App.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ThumbnailCachePlugin>>();
                    _currentPlugin = new ThumbnailCachePlugin(fileGuardian, thumbLogger);
                    break;
            }

            if (_currentPlugin == null) return;

            var items = await _currentPlugin.AnalyzeAsync();
            foreach (var item in items) _foundItems.Add(item);

            var totalSize = items.Sum(i => i.Size) / 1024 / 1024;
            StatusText.Text = $"✅ {items.Count} éléments trouvés" + (totalSize > 0 ? $" ({totalSize} Mo)" : "");
            CleanBtn.IsEnabled = items.Count > 0;
        }
        catch (System.Exception ex)
        {
            StatusText.Text = $"❌ Erreur: {ex.Message}";
        }

        AnalyzeBtn.IsEnabled = true;
    }

    private async void Clean_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPlugin == null || _foundItems.Count == 0) return;

        var selectedPlugin = ((ComboBoxItem)PluginComboBox.SelectedItem).Tag as string;
        if (selectedPlugin == "registry")
        {
            var warnResult = MessageBox.Show(
                "⚠️ Vous allez modifier le registre Windows.\n\n" +
                "Un backup sera créé automatiquement, mais cette opération est destinée aux utilisateurs avancés.\n\n" +
                "Continuer ?",
                "Attention - Mode Expert",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (warnResult != MessageBoxResult.Yes) return;
        }

        var result = MessageBox.Show(
            $"Supprimer {_foundItems.Count} éléments ?\nUn backup sera créé.",
            "Confirmation",
            MessageBoxButton.YesNo);

        if (result != MessageBoxResult.Yes) return;

        CleanBtn.IsEnabled = false;
        AnalyzeBtn.IsEnabled = false;
        ProgressBar.Visibility = Visibility.Visible;

        try
        {
            var backupManager = App.Services.GetRequiredService<IBackupManager>();
            var progress = new Progress<double>(p => ProgressBar.Value = p);

            var operationResult = await _currentPlugin.CleanAsync(_foundItems, backupManager, progress);

            if (operationResult.Success)
            {
                StatusText.Text = $"✅ {operationResult.Message}";
                _foundItems.Clear();
            }
            else
            {
                StatusText.Text = $"⚠️ {operationResult.Message}";
            }
        }
        catch (System.Exception ex)
        {
            StatusText.Text = $"❌ Erreur: {ex.Message}";
        }

        ProgressBar.Visibility = Visibility.Collapsed;
        ProgressBar.Value = 0;
        AnalyzeBtn.IsEnabled = true;
        CleanBtn.IsEnabled = false;
    }

    private void OpenRestore_Click(object sender, RoutedEventArgs e)
    {
        new RestoreWindow().ShowDialog();
    }
}
