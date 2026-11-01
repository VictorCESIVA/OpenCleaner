using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenCleaner.Core.Tools;

namespace OpenCleaner.UI;

internal static class ElevatedWinget
{
    internal const string HelperArgument = "--winget-upgrade-helper";
    internal const int MaxTargets = 200;
    internal const int MaxRequestBytes = 256 * 1024;
    internal const int MaxLineChars = 64 * 1024;
    internal const int MaxPackageLogBytes = 128 * 1024;
    internal const int MaxPackageLogLines = 2000;
    internal const int MaxTotalLogBytes = 2 * 1024 * 1024;

    private const string PipePrefix = "OpenCleaner-Winget-";
    private static readonly IReadOnlyList<string> LegacyUpgradeAllArguments =
    [
        "upgrade",
        "--all",
        "--silent",
        "--accept-package-agreements",
        "--accept-source-agreements",
        "--disable-interactivity"
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions StrictJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private sealed record HelperMessage(
        string? Kind = null,
        string? RequestId = null,
        string? Id = null,
        string? Text = null,
        string? Phase = null,
        int? ExitCode = null,
        string? Message = null,
        int? Succeeded = null,
        int? Failed = null,
        int? Cancelled = null);

    private sealed record UpgradeRequestMessage(
        string? Kind = null,
        string? RequestId = null,
        List<UpgradeTargetMessage>? Targets = null);

    private sealed record UpgradeTargetMessage(
        string? Id = null,
        string? Source = null,
        string? Scope = null,
        bool? IncludeUnknown = null);

    private sealed record LegacyRequestMessage(string? Kind = null, string? RequestId = null);

    private sealed record ParsedRequest(
        string RequestId,
        IReadOnlyList<WingetUpgradeTarget> Targets,
        bool IsLegacyAll);

    public static async Task<WingetBatchResult> RunAsync(
        IReadOnlyList<WingetUpgradeTarget> targets,
        IProgress<WingetPackageUpdate>? progress = null,
        CancellationToken ct = default,
        Func<ProcessStartInfo, Process?>? startProcess = null)
    {
        ArgumentNullException.ThrowIfNull(targets);
        if (!TryValidateTargets(targets, out string error)) throw new ArgumentException(error, nameof(targets));
        ct.ThrowIfCancellationRequested();

        string pipeName = PipePrefix + Guid.NewGuid().ToString("N");
        await using var pipe = CreateServer(pipeName);
        using Process process = StartHelper(pipeName, startProcess);
        await ConnectAsync(pipe, process, ct);

        using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        var request = new UpgradeRequestMessage(
            "upgrade",
            Guid.NewGuid().ToString("N"),
            targets.Select(target => new UpgradeTargetMessage(
                target.Id,
                target.Source.ToLowerInvariant(),
                target.Scope.ToLowerInvariant(),
                target.IncludeUnknown)).ToList());
        await writer.WriteLineAsync(JsonSerializer.Serialize(request, JsonOptions));
        return await ReadBatchAsync(pipe, writer, progress, ct);
    }

    public static async Task<WingetResult> RunAsync(IProgress<string>? progress, CancellationToken ct,
        Func<ProcessStartInfo, Process?>? startProcess = null)
    {
        ct.ThrowIfCancellationRequested();
        string pipeName = PipePrefix + Guid.NewGuid().ToString("N");
        await using var pipe = CreateServer(pipeName);
        using Process process = StartHelper(pipeName, startProcess);
        await ConnectAsync(pipe, process, ct);

        using (var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true })
            await writer.WriteLineAsync(JsonSerializer.Serialize(
                new LegacyRequestMessage("upgrade-all", Guid.NewGuid().ToString("N")), JsonOptions));
        return await ReadMessagesAsync(pipe, progress, ct);
    }

