using System.IO;
using OpenCleaner.Contracts;

namespace OpenCleaner.UI;

internal static class CleanupResults
{
    public static List<CleanableItem> GetRemainingItems(IEnumerable<CleanableItem> items) =>
        items.Where(StillExists).ToList();

    private static bool StillExists(CleanableItem item)
    {
        if (item.Type is not (ItemType.File or ItemType.Directory)) return true;
        try
        {
            File.GetAttributes(item.Path);
            return true;
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        // An inaccessible file is still a remaining result, not a successful deletion.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return true; }
    }
}
