using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
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
    internal const int MaxLineBytes = 64 * 1024;
    internal const int MaxMessageBytes = 256 * 1024;
    internal const int MaxErrorLength = 512;
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(30);
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly JsonDocumentOptions StrictDocumentOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 8
    };

    internal sealed record HelperMessage(
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
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
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
        connectCancellation.CancelAfter(ConnectTimeout);
        Task connect = pipe.WaitForConnectionAsync(connectCancellation.Token);
        Task exited = process.WaitForExitAsync();
        if (await Task.WhenAny(connect, exited) == exited && !connect.IsCompletedSuccessfully)
        {
            await connectCancellation.CancelAsync();
            ct.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Le processus administrateur s'est arrêté avant de se connecter.");
        }
        try
        {
            await connect;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException("Le processus administrateur n'a pas ouvert la connexion à temps.");
        }

        string requestLine = JsonSerializer.Serialize(new HelperMessage(Request: request));
        if (StrictUtf8.GetByteCount(requestLine) > MaxLineBytes)
            throw new InvalidOperationException("La demande dépasse la taille maximale autorisée.");
        using (var writeCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            writeCancellation.CancelAfter(ReadTimeout);
            try
            {
                using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                await writer.WriteLineAsync(requestLine.AsMemory(), writeCancellation.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException("Le processus administrateur n'a pas lu la demande à temps.");
            }
        }

        using var readCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        readCancellation.CancelAfter(ReadTimeout);
        string? line;
        try
        {
            line = await ReadLimitedLineAsync(pipe, MaxLineBytes, MaxMessageBytes, readCancellation.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException("Le processus administrateur n'a pas répondu à temps.");
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidOperationException("La réponse du processus administrateur est invalide.", ex);
        }
        if (line == null)
            throw new InvalidOperationException("La connexion au processus administrateur a été interrompue.");
        if (!TryParseHelperMessage(line, out HelperMessage? reply, out string parseError))
            throw new InvalidOperationException("Réponse invalide du processus administrateur : " + Cap(parseError));
        return new MachineStartupResult(reply?.Success == true,
            Cap(reply?.Message ?? (reply?.Success == true ? "Élément mis à jour." : "Opération refusée.")));
    }

    public static async Task RunHelperAsync(string pipeName)
    {
        if (!IsHelperPipeName(pipeName)) return;
        if (!IsElevated()) return;

        await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync((int)ConnectTimeout.TotalMilliseconds);
        }
        catch (TimeoutException)
        {
            return;
        }
        using var readCancellation = new CancellationTokenSource(ReadTimeout);
        string? line;
        try
        {
            line = await ReadLimitedLineAsync(pipe, MaxLineBytes, MaxMessageBytes, readCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (InvalidDataException)
        {
            await WriteReplyAsync(pipe, new HelperMessage(Success: false, Message: "Message trop volumineux ou invalide."));
            return;
        }

        await WriteReplyAsync(pipe, HandleHelperMessage(line));
    }

    internal static HelperMessage HandleHelperMessage(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return Failure("Demande invalide.");
        if (!TryParseHelperMessage(line, out HelperMessage? message, out string parseError))
            return Failure(parseError);
        MachineStartupRequest? request = message?.Request;
        if (request == null)
            return Failure("Demande invalide.");
        if (!MachineStartupService.Validate(request, out string validationError))
            return Failure(validationError);
        try
        {
            MachineStartupResult result = new MachineStartupService().Apply(request);
            return new HelperMessage(Success: result.Success, Message: Cap(result.Message));
        }
        catch (Exception ex)
        {
            return Failure("Erreur interne : " + ex.Message);
        }
    }

    internal static bool TryParseHelperMessage(string json, out HelperMessage? message, out string error)
    {
        message = null;
        error = "";
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "Message vide.";
            return false;
        }
        try
        {
            if (StrictUtf8.GetByteCount(json) > MaxLineBytes)
            {
                error = "Message trop volumineux.";
                return false;
            }
        }
        catch (EncoderFallbackException)
        {
            error = "Encodage invalide.";
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json, StrictDocumentOptions);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "Message JSON invalide.";
                return false;
            }

            MachineStartupRequest? request = null;
            bool success = false;
            string? replyMessage = null;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in root.EnumerateObject())
            {
                if (!seen.Add(property.Name))
                {
                    error = "Membre JSON dupliqué.";
                    return false;
                }
                switch (property.Name)
                {
                    case "Request":
                        if (!TryParseRequest(property.Value, out request, out error)) return false;
                        break;
                    case "Success":
                        if (property.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                        {
                            error = "Champ Success invalide.";
                            return false;
                        }
                        success = property.Value.GetBoolean();
                        break;
                    case "Message":
                        if (property.Value.ValueKind == JsonValueKind.Null)
                            replyMessage = null;
                        else if (property.Value.ValueKind == JsonValueKind.String)
                            replyMessage = property.Value.GetString();
                        else
                        {
                            error = "Champ Message invalide.";
                            return false;
                        }
                        break;
                    default:
                        error = "Membre JSON inconnu.";
                        return false;
                }
            }

            message = new HelperMessage(request, success, replyMessage);
            return true;
        }
        catch (JsonException)
        {
            error = "Message JSON invalide.";
            return false;
        }
    }

    internal static async Task<string?> ReadLimitedLineAsync(Stream stream, int maxLineBytes, int maxTotalBytes,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLineBytes);
        if (maxTotalBytes < maxLineBytes) throw new ArgumentOutOfRangeException(nameof(maxTotalBytes));

        using var line = new MemoryStream(Math.Min(maxLineBytes, 4096));
        byte[] buffer = new byte[4096];
        int totalBytes = 0;
        while (true)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(), ct);
            if (read == 0)
                return line.Length == 0 ? null : DecodeLine(line);
            for (int index = 0; index < read; index++)
            {
                byte value = buffer[index];
                if (value == (byte)'\n')
                    return DecodeLine(line);
                totalBytes++;
                if (totalBytes > maxTotalBytes)
                    throw new InvalidDataException("Message too large.");
                if (line.Length >= maxLineBytes)
                    throw new InvalidDataException("Line too large.");
                line.WriteByte(value);
            }
        }
    }

    internal static string Cap(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "Opération refusée.";
        if (text.Length <= MaxErrorLength) return text;
        int length = MaxErrorLength;
        if (char.IsHighSurrogate(text[length - 1])) length--;
        return text[..length];
    }

    private static async Task WriteReplyAsync(NamedPipeClientStream pipe, HelperMessage reply)
    {
        string line = JsonSerializer.Serialize(reply);
        using var writeCancellation = new CancellationTokenSource(ReadTimeout);
        try
        {
            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            await writer.WriteLineAsync(line.AsMemory(), writeCancellation.Token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static HelperMessage Failure(string? message) =>
        new(Success: false, Message: Cap(message));

    private static bool TryParseRequest(JsonElement element, out MachineStartupRequest? request, out string error)
    {
        request = null;
        error = "";
        if (element.ValueKind == JsonValueKind.Null) return true;
        if (element.ValueKind != JsonValueKind.Object)
        {
            error = "Demande invalide.";
            return false;
        }

        MachineStartupAction? action = null;
        MachineStartupKey? key = null;
        string? valueName = null;
        string? expectedValue = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!seen.Add(property.Name))
            {
                error = "Membre de demande dupliqué.";
                return false;
            }
            switch (property.Name)
            {
                case "Action":
                    if (!TryReadEnum(property.Value, out int actionValue, out error)) return false;
                    if (!Enum.IsDefined(typeof(MachineStartupAction), actionValue))
                    {
                        error = "Action inconnue.";
                        return false;
                    }
                    action = (MachineStartupAction)actionValue;
                    break;
                case "Key":
                    if (!TryReadEnum(property.Value, out int keyValue, out error)) return false;
                    if (!Enum.IsDefined(typeof(MachineStartupKey), keyValue))
                    {
                        error = "Clé de démarrage inconnue.";
                        return false;
                    }
                    key = (MachineStartupKey)keyValue;
                    break;
                case "ValueName":
                    if (property.Value.ValueKind != JsonValueKind.String)
                    {
                        error = "Nom d'élément invalide.";
                        return false;
                    }
                    valueName = property.Value.GetString();
                    break;
                case "ExpectedValue":
                    if (property.Value.ValueKind == JsonValueKind.Null)
                        expectedValue = null;
                    else if (property.Value.ValueKind == JsonValueKind.String)
                        expectedValue = property.Value.GetString();
                    else
                    {
                        error = "Valeur attendue invalide.";
                        return false;
                    }
                    break;
                default:
                    error = "Membre de demande inconnu.";
                    return false;
            }
        }

        if (action is null || key is null || valueName is null)
        {
            error = "Demande incomplète.";
            return false;
        }
        request = new MachineStartupRequest(action.Value, key.Value, valueName, expectedValue);
        return true;
    }

    private static bool TryReadEnum(JsonElement element, out int value, out string error)
    {
        value = 0;
        error = "";
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out value))
        {
            error = "Valeur d'énumération invalide.";
            return false;
        }
        return true;
    }

    private static string DecodeLine(MemoryStream line)
    {
        byte[] bytes = line.ToArray();
        int length = bytes.Length;
        if (length > 0 && bytes[length - 1] == (byte)'\r') length--;
        try
        {
            return StrictUtf8.GetString(bytes, 0, length);
        }
        catch (DecoderFallbackException)
        {
            throw new InvalidDataException("Invalid UTF-8 message.");
        }
    }

    internal static bool IsHelperPipeName(string pipeName) =>
        pipeName.StartsWith(PipePrefix, StringComparison.Ordinal) &&
        Guid.TryParseExact(pipeName[PipePrefix.Length..], "N", out _);
}