    internal static async Task<WingetBatchResult> ReadBatchAsync(Stream input, StreamWriter? writer,
        IProgress<WingetPackageUpdate>? progress, CancellationToken ct)
    {
        var packages = new Dictionary<string, WingetPackageUpdate>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        int succeeded = 0;
        int failed = 0;
        int cancelled = 0;
        bool resultReceived = false;

        using CancellationTokenRegistration registration = ct.Register(() => _ = Task.Run(() =>
        {
            if (writer == null) return;
            try
            {
                lock (writer) writer.WriteLine("{\"kind\":\"cancel\"}");
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException) { }
        }));

        var lineReader = new LimitedLineReader(input);
        while (true)
        {
            LimitedLineReader.Result line = await lineReader.ReadLineAsync(MaxLineChars, ct);
            if (line.Kind == LimitedLineReader.ResultKind.Oversized)
                throw new InvalidOperationException("Réponse administrateur trop volumineuse.");
            if (line.Kind == LimitedLineReader.ResultKind.Eof) break;
            HelperMessage? message;
            try { message = JsonSerializer.Deserialize<HelperMessage>(line.Text, JsonOptions); }
            catch (JsonException) { continue; }
            if (message == null) continue;

            switch (message.Kind?.ToLowerInvariant())
            {
                case "accepted":
                    break;
                case "output":
                    if (!string.IsNullOrEmpty(message.Id) && message.Text != null)
                        progress?.Report(new WingetPackageUpdate(
                            message.Id, WingetUpgradePhase.Output, 0, message.Text));
                    break;
                case "status":
                    if (!string.IsNullOrEmpty(message.Id))
                    {
                        if (!packages.ContainsKey(message.Id)) order.Add(message.Id);
                        var update = new WingetPackageUpdate(
                            message.Id, message.Phase ?? "", message.ExitCode ?? 0, message.Message);
                        packages[message.Id] = update;
                        progress?.Report(update);
                    }
                    break;
                case "result":
                    succeeded = message.Succeeded ?? 0;
                    failed = message.Failed ?? 0;
                    cancelled = message.Cancelled ?? 0;
                    resultReceived = true;
                    break;
                case "error":
                    ct.ThrowIfCancellationRequested();
                    throw new InvalidOperationException(
                        message.Text ?? message.Message ?? "Erreur du processus administrateur.");
            }
            if (resultReceived) break;
        }

        if (resultReceived)
            return new WingetBatchResult(order.Select(id => packages[id]).ToList(), succeeded, failed, cancelled);
        ct.ThrowIfCancellationRequested();
        throw new InvalidOperationException("La connexion au processus administrateur a été interrompue.");
    }

    internal static async Task<WingetResult> ReadMessagesAsync(Stream pipe, IProgress<string>? progress,
        CancellationToken ct)
    {
        var lineReader = new LimitedLineReader(pipe);
        using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        using var registration = ct.Register(() => _ = Task.Run(() =>
        {
            try
            {
                lock (writer) writer.WriteLine("cancel");
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException) { }
        }));

        var output = new StringBuilder();
        int? lastExitCode = null;
        while (true)
        {
            LimitedLineReader.Result line = await lineReader.ReadLineAsync(MaxLineChars, ct);
            if (line.Kind == LimitedLineReader.ResultKind.Oversized)
                throw new InvalidOperationException("Réponse administrateur trop volumineuse.");
            if (line.Kind == LimitedLineReader.ResultKind.Eof) break;
            HelperMessage? message;
            try { message = JsonSerializer.Deserialize<HelperMessage>(line.Text, JsonOptions); }
            catch (JsonException) { continue; }
            if (message == null) continue;

            switch (message.Kind?.ToLowerInvariant())
            {
                case "output":
                    lock (output)
                    {
                        output.AppendLine(message.Text);
                        progress?.Report(message.Text ?? "");
                    }
                    break;
                case "status":
                    if (message.ExitCode.HasValue) lastExitCode = message.ExitCode;
                    break;
                case "result":
                    ct.ThrowIfCancellationRequested();
                    return new WingetResult(message.ExitCode ?? lastExitCode ?? -1, output.ToString().Trim());
                case "error":
                    ct.ThrowIfCancellationRequested();
                    throw new InvalidOperationException(message.Text ?? message.Message ?? "Erreur administrateur.");
            }
        }
        ct.ThrowIfCancellationRequested();
        throw new InvalidOperationException("La connexion au processus administrateur a été interrompue.");
    }

