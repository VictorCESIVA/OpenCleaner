using System.ComponentModel;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using OpenCleaner.Core.Tools;
using OpenCleaner.UI;
using Xunit;

namespace OpenCleaner.UI
{
    internal sealed class Program;
}

namespace OpenCleaner.UI.Tests
{
    public sealed class ElevatedWingetTests
    {
        private sealed record SessionOutcome(string Output, bool RanWinget, bool ResolvedWinget);

        [Fact]
        public async Task RefusedElevationIsReported()
        {
            var target = new WingetUpgradeTarget("Contoso.App");

            var error = await Assert.ThrowsAsync<Win32Exception>(() => ElevatedWinget.RunAsync(
                [target], null, CancellationToken.None, _ => throw new Win32Exception(1223)));

            Assert.Equal(1223, error.NativeErrorCode);
        }

        [Fact]
        public async Task InvalidTargetIsRejectedBeforeElevation()
        {
            bool started = false;

            await Assert.ThrowsAsync<ArgumentException>(() => ElevatedWinget.RunAsync(
                [new WingetUpgradeTarget("bad id")], null, CancellationToken.None,
                _ => { started = true; return null; }));

            Assert.False(started);
        }

        [Fact]
        public async Task TooManyTargetsAreRejectedBeforeElevation()
        {
            bool started = false;
            WingetUpgradeTarget[] targets = Enumerable.Range(0, ElevatedWinget.MaxTargets + 1)
                .Select(index => new WingetUpgradeTarget($"Contoso.App{index}"))
                .ToArray();

            await Assert.ThrowsAsync<ArgumentException>(() => ElevatedWinget.RunAsync(
                targets, null, CancellationToken.None, _ => { started = true; return null; }));

            Assert.False(started);
        }

