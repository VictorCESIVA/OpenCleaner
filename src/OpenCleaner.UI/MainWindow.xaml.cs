using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using OpenCleaner.Contracts;
using OpenCleaner.Core.Security;
using OpenCleaner.Plugins.System.Plugins;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace OpenCleaner.UI;

// ─────────────────────────────────────────────────────────────────────────────
//  MAINWINDOW
// ─────────────────────────────────────────────────────────────────────────────

public partial class MainWindow : Window
{
    private readonly ObservableCollection<CleanableItem> _foundItems = new();
    private ICleanerPlugin? _currentPlugin;
    private string _selectedPluginTag = "system";
    private bool _isDarkTheme = true;

    // Map tag → (titre, description)
    private static readonly Dictionary<string, (string Title, string Desc)> PluginMeta = new()
    {
        ["system"]        = ("Fichiers temporaires système",    "Analyse %TEMP%, Windows\\Temp et Prefetch"),
        ["windowsupdate"] = ("Windows Update",                  "Cache SoftwareDistribution (fichiers ≥ 3 jours)"),
        ["thumbnails"]    = ("Cache miniatures Windows",        "Fichiers thumbcache_*.db de l'Explorateur"),
        ["browser"]       = ("Navigateurs Chrome / Edge / Brave", "Cache, cookies, Service Workers"),
        ["steam"]         = ("Steam",                           "Cache de téléchargements et workshops"),
        ["office"]        = ("Microsoft Office",                "Cache Word/Excel et fichiers AutoRecover ≥ 7 jours"),
        ["registry"]      = ("Registre Windows",                "Mode expert — analyse les clés orphelines"),
    };

    public MainWindow()
    {
        InitializeComponent();
        FilesListView.ItemsSource = _foundItems;
        ApplyTheme(DetectWindowsTheme());
        UpdateSidebarSelection("system");
        UpdatePluginHeader("system");
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  SIDEBAR  NAVIGATION
    // ──────────────────────────────────────────────────────────────────────────

    private void SidebarPlugin_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tag) return;

