using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    public async Task NewBackupIsEncryptedSignedAndRestorable()
    {
        var manager = Manager();
        string file = Path.Combine(_root, "secret.txt");
        await File.WriteAllTextAsync(file, "top-secret-value");

        var backup = await manager.CreateBackupAsync(file);
        byte[] payload = await File.ReadAllBytesAsync(backup.BackupPath);
        Assert.StartsWith("OCBK0001", Encoding.ASCII.GetString(payload, 0, 8));
        Assert.DoesNotContain("top-secret-value", Encoding.UTF8.GetString(payload));

        var metadata = JsonNode.Parse(await File.ReadAllTextAsync(MetadataPath(backup)))!;
        Assert.True(metadata["Encrypted"]!.GetValue<bool>());
        Assert.False(string.IsNullOrWhiteSpace(metadata["Signature"]!.GetValue<string>()));

        Assert.True(await manager.VerifyBackupAsync(backup));
        Assert.True(await manager.RestoreAsync(backup.BackupId));
        Assert.Equal("top-secret-value", await File.ReadAllTextAsync(file));
    }

    [Fact]
    public async Task RestoreAsync_RejectsTargetInsideBackupRoot()
    {
        var manager = Manager();
        var legacy = await CreateLegacyBackupAsync("legacy.txt", "source");
        var metadata = JsonNode.Parse(await File.ReadAllTextAsync(legacy.MetadataPath))!;
        string forgedTarget = Path.Combine(Backups, "forged-restore.txt");
        metadata["OriginalPath"] = forgedTarget;
        await File.WriteAllTextAsync(legacy.MetadataPath, metadata.ToJsonString());

        Assert.False(await manager.RestoreAsync(legacy.Id));
        Assert.False(File.Exists(forgedTarget));
    }

    [Fact]
    public async Task RestoreAsync_RejectsProtectedTarget()
    {
        var manager = Manager();
        var legacy = await CreateLegacyBackupAsync("legacy.txt", "source");
        string protectedTarget = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "OpenCleaner-restore-test.txt");
        var metadata = JsonNode.Parse(await File.ReadAllTextAsync(legacy.MetadataPath))!;
        metadata["OriginalPath"] = protectedTarget;
        await File.WriteAllTextAsync(legacy.MetadataPath, metadata.ToJsonString());

        Assert.False(await manager.RestoreAsync(legacy.Id));
        Assert.False(File.Exists(protectedTarget));
    }

    [Fact]
    public async Task MultiChunkBackupRoundTrips()
    {
        var manager = Manager();
        string file = Path.Combine(_root, "large.bin");
        byte[] content = new byte[2_500_000];
        Random.Shared.NextBytes(content);
        await File.WriteAllBytesAsync(file, content);

        var backup = await manager.CreateBackupAsync(file);
        Assert.True(await manager.VerifyBackupAsync(backup));
        Assert.True(await manager.RestoreAsync(backup.BackupId));
        Assert.Equal(content, await File.ReadAllBytesAsync(file));
    }

    [Fact]
    public async Task MetadataTamperingIsRejected()
    {
        var manager = Manager();
        string file = Path.Combine(_root, "source.txt");
        await File.WriteAllTextAsync(file, "source");
        var backup = await manager.CreateBackupAsync(file);
        string forgedTarget = Path.Combine(_root, "forged.txt");
        var metadata = JsonNode.Parse(await File.ReadAllTextAsync(MetadataPath(backup)))!;
        metadata["OriginalPath"] = forgedTarget;
        await File.WriteAllTextAsync(MetadataPath(backup), metadata.ToJsonString());

        Assert.False(await manager.RestoreAsync(backup.BackupId));
        Assert.False(await manager.VerifyBackupAsync(backup));
        Assert.False(File.Exists(forgedTarget));
    }

    [Fact]
    public async Task VerifyBackupAsyncRejectsTamperedPayload()
    {
        var manager = Manager();
        string file = Path.Combine(_root, "source.txt");
        await File.WriteAllTextAsync(file, "source");
        var backup = await manager.CreateBackupAsync(file);
        Assert.True(await manager.VerifyBackupAsync(backup));

        byte[] payload = await File.ReadAllBytesAsync(backup.BackupPath);
        payload[^1] ^= 0xFF;
        await File.WriteAllBytesAsync(backup.BackupPath, payload);

        Assert.False(await manager.VerifyBackupAsync(backup));
        Assert.False(await manager.RestoreAsync(backup.BackupId));
    }

    [Fact]
    public async Task LegacyBackupIsMigratedToEncryptedFormat()
    {
        var manager = Manager();
        var legacy = await CreateLegacyBackupAsync("legacy.txt", "legacy-content");

        Assert.Equal(1, await manager.UpgradeLegacyBackupsAsync());
        Assert.False(File.Exists(legacy.PayloadPath));
        string encryptedPath = legacy.PayloadPath + ".ocb";
        Assert.True(File.Exists(encryptedPath));
        Assert.StartsWith("OCBK0001", Encoding.ASCII.GetString(await File.ReadAllBytesAsync(encryptedPath), 0, 8));

        var metadata = JsonNode.Parse(await File.ReadAllTextAsync(legacy.MetadataPath))!;
        Assert.True(metadata["Encrypted"]!.GetValue<bool>());
        Assert.False(string.IsNullOrWhiteSpace(metadata["Signature"]!.GetValue<string>()));
        Assert.Equal(0, await manager.UpgradeLegacyBackupsAsync());
        Assert.True(await manager.RestoreAsync(legacy.Id));
        Assert.Equal("legacy-content", await File.ReadAllTextAsync(Path.Combine(_root, "legacy.txt")));
    }

    private async Task<(string Id, string MetadataPath, string PayloadPath)> CreateLegacyBackupAsync(
        string fileName, string content)
    {
        string id = Guid.NewGuid().ToString("N");
        string directory = Path.Combine(Backups, "2020-01-01", id);
        string dataDirectory = Path.Combine(directory, "data");
        Directory.CreateDirectory(dataDirectory);
        string payload = Path.Combine(dataDirectory, fileName);
        await File.WriteAllTextAsync(payload, content);
        byte[] contentBytes = Encoding.UTF8.GetBytes(content);
        string hash = Convert.ToHexString(SHA256.HashData(contentBytes));
        var metadata = new
        {
            OriginalPath = Path.Combine(_root, fileName),
            OriginalHash = hash,
            CreationDate = DateTime.UtcNow.AddDays(-1),
            FileSize = contentBytes.Length,
            FileName = fileName,
            Encrypted = false
        };
        string metadataPath = Path.Combine(directory, "metadata.json");
        await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(metadata));
        return (id, metadataPath, payload);
    }

    private static string MetadataPath(OpenCleaner.Contracts.BackupInfo backup) =>
        Path.Combine(Directory.GetParent(Directory.GetParent(backup.BackupPath)!.FullName)!.FullName, "metadata.json");

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
