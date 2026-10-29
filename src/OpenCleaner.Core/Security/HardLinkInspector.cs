using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace OpenCleaner.Core.Security;

/// <summary>
/// Compte les noms physiques d'un fichier (hard links). Supprimer l'un de ces noms
/// ne libère aucun octet tant que les autres existent ; les nettoyeurs doivent donc les ignorer.
/// </summary>
public static class HardLinkInspector
{
    public static int CountHardLinks(string path)
    {
        // Hors Windows, le nombre de noms physiques n'est pas vérifié : rester conservateur.
        if (!OperatingSystem.IsWindows()) return -1;
        try
        {
            using SafeFileHandle handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            return GetFileInformationByHandle(handle, out ByHandleFileInformation information)
                ? (int)information.NumberOfLinks
                : -1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                       or NotSupportedException)
        {
            return -1;
        }
    }

    /// <summary>Vrai uniquement si le nombre de liens est connu et égal à un.</summary>
    public static bool HasSingleLink(string path) => CountHardLinks(path) == 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle,
        out ByHandleFileInformation information);
}
