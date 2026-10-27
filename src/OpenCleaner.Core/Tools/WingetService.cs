using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;

namespace OpenCleaner.Core.Tools;

public sealed record WingetResult(int ExitCode, string Output, bool TimedOut = false);

[SupportedOSPlatform("windows")]
public static class WingetService
{
    public static bool IsAvailable()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "winget",
                Arguments = "--version",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            if (process == null) return false;
            if (!process.WaitForExit(5000))
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                return false;
            }
            return process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        { return false; }
    }

    public static async Task<WingetResult> RunAsync(string arguments, IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var start = new ProcessStartInfo
        {
            FileName = "winget",
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new InvalidOperationException("winget could not be started");
        var output = new StringBuilder();
        var sync = new object();
        Task Pump(StreamReader reader) => Task.Run(async () =>
        {
            while (await reader.ReadLineAsync(ct) is { } line)
            {
                lock (sync) output.AppendLine(line);
                progress?.Report(line);
            }
        }, ct);
        Task outputTask = Pump(process.StandardOutput);
        Task errorTask = Pump(process.StandardError);
        try
        {
            await process.WaitForExitAsync(ct);
            await Task.WhenAll(outputTask, errorTask);
            return new WingetResult(process.ExitCode, output.ToString().Trim());
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw;
        }
    }
}