    public static async Task RunHelperAsync(string pipeName)
    {
        if (!IsHelperPipeName(pipeName)) return;
        await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(30000);
        await RunHelperSessionAsync(pipe, pipe, IsElevated(), ResolveWingetAsync, RunWingetAsync);
    }

    internal static async Task<WingetBatchResult?> RunHelperSessionAsync(
        Stream input,
        Stream output,
        bool isElevated,
        Func<CancellationToken, Task<string>> resolveWinget,
        Func<string, IReadOnlyList<string>, IProgress<string>?, CancellationToken, Task<WingetResult>> runWinget,
        CancellationToken ct = default)
    {
        using var writer = new StreamWriter(output, leaveOpen: true) { AutoFlush = true };
        var writeLock = new object();
        void Send(HelperMessage message)
        {
            lock (writeLock) writer.WriteLine(JsonSerializer.Serialize(message, JsonOptions));
        }

        if (!isElevated)
        {
            Send(new HelperMessage(Kind: "error", Message: "Droits administrateur requis."));
            return null;
        }

        var lineReader = new LimitedLineReader(input);
        LimitedLineReader.Result first = await lineReader.ReadLineAsync(MaxLineChars, ct);
        if (first.Kind == LimitedLineReader.ResultKind.Eof)
        {
            Send(new HelperMessage(Kind: "error", Message: "Demande absente."));
            return null;
        }
        if (first.Kind == LimitedLineReader.ResultKind.Oversized ||
            Encoding.UTF8.GetByteCount(first.Text) > MaxRequestBytes)
        {
            Send(new HelperMessage(Kind: "error", Message: "Demande trop volumineuse."));
            return null;
        }
        if (!TryParseRequest(first.Text, out ParsedRequest? request, out string validationError))
        {
            Send(new HelperMessage(Kind: "error", Message: validationError));
            return null;
        }

        Send(new HelperMessage(Kind: "accepted", RequestId: request.RequestId));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ = ReadCancellationLoopAsync(
            lineReader, Send, cancellation, Encoding.UTF8.GetByteCount(first.Text) + 1);

        try
        {
            if (request.IsLegacyAll)
            {
                var legacyBudget = new LogBudget(MaxTotalLogBytes, int.MaxValue);
                var legacyProgress = new SynchronousProgress(text =>
                {
                    if (legacyBudget.TryAdd(text))
                        Send(new HelperMessage(Kind: "output", Id: "all", Text: text));
                });
                try
                {
                    string legacyPath = await resolveWinget(cancellation.Token);
                    WingetResult legacyResult = await runWinget(
                        legacyPath, LegacyUpgradeAllArguments, legacyProgress, cancellation.Token);
                    bool legacySuccess = legacyResult.ExitCode == 0;
                    string legacyPhase = legacySuccess
                        ? WingetUpgradePhase.Succeeded
                        : WingetUpgradePhase.Failed;
                    string? legacyMessage = legacySuccess ? null : $"Code de sortie {legacyResult.ExitCode}.";
                    Send(new HelperMessage(
                        Kind: "status", Id: "all", Phase: legacyPhase,
                        ExitCode: legacyResult.ExitCode, Message: legacyMessage));
                    Send(new HelperMessage(
                        Kind: "result", Succeeded: legacySuccess ? 1 : 0,
                        Failed: legacySuccess ? 0 : 1, Cancelled: 0));
                    return new WingetBatchResult(
                        [new WingetPackageUpdate("all", legacyPhase, legacyResult.ExitCode, legacyMessage)],
                        legacySuccess ? 1 : 0, legacySuccess ? 0 : 1, 0);
                }
                catch (OperationCanceledException)
                {
                    Send(new HelperMessage(
                        Kind: "status", Id: "all", Phase: WingetUpgradePhase.Cancelled,
                        ExitCode: -1, Message: "Mise à niveau annulée."));
                    Send(new HelperMessage(Kind: "result", Succeeded: 0, Failed: 0, Cancelled: 1));
                    return new WingetBatchResult(
                        [new WingetPackageUpdate("all", WingetUpgradePhase.Cancelled, -1, "Mise à niveau annulée.")],
                        0, 0, 1);
                }
            }

            var packages = new List<WingetPackageUpdate>();
            int succeeded = 0;
            int failed = 0;
            int cancelled = 0;
            var totalBudget = new LogBudget(MaxTotalLogBytes, int.MaxValue);
            string wingetPath;
            try { wingetPath = await resolveWinget(cancellation.Token); }
            catch (OperationCanceledException)
            {
                foreach (WingetUpgradeTarget target in request.Targets)
                {
                    cancelled++;
                    packages.Add(new WingetPackageUpdate(
                        target.Id, WingetUpgradePhase.Skipped, -1,
                        "Mise à niveau ignorée après annulation."));
                    Send(new HelperMessage(
                        Kind: "status", Id: target.Id, Phase: WingetUpgradePhase.Skipped,
                        ExitCode: -1, Message: "Mise à niveau ignorée après annulation."));
                }
                Send(new HelperMessage(
                    Kind: "result", Succeeded: 0, Failed: 0, Cancelled: cancelled));
                return new WingetBatchResult(packages, 0, 0, cancelled);
            }

            int index = 0;
            for (; index < request.Targets.Count; index++)
            {
                if (cancellation.IsCancellationRequested) break;
                WingetUpgradeTarget target = request.Targets[index];
                IReadOnlyList<string> arguments = WingetUpgradeService.BuildUpgradeArguments(target);
                Send(new HelperMessage(Kind: "status", Id: target.Id, Phase: WingetUpgradePhase.Running));

                var packageBudget = new LogBudget(MaxPackageLogBytes, MaxPackageLogLines);
                var progress = new SynchronousProgress(text =>
                {
                    if (packageBudget.TryAdd(text) && totalBudget.TryAdd(text))
                        Send(new HelperMessage(Kind: "output", Id: target.Id, Text: text));
                });

                WingetResult result;
                try
                {
                    result = await runWinget(wingetPath, arguments, progress, cancellation.Token);
                }
                catch (Exception ex) when (ex is OperationCanceledException || cancellation.IsCancellationRequested)
                {
                    cancelled++;
                    packages.Add(new WingetPackageUpdate(
                        target.Id, WingetUpgradePhase.Cancelled, -1, "Mise à niveau annulée."));
                    Send(new HelperMessage(
                        Kind: "status", Id: target.Id, Phase: WingetUpgradePhase.Cancelled,
                        ExitCode: -1, Message: "Mise à niveau annulée."));
                    index++;
                    break;
                }
                catch (Exception ex)
                {
                    failed++;
                    packages.Add(new WingetPackageUpdate(
                        target.Id, WingetUpgradePhase.Failed, -1, ex.Message));
                    Send(new HelperMessage(
                        Kind: "status", Id: target.Id, Phase: WingetUpgradePhase.Failed,
                        ExitCode: -1, Message: ex.Message));
                    continue;
                }

                bool success = result.ExitCode == 0;
                if (success) succeeded++; else failed++;
                string phase = success ? WingetUpgradePhase.Succeeded : WingetUpgradePhase.Failed;
                string? message = success ? null : $"Code de sortie {result.ExitCode}.";
                packages.Add(new WingetPackageUpdate(target.Id, phase, result.ExitCode, message));
                Send(new HelperMessage(
                    Kind: "status", Id: target.Id, Phase: phase, ExitCode: result.ExitCode, Message: message));
            }

            for (; index < request.Targets.Count; index++)
            {
                WingetUpgradeTarget target = request.Targets[index];
                cancelled++;
                packages.Add(new WingetPackageUpdate(
                    target.Id, WingetUpgradePhase.Skipped, -1, "Mise à niveau ignorée après annulation."));
                Send(new HelperMessage(
                    Kind: "status", Id: target.Id, Phase: WingetUpgradePhase.Skipped,
                    ExitCode: -1, Message: "Mise à niveau ignorée après annulation."));
            }

            Send(new HelperMessage(
                Kind: "result", Succeeded: succeeded, Failed: failed, Cancelled: cancelled));
            return new WingetBatchResult(packages, succeeded, failed, cancelled);
        }
        catch (OperationCanceledException)
        {
            Send(new HelperMessage(Kind: "error", Message: "Opération annulée."));
            return null;
        }
        catch (Exception ex)
        {
            Send(new HelperMessage(Kind: "error", Message: ex.Message));
            return null;
        }
        finally
        {
            try { await cancellation.CancelAsync(); } catch (ObjectDisposedException) { }
        }
    }

