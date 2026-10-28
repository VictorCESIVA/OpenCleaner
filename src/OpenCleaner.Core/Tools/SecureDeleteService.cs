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
                path = PathSafety.Normalize(path);
                await using var deletion = new FileDeletionHandle(path, forOverwrite: true);
                if (!PathSafety.IsSamePath(deletion.ResolvedPath, path))
                    throw new IOException("Resolved file target changed, aborting overwrite");
                var stream = deletion.Stream;
                long length = stream.Length;
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
                            deletion.EnsureSingleLink();
                            int chunk = (int)Math.Min(buffer.Length, remaining);
                            RandomNumberGenerator.Fill(buffer.AsSpan(0, chunk));
                            await stream.WriteAsync(buffer.AsMemory(0, chunk), ct);
                            remaining -= chunk;
                        }
                        await stream.FlushAsync(ct);
                        stream.Flush(flushToDisk: true);
                    }
                    deletion.EnsureSingleLink();
                    stream.SetLength(0);
                }
                finally { ArrayPool<byte>.Shared.Return(buffer); }
                deletion.Delete();
                deleted++;
            }
            finally { progress?.Report((index + 1d) / list.Count); }
        }
        return deleted;
    }
}
