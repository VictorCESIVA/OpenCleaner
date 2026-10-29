namespace OpenCleaner.Core.Tools;

public sealed record DiskSpaceAlert(string Drive, long FreeBytes, long TotalBytes, double FreeRatio)
{
    public double FreePercent => FreeRatio * 100;
}

/// <summary>Signale le volume connu le plus proche de la saturation, sous 10 % d'espace libre.</summary>
public static class DiskSpaceAlertService
{
    public const double Threshold = 0.10;

    public static DiskSpaceAlert? FindLowSpace(IEnumerable<SystemDiskInfo> disks) =>
        FindLowSpace(disks, Threshold);

    public static DiskSpaceAlert? FindLowSpace(IEnumerable<SystemDiskInfo> disks, double threshold)
    {
        ArgumentNullException.ThrowIfNull(disks);
        if (threshold <= 0 || threshold >= 1)
            throw new ArgumentOutOfRangeException(nameof(threshold), "Threshold must be between 0 and 1.");

        DiskSpaceAlert? worst = null;
        foreach (var disk in disks)
        {
            if (disk.TotalBytes <= 0) continue;
            long free = Math.Clamp(disk.FreeBytes, 0, disk.TotalBytes);
            double ratio = (double)free / disk.TotalBytes;
            if (ratio >= threshold) continue;
            if (worst == null || ratio < worst.FreeRatio)
                worst = new DiskSpaceAlert(disk.Name, free, disk.TotalBytes, ratio);
        }
        return worst;
    }
}
