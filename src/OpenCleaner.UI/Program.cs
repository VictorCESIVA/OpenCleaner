using System.Windows;

namespace OpenCleaner.UI;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Velopack.VelopackApp.Build().Run();
        var userId = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        using var mutex = new Mutex(false, @"Global\OpenCleaner-" + userId);
        bool ownsMutex;
        try { ownsMutex = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { ownsMutex = true; }
        if (!ownsMutex)
        {
            if (!args.Contains("--background"))
                MessageBox.Show("OpenCleaner est déjà ouvert. Terminez l'opération dans la fenêtre existante.", "OpenCleaner");
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
