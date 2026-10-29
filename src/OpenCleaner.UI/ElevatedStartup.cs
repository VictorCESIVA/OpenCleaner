using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;
using OpenCleaner.Core.Tools;

namespace OpenCleaner.UI;

/// <summary>
/// Élévation ponctuelle pour les éléments HKLM : le processus élevé ne reçoit qu'une
/// demande JSON validée (Run/RunOnce, nom, valeur attendue) et ne peut rien exécuter d'autre.
/// </summary>
internal static class ElevatedStartup
{
    internal const string HelperArgument = "--startup-helper";
    private const string PipePrefix = "OpenCleaner-Startup-";

    private sealed record HelperMessage(
        MachineStartupRequest? Request = null,
        bool Success = false,
        string? Message = null);

    public static bool IsElevated()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static async Task<MachineStartupResult> RunAsync(MachineStartupRequest request,
        CancellationToken ct = default, Func<ProcessStartInfo, Process?>? startProcess = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!MachineStartupService.Validate(request, out string validationError))
            return new MachineStartupResult(false, validationError);
        ct.ThrowIfCancellationRequested();

        string pipeName = PipePrefix + Guid.NewGuid().ToString("N");
        await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var start = new ProcessStartInfo
        {
            FileName = Environment.ProcessPath ?? throw new InvalidOperationException("Exécutable introuvable."),
            UseShellExecute = true,
            Verb = "runas"
        };
        if (Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "OpenCleaner.UI.dll"));
        start.ArgumentList.Add(HelperArgument);
        start.ArgumentList.Add(pipeName);

        using Process process = (startProcess ?? Process.Start)(start)
            ?? throw new InvalidOperationException("Impossible de lancer le processus administrateur.");
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

        using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        await writer.WriteLineAsync(JsonSerializer.Serialize(new HelperMessage(Request: request)));
        using var reader = new StreamReader(pipe, leaveOpen: true);
        string? line = await reader.ReadLineAsync(ct);
        if (line == null)
            throw new InvalidOperationException("La connexion au processus administrateur a été interrompue.");
        HelperMessage? reply = JsonSerializer.Deserialize<HelperMessage>(line);
        return new MachineStartupResult(reply?.Success == true,
            reply?.Message ?? (reply?.Success == true ? "Élément mis à jour." : "Opération refusée."));
    }

    public static async Task RunHelperAsync(string pipeName)
    {
        if (!pipeName.StartsWith(PipePrefix, StringComparison.Ordinal) ||
            !Guid.TryParseExact(pipeName[PipePrefix.Length..], "N", out _)) return;
        if (!IsElevated()) return;

        await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(30000);
        using var reader = new StreamReader(pipe, leaveOpen: true);
        using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        string? line = await reader.ReadLineAsync();
        HelperMessage reply;
        try
        {
            HelperMessage? message = line == null ? null : JsonSerializer.Deserialize<HelperMessage>(line);
            if (message?.Request == null)
            {
                reply = new HelperMessage(Success: false, Message: "Demande invalide.");
            }
            else
            {
                MachineStartupResult result = new MachineStartupService().Apply(message.Request);
                reply = new HelperMessage(Success: result.Success, Message: result.Message);
            }
        }
        catch (Exception ex)
        {
            reply = new HelperMessage(Success: false, Message: ex.Message);
        }
        await writer.WriteLineAsync(JsonSerializer.Serialize(reply));
    }

    internal static bool IsHelperPipeName(string pipeName) =>
        pipeName.StartsWith(PipePrefix, StringComparison.Ordinal) &&
        Guid.TryParseExact(pipeName[PipePrefix.Length..], "N", out _);
}
