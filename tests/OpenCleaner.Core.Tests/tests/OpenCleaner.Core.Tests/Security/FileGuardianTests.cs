using Xunit;
using Moq;
using OpenCleaner.Core.Security;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Threading.Tasks;
using OpenCleaner.Contracts;
public class FileGuardianTests
{
    private readonly Mock<IBackupManager> _backupMock;
    private readonly Mock<ILogger<FileGuardian>> _loggerMock;
    private readonly FileGuardian _guardian;

    public FileGuardianTests()
    {
        _backupMock = new Mock<IBackupManager>();
        _loggerMock = new Mock<ILogger<FileGuardian>>();
        _guardian = new FileGuardian(_backupMock.Object, _loggerMock.Object);
    }

    [Fact]
    public void IsSystemCriticalPath_System32_ReturnsTrue()
    {
        var result = _guardian.IsSystemCriticalPath(@"C:\Windows\System32\kernel32.dll");
        Assert.True(result, "System32 devrait etre bloque !");
    }

    [Fact]
    public void IsSystemCriticalPath_Temp_ReturnsFalse()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), "test.txt");
        var result = _guardian.IsSystemCriticalPath(tempFile);
        Assert.False(result, "Le dossier Temp devrait etre autorise");
    }

    [Fact]
    public void IsSystemCriticalPath_System32PrefixOnly_ReturnsFalse()
    {
        var fakeSibling = @"C:\Windows\System32_backup\kernel32.dll";
        var result = _guardian.IsSystemCriticalPath(fakeSibling);
        Assert.False(result, "Un simple prefixe du dossier critique ne doit pas etre bloque");
    }

    [Fact]
    public async Task SafeDeleteAsync_System32File_ReturnsFailureWithoutDeleting()
    {
        var result = await _guardian.SafeDeleteAsync(@"C:\Windows\System32\kernel32.dll");
        
        Assert.False(result.Success);
        Assert.Contains("critical", result.Message.ToLower());
        _backupMock.Verify(b => b.CreateBackupAsync(It.IsAny<string>(), default), Times.Never);
    }
}