    private static async Task ReadCancellationLoopAsync(LimitedLineReader reader, Action<HelperMessage> send,
        CancellationTokenSource cancellation, long inboundBytes)
    {
        try
        {
            while (true)
            {
                LimitedLineReader.Result line = await reader.ReadLineAsync(MaxLineChars, CancellationToken.None);
                if (line.Kind == LimitedLineReader.ResultKind.Oversized)
                {
                    send(new HelperMessage(Kind: "error", Message: "Demande trop volumineuse."));
                    await cancellation.CancelAsync();
                    return;
                }
                if (line.Kind != LimitedLineReader.ResultKind.Line) break;
                inboundBytes += Encoding.UTF8.GetByteCount(line.Text) + 1;
                if (inboundBytes > MaxRequestBytes)
                {
                    send(new HelperMessage(Kind: "error", Message: "Demande trop volumineuse."));
                    await cancellation.CancelAsync();
                    return;
                }
                if (string.Equals(line.Text.Trim(), "cancel", StringComparison.OrdinalIgnoreCase))
                {
                    await cancellation.CancelAsync();
                    return;
                }
                string? kind = null;
                try
                {
                    using JsonDocument document = JsonDocument.Parse(line.Text);
                    if (document.RootElement.ValueKind == JsonValueKind.Object &&
                        document.RootElement.TryGetProperty("kind", out JsonElement value) &&
                        value.ValueKind == JsonValueKind.String)
                        kind = value.GetString();
                }
                catch (JsonException)
                {
                    send(new HelperMessage(Kind: "error", Message: "Message administrateur invalide."));
                    await cancellation.CancelAsync();
                    return;
                }

                if (string.Equals(kind, "cancel", StringComparison.OrdinalIgnoreCase))
                {
                    await cancellation.CancelAsync();
                    return;
                }
                if (string.Equals(kind, "upgrade", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(kind, "upgrade-all", StringComparison.OrdinalIgnoreCase))
                {
                    send(new HelperMessage(Kind: "error", Message: "Une seule demande de mise à niveau est autorisée."));
                    await cancellation.CancelAsync();
                    return;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException) { }
        finally
        {
            try { await cancellation.CancelAsync(); } catch (ObjectDisposedException) { }
        }
    }

    private static bool TryParseRequest(string line, [NotNullWhen(true)] out ParsedRequest? request, out string error)
    {
        request = null;
        error = "Demande invalide.";
        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            if (!document.RootElement.TryGetProperty("kind", out JsonElement kindElement) ||
                kindElement.ValueKind != JsonValueKind.String) return false;
            string? kind = kindElement.GetString();

            if (string.Equals(kind, "upgrade-all", StringComparison.OrdinalIgnoreCase))
            {
                LegacyRequestMessage? legacy = JsonSerializer.Deserialize<LegacyRequestMessage>(line, StrictJsonOptions);
                if (legacy == null) return false;
                string legacyId = legacy.RequestId ?? "";
                if (!IsValidRequestId(legacyId)) return false;
                request = new ParsedRequest(legacyId, [], true);
                return true;
            }

            if (!string.Equals(kind, "upgrade", StringComparison.OrdinalIgnoreCase)) return false;
            UpgradeRequestMessage? message = JsonSerializer.Deserialize<UpgradeRequestMessage>(line, StrictJsonOptions);
            if (message == null || !IsValidRequestId(message.RequestId)) return false;
            if (message.Targets == null || message.Targets.Count is < 1 or > MaxTargets)
            {
                error = "Nombre de paquets invalide.";
                return false;
            }

            var targets = new List<WingetUpgradeTarget>(message.Targets.Count);
            foreach (UpgradeTargetMessage? target in message.Targets)
            {
                if (target == null) return false;
                if (!WingetPackageId.IsValid(target.Id))
                {
                    error = "Identifiant de paquet invalide.";
                    return false;
                }
                if (target.Source == null || !WingetUpgradeService.IsAllowedSource(target.Source))
                {
                    error = "Source winget invalide.";
                    return false;
                }
                if (target.Scope == null || !WingetUpgradeService.IsAllowedScope(target.Scope))
                {
                    error = "Portée winget invalide.";
                    return false;
                }
                targets.Add(new WingetUpgradeTarget(
                    target.Id, target.Source.ToLowerInvariant(), target.Scope.ToLowerInvariant(),
                    target.IncludeUnknown ?? false));
            }

            request = new ParsedRequest(message.RequestId!, targets, false);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsValidRequestId(string? requestId) =>
        !string.IsNullOrWhiteSpace(requestId) && requestId.Length <= 128 &&
        requestId.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');

    internal static bool TryValidateTargets(IReadOnlyList<WingetUpgradeTarget>? targets, out string error)
    {
        error = "";
        if (targets == null || targets.Count is < 1 or > MaxTargets)
        {
            error = "Nombre de paquets invalide.";
            return false;
        }
        foreach (WingetUpgradeTarget? target in targets)
        {
            if (target == null || !WingetPackageId.IsValid(target.Id))
            {
                error = "Identifiant de paquet invalide.";
                return false;
            }
            if (!WingetUpgradeService.IsAllowedSource(target.Source))
            {
                error = "Source winget invalide.";
                return false;
            }
            if (!WingetUpgradeService.IsAllowedScope(target.Scope))
            {
                error = "Portée winget invalide.";
                return false;
            }
        }
        return true;
    }

    internal static bool IsHelperPipeName(string? pipeName) =>
        pipeName != null && pipeName.StartsWith(PipePrefix, StringComparison.Ordinal) &&
        Guid.TryParseExact(pipeName[PipePrefix.Length..], "N", out _);

    internal static bool IsElevated()
    {
        try
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception ex) when (ex is PlatformNotSupportedException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static NamedPipeServerStream CreateServer(string pipeName) =>
        new(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    private static Process StartHelper(string pipeName, Func<ProcessStartInfo, Process?>? startProcess)
    {
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
        return (startProcess ?? Process.Start)(start)
            ?? throw new InvalidOperationException("Impossible de démarrer winget en administrateur.");
    }

    private static async Task ConnectAsync(NamedPipeServerStream pipe, Process process, CancellationToken ct)
    {
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
    }

    private static Task<WingetResult> RunWingetAsync(string path, IReadOnlyList<string> arguments,
        IProgress<string>? progress, CancellationToken ct) =>
        WingetService.RunAsync(arguments, progress, ct, path);

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

    private sealed class LogBudget(int maxBytes, int maxLines)
    {
        private int _bytes;
        private int _lines;

        public bool TryAdd(string? text)
        {
            if (text == null) return false;
            int bytes = Encoding.UTF8.GetByteCount(text) + 1;
            lock (this)
            {
                if (_lines >= maxLines || _bytes + bytes > maxBytes) return false;
                _bytes += bytes;
                _lines++;
                return true;
            }
        }
    }

    internal sealed class LimitedLineReader(Stream stream)
    {
        private readonly StreamReader _reader = new(stream, leaveOpen: true);
        private readonly char[] _buffer = new char[4096];
        private int _start;
        private int _end;

        internal enum ResultKind { Line, Eof, Oversized }

        internal readonly record struct Result(ResultKind Kind, string Text);

        public async Task<Result> ReadLineAsync(int maxChars, CancellationToken ct)
        {
            var text = new StringBuilder();
            bool oversized = false;
            while (true)
            {
                if (_start == _end)
                {
                    _end = await _reader.ReadAsync(_buffer.AsMemory(), ct);
                    _start = 0;
                    if (_end == 0)
                        return oversized ? new Result(ResultKind.Oversized, "") :
                            text.Length == 0 ? new Result(ResultKind.Eof, "") :
                            new Result(ResultKind.Line, text.ToString());
                }
                char c = _buffer[_start++];
                if (c == '\n')
                    return oversized ? new Result(ResultKind.Oversized, "") :
                        new Result(ResultKind.Line, text.ToString());
                if (c == '\r') continue;
                if (text.Length >= maxChars)
                {
                    oversized = true;
                    continue;
                }
                text.Append(c);
            }
        }
    }
}
