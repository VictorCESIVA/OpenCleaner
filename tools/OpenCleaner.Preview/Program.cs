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
        var windows=new Window[]{main,new SettingsWindow(),new RestoreWindow(),new PrivacyWindow(),new DuplicateWindow(),new DiskSpaceWindow(),new DevCleanerWindow(),new RecoveryKeyWindow("OC-EXEMPLE-CODE-RECUPERATION",false),new HealthWindow(),uninstaller,startup,new UpdatesWindow(),new ShredderWindow()};
        foreach(var window in windows)
        {
            var name=window.GetType().Name;
            Render(window,Path.Combine(output,name+".png"),window.Width,window.Height-36);
            Render(window,Path.Combine(output,name+"-minimum.png"),window.MinWidth,window.MinHeight-36);
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
        typeof(MainWindow).GetMethod("ApplyTheme",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(main,new object[]{false});
        Render(main,Path.Combine(output,"MainWindow-light.png"),main.Width,main.Height-36);
        foreach(var window in windows.Skip(1)) Render(window,Path.Combine(output,window.GetType().Name+"-light.png"),window.Width,window.Height-36);
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
        Render(uninstaller,Path.Combine(output,"UninstallerWindow-loading.png"),uninstaller.Width,uninstaller.Height-36);
        ((StackPanel)uninstaller.FindName("LoadingState")).Visibility=Visibility.Collapsed;
        typeof(UninstallerWindow).GetMethod("UpdateEmptyState",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(uninstaller,null);
        ((TextBlock)uninstaller.FindName("CountText")).Text="0 application";
        ((TextBlock)uninstaller.FindName("StatusText")).Text="Aucune application détectée.";
        Render(uninstaller,Path.Combine(output,"UninstallerWindow-empty.png"),uninstaller.Width,uninstaller.Height-36);

        ((ListBox)startup.FindName("StartupList")).ItemsSource=null;
        ((StackPanel)startup.FindName("LoadingState")).Visibility=Visibility.Visible;
        ((TextBlock)startup.FindName("StatusText")).Text="Lecture des éléments de démarrage…";
        Render(startup,Path.Combine(output,"StartupWindow-loading.png"),startup.Width,startup.Height-36);
        ((StackPanel)startup.FindName("LoadingState")).Visibility=Visibility.Collapsed;
        typeof(StartupWindow).GetMethod("UpdateEmptyState",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(startup,null);
        ((TextBlock)startup.FindName("CountText")).Text="0 élément";
        ((TextBlock)startup.FindName("StatusText")).Text="Aucun élément de démarrage détecté.";
        Render(startup,Path.Combine(output,"StartupWindow-empty.png"),startup.Width,startup.Height-36);
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

    static void Render(Window window,string output,double width,double height)
    {
        var view=(FrameworkElement)window.Content;view.Width=width;view.Height=height;
        view.Measure(new Size(width,height));view.Arrange(new Rect(0,0,width,height));view.UpdateLayout();
        var target=new RenderTargetBitmap((int)width,(int)height,96,96,PixelFormats.Pbgra32);target.Render(view);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(target));using var stream=File.Create(output);encoder.Save(stream);
    }
}
