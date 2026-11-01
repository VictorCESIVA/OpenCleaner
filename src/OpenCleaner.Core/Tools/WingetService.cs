using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;

namespace OpenCleaner.Core.Tools;

public sealed record WingetResult(
    int ExitCode,
    string Output,
    bool TimedOut = false,
    bool Truncated = false,
    string Error = "");

[SupportedOSPlatform("windows")]
public static class WingetService
{
    internal const int MaxOutputBytes = 1024 * 1024;
    internal const int MaxOutputLines = 10000;
    internal const int MaxLineLength = 8192;

    public static bool IsAvailable()
    {
        try
        {
            var start = new ProcessStartInfo
            {
                FileName = "winget",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.ArgumentList.Add("--version");
            using var process = Process.Start(start);
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

    public static Task<WingetResult> RunAsync(string arguments, IProgress<string>? progress = null,
        CancellationToken ct = default, string executablePath = "winget") =>
        RunAsync(SplitArguments(arguments), progress, ct, executablePath);

    public static async Task<WingetResult> RunAsync(IReadOnlyList<string> arguments,
        IProgress<string>? progress = null, CancellationToken ct = default, string executablePath = "winget")
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count == 0) throw new ArgumentException("Au moins un argument est requis.", nameof(arguments));

        var start = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (string argument in arguments)
        {
            if (argument == null) throw new ArgumentException("Argument nul.", nameof(arguments));
            start.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new InvalidOperationException("winget could not be started");
        var budget = new OutputBudget();
        var output = new BoundedOutput(budget);
        var error = new BoundedOutput(budget);
        int reportedLines = 0;
        var sync = new object();

        Task Pump(StreamReader reader, BoundedOutput target) => Task.Run(async () =>
        {
            while (await reader.ReadLineAsync(ct) is { } line)
            {
                bool report;
                lock (sync)
                {
                    target.Append(line);
                    report = reportedLines < MaxOutputLines;
                    if (report) reportedLines++;
                }
                if (report) progress?.Report(line);
            }
        }, ct);

        Task outputTask = Pump(process.StandardOutput, output);
        Task errorTask = Pump(process.StandardError, error);
        try
        {
            await process.WaitForExitAsync(ct);
            await Task.WhenAll(outputTask, errorTask);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            try { await Task.WhenAll(outputTask, errorTask); } catch (OperationCanceledException) { }
            throw;
        }

        string stdout = output.Text;
        string stderr = error.Text;
        bool truncated = output.Truncated || error.Truncated;
        if (truncated)
        {
            const string notice = "[sortie tronquée]";
            if (!stdout.Contains(notice, StringComparison.Ordinal))
                stdout = stdout.Length == 0 ? notice : stdout + Environment.NewLine + notice;
        }
        return new WingetResult(process.ExitCode, stdout.TrimEnd(), Truncated: truncated, Error: stderr.TrimEnd());
    }

    internal static IReadOnlyList<string> SplitArguments(string commandLine)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        var result = new List<string>();
        var current = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < commandLine.Length; i++)
        {
            char c = commandLine[i];
            if (c == '\\' && i + 1 < commandLine.Length && commandLine[i + 1] == '"')
            {
                current.Append('"');
                i++;
            }
            else if (c == '"')
            {
                quoted = !quoted;
            }
            else if (char.IsWhiteSpace(c) && !quoted)
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }
        if (quoted) throw new ArgumentException("Guillemet non fermé.", nameof(commandLine));
        if (current.Length > 0) result.Add(current.ToString());
        if (result.Count == 0) throw new ArgumentException("Au moins un argument est requis.", nameof(commandLine));
        return result;
    }

    private static void Kill(Process process)
    {
        try { process.Kill(entireProcessTree: true); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
    }

    internal sealed class OutputBudget
    {
        private int _bytes;
        private int _lines;

        public bool Truncated { get; private set; }

        public bool TryReserve(int bytes)
        {
            lock (this)
            {
                if (_lines >= MaxOutputLines || _bytes + bytes > MaxOutputBytes)
                {
                    Truncated = true;
                    return false;
                }
                _bytes += bytes;
                _lines++;
                return true;
            }
        }
    }

    internal sealed class BoundedOutput
    {
        private readonly StringBuilder _text = new();
        private readonly OutputBudget _budget;
        private bool _lineTruncated;

        public BoundedOutput() : this(new OutputBudget()) { }

        public BoundedOutput(OutputBudget budget) => _budget = budget;

        public bool Truncated => _lineTruncated || _budget.Truncated;
        public int LineCount { get; private set; }
        public string Text => _text.ToString();

        public void Append(string? line)
        {
            if (line == null) return;
            if (line.Length > MaxLineLength)
            {
                line = line[..MaxLineLength];
                _lineTruncated = true;
            }
            int bytes = Encoding.UTF8.GetByteCount(line) + Environment.NewLine.Length;
            if (!_budget.TryReserve(bytes)) return;
            _text.AppendLine(line);
            LineCount++;
        }
    }
}
