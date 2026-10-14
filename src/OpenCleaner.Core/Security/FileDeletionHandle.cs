using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace OpenCleaner.Core.Security;

/// <summary>Keep the verified Windows file locked and delete that same object by handle.</summary>
internal sealed class FileDeletionHandle : IAsyncDisposable
{
    private readonly string _path;
    private readonly FileAttributes _attributes;
    public FileStream Stream { get; }

    public FileDeletionHandle(string path)
    {
        _path = path;
        if (!OperatingSystem.IsWindows())
        {
            Stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
                65536, FileOptions.Asynchronous);
            _attributes = File.GetAttributes(path);
            return;
        }

        // DELETE + READ + WRITE_ATTRIBUTES. Do not share write or delete access with other
        // processes: a path replacement must not swap a different file in after verification.
        SafeFileHandle handle = CreateFile(path, 0x80000000u | 0x00010000u | 0x00000100u, 1, IntPtr.Zero,
            3, 0x00200000u | 0x08000000u, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            IOException error = NativeError("Cannot lock file for deletion");
            handle.Dispose();
            throw error;
        }
        try
        {
            var resolvedPath = new StringBuilder(32768);
            uint length = GetFinalPathNameByHandle(handle, resolvedPath, (uint)resolvedPath.Capacity, 0);
            if (length == 0 || length >= resolvedPath.Capacity) throw NativeError("Cannot resolve file handle");
            string resolved = resolvedPath.ToString();
            if (resolved.StartsWith(@"\\?\", StringComparison.Ordinal)) resolved = resolved[4..];
            if (PathSafety.IsCritical(resolved)) throw new IOException("Resolved file target is unsafe");
            if (!GetFileInformationByHandle(handle, out var information)) throw NativeError("Cannot inspect file handle");
            _attributes = (FileAttributes)information.FileAttributes;
            if ((_attributes & (FileAttributes.ReparsePoint | FileAttributes.System | FileAttributes.Directory)) != 0)
                throw new IOException("Linked, directory or system file target blocked");
            Stream = new FileStream(handle, FileAccess.Read, 65536, isAsync: false);
        }
        catch { handle.Dispose(); throw; }
    }

    public void Delete()
    {
        if (!OperatingSystem.IsWindows())
        {
            if ((_attributes & FileAttributes.ReadOnly) != 0) File.SetAttributes(_path, _attributes & ~FileAttributes.ReadOnly);
            try { File.Delete(_path); }
            catch { File.SetAttributes(_path, _attributes); throw; }
            return;
        }

        bool changedAttributes = (_attributes & FileAttributes.ReadOnly) != 0;
        if (changedAttributes) SetAttributes(_attributes & ~FileAttributes.ReadOnly);
        var disposition = new FileDispositionInfo { DeleteFile = 1 };
        if (!SetFileDisposition(Stream.SafeFileHandle, 4, ref disposition, 1))
        {
            IOException error = NativeError("Cannot delete verified file");
            if (changedAttributes) SetAttributes(_attributes);
            throw error;
        }
    }

    private void SetAttributes(FileAttributes attributes)
    {
        if (attributes == 0) attributes = FileAttributes.Normal;
        var info = new FileBasicInfo { FileAttributes = (uint)attributes };
        if (!SetFileBasicInfo(Stream.SafeFileHandle, 0, ref info, (uint)Marshal.SizeOf<FileBasicInfo>()))
            throw NativeError("Cannot update verified file attributes");
    }

    private static IOException NativeError(string message)
    {
        var error = new Win32Exception(Marshal.GetLastWin32Error());
        return new IOException($"{message}: {error.Message}", error);
    }
    public ValueTask DisposeAsync() => Stream.DisposeAsync();

    [StructLayout(LayoutKind.Sequential)]
    private struct FileDispositionInfo { public byte DeleteFile; }
    [StructLayout(LayoutKind.Sequential)]
    private struct FileBasicInfo
    {
        public long CreationTime, LastAccessTime, LastWriteTime, ChangeTime;
        public uint FileAttributes;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime, LastAccessTime, LastWriteTime;
        public uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint sharing, IntPtr security,
        uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, uint capacity, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out ByHandleFileInformation information);
    [DllImport("kernel32.dll", EntryPoint = "SetFileInformationByHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileDisposition(SafeFileHandle handle, int informationClass, ref FileDispositionInfo information, uint size);
    [DllImport("kernel32.dll", EntryPoint = "SetFileInformationByHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileBasicInfo(SafeFileHandle handle, int informationClass, ref FileBasicInfo information, uint size);
}
