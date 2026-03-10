using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenCleaner.Contracts;
using OpenCleaner.Core.Security;
using OpenCleaner.Plugins.System;
using OpenCleaner.Plugins.System.Plugins;
using System.Windows;
namespace OpenCleaner.UI;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddDebug());
        services.AddSingleton<IBackupManager, BackupManager>();
        services.AddSingleton<IFileGuardian, FileGuardian>();
        services.AddSingleton<IRegistryGuardian, RegistryGuardian>();
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<SystemTempPlugin>>(sp =>
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>().CreateLogger<SystemTempPlugin>());
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<ChromeCleanerPlugin>>(sp =>
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>().CreateLogger<ChromeCleanerPlugin>());
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<RegistryCleanerPlugin>>(sp =>
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>().CreateLogger<RegistryCleanerPlugin>());
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<SteamCleanerPlugin>>(sp =>
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>().CreateLogger<SteamCleanerPlugin>());
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<WindowsUpdatePlugin>>(sp =>
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>().CreateLogger<WindowsUpdatePlugin>());
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<OfficeCleanerPlugin>>(sp =>
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>().CreateLogger<OfficeCleanerPlugin>());
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<ThumbnailCachePlugin>>(sp =>
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>().CreateLogger<ThumbnailCachePlugin>());
        services.AddTransient<SystemTempPlugin>();
        services.AddTransient<ChromeCleanerPlugin>();
        services.AddTransient<RegistryCleanerPlugin>();
        services.AddTransient<SteamCleanerPlugin>();
        services.AddTransient<WindowsUpdatePlugin>();
        services.AddTransient<OfficeCleanerPlugin>();
        services.AddTransient<ThumbnailCachePlugin>();
        Services = services.BuildServiceProvider();

        new MainWindow().Show();
    }
}
