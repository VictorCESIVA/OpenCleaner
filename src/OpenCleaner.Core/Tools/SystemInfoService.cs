using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace OpenCleaner.Core.Tools;

public sealed record SystemDiskInfo(string Name, string FileSystem, long TotalBytes, long FreeBytes);
public sealed record SystemHealthInfo(
    string OsName,
    string OsVersion,
    string Build,
    string CpuName,
    int LogicalProcessors,
    ulong TotalMemoryBytes,
    ulong AvailableMemoryBytes,
    TimeSpan Uptime,
    string RuntimeVersion,
    IReadOnlyList<SystemDiskInfo> Disks);

[SupportedOSPlatform("windows")]
public static class SystemInfoService
{
    public static SystemHealthInfo Get()
    {
        string product = "Windows";
        string display = "";
        string build = Environment.OSVersion.Version.Build.ToString();
        using (RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
        {
            if (key != null)
            {
                product = key.GetValue("ProductName")?.ToString() ?? product;
                display = key.GetValue("DisplayVersion")?.ToString() ?? "";
                build = key.GetValue("CurrentBuildNumber")?.ToString() ?? build;
            }
        }
        if (int.TryParse(build, out int buildNumber) && buildNumber >= 22000)
            product = "Windows 11";

        string cpu = "Processeur inconnu";
        using (RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
            cpu = key?.GetValue("ProcessorNameString")?.ToString()?.Trim() ?? cpu;

        ulong totalMemory = 0;
        ulong availableMemory = 0;
        var memory = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (GlobalMemoryStatusEx(ref memory))
        {
            totalMemory = memory.TotalPhys;
            availableMemory = memory.AvailPhys;
        }

        var disks = DriveInfo.GetDrives()
            .Where(d => d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable)
            .Select(d =>
            {
                try { return new SystemDiskInfo(d.Name, d.DriveFormat, d.TotalSize, d.AvailableFreeSpace); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { return new SystemDiskInfo(d.Name, "", 0, 0); }
            })
            .ToList();

        return new SystemHealthInfo(
            product,
            display,
            build,
            cpu,
            Environment.ProcessorCount,
            totalMemory,
            availableMemory,
            TimeSpan.FromMilliseconds(Environment.TickCount64),
            System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            disks);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
