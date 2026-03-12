using System.Windows;

namespace OpenCleaner.UI;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Velopack.VelopackApp.Build().Run();

        var app = new App();
        app.Run();
    }
}
