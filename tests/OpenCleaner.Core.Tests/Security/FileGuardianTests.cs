using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenCleaner.Contracts;
using OpenCleaner.Core.Security;
using Xunit;

namespace OpenCleaner.Core.Tests.Security;

public sealed class FileGuardianTests
{
    private readonly Mock<IBackupManager> _backupMock = new();
    private readonly FileGuardian _guardian;

    public FileGuardianTests()
    {
        _guardian = new FileGuardian(_backupMock.Object, NullLogger<FileGuardian>.Instance);
    }

    [Fact]
    public void IsSystemCriticalPath_System32_ReturnsTrue() =>
        Assert.True(_guardian.IsSystemCriticalPath(@"C:\Windows\System32\kernel32.dll"));

    [Fact]
    public void IsSystemCriticalPath_WindowsRootFile_ReturnsTrue() =>
        Assert.True(_guardian.IsSystemCriticalPath(@"C:\Windows\notepad.exe"));

    [Fact]
    public void IsSystemCriticalPath_UsrClassDat_ReturnsTrue()
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "Windows", "UsrClass.dat");
        Assert.True(_guardian.IsSystemCriticalPath(path));
    }

    [Fact]
    public void IsSystemCriticalPath_WindowsTemp_ReturnsFalse() =>
        Assert.False(_guardian.IsSystemCriticalPath(@"C:\Windows\Temp\opencleaner-test.txt"));

    [Fact]
    public void IsSystemCriticalPath_UserTemp_ReturnsFalse() =>
        Assert.False(_guardian.IsSystemCriticalPath(Path.Combine(Path.GetTempPath(), "test.txt")));

    [Fact]
    public void IsSystemCriticalPath_ProgramData_ReturnsTrue() =>
        Assert.True(_guardian.IsSystemCriticalPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Vendor", "config.json")));

    [Fact]
    public void IsSystemCriticalPath_NonWindowsSiblingPrefix_ReturnsFalse() =>
        Assert.False(_guardian.IsSystemCriticalPath(@"C:\WindowsBackup\kernel32.dll"));

    [Theory]
    [InlineData(@"//localhost/C$/Windows/System32/kernel32.dll")]
    [InlineData(@"\\?\UNC\server\share\file.txt")]
    [InlineData(@"\\?\C:\Windows\System32\kernel32.dll")]
    [InlineData(@"C:\Windows\System32.\kernel32.dll")]
    [InlineData(@"C:\Windows\System32 \kernel32.dll")]
    [InlineData(@"C:\PROGRA~1\app.exe")]
    public void IsSystemCriticalPath_Win32Aliases_ReturnsTrue(string path) =>
        Assert.True(_guardian.IsSystemCriticalPath(path));

    [Fact]
    public async Task SafeDeleteAsync_System32File_ReturnsFailureWithoutDeleting()
    {
        var result = await _guardian.SafeDeleteAsync(@"C:\Windows\System32\kernel32.dll");

        Assert.False(result.Success);
        Assert.Contains("critical", result.Message.ToLowerInvariant());
        _backupMock.Verify(b => b.CreateBackupAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
