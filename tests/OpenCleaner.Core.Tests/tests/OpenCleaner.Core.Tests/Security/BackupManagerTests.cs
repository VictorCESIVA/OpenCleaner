using Microsoft.Extensions.Logging;
using Moq;
using OpenCleaner.Core.Security;
using Xunit;

public class BackupManagerTests
{
    [Fact]
    public async Task RestoreAsync_BackupMovedToAnotherDateFolder_ReturnsTrueAndRestoresFile()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "OpenCleaner.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var manager = new BackupManager(Mock.Of<ILogger<BackupManager>>(), tempRoot);
            var originalFile = Path.Combine(tempRoot, "original.txt");
            await File.WriteAllTextAsync(originalFile, "version-1");

            var backup = await manager.CreateBackupAsync(originalFile);
            await File.WriteAllTextAsync(originalFile, "version-2");

            var originalBackupDir = Directory.GetParent(Directory.GetParent(backup.BackupPath)!.FullName)!.FullName;
            var movedDateDir = Path.Combine(tempRoot, "1999-01-01");
            Directory.CreateDirectory(movedDateDir);
            var movedBackupDir = Path.Combine(movedDateDir, backup.BackupId);
            Directory.Move(originalBackupDir, movedBackupDir);

            var restored = await manager.RestoreAsync(backup.BackupId);

            Assert.True(restored);
            var content = await File.ReadAllTextAsync(originalFile);
            Assert.Equal("version-1", content);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}
