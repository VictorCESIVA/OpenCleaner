using System.ComponentModel;
using OpenCleaner.Core.Tools;
using OpenCleaner.UI;
using Xunit;

namespace OpenCleaner.UI.Tests;

public sealed class ElevatedStartupTests
{
    [Fact]
    public async Task InvalidRequestIsRejectedBeforeAnyElevation()
    {
        bool started = false;
        var request = new MachineStartupRequest(MachineStartupAction.Disable, MachineStartupKey.Run, "", "x");

        MachineStartupResult result = await ElevatedStartup.RunAsync(request, CancellationToken.None,
            _ => { started = true; return null; });

        Assert.False(result.Success);
        Assert.False(started);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    public async Task RefusedElevationKeepsTheOriginalError()
    {
        var request = new MachineStartupRequest(MachineStartupAction.Enable, MachineStartupKey.Run, "Application");

        var error = await Assert.ThrowsAsync<Win32Exception>(() =>
            ElevatedStartup.RunAsync(request, CancellationToken.None,
                _ => throw new Win32Exception(1223)));

        Assert.Equal(1223, error.NativeErrorCode);
    }

    [Fact]
    public void HelperPipeNameIsStrictlyValidated()
    {
        Assert.True(ElevatedStartup.IsHelperPipeName("OpenCleaner-Startup-" + Guid.NewGuid().ToString("N")));
        Assert.False(ElevatedStartup.IsHelperPipeName("OpenCleaner-Startup-"));
        Assert.False(ElevatedStartup.IsHelperPipeName("OpenCleaner-Startup-pas-un-guid"));
        Assert.False(ElevatedStartup.IsHelperPipeName("Autre-OpenCleaner-Startup-" + Guid.NewGuid().ToString("N")));
    }

    [Fact]
    public void OversizedHelperMessageIsRejected()
    {
        string line = new string('x', ElevatedStartup.MaxLineBytes + 1);

        bool parsed = ElevatedStartup.TryParseHelperMessage(line, out _, out string error);

        Assert.False(parsed);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public async Task OversizedPipeLineIsRejected()
    {
        byte[] payload = new byte[ElevatedStartup.MaxLineBytes + 1];
        Array.Fill(payload, (byte)'x');
        using var stream = new MemoryStream(payload);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            ElevatedStartup.ReadLimitedLineAsync(stream, ElevatedStartup.MaxLineBytes,
                ElevatedStartup.MaxMessageBytes, CancellationToken.None));
    }

    [Theory]
    [InlineData("{\"Request\":")]
    [InlineData("[]")]
    [InlineData("{\"Request\":{\"Action\":1,\"Key\":0,\"ValueName\":\"Application\",\"ExpectedValue\":null,\"Command\":\"calc.exe\"},\"Success\":false,\"Message\":null}")]
    [InlineData("{\"Request\":{\"Action\":\"Enable\",\"Key\":0,\"ValueName\":\"Application\",\"ExpectedValue\":null},\"Success\":false,\"Message\":null}")]
    [InlineData("{\"Request\":{\"Action\":99,\"Key\":0,\"ValueName\":\"Application\",\"ExpectedValue\":null},\"Success\":false,\"Message\":null}")]
    [InlineData("{\"Request\":{\"Action\":1,\"Key\":99,\"ValueName\":\"Application\",\"ExpectedValue\":null},\"Success\":false,\"Message\":null}")]
    [InlineData("{\"Request\":{\"Action\":1,\"Key\":0,\"ValueName\":42,\"ExpectedValue\":null},\"Success\":false,\"Message\":null}")]
    [InlineData("{\"Request\":{\"Action\":1,\"Key\":0,\"ValueName\":\"Application\",\"ExpectedValue\":42},\"Success\":false,\"Message\":null}")]
    [InlineData("{\"Request\":[],\"Success\":false,\"Message\":null}")]
    [InlineData("{\"Request\":{\"Action\":1,\"Key\":0,\"ValueName\":\"Application\",\"ExpectedValue\":null},\"Success\":\"false\",\"Message\":null}")]
    [InlineData("{\"Request\":{\"Action\":1,\"Key\":0,\"ValueName\":\"Application\",\"ExpectedValue\":null},\"Success\":false,\"Message\":null,\"Extra\":true}")]
    public void MalformedOrUnknownHelperMessagesAreRejected(string line)
    {
        ElevatedStartup.HelperMessage reply = ElevatedStartup.HandleHelperMessage(line);

        Assert.False(reply.Success);
        Assert.False(string.IsNullOrWhiteSpace(reply.Message));
        Assert.True(reply.Message.Length <= ElevatedStartup.MaxErrorLength);
    }

    [Fact]
    public void InvalidOperationIsRejectedBeforeRegistryAccess()
    {
        const string line =
            "{\"Request\":{\"Action\":0,\"Key\":0,\"ValueName\":\"Application\",\"ExpectedValue\":null},\"Success\":false,\"Message\":null}";

        ElevatedStartup.HelperMessage reply = ElevatedStartup.HandleHelperMessage(line);

        Assert.False(reply.Success);
        Assert.Equal("Valeur attendue absente ou trop longue.", reply.Message);
    }

    [Fact]
    public void ValidHelperMessageParsesStrictly()
    {
        const string line =
            "{\"Request\":{\"Action\":1,\"Key\":0,\"ValueName\":\"Application\",\"ExpectedValue\":null},\"Success\":false,\"Message\":null}";

        bool parsed = ElevatedStartup.TryParseHelperMessage(line, out ElevatedStartup.HelperMessage? message,
            out string error);

        Assert.True(parsed);
        Assert.Equal("", error);
        Assert.NotNull(message?.Request);
        Assert.Equal(MachineStartupAction.Enable, message!.Request!.Action);
        Assert.Equal(MachineStartupKey.Run, message.Request.Key);
        Assert.Equal("Application", message.Request.ValueName);
    }

    [Fact]
    public void ElevationProbeDoesNotThrow() => _ = ElevatedStartup.IsElevated();
}
