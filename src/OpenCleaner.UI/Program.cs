using System.Windows;
using OpenCleaner.Core.Settings;
using OpenCleaner.UI.Localization;

namespace OpenCleaner.UI;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == ElevatedWinget.HelperArgument)
        {
            try { ElevatedWinget.RunHelperAsync(args[1]).GetAwaiter().GetResult(); }
            catch (Exception) { /* The parent reports a disconnected helper. */ }
            return;
        }
        if (args.Length == 2 && args[0] == ElevatedStartup.HelperArgument)
        {
            try { ElevatedStartup.RunHelperAsync(args[1]).GetAwaiter().GetResult(); }
            catch (Exception) { /* The parent reports a disconnected helper. */ }
            return;
        }
        Velopack.VelopackApp.Build().Run();
        AppPreferences preferences;
        try { preferences = PreferencesStore.Load(); }
        catch { preferences = new AppPreferences().Normalize(); }
        try { LocalizationService.Instance.SetLanguage(preferences.Language); }
        catch { /* Fall back to the default language. */ }
        StartupTrace.Mark("prefs-loaded");
        App.SetStartupPreferences(preferences);
        var userId = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        using var mutex = new Mutex(false, @"Global\OpenCleaner-" + userId);
        bool ownsMutex;
        try { ownsMutex = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { ownsMutex = true; }
        StartupTrace.Mark(ownsMutex ? "mutex-acquired" : "mutex-busy");
        if (!ownsMutex)
        {
            if (!args.Contains("--background"))
                MessageBox.Show(LocalizationService.Instance.Get("App.AlreadyRunning"), "OpenCleaner");
            else Environment.ExitCode = 1;
            return;
        }
        try
        {
            var app = new App();
            app.InitializeComponent();
            app.Run();
        }
        finally { mutex.ReleaseMutex(); }
    }
}
