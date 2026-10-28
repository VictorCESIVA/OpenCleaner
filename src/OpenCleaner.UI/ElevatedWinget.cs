using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using OpenCleaner.Core.Tools;

namespace OpenCleaner.UI;

// The elevated helper only supports this fixed operation; no command is accepted from the pipe.
internal static class ElevatedWinget
{
    internal const string HelperArgument = "--winget-upgrade-helper";
    internal const string UpgradeArguments =
        "upgrade --all --silent --accept-package-agreements --accept-source-agreements --disable-interactivity";

    private sealed record Message(string Kind, string? Text = null, int? ExitCode = null);

    public static async Task<WingetResult> RunAsync(IProgress<string>? progress, CancellationToken ct,
        Func<ProcessStartInfo, Process?>? startProcess = null)
    {
        ct.ThrowIfCancellationRequested();
        string pipeName = "OpenCleaner-Winget-" + Guid.NewGuid().ToString("N");
        await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var start = new ProcessStartInfo
        {
            FileName = Environment.ProcessPath ?? throw new InvalidOperationException("Exécutable introuvable."),
            UseShellExecute = true,
            Verb = "runas"
        };
        if (Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add(HelperArgument);
        start.ArgumentList.Add(pipeName);

        using Process process = (startProcess ?? Process.Start)(start) ?? throw new InvalidOperationException("Impossible de démarrer winget en administrateur.");
        using var connectCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task connect = pipe.WaitForConnectionAsync(connectCancellation.Token);
        Task exited = process.WaitForExitAsync();
        if (await Task.WhenAny(connect, exited) == exited && !connect.IsCompletedSuccessfully)
        {
            await connectCancellation.CancelAsync();
            ct.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Le processus administrateur s'est arrêté avant de se connecter.");
        }
        await connect;

        return await ReadMessagesAsync(pipe, progress, ct);
    }

    internal static async Task<WingetResult> ReadMessagesAsync(Stream pipe, IProgress<string>? progress, CancellationToken ct)
    {
        using var reader = new StreamReader(pipe, leaveOpen: true);
        using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        using var cancellationRegistration = ct.Register(() => _ = Task.Run(() =>
        {
            try { writer.WriteLine("cancel"); } catch (IOException) { } catch (ObjectDisposedException) { }
        }));
        var output = new System.Text.StringBuilder();
        while (await reader.ReadLineAsync() is { } line)
        {
            Message? message = JsonSerializer.Deserialize<Message>(line);
            if (message?.Kind == "output")
            {
                output.AppendLine(message.Text);
                progress?.Report(message.Text ?? "");
            }
            else if (message?.Kind == "result")
            {
                ct.ThrowIfCancellationRequested();
                return new WingetResult(message.ExitCode ?? -1, output.ToString().Trim());
            }
            else if (message?.Kind == "error")
            {
                ct.ThrowIfCancellationRequested();
                throw new InvalidOperationException(message.Text);
            }
        }
        ct.ThrowIfCancellationRequested();
        throw new InvalidOperationException("La connexion au processus administrateur a été interrompue.");
    }

    public static async Task RunHelperAsync(string pipeName)
    {
        if (!pipeName.StartsWith("OpenCleaner-Winget-", StringComparison.Ordinal) ||
            !Guid.TryParseExact(pipeName["OpenCleaner-Winget-".Length..], "N", out _)) return;
        await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(30000);
        using var reader = new StreamReader(pipe, leaveOpen: true);
        using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        using var cancellation = new CancellationTokenSource();
        var writeLock = new object();
        void Send(Message message)
        {
            lock (writeLock) writer.WriteLine(JsonSerializer.Serialize(message));
        }
        // A closed pipe also stops the child if the main application disappears.
        _ = Task.Run(async () =>
        {
            try { await reader.ReadLineAsync(); }
            catch (IOException) { }
            finally
            {
                try { await cancellation.CancelAsync(); } catch (ObjectDisposedException) { }
            }
        });
        try
        {
            string wingetPath = await ResolveWingetAsync(cancellation.Token);
            var progress = new SynchronousProgress(line => Send(new Message("output", line)));
            WingetResult result = await WingetService.RunAsync(UpgradeArguments, progress, cancellation.Token, wingetPath);
            Send(new Message("result", ExitCode: result.ExitCode));
        }
        catch (OperationCanceledException)
        {
            try { Send(new Message("error", "Opération annulée.")); } catch (IOException) { }
        }
        catch (Exception ex)
        {
            try { Send(new Message("error", ex.Message)); } catch (IOException) { }
        }
    }

    // Do not resolve winget through PATH in a privileged process: that could execute a
    // user-controlled binary. App Installer's package directory is protected by Windows.
    private static async Task<string> ResolveWingetAsync(CancellationToken ct)
    {
        string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        string powershell = Path.Combine(system, "WindowsPowerShell", "v1.0", "powershell.exe");
        string module = Path.Combine(system, "WindowsPowerShell", "v1.0", "Modules", "Appx", "Appx.psd1");
        var start = new ProcessStartInfo
        {
            FileName = powershell,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add($"$ErrorActionPreference = 'Stop'; Import-Module -Name '{module.Replace("'", "''")}'; (Appx\\Get-AppxPackage -Name Microsoft.DesktopAppInstaller).InstallLocation");
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Impossible de trouver winget.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(ct);
        Task<string> stderr = process.StandardError.ReadToEndAsync(ct);
        try { await process.WaitForExitAsync(ct); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw;
        }
        string location = (await stdout).Trim();
        _ = await stderr;
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
        string directory = Path.GetFileName(location);
        if (process.ExitCode != 0 || string.IsNullOrEmpty(location) ||
            !string.Equals(Path.GetDirectoryName(location), root, StringComparison.OrdinalIgnoreCase) ||
            !directory.StartsWith("Microsoft.DesktopAppInstaller_", StringComparison.OrdinalIgnoreCase) ||
            !directory.EndsWith("__8wekyb3d8bbwe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("winget est introuvable pour le compte administrateur. Installez ou réparez le Programme d'installation d'applications Windows pour ce compte.");
        return Path.Combine(location, "winget.exe");
    }

    private sealed class SynchronousProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
    }
}
