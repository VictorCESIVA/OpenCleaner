using Microsoft.Win32;
using OpenCleaner.Core.Tools;
using Xunit;

namespace OpenCleaner.Core.Tests.Tools;

public sealed class MachineStartupServiceTests : IDisposable
{
    private readonly string _basePath = @"Software\OpenCleaner\Tests\" + Guid.NewGuid().ToString("N");
    private readonly RegistryKey _root;
    private readonly MachineStartupService _service;

    public MachineStartupServiceTests()
    {
        _root = Registry.CurrentUser.CreateSubKey(_basePath)!;
        _service = new MachineStartupService(_root, "Run", "RunOnce", "Backup");
    }

    private void SetValue(string keyName, string name, object value, RegistryValueKind kind)
    {
        using RegistryKey key = _root.CreateSubKey(keyName)!;
        key.SetValue(name, value, kind);
    }

    private (object? Value, RegistryValueKind Kind)? ReadValue(string keyName, string name)
    {
        using RegistryKey? key = _root.OpenSubKey(keyName);
        if (key == null) return null;
        object? value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        return value == null ? null : (value, key.GetValueKind(name));
    }

    private bool BackupContains(string keyName, string name) =>
        ReadValue($"Backup\\{keyName}", name) != null;

    private void AssertStored(string keyName, string name, object expected, RegistryValueKind kind)
    {
        var stored = ReadValue(keyName, name);
        Assert.NotNull(stored);
        Assert.Equal(expected, stored.Value.Value);
        Assert.Equal(kind, stored.Value.Kind);
    }

    [Fact]
    public void DisableThenEnableRoundTripsTheValue()
    {
        SetValue("Run", "Application", @"C:\Program Files\App\app.exe", RegistryValueKind.String);

        MachineStartupResult disabled = _service.Apply(new MachineStartupRequest(
            MachineStartupAction.Disable, MachineStartupKey.Run, "Application",
            @"C:\Program Files\App\app.exe"));

        Assert.True(disabled.Success, disabled.Message);
        Assert.Null(ReadValue("Run", "Application"));
        Assert.True(BackupContains("Run", "Application"));

        MachineStartupResult enabled = _service.Apply(new MachineStartupRequest(
            MachineStartupAction.Enable, MachineStartupKey.Run, "Application"));

        Assert.True(enabled.Success, enabled.Message);
        AssertStored("Run", "Application", @"C:\Program Files\App\app.exe", RegistryValueKind.String);
        Assert.False(BackupContains("Run", "Application"));
    }

    [Fact]
    public void ExpandStringKindAndRawValueArePreserved()
    {
        const string raw = @"%LOCALAPPDATA%\App\app.exe";
        SetValue("Run", "Portable", raw, RegistryValueKind.ExpandString);
        string expanded = Environment.ExpandEnvironmentVariables(raw);

        Assert.True(_service.Apply(new MachineStartupRequest(MachineStartupAction.Disable,
            MachineStartupKey.Run, "Portable", expanded)).Success);
        AssertStored("Backup\\Run", "Portable", raw, RegistryValueKind.ExpandString);

        Assert.True(_service.Apply(new MachineStartupRequest(MachineStartupAction.Enable,
            MachineStartupKey.Run, "Portable")).Success);
        AssertStored("Run", "Portable", raw, RegistryValueKind.ExpandString);
    }

    [Fact]
    public void ChangedCommandIsRefusedWithoutWritingAnything()
    {
        SetValue("Run", "Application", @"C:\New\app.exe", RegistryValueKind.String);

        MachineStartupResult result = _service.Apply(new MachineStartupRequest(MachineStartupAction.Disable,
            MachineStartupKey.Run, "Application", @"C:\Old\app.exe"));

        Assert.False(result.Success);
        AssertStored("Run", "Application", @"C:\New\app.exe", RegistryValueKind.String);
        Assert.False(BackupContains("Run", "Application"));
    }

