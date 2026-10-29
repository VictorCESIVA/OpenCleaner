using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenCleaner.UI;
using OpenCleaner.Contracts;
using OpenCleaner.Core.Tools;

internal class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        var output = args.Length > 0 ? args[0] : Path.Combine("artifacts", "ui-preview");
        Directory.CreateDirectory(output);
        var app=new App();app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
        typeof(App).GetMethod("BuildServices",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,null);
        var main=new MainWindow();
        var uninstaller=new UninstallerWindow();
        SeedUninstaller(uninstaller);
        var startup=new StartupWindow();
        SeedStartup(startup);
        var health=new HealthWindow();
        health.Populate(new SystemHealthInfo("Windows 11", "24H2", "26100", "Processeur exemple", 8,
            16UL * 1024 * 1024 * 1024, 6UL * 1024 * 1024 * 1024, TimeSpan.FromHours(34), ".NET",
            [new SystemDiskInfo("C:\\", "NTFS", 500L * 1024 * 1024 * 1024, 42L * 1024 * 1024 * 1024)]));
        if (!((TextBlock)health.FindName("MemoryText")).Text.Contains("utilisés") ||
            !((StackPanel)health.FindName("DisksPanel")).Children.OfType<StackPanel>().Single().Children.OfType<TextBlock>().Any(t => t.Text.Contains("Espace faible")))
            throw new InvalidOperationException("Health indicators are incorrect");
        var unavailable = new HealthWindow();
        unavailable.Populate(new SystemHealthInfo("Windows", "", "", "", 0, 0, 0, TimeSpan.Zero, "", [new SystemDiskInfo("C:\\", "", 0, 0)]));
        if (((TextBlock)unavailable.FindName("MemoryText")).Text != "Indisponible" ||
            !((TextBlock)unavailable.FindName("AdviceText")).Text.Contains("indisponible"))
            throw new InvalidOperationException("Unavailable health indicators are incorrect");
        var windows=new FrameworkElement[]{main,new SettingsWindow(),new RestoreWindow(),new PrivacyWindow(),new DuplicateWindow(),new DiskSpaceWindow(),new DevCleanerWindow(),new RecoveryKeyWindow("OC-EXEMPLE-CODE-RECUPERATION",false),health,uninstaller,startup,new UpdatesWindow(),new ShredderWindow()};
        foreach(var window in windows)
        {
            var name=window.GetType().Name;
            Render(window,Path.Combine(output,name+".png"),window is Window w ? w.Width : 1048,window is Window h ? h.Height-36 : 824);
            Render(window,Path.Combine(output,name+"-minimum.png"),window is Window mw ? mw.MinWidth : 808,window is Window mh ? mh.MinHeight-36 : 704);
            Console.WriteLine(name+" instantiated and rendered");
        }
        ((Border)main.FindName("BrowserOptionsPanel")).Visibility=Visibility.Visible;
        ((TextBlock)main.FindName("PluginTitle")).Text="Navigateurs Chrome / Edge / Brave";
        Render(main,Path.Combine(output,"MainWindow-browser-minimum.png"),main.MinWidth,main.MinHeight-36);
        ((Border)main.FindName("BrowserOptionsPanel")).Visibility=Visibility.Collapsed;
        ((Border)main.FindName("LoadingOverlay")).Visibility=Visibility.Visible;
        Render(main,Path.Combine(output,"MainWindow-loading.png"),main.Width,main.Height-36);
        ((Border)main.FindName("LoadingOverlay")).Visibility=Visibility.Collapsed;
        ((TextBlock)main.FindName("PluginTitle")).Text="Fichiers temporaires système";
        var files=(ListView)main.FindName("FilesListView");
        files.ItemsSource=new[]{
            new CleanableItem("preview-safe",@"C:\Exemple\Temp\cache.tmp",1024*512,"Fichier temporaire",RiskLevel.Safe,ItemType.File),
            new CleanableItem("preview-recommended",@"C:\Exemple\Navigateur\Cookies",1024*24,"Cookies du navigateur",RiskLevel.Recommended,ItemType.File),
            new CleanableItem("preview-expert",@"HKCU\Software\Exemple",0,"Clé à vérifier",RiskLevel.ExpertOnly,ItemType.RegistryKey)};
        ((StackPanel)main.FindName("EmptyState")).Visibility=Visibility.Collapsed;
        Render(main,Path.Combine(output,"MainWindow-example-data.png"),main.Width,main.Height-36);
        main.ShowTool("health", (Button)main.FindName("HealthNav"));
        var integratedHealth = (HealthWindow)((ContentControl)main.FindName("ToolContent")).Content;
        if (Window.GetWindow(integratedHealth) != main) throw new InvalidOperationException("Tool owner is not the main window");
        integratedHealth.Populate(new SystemHealthInfo("Windows 11", "24H2", "26100", "Processeur exemple", 8,
            16UL * 1024 * 1024 * 1024, 6UL * 1024 * 1024 * 1024, TimeSpan.FromHours(34), ".NET",
            [new SystemDiskInfo("C:\\", "NTFS", 500L * 1024 * 1024 * 1024, 42L * 1024 * 1024 * 1024)]));
        ((TextBlock)integratedHealth.FindName("RecycleText")).Text = "Corbeille : 18 éléments • 120 Mo";
        Render(main,Path.Combine(output,"MainWindow-health.png"),main.Width,main.Height-36);
        Render(main,Path.Combine(output,"MainWindow-health-minimum.png"),main.MinWidth,main.MinHeight-36);
        main.ShowCleaner("system");
        typeof(MainWindow).GetMethod("ApplyTheme",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(main,new object[]{false});
        Render(main,Path.Combine(output,"MainWindow-light.png"),main.Width,main.Height-36);
        foreach(var window in windows.Skip(1)) Render(window,Path.Combine(output,window.GetType().Name+"-light.png"),window is Window w ? w.Width : 1048,window is Window h ? h.Height-36 : 824);
        int windowCount = app.Windows.Count;
        foreach (string key in new[] { "health", "disk", "duplicates", "privacy", "dev", "restore", "updates", "uninstall", "startup", "shredder", "settings" })
        {
            main.ShowTool(key);
            if (((ContentControl)main.FindName("ToolContent")).Content is not UserControl page || Window.GetWindow(page) != main)
                throw new InvalidOperationException($"Navigation failed: {key}");
        }
        if (app.Windows.Count != windowCount) throw new InvalidOperationException("Navigation opened another window");
        var toolContent = (ContentControl)main.FindName("ToolContent");
        var settingsView = toolContent.Content;
        if (!UiOperation.TryBegin(main, out var running)) throw new InvalidOperationException("Could not start navigation guard");
        using (running)
        {
            if (((Button)main.FindName("ToolCancelBtn")).Visibility != Visibility.Visible)
                throw new InvalidOperationException("Tool cancellation is unavailable");
            main.ShowTool("health");
        }
        if (!ReferenceEquals(settingsView, toolContent.Content)) throw new InvalidOperationException("Navigation allowed during operation");
        main.ShowTool("health");
        if (!ReferenceEquals(integratedHealth, toolContent.Content)) throw new InvalidOperationException("Tool state was not preserved");
        main.ShowCleaner("system");
        if (((ScrollViewer)main.FindName("DashboardScroll")).Visibility != Visibility.Visible)
            throw new InvalidOperationException("Cleaner navigation failed");
        RenderToolStates(uninstaller,startup,output);
        Console.WriteLine("Read-only WPF constructor and rendering smoke passed; no startup, scans, cleaning or scheduling invoked.");
        (App.Services as IDisposable)?.Dispose();
    }
    static void SeedUninstaller(UninstallerWindow window)
    {
        var rows=InstalledProgramService.GetInstalledPrograms().Take(14)
            .Select(program=>new InstalledProgramRow(program)).ToList();
        Task.Run(()=>Task.WhenAll(rows.Select(row=>row.LoadIconAsync()))).GetAwaiter().GetResult();

        var list=(ListBox)window.FindName("ProgramsList");
        list.ItemsSource=rows;
        int index=rows.FindIndex(row=>row.CanUninstall);
        list.SelectedIndex=index>=0?index:0;
        ((TextBlock)window.FindName("CountText")).Text=rows.Count==1?"1 application":$"{rows.Count:N0} applications";
        ((TextBlock)window.FindName("StatusText")).Text="Sélectionnez une application pour lancer son désinstalleur.";
    }

    static void RenderToolStates(UninstallerWindow uninstaller,StartupWindow startup,string output)
    {
        ((ListBox)uninstaller.FindName("ProgramsList")).ItemsSource=null;
        ((StackPanel)uninstaller.FindName("LoadingState")).Visibility=Visibility.Visible;
        ((TextBlock)uninstaller.FindName("StatusText")).Text="Lecture des applications installées…";
        Render(uninstaller,Path.Combine(output,"UninstallerWindow-loading.png"),1048,824);
        ((StackPanel)uninstaller.FindName("LoadingState")).Visibility=Visibility.Collapsed;
        typeof(UninstallerWindow).GetMethod("UpdateEmptyState",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(uninstaller,null);
        ((TextBlock)uninstaller.FindName("CountText")).Text="0 application";
        ((TextBlock)uninstaller.FindName("StatusText")).Text="Aucune application détectée.";
        Render(uninstaller,Path.Combine(output,"UninstallerWindow-empty.png"),1048,824);

        ((ListBox)startup.FindName("StartupList")).ItemsSource=null;
        ((StackPanel)startup.FindName("LoadingState")).Visibility=Visibility.Visible;
        ((TextBlock)startup.FindName("StatusText")).Text="Lecture des éléments de démarrage…";
        Render(startup,Path.Combine(output,"StartupWindow-loading.png"),1048,824);
        ((StackPanel)startup.FindName("LoadingState")).Visibility=Visibility.Collapsed;
        typeof(StartupWindow).GetMethod("UpdateEmptyState",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(startup,null);
        ((TextBlock)startup.FindName("CountText")).Text="0 élément";
        ((TextBlock)startup.FindName("StatusText")).Text="Aucun élément de démarrage détecté.";
        Render(startup,Path.Combine(output,"StartupWindow-empty.png"),1048,824);
    }

    static void SeedStartup(StartupWindow window)
    {
        var rows=StartupItemService.GetItems().Take(14)
            .Select(item=>new StartupItemRow(item)).ToList();
        Task.Run(()=>Task.WhenAll(rows.Select(row=>row.LoadIconAsync()))).GetAwaiter().GetResult();
        var list=(ListBox)window.FindName("StartupList");
        list.ItemsSource=rows;
        list.SelectedIndex=rows.Count>0?0:-1;
        ((TextBlock)window.FindName("CountText")).Text=rows.Count==1?"1 élément":$"{rows.Count:N0} éléments";
        ((TextBlock)window.FindName("StatusText")).Text="Sélectionnez un élément pour l'activer ou le désactiver.";
    }

    static void Render(FrameworkElement window,string output,double width,double height)
    {
        var view=window is Window shell ? (FrameworkElement)shell.Content : window;view.Width=width;view.Height=height;
        if (view is UserControl control)
        {
            control.SetResourceReference(Control.ForegroundProperty, "TextPrimary");
            control.SetResourceReference(Control.BackgroundProperty, "BgMain");
        }
        view.Measure(new Size(width,height));view.Arrange(new Rect(0,0,width,height));view.UpdateLayout();
        var target=new RenderTargetBitmap((int)width,(int)height,96,96,PixelFormats.Pbgra32);target.Render(view);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(target));using var stream=File.Create(output);encoder.Save(stream);
    }
}
