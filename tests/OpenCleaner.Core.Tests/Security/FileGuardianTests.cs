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
    public void IsSystemCriticalPath_WindowsPrefetch_ReturnsFalse() =>
        Assert.False(_guardian.IsSystemCriticalPath(@"C:\Windows\Prefetch\opencleaner-test.pf"));

    [Fact]
    public void IsSystemCriticalPath_UserTemp_ReturnsFalse() =>
        Assert.False(_guardian.IsSystemCriticalPath(Path.Combine(Path.GetTempPath(), "test.txt")));

    [Fact]
    public void IsSystemCriticalPath_ProgramData_ReturnsTrue() =>
        Assert.True(_guardian.IsSystemCriticalPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Vendor", "config.json")));

    [Fact]
    public void IsSystemCriticalPath_PublicProfile_ReturnsTrue()
    {
        string systemDrive = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? @"C:\";
        Assert.True(_guardian.IsSystemCriticalPath(Path.Combine(systemDrive, "Users", "Public", "document.txt")));
    }

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
    public void ScanPathCheckKeepsCriticalProtections()
    {
        Assert.True(_guardian.IsSystemCriticalPathForScan(@"C:\Windows\notepad.exe"));
        Assert.True(_guardian.IsSystemCriticalPathForScan(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Vendor", "config.json")));
        Assert.False(_guardian.IsSystemCriticalPathForScan(Path.Combine(Path.GetTempPath(), "test.txt")));
    }

    [Fact]
    public async Task SafeDeleteAsync_System32File_ReturnsFailureWithoutDeleting()
    {
        var result = await _guardian.SafeDeleteAsync(@"C:\Windows\System32\kernel32.dll");

        Assert.False(result.Success);
        Assert.Contains("critical", result.Message.ToLowerInvariant());
        _backupMock.Verify(b => b.CreateBackupAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SafeDeleteAsync_ExcludedPathIsBlockedBeforeAnyBackup()
    {
        string directory = Path.Combine(Path.GetTempPath(), "OpenCleaner-Excluded-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "important.txt");
        await File.WriteAllTextAsync(file, "conserver");
        try
        {
            using var _ = CleaningExclusions.OverrideForTests([directory]);

            OperationResult result = await _guardian.SafeDeleteAsync(file);

            Assert.False(result.Success);
            Assert.True(File.Exists(file));
            Assert.True(_guardian.IsSystemCriticalPathForScan(file));
            _backupMock.Verify(b => b.CreateBackupAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