    [Fact]
    public void MissingValueIsRefused()
    {
        MachineStartupResult result = _service.Apply(new MachineStartupRequest(
            MachineStartupAction.Disable, MachineStartupKey.Run, "Absent", "x"));

        Assert.False(result.Success);
        Assert.False(BackupContains("Run", "Absent"));
    }

    [Fact]
    public void ExistingBackupBlocksASecondDisable()
    {
        SetValue("Run", "Application", "value", RegistryValueKind.String);
        SetValue("Backup\\Run", "Application", "older", RegistryValueKind.String);

        MachineStartupResult result = _service.Apply(new MachineStartupRequest(
            MachineStartupAction.Disable, MachineStartupKey.Run, "Application", "value"));

        Assert.False(result.Success);
        AssertStored("Run", "Application", "value", RegistryValueKind.String);
        AssertStored("Backup\\Run", "Application", "older", RegistryValueKind.String);
    }

    [Fact]
    public void EnablingAnItemThatReappearedIsRefusedAndKeepsTheBackup()
    {
        SetValue("Backup\\Run", "Application", "original", RegistryValueKind.String);
        SetValue("Run", "Application", "newer", RegistryValueKind.String);

        MachineStartupResult result = _service.Apply(new MachineStartupRequest(
            MachineStartupAction.Enable, MachineStartupKey.Run, "Application"));

        Assert.False(result.Success);
        AssertStored("Run", "Application", "newer", RegistryValueKind.String);
        AssertStored("Backup\\Run", "Application", "original", RegistryValueKind.String);
    }

    [Fact]
    public void RunOnceUsesItsOwnPaths()
    {
        SetValue("RunOnce", "Once", "once.exe", RegistryValueKind.String);

        Assert.True(_service.Apply(new MachineStartupRequest(MachineStartupAction.Disable,
            MachineStartupKey.RunOnce, "Once", "once.exe")).Success);
        Assert.Null(ReadValue("RunOnce", "Once"));
        Assert.True(BackupContains("RunOnce", "Once"));
        Assert.False(BackupContains("Run", "Once"));

        Assert.True(_service.Apply(new MachineStartupRequest(MachineStartupAction.Enable,
            MachineStartupKey.RunOnce, "Once")).Success);
        AssertStored("RunOnce", "Once", "once.exe", RegistryValueKind.String);
    }

    [Fact]
    public void UnsupportedRegistryKindIsRefused()
    {
        SetValue("Run", "Multi", new[] { "a", "b" }, RegistryValueKind.MultiString);

        MachineStartupResult result = _service.Apply(new MachineStartupRequest(
            MachineStartupAction.Disable, MachineStartupKey.Run, "Multi", "a b"));

        Assert.False(result.Success);
        Assert.NotNull(ReadValue("Run", "Multi"));
        Assert.False(BackupContains("Run", "Multi"));
    }

    [Theory]
    [InlineData(MachineStartupAction.Disable, MachineStartupKey.Run, "", "x")]
    [InlineData(MachineStartupAction.Disable, MachineStartupKey.Run, "nom", null)]
    [InlineData((MachineStartupAction)99, MachineStartupKey.Run, "nom", "x")]
    [InlineData(MachineStartupAction.Disable, (MachineStartupKey)99, "nom", "x")]
    public void InvalidRequestsAreRejected(MachineStartupAction action, MachineStartupKey key,
        string name, string? expected)
    {
        var request = new MachineStartupRequest(action, key, name, expected);
        Assert.False(MachineStartupService.Validate(request, out string error));
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.False(_service.Apply(request).Success);
    }

    [Fact]
    public void OverlongValueNameIsRejected()
    {
        var request = new MachineStartupRequest(MachineStartupAction.Disable, MachineStartupKey.Run,
            new string('a', 600), "x");
        Assert.False(MachineStartupService.Validate(request, out _));
    }

    public void Dispose()
    {
        _root.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_basePath, throwOnMissingSubKey: false);
    }
}