        [Fact]
        public async Task InvalidTargetIdIsRejectedByHelper()
        {
            const string request = """
                {"kind":"upgrade","requestId":"req-1","targets":[{"id":"bad id","source":"winget","scope":"default","includeUnknown":false}]}
                """;

            SessionOutcome outcome = await RunSessionAsync(request);

            Assert.False(outcome.RanWinget);
            Assert.False(outcome.ResolvedWinget);
            Assert.Contains("error", outcome.Output, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task TooManyTargetsAreRejectedByHelper()
        {
            string targets = string.Join(",", Enumerable.Range(0, ElevatedWinget.MaxTargets + 1)
                .Select(index => $"{{\"id\":\"Contoso.App{index}\",\"source\":\"winget\",\"scope\":\"default\",\"includeUnknown\":false}}"));
            string request = $"{{\"kind\":\"upgrade\",\"requestId\":\"req-1\",\"targets\":[{targets}]}}";

            SessionOutcome outcome = await RunSessionAsync(request);

            Assert.False(outcome.RanWinget);
            Assert.False(outcome.ResolvedWinget);
        }

        [Fact]
        public async Task OversizedRequestIsRejectedWithoutLaunchingWinget()
        {
            string request = new string('x', ElevatedWinget.MaxLineChars + 1024);

            SessionOutcome outcome = await RunSessionAsync(request);

            Assert.False(outcome.RanWinget);
            Assert.False(outcome.ResolvedWinget);
        }

        [Fact]
        public async Task UnknownFieldsAreRejected()
        {
            const string request = """
                {"kind":"upgrade","requestId":"req-1","targets":[{"id":"Contoso.App","source":"winget","scope":"default","includeUnknown":false}],"arguments":["--all"]}
                """;

            SessionOutcome outcome = await RunSessionAsync(request);

            Assert.False(outcome.RanWinget);
            Assert.False(outcome.ResolvedWinget);
        }

        [Fact]
        public async Task MalformedJsonIsRejected()
        {
            SessionOutcome outcome = await RunSessionAsync("{not json");

            Assert.False(outcome.RanWinget);
            Assert.False(outcome.ResolvedWinget);
            Assert.Contains("error", outcome.Output, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task NonElevatedHelperRefusesToRun()
        {
            using var input = new MemoryStream(Encoding.UTF8.GetBytes(
                "{\"kind\":\"upgrade\",\"requestId\":\"req-1\",\"targets\":[{\"id\":\"Contoso.App\"}]}\n"));
            using var output = new MemoryStream();
            bool ran = false;

            await ElevatedWinget.RunHelperSessionAsync(
                input, output, false,
                _ => Task.FromResult("winget.exe"),
                (_, _, _, _) => { ran = true; return Task.FromResult(new WingetResult(0, "")); });

            Assert.False(ran);
            output.Position = 0;
            using var reader = new StreamReader(output);
            Assert.Contains("error", await reader.ReadToEndAsync(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task PartialFailureContinuesTheBatch()
        {
            var targets = new[]
            {
                new WingetUpgradeTarget("Contoso.First"),
                new WingetUpgradeTarget("Contoso.Second")
            };
            string request = SerializeTargets("req-partial", targets);

            var runs = new List<string>();
            var (batch, messages) = await RunPipeSessionAsync(request, async (path, arguments, progress, ct) =>
            {
                await Task.Yield();
                string id = arguments[2];
                lock (runs) runs.Add(id);
                progress?.Report("sortie " + id);
                return id == "Contoso.Second"
                    ? new WingetResult(5, "échec")
                    : new WingetResult(0, "ok");
            });

            Assert.Equal(2, runs.Count);
            Assert.NotNull(batch);
            Assert.Equal(1, batch!.Succeeded);
            Assert.Equal(1, batch.Failed);
            Assert.Equal(0, batch.Cancelled);
            Assert.Contains(messages, message => Kind(message) == "status" && Phase(message) == WingetUpgradePhase.Succeeded);
            Assert.Contains(messages, message => Kind(message) == "status" && Phase(message) == WingetUpgradePhase.Failed);
            Assert.Contains(messages, message => Kind(message) == "result");
        }

        [Fact]
        public async Task CancelKillsTheCurrentPackageAndSkipsTheRest()
        {
            var targets = new[]
            {
                new WingetUpgradeTarget("Contoso.First"),
                new WingetUpgradeTarget("Contoso.Second")
            };
            string request = SerializeTargets("req-cancel", targets);

            var (batch, messages) = await RunPipeSessionAsync(request,
                async (path, arguments, progress, ct) =>
                {
                    progress?.Report("en cours");
                    await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                    return new WingetResult(0, "");
                },
                writer => writer.WriteLineAsync("{\"kind\":\"cancel\"}"));

            Assert.NotNull(batch);
            Assert.Equal(0, batch!.Succeeded);
            Assert.Equal(0, batch.Failed);
            Assert.True(batch.Cancelled >= 1);
            Assert.Contains(messages, message => Kind(message) == "status" &&
                Phase(message) is WingetUpgradePhase.Cancelled or WingetUpgradePhase.Skipped);
            Assert.Contains(messages, message => Kind(message) == "result");
        }

        [Fact]
        public async Task ParentReadsBatchAndProgress()
        {
            string messages = string.Join('\n',
                "{\"kind\":\"accepted\",\"requestId\":\"req-1\"}",
                "{\"kind\":\"status\",\"id\":\"Contoso.App\",\"phase\":\"running\"}",
                "{\"kind\":\"output\",\"id\":\"Contoso.App\",\"text\":\"téléchargement\"}",
                "{\"kind\":\"status\",\"id\":\"Contoso.App\",\"phase\":\"succeeded\",\"exitCode\":0}",
                "{\"kind\":\"result\",\"succeeded\":1,\"failed\":0,\"cancelled\":0}") + "\n";
            using var input = new MemoryStream(Encoding.UTF8.GetBytes(messages));
            var updates = new List<WingetPackageUpdate>();

            WingetBatchResult batch = await ElevatedWinget.ReadBatchAsync(
                input, null, new ListProgress(updates), CancellationToken.None);

            Assert.Equal(1, batch.Succeeded);
            Assert.Equal(0, batch.Failed);
            Assert.Single(batch.Packages);
            Assert.Equal(WingetUpgradePhase.Succeeded, batch.Packages[0].Phase);
            Assert.Contains(updates, update => update.Phase == WingetUpgradePhase.Output);
            Assert.Contains(updates, update => update.Phase == WingetUpgradePhase.Running);
        }

        [Fact]
        public async Task DisconnectIsReported()
        {
            using var input = new MemoryStream();
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                ElevatedWinget.ReadBatchAsync(input, null, null, CancellationToken.None));
        }

        private static async Task<SessionOutcome> RunSessionAsync(string request)
        {
            using var input = new MemoryStream(Encoding.UTF8.GetBytes(request + "\n"));
            using var output = new MemoryStream();
            bool ran = false;
            bool resolved = false;

            await ElevatedWinget.RunHelperSessionAsync(
                input, output, true,
                _ => { resolved = true; return Task.FromResult("winget.exe"); },
                (_, _, _, _) => { ran = true; return Task.FromResult(new WingetResult(0, "")); });

            output.Position = 0;
            using var reader = new StreamReader(output);
            return new SessionOutcome(await reader.ReadToEndAsync(), ran, resolved);
        }

        private static async Task<(WingetBatchResult? Batch, List<JsonElement> Messages)> RunPipeSessionAsync(
            string request,
            Func<string, IReadOnlyList<string>, IProgress<string>?, CancellationToken, Task<WingetResult>> run,
            Func<StreamWriter, Task>? onRunning = null)
        {
            string name = Guid.NewGuid().ToString("N");
            await using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            await using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            Task connected = server.WaitForConnectionAsync();
            await client.ConnectAsync();
            await connected;

            Task<WingetBatchResult?> session = ElevatedWinget.RunHelperSessionAsync(
                server, server, true, _ => Task.FromResult("winget.exe"), run);
            using var writer = new StreamWriter(client, leaveOpen: true) { AutoFlush = true };
            await writer.WriteLineAsync(request);
            using var reader = new StreamReader(client, leaveOpen: true);

            var messages = new List<JsonElement>();
            bool cancelSent = false;
            while (true)
            {
                string? line = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
                Assert.NotNull(line);
                using JsonDocument document = JsonDocument.Parse(line);
                JsonElement message = document.RootElement.Clone();
                messages.Add(message);
                string kind = Kind(message);
                if (!cancelSent && onRunning != null && kind == "status" &&
                    Phase(message) == WingetUpgradePhase.Running)
                {
                    cancelSent = true;
                    await onRunning(writer);
                }
                if (kind is "result" or "error") break;
            }

            return (await session.WaitAsync(TimeSpan.FromSeconds(10)), messages);
        }

        private static string SerializeTargets(string requestId, IEnumerable<WingetUpgradeTarget> targets) =>
            JsonSerializer.Serialize(new
            {
                kind = "upgrade",
                requestId,
                targets = targets.Select(target => new
                {
                    id = target.Id,
                    source = target.Source,
                    scope = target.Scope,
                    includeUnknown = target.IncludeUnknown
                })
            });

        private static string Kind(JsonElement message) =>
            message.TryGetProperty("kind", out JsonElement value) ? value.GetString() ?? "" : "";

        private static string? Phase(JsonElement message) =>
            message.TryGetProperty("phase", out JsonElement value) ? value.GetString() : null;

        private sealed class ListProgress(List<WingetPackageUpdate> updates) : IProgress<WingetPackageUpdate>
        {
            public void Report(WingetPackageUpdate value)
            {
                lock (updates) updates.Add(value);
            }
        }
    }
}
