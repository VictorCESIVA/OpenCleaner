using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using OpenCleaner.Core.Security;
using Xunit;

namespace OpenCleaner.Core.Tests.Security;

public sealed class BackupManagerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "OpenCleaner.Tests", Guid.NewGuid().ToString("N"));
    private string Backups => Path.Combine(_root, "backups");

    public BackupManagerTests() => Directory.CreateDirectory(_root);

    private BackupManager Manager(ILogger<BackupManager>? logger = null) =>
        new(logger ?? new CapturingLogger<BackupManager>(), Backups);

    [Fact]
    public async Task RestoreAsync_BackupMovedToAnotherDateFolder_ReturnsTrueAndRestoresFile()
    {
        var logger = new CapturingLogger<BackupManager>();
        var manager = Manager(logger);
        var originalFile = Path.Combine(_root, "original.txt");
        await File.WriteAllTextAsync(originalFile, "version-1");

        var backup = await manager.CreateBackupAsync(originalFile);
        await File.WriteAllTextAsync(originalFile, "version-2");

        var originalBackupDir = Directory.GetParent(Directory.GetParent(backup.BackupPath)!.FullName)!.FullName;
        var movedDateDir = Path.Combine(Backups, "1999-01-01");
        Directory.CreateDirectory(movedDateDir);
        Directory.Move(originalBackupDir, Path.Combine(movedDateDir, backup.BackupId));

        Assert.True(await manager.RestoreAsync(backup.BackupId), string.Join("\n", logger.Messages));
        Assert.Equal("version-1", await File.ReadAllTextAsync(originalFile));
    }

    [Fact]
    public async Task RestoreAsync_RejectsTargetInsideBackupRoot()
    {
        string outside = Path.Combine(Path.GetTempPath(), "OpenCleaner.Tests", Guid.NewGuid().ToString("N") + ".txt");
        await File.WriteAllTextAsync(outside, "source");
        try
        {
            var manager = Manager();
            var backup = await manager.CreateBackupAsync(outside);
            string metadataPath = Path.Combine(
                Directory.GetParent(Directory.GetParent(backup.BackupPath)!.FullName)!.FullName, "metadata.json");
            var metadata = JsonNode.Parse(await File.ReadAllTextAsync(metadataPath))!;
            string forgedTarget = Path.Combine(Backups, "forged-restore.txt");
            metadata["OriginalPath"] = forgedTarget;
            await File.WriteAllTextAsync(metadataPath, metadata.ToJsonString());

            Assert.False(await manager.RestoreAsync(backup.BackupId));
            Assert.False(File.Exists(forgedTarget));
        }
        finally
        {
            if (File.Exists(outside)) File.Delete(outside);
        }
    }

    [Fact]
    public async Task RestoreAsync_RejectsProtectedTarget()
    {
        string outside = Path.Combine(Path.GetTempPath(), "OpenCleaner.Tests", Guid.NewGuid().ToString("N") + ".txt");
        string protectedTarget = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "OpenCleaner-restore-test.txt");
        await File.WriteAllTextAsync(outside, "source");
        try
        {
            var manager = Manager();
            var backup = await manager.CreateBackupAsync(outside);
            string metadataPath = Path.Combine(
                Directory.GetParent(Directory.GetParent(backup.BackupPath)!.FullName)!.FullName, "metadata.json");
            var metadata = JsonNode.Parse(await File.ReadAllTextAsync(metadataPath))!;
            metadata["OriginalPath"] = protectedTarget;
            await File.WriteAllTextAsync(metadataPath, metadata.ToJsonString());

            Assert.False(await manager.RestoreAsync(backup.BackupId));
            Assert.False(File.Exists(protectedTarget));
        }
        finally
        {
            if (File.Exists(outside)) File.Delete(outside);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add($"{logLevel}: {formatter(state, exception)} {exception}");
        }
    }
}
