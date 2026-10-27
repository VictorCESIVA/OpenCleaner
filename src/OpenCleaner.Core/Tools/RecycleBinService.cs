using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace OpenCleaner.Core.Tools;

public sealed record RecycleBinInfo(long ItemCount, long SizeBytes);

[SupportedOSPlatform("windows")]
public static class RecycleBinService
{
    private const uint NoConfirmation = 0x00000001;
    private const uint NoProgressUi = 0x00000002;
    private const uint NoSound = 0x00000004;

    public static RecycleBinInfo Query()
    {
        var info = new QueryInfo { Size = (uint)Marshal.SizeOf<QueryInfo>() };
        int result = SHQueryRecycleBin(null, ref info);
        if (result >= 0) return new RecycleBinInfo(info.ItemCount, info.SizeBytes);

        long count = 0;
        long size = 0;
        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady) continue;
            var driveInfo = new QueryInfo { Size = (uint)Marshal.SizeOf<QueryInfo>() };
            if (SHQueryRecycleBin(drive.RootDirectory.FullName, ref driveInfo) < 0) continue;
            count += driveInfo.ItemCount;
            size += driveInfo.SizeBytes;
        }
        return new RecycleBinInfo(count, size);
    }

    public static void Empty()
    {
        int result = SHEmptyRecycleBin(IntPtr.Zero, null, NoConfirmation | NoProgressUi | NoSound);
        if (result < 0) Marshal.ThrowExceptionForHR(result);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct QueryInfo
    {
        public uint Size;
        public long SizeBytes;
        public long ItemCount;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? rootPath, ref QueryInfo info);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr window, string? rootPath, uint flags);
}
