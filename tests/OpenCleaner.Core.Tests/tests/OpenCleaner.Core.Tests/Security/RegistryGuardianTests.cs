using Xunit;
using Moq;
using OpenCleaner.Core.Security;
using Microsoft.Extensions.Logging;

public class RegistryGuardianTests
{
    private readonly RegistryGuardian _guardian;

    public RegistryGuardianTests()
    {
        _guardian = new RegistryGuardian(Mock.Of<ILogger<RegistryGuardian>>());
    }

    [Fact]
    public void IsSystemCriticalKey_Services_ReturnsTrue()
    {
        // Test CRITIQUE : le dossier des services Windows doit être protégé
        var result = _guardian.IsSystemCriticalKey(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services");
        Assert.True(result, "Les services Windows ne doivent jamais être touchés !");
    }

    [Fact]
    public void IsSystemCriticalKey_Software_ReturnsFalse()
    {
        // Test : HKLM\SOFTWARE\MonApp est safe
        var result = _guardian.IsSystemCriticalKey(@"HKEY_LOCAL_MACHINE\SOFTWARE\TestApp");
        Assert.False(result, "Les clés logiciels tierces devraient être nettoyables");
    }

    [Fact]
    public void IsSystemCriticalKey_CLSID_ReturnsTrue()
    {
        // Test : Les CLSID (COM) sont critiques
        var result = _guardian.IsSystemCriticalKey(@"HKEY_CLASSES_ROOT\CLSID\{12345678-1234-1234-1234-123456789012}");
        Assert.True(result, "Les CLSID COM sont critiques pour Windows");
    }
}