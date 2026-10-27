using System.Buffers;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using OpenCleaner.Core.Security;

namespace OpenCleaner.Core.Tools;

[SupportedOSPlatform("windows")]
public static class SecureDeleteService
{
    private const int BufferSize = 1024 * 1024;

    public static async Task<int> ShredAsync(IEnumerable<string> paths, int passes,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (passes is < 1 or > 7) throw new ArgumentOutOfRangeException(nameof(passes));
        var list = paths.Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        int deleted = 0;
        for (int index = 0; index < list.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            string path = list[index];
            try
            {
                if (!File.Exists(path) || PathSafety.IsCritical(path) || PathSafety.HasReparsePoint(path))
                    continue;
                FileAttributes attributes = File.GetAttributes(path);
                bool readOnly = (attributes & FileAttributes.ReadOnly) != 0;
                if (readOnly) File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
                try
                {
                    long length = new FileInfo(path).Length;
                    await using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write,
                                     FileShare.None, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
                    {
                        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
                        try
                        {
                            for (int pass = 0; pass < passes; pass++)
                            {
                                stream.Position = 0;
                                long remaining = length;
                                while (remaining > 0)
                                {
                                    ct.ThrowIfCancellationRequested();
                                    int chunk = (int)Math.Min(buffer.Length, remaining);
                                    RandomNumberGenerator.Fill(buffer.AsSpan(0, chunk));
                                    await stream.WriteAsync(buffer.AsMemory(0, chunk), ct);
                                    remaining -= chunk;
                                }
                                await stream.FlushAsync(ct);
                                stream.Flush(flushToDisk: true);
                            }
                            stream.SetLength(0);
                        }
                        finally { ArrayPool<byte>.Shared.Return(buffer); }
                    }
                    File.Delete(path);
                    deleted++;
                }
                catch
                {
                    if (readOnly && File.Exists(path)) File.SetAttributes(path, attributes);
                    throw;
                }
            }
            finally { progress?.Report((index + 1d) / list.Count); }
        }
        return deleted;
    }
}