        _selectedPluginTag = tag;
        _foundItems.Clear();
        UpdateSidebarSelection(tag);
        UpdatePluginHeader(tag);
        ResetStats();
        CleanBtn.IsEnabled = false;
        EmptyState.Visibility = Visibility.Visible;
        ActionStatus.Text = "";
    }

    private void UpdateSidebarSelection(string activeTag)
    {
        foreach (var child in SidebarPanel.Children)
        {
            if (child is not Button btn) continue;
            var tag = btn.Tag as string ?? "";
            btn.Style = tag == activeTag
                ? (Style)FindResource("SidebarBtnActiveStyle")
                : (Style)FindResource("SidebarBtnStyle");
        }
    }

    private void UpdatePluginHeader(string tag)
    {
        if (!PluginMeta.TryGetValue(tag, out var meta)) return;
        PluginTitle.Text       = meta.Title;
        PluginDescription.Text = meta.Desc;
        StatusText.Text        = "Prêt";
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  ANALYSER
    // ──────────────────────────────────────────────────────────────────────────

    // ──────────────────────────────────────────────────────────────────────────
    //  OVERLAY CHARGEMENT
    // ──────────────────────────────────────────────────────────────────────────

    private void ShowLoading(string text, string sub = "Scan des fichiers, merci de patienter")
    {
        LoadingText.Text    = text;
        LoadingSubText.Text = sub;
        LoadingOverlay.Visibility = Visibility.Visible;

        // Animation code-behind — évite les problèmes de namescope du Storyboard XAML
        var spin = new DoubleAnimation
        {
            From            = 0,
            To              = 360,
            Duration        = new Duration(TimeSpan.FromSeconds(1)),
            RepeatBehavior  = RepeatBehavior.Forever,
            EasingFunction  = null
        };
        SpinnerRotate.BeginAnimation(RotateTransform.AngleProperty, spin);
    }

    private void HideLoading()
    {
        // Stoppe l'animation proprement
        SpinnerRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        LoadingOverlay.Visibility   = Visibility.Collapsed;
        ProgressBar.IsIndeterminate = false;
        ProgressBar.Visibility      = Visibility.Collapsed;
    }

    private async void Analyze_Click(object sender, RoutedEventArgs e)
    {
        _foundItems.Clear();
        EmptyState.Visibility = Visibility.Collapsed;
        AnalyzeBtn.IsEnabled  = false;
        CleanBtn.IsEnabled    = false;
        ResetStats();

        ShowLoading("Analyse en cours…");
        ProgressBar.Visibility      = Visibility.Visible;
        ProgressBar.IsIndeterminate = true;
        StatusText.Text = "Analyse en cours…";

        // Laisser WPF rendre l'overlay AVANT de bloquer le thread sur le scan
        await Task.Yield();

        // ══ Vérification spécifique plugin navigateur ══
        if (_selectedPluginTag == "browser")
        {
            var running = new[]
            {
                (Proc: "chrome",  Label: "Google Chrome"),
                (Proc: "msedge",  Label: "Microsoft Edge"),
                (Proc: "brave",   Label: "Brave"),
            }
            .Where(b => Process.GetProcessesByName(b.Proc).Any())
            .Select(b => b.Label)
            .ToList();

            if (running.Count > 0)
            {
                HideLoading();
                AnalyzeBtn.IsEnabled = true;
                var names = string.Join(", ", running);
                var body  = names + (running.Count > 1 ? " sont ouverts." : " est ouvert.")
                          + "\n\nL'analyse des caches nécessite que ces navigateurs soient fermés "
                          + "pour éviter des erreurs d'accès aux fichiers verrouillés.\n\n"
                          + "Fermez " + names + " puis relancez l'analyse.";
                MessageBox.Show(body, "Navigateurs ouverts", MessageBoxButton.OK, MessageBoxImage.Warning);
                StatusText.Text = "Fermez " + names + " avant d'analyser.";
                return;
            }
        }


        try
        {
            var fileGuardian     = App.Services.GetRequiredService<IFileGuardian>();
            var registryGuardian = App.Services.GetRequiredService<IRegistryGuardian>();

            _currentPlugin = _selectedPluginTag switch
            {
                "system"        => new SystemTempPlugin(fileGuardian,
                                        GetLogger<SystemTempPlugin>()),
                "windowsupdate" => new WindowsUpdatePlugin(fileGuardian,
                                        GetLogger<WindowsUpdatePlugin>()),
                "thumbnails"    => new ThumbnailCachePlugin(fileGuardian,
                                        GetLogger<ThumbnailCachePlugin>()),
                "browser"       => new ChromeCleanerPlugin(fileGuardian,
                                        GetLogger<ChromeCleanerPlugin>()),
                "steam"         => new SteamCleanerPlugin(fileGuardian,
                                        GetLogger<SteamCleanerPlugin>()),
                "office"        => new OfficeCleanerPlugin(fileGuardian,
                                        GetLogger<OfficeCleanerPlugin>()),
                "registry"      => new RegistryCleanerPlugin(registryGuardian,
                                        GetLogger<RegistryCleanerPlugin>()),
                _               => null
            };

            if (_currentPlugin == null) return;

            var progress = new Progress<double>(p =>
            {
                ProgressBar.IsIndeterminate = false;
                ProgressBar.Value = p * 100;
            });

            var items = await _currentPlugin.AnalyzeAsync(progress);

            // Fade-in résultats
            foreach (var item in items) _foundItems.Add(item);

            if (_foundItems.Count > 0)
            {
                EmptyState.Visibility = Visibility.Collapsed;
                var story = (Storyboard)FindResource("FadeInStory");
                story.Begin();
            }
            else
            {
                EmptyState.Visibility = Visibility.Visible;
            }

            UpdateStats(items);

            long totalMb = items.Sum(i => i.Size) / 1024 / 1024;
            StatusText.Text = $"✅ {items.Count} éléments — {totalMb} Mo";
            CleanBtn.IsEnabled = items.Count > 0;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"❌ {ex.Message}";
            EmptyState.Visibility = Visibility.Visible;
        }
        finally
        {
            HideLoading();
            AnalyzeBtn.IsEnabled = true;
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  NETTOYER
    // ──────────────────────────────────────────────────────────────────────────

    private async void Clean_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPlugin == null || _foundItems.Count == 0) return;

        if (_selectedPluginTag == "registry")
        {
            var warn = MessageBox.Show(
                "⚠️ Vous allez modifier le registre Windows.\n\nUn backup sera créé automatiquement, mais cette opération est destinée aux utilisateurs avancés.\n\nContinuer ?",
                "Attention — Mode Expert",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (warn != MessageBoxResult.Yes) return;
        }

        var confirm = MessageBox.Show(
            $"Supprimer {_foundItems.Count} élément(s) ?\nUn backup sera créé automatiquement.",
            "Confirmation",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        CleanBtn.IsEnabled   = false;
        AnalyzeBtn.IsEnabled = false;
        ShowLoading("Nettoyage en cours…", "Suppression sécurisée avec backup");
        ProgressBar.Visibility      = Visibility.Visible;
        ProgressBar.IsIndeterminate = true;
        StatusText.Text = "Nettoyage en cours…";

        // Laisser WPF rendre l'overlay avant les opérations I/O
        await Task.Yield();

        try
        {
            var backupManager = App.Services.GetRequiredService<IBackupManager>();
            var progress = new Progress<double>(p =>
            {
                ProgressBar.IsIndeterminate = false;
                ProgressBar.Value = p * 100;
            });

            var result = await _currentPlugin.CleanAsync(_foundItems, backupManager, progress);

            ActionStatus.Text = result.Success
                ? $"✅ {result.Message}"
                : $"⚠️ {result.Message}";

            StatusText.Text = result.Success ? "Nettoyage terminé" : "Partiel";

            if (result.Success)
            {
                _foundItems.Clear();
                EmptyState.Visibility = Visibility.Visible;
                ResetStats();
            }
        }
        catch (Exception ex)
        {
            StatusText.Text   = "❌ Erreur";
            ActionStatus.Text = ex.Message;
        }
        finally
        {
            HideLoading();
            AnalyzeBtn.IsEnabled = true;
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  STATS CARD
    // ──────────────────────────────────────────────────────────────────────────

    private void UpdateStats(IReadOnlyList<CleanableItem> items)
    {
        StatFiles.Text = items.Count.ToString();

        long bytes = items.Sum(i => i.Size);
        StatSize.Text = bytes switch
        {
            >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:0.#} Go",
            >= 1_048_576     => $"{bytes / 1_048_576.0:0.#} Mo",
            >= 1_024         => $"{bytes / 1_024.0:0.#} Ko",
            _                => $"{bytes} o"
        };

        // Risque le plus élevé présent
        var maxRisk = items.Count > 0
            ? items.Max(i => i.RiskLevel)
            : RiskLevel.Safe;

        StatRisk.Text = maxRisk switch
        {
            RiskLevel.Safe        => "🟢 Safe",
            RiskLevel.Recommended => "🟠 Warn",
            RiskLevel.ExpertOnly  => "🔴 Expert",
            _                     => "—"
        };
        StatRisk.Foreground = maxRisk switch
        {
            RiskLevel.Safe        => new SolidColorBrush(Color.FromRgb(0x10, 0x7C, 0x10)),
            RiskLevel.Recommended => new SolidColorBrush(Color.FromRgb(0xFF, 0x8C, 0x00)),
            RiskLevel.ExpertOnly  => new SolidColorBrush(Color.FromRgb(0xD1, 0x34, 0x38)),
            _                     => (SolidColorBrush)FindResource("TextSecond")
        };
    }

    private void ResetStats()
    {
        StatFiles.Text = "—";
        StatSize.Text  = "—";
        StatRisk.Text  = "—";
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  THÈME
    // ──────────────────────────────────────────────────────────────────────────

    private bool DetectWindowsTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var val = key?.GetValue("AppsUseLightTheme");
            return val is int i && i == 0; // 0 = dark
        }
        catch { return true; } // dark par défaut
    }

    private void ApplyTheme(bool dark)
    {
        _isDarkTheme = dark;
        // On écrit dans this.Resources (Window scope) qui a priorité
        // sur Application.Current.Resources pour les DynamicResource
        // définis dans Window.Resources.
        var res = this.Resources;

        if (dark)
        {
            res["BgMain"]      = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1C));
            res["BgSidebar"]   = new SolidColorBrush(Color.FromRgb(0x16, 0x16, 0x16));
            res["BgCard"]      = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x25));
            res["BgHover"]     = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x2D));
            res["TextPrimary"] = new SolidColorBrush(Colors.White);
            res["TextSecond"]  = new SolidColorBrush(Color.FromRgb(0xAB, 0xAB, 0xAB));
            res["Border"]      = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
            ThemeIcon.Text     = "☀️  Thème clair";
        }
        else
        {
            res["BgMain"]      = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));
            res["BgSidebar"]   = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));
            res["BgCard"]      = new SolidColorBrush(Colors.White);
            res["BgHover"]     = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0));
            res["TextPrimary"] = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1C));
            res["TextSecond"]  = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60));
            res["Border"]      = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));
            ThemeIcon.Text     = "🌙  Thème sombre";
        }
    }

    private void ThemeToggle_Click(object sender, RoutedEventArgs e)
        => ApplyTheme(!_isDarkTheme);

    // ──────────────────────────────────────────────────────────────────────────
    //  UTILITAIRES
    // ──────────────────────────────────────────────────────────────────────────

    private void OpenRestore_Click(object sender, RoutedEventArgs e)
        => new RestoreWindow().ShowDialog();

    private void OpenDuplicates_Click(object sender, RoutedEventArgs e)
        => new DuplicateWindow { Owner = this }.Show();

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
        => new SettingsWindow { Owner = this }.ShowDialog();

    private static Microsoft.Extensions.Logging.ILogger<T> GetLogger<T>()
        => App.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<T>>();
}
