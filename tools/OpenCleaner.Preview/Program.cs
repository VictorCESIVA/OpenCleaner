using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenCleaner.UI;
using OpenCleaner.Contracts;

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
        var windows=new Window[]{main,new SettingsWindow(),new RestoreWindow(),new PrivacyWindow(),new DuplicateWindow(),new DiskSpaceWindow(),new DevCleanerWindow()};
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
        Console.WriteLine("Read-only WPF constructor and rendering smoke passed; no startup, scans, cleaning or scheduling invoked.");
        (App.Services as IDisposable)?.Dispose();
    }
    static void Render(Window window,string output,double width,double height)
    {
        var view=(FrameworkElement)window.Content;view.Width=width;view.Height=height;
        view.Measure(new Size(width,height));view.Arrange(new Rect(0,0,width,height));view.UpdateLayout();
        var target=new RenderTargetBitmap((int)width,(int)height,96,96,PixelFormats.Pbgra32);target.Render(view);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(target));using var stream=File.Create(output);encoder.Save(stream);
    }
}
