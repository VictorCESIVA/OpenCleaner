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
    public void ElevationProbeDoesNotThrow() => _ = ElevatedStartup.IsElevated();
}
