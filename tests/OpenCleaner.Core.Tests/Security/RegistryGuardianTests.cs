using Microsoft.Extensions.Logging.Abstractions;
using OpenCleaner.Core.Security;
using Xunit;

namespace OpenCleaner.Core.Tests.Security;

public sealed class RegistryGuardianTests
{
    private readonly RegistryGuardian _guardian = new(NullLogger<RegistryGuardian>.Instance);

    [Theory]
    [InlineData(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services")]
    [InlineData(@"HKEY_LOCAL_MACHINE\SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Run")]
    [InlineData(@"HKEY_USERS\S-1-5-18\Software\Microsoft\Windows\CurrentVersion\Run")]
    [InlineData(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows Defender")]
    [InlineData(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows Defender")]
    [InlineData(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run")]
    [InlineData(@"HKEY_CLASSES_ROOT\CLSID\{12345678-1234-1234-1234-123456789012}")]
    public void SensitiveKeys_AreBlocked(string path) => Assert.True(_guardian.IsSystemCriticalKey(path));

    [Theory]
    [InlineData(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\StreamMRU\child")]
    [InlineData(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\ComDlg32\OpenSavePidlMRU\child")]
    public void AllowlistedHistoryKeys_AreAllowed(string path) => Assert.False(_guardian.IsSystemCriticalKey(path));

    [Theory]
    [InlineData(@"HKEY_LOCAL_MACHINE\SOFTWARE\TestApp")]
    [InlineData(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\StreamMRU2")]
    public void KeysOutsideTheAllowlist_AreBlocked(string path) => Assert.True(_guardian.IsSystemCriticalKey(path));

    [Theory]
    [InlineData("HKCU")]
    [InlineData(@"HKCU\\Software\test")]
    [InlineData("relative")]
    public void MalformedOrRootKeys_AreBlocked(string path) => Assert.True(_guardian.IsSystemCriticalKey(path));
}
