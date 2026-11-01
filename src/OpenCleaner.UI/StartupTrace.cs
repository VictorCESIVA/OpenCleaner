using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace OpenCleaner.UI;

/// <summary>
/// Opt-in startup timing marks. Disabled unless OPENCLEANER_TRACE_STARTUP=1,
/// in which case TSV lines are appended to OPENCLEANER_TRACE_FILE or the
/// default log under %LOCALAPPDATA%\OpenCleaner.
/// </summary>
internal static class StartupTrace
{
    private static readonly bool Enabled = string.Equals(
        Environment.GetEnvironmentVariable("OPENCLEANER_TRACE_STARTUP"), "1", StringComparison.Ordinal);
    private static readonly string FilePath = Enabled ? ResolveTracePath() : string.Empty;
    private static readonly long StartTimestamp = Enabled ? ResolveProcessStartTimestamp() : 0;
    private static readonly object Sync = new();
    private static bool _headerWritten;

    public static void Mark(string mark)
    {
        if (!Enabled) return;
        try
        {
            lock (Sync)
            {
                string? directory = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                var builder = new StringBuilder();
                if (!_headerWritten)
                {
                    bool exists = File.Exists(FilePath) && new FileInfo(FilePath).Length > 0;
                    if (!exists)
                        builder.AppendLine("mark\telapsed_ms\tpid\tthread\tworking_set\tgc_heap\ttimestamp_utc");
                    _headerWritten = true;
                }

                double elapsedMs = (Stopwatch.GetTimestamp() - StartTimestamp) * 1000.0 / Stopwatch.Frequency;
                builder.Append(mark).Append('\t')
                    .Append(elapsedMs.ToString("F1", CultureInfo.InvariantCulture)).Append('\t')
                    .Append(Environment.ProcessId.ToString(CultureInfo.InvariantCulture)).Append('\t')
                    .Append(Environment.CurrentManagedThreadId.ToString(CultureInfo.InvariantCulture)).Append('\t')
                    .Append(Environment.WorkingSet.ToString(CultureInfo.InvariantCulture)).Append('\t')
                    .Append(GC.GetTotalMemory(forceFullCollection: false).ToString(CultureInfo.InvariantCulture)).Append('\t')
                    .Append(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture))
                    .AppendLine();
                File.AppendAllText(FilePath, builder.ToString());
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Tracing must never interfere with startup.
        }
    }

    private static string ResolveTracePath()
    {
        try
        {
            string? overridePath = Environment.GetEnvironmentVariable("OPENCLEANER_TRACE_FILE");
            if (!string.IsNullOrWhiteSpace(overridePath)) return Path.GetFullPath(overridePath);
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OpenCleaner", "startup-trace.log");
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Path.Combine(Path.GetTempPath(), "OpenCleaner-startup-trace.log");
        }
    }

    private static long ResolveProcessStartTimestamp()
    {
        try
        {
            DateTime startedAt = Process.GetCurrentProcess().StartTime;
            double sinceStartMs = Math.Max(0, (DateTime.Now - startedAt).TotalMilliseconds);
            return Stopwatch.GetTimestamp() - (long)(sinceStartMs * Stopwatch.Frequency / 1000.0);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            return Stopwatch.GetTimestamp();
        }
    }
}
