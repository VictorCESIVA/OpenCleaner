using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OpenCleaner.UI;

internal static class ShellIconLoader
{
    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiLargeIcon = 0x000000000;
    private static readonly ConcurrentDictionary<string, Lazy<ImageSource?>> Cache =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly BlockingCollection<IconRequest> Queue = [];
    private static readonly Thread Worker;

    static ShellIconLoader()
    {
        Worker = new Thread(ProcessQueue)
        {
            IsBackground = true,
            Name = "OpenCleaner icon loader",
            Priority = ThreadPriority.BelowNormal
        };
        Worker.SetApartmentState(ApartmentState.STA);
        Worker.Start();
    }

    public static async Task<ImageSource?> GetAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (Cache.TryGetValue(path, out Lazy<ImageSource?>? cached)) return cached.Value;

        var completion = new TaskCompletionSource<ImageSource?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Queue.Add(new IconRequest(path, completion));
        return await completion.Task;
    }

    private static void ProcessQueue()
    {
        foreach (IconRequest request in Queue.GetConsumingEnumerable())
        {
            try { request.Completion.TrySetResult(Get(request.Path)); }
            catch (Exception ex) { request.Completion.TrySetException(ex); }
        }
    }

    public static ImageSource? Get(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        return Cache.GetOrAdd(path, key => new Lazy<ImageSource?>(() => Load(key))).Value;
    }

    private static ImageSource? Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var info = new ShFileInfo();
            IntPtr result = SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<ShFileInfo>(),
                ShgfiIcon | ShgfiLargeIcon);
            if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;

            try
            {
                BitmapSource source = Imaging.CreateBitmapSourceFromHIcon(
                    info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally { DestroyIcon(info.hIcon); }
        }
        catch (Exception)
        {
            return null;
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint fileAttributes, ref ShFileInfo info,
        uint infoSize, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    private sealed record IconRequest(string Path, TaskCompletionSource<ImageSource?> Completion);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string? szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string? szTypeName;
    }
}
