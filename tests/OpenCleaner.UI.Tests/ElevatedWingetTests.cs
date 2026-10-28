using System.ComponentModel;
using System.IO.Pipes;
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
        [Fact]
        public async Task RefusedElevationIsReported()
        {
            var error = await Assert.ThrowsAsync<Win32Exception>(() => ElevatedWinget.RunAsync(null,
                CancellationToken.None, _ => throw new Win32Exception(1223)));
            Assert.Equal(1223, error.NativeErrorCode);
        }

        [Fact]
        public async Task ReceivesOutputAndCompletion()
        {
            var name = Guid.NewGuid().ToString("N");
            await using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            await using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            var connected = server.WaitForConnectionAsync();
            await client.ConnectAsync();
            await connected;
            var result = ElevatedWinget.ReadMessagesAsync(server, null, CancellationToken.None);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            await writer.WriteLineAsync("{\"Kind\":\"output\",\"Text\":\"updated\"}");
            await writer.WriteLineAsync("{\"Kind\":\"result\",\"ExitCode\":0}");
            var completed = await result;
            Assert.Equal(0, completed.ExitCode);
            Assert.Equal("updated", completed.Output);
        }

        [Fact]
        public async Task CancellationIsSentToHelper()
        {
            var name = Guid.NewGuid().ToString("N");
            await using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            await using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            var connected = server.WaitForConnectionAsync();
            await client.ConnectAsync();
            await connected;
            using var cancellation = new CancellationTokenSource();
            var result = ElevatedWinget.ReadMessagesAsync(server, null, cancellation.Token);
            cancellation.Cancel();
            using var reader = new StreamReader(client, leaveOpen: true);
            Assert.Equal("cancel", await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)));
            using var writer = new StreamWriter(client) { AutoFlush = true };
            await writer.WriteLineAsync("{\"Kind\":\"error\",\"Text\":\"cancelled\"}");
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => result);
        }

        [Fact]
        public async Task DisconnectIsReported()
        {
            await using var pipe = new MemoryStream();
            await Assert.ThrowsAsync<InvalidOperationException>(() => ElevatedWinget.ReadMessagesAsync(pipe, null, CancellationToken.None));
        }
    }
}
