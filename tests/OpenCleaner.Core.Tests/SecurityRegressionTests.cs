using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using Moq;
using OpenCleaner.Contracts;
using OpenCleaner.Core.Security;
using Xunit;

namespace OpenCleaner.Core.Tests;

public sealed class SecurityRegressionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "OpenCleaner.Tests", Guid.NewGuid().ToString("N"));
    private string Backups => Path.Combine(_root, "backups");
    private string Original => Path.Combine(_root, "source.txt");
    public SecurityRegressionTests() => Directory.CreateDirectory(_root);
    private BackupManager Manager() => new(NullLogger<BackupManager>.Instance, Backups);
    private static FileGuardian Guardian(IBackupManager backups) => new(backups, NullLogger<FileGuardian>.Instance);

    [Theory]
    [InlineData("relative.txt")]
    [InlineData(@"C:\")]
    [InlineData(@"\\?\C:\Windows\System32\kernel32.dll")]
    [InlineData(@"C:\temp\file.txt:stream")]
    [InlineData(@"C:\Program Files (x86)\app.exe")]
    public void UnsafePathsAreBlocked(string path) => Assert.True(Guardian(Mock.Of<IBackupManager>()).IsSystemCriticalPath(path));

    [Fact]
    public async Task FailedBackupNeverDeletesSource()
    {
        await File.WriteAllTextAsync(Original, "keep me");
        var backups = new Mock<IBackupManager>();
        backups.Setup(b => b.CreateBackupAsync(Original, It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("full disk"));
        var result = await Guardian(backups.Object).SafeDeleteAsync(Original);
        Assert.False(result.Success);
        Assert.Equal("keep me", await File.ReadAllTextAsync(Original));
    }

    [Fact]
    public async Task ChangedFileAfterBackupIsKept()
    {
        await File.WriteAllTextAsync(Original, "before");
        string backupPath = Path.Combine(_root, "snapshot.txt");
        File.Copy(Original, backupPath);
        string hash = await OpenCleaner.Core.HashHelper.ComputeHashAsync(backupPath);
        var backups = new Mock<IBackupManager>();
        backups.Setup(b => b.CreateBackupAsync(Original, It.IsAny<CancellationToken>())).Returns(async () =>
        {
            await File.WriteAllTextAsync(Original, "changed after copy");
            return new BackupInfo(Guid.NewGuid().ToString("N"), Original, backupPath, DateTime.UtcNow, 6, hash);
        });
        var result = await Guardian(backups.Object).SafeDeleteAsync(Original);
        Assert.False(result.Success);
        Assert.Contains("changed", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("changed after copy", await File.ReadAllTextAsync(Original));
    }

    [Fact]
    public async Task VerifiedDeleteCanBeRestoredAndDoesNotClaimNetFreeSpace()
    {
        await File.WriteAllTextAsync(Original, "recoverable");
        using var manager = Manager();
        var result = await Guardian(manager).SafeDeleteAsync(Original);
        Assert.True(result.Success);
        Assert.False(File.Exists(Original));
        Assert.Equal(1, result.ItemsProcessed);
        Assert.Equal(0, result.BytesRecovered);
        var backup = Assert.Single(await manager.GetAllBackupsAsync());
        Assert.True(await manager.RestoreAsync(backup.BackupId));
        Assert.Equal("recoverable", await File.ReadAllTextAsync(Original));
    }

    [Fact]
    public async Task RecursiveCleaningNeverDeletesItsOwnBackups()
    {
        await File.WriteAllTextAsync(Original, "restore me");
        using var manager = Manager();
        var guardian = Guardian(manager);
        var result = await guardian.SafeCleanDirectoryAsync(_root, recursive: true);
        Assert.True(result.Success);
        Assert.Equal(1, result.ItemsProcessed);
        var backup = Assert.Single(await manager.GetAllBackupsAsync());
        Assert.False((await guardian.SafeDeleteAsync(backup.BackupPath, createBackup: false)).Success);
        Assert.True(await manager.RestoreAsync(backup.BackupId));
        Assert.Equal("restore me", await File.ReadAllTextAsync(Original));
    }

    [Fact]
    public async Task ReadOnlyFileIsBackedUpAndDeletedSafely()
    {
        await File.WriteAllTextAsync(Original, "read only");
        File.SetAttributes(Original, FileAttributes.ReadOnly);
        using var manager = Manager();
        try
        {
            var result = await Guardian(manager).SafeDeleteAsync(Original);
            Assert.True(result.Success, result.Message);
            var backup = Assert.Single(await manager.GetAllBackupsAsync());
            Assert.True(await manager.RestoreAsync(backup.BackupId));
            Assert.Equal("read only", await File.ReadAllTextAsync(Original));
        }
        finally { if (File.Exists(Original)) File.SetAttributes(Original, FileAttributes.Normal); }
    }

    [Fact]
    public async Task CorruptedBackupCannotOverwriteCurrentFile()
    {
        await File.WriteAllTextAsync(Original, "initial");
        using var manager = Manager();
        var backup = await manager.CreateBackupAsync(Original);
        await File.WriteAllTextAsync(backup.BackupPath, "tampered");
        await File.WriteAllTextAsync(Original, "current");
        Assert.False(await manager.RestoreAsync(backup.BackupId));
        Assert.Equal("current", await File.ReadAllTextAsync(Original));
    }

    [Fact]
    public async Task RestorePreservesExistingNewerFileAsAnotherBackup()
    {
        await File.WriteAllTextAsync(Original, "initial");
        using var manager = Manager();
        var backup = await manager.CreateBackupAsync(Original);
        await File.WriteAllTextAsync(Original, "newer data");
        Assert.True(await manager.RestoreAsync(backup.BackupId));
        Assert.Equal("initial", await File.ReadAllTextAsync(Original));
        var preserved = Assert.Single(await manager.GetAllBackupsAsync(), b => b.BackupId != backup.BackupId);
        Assert.True(await manager.RestoreAsync(preserved.BackupId));
        Assert.Equal("newer data", await File.ReadAllTextAsync(Original));
    }

    [Fact]
    public async Task BackupIdTraversalCannotDeleteOutsideBackupRoot()
    {
        string outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(outside);
        string sentinel = Path.Combine(outside, "keep.txt");
        await File.WriteAllTextAsync(sentinel, "keep");
        using var manager = Manager();
        Assert.False(await manager.DeleteBackupAsync(outside));
        Assert.False(await manager.DeleteBackupAsync(@"..\..\outside"));
        Assert.False(await manager.RestoreAsync(outside));
        Assert.True(File.Exists(sentinel));
    }

    [Fact]
    public async Task CleanupUsesBackupMetadataAndLeavesUnrecognizedDirectories()
    {
        string backupDirectory = Path.Combine(Backups, "2020-01-01", Guid.NewGuid().ToString("N"));
        string dataDirectory = Path.Combine(backupDirectory, "data");
        Directory.CreateDirectory(dataDirectory);
        string payload = Path.Combine(dataDirectory, "old.txt");
        await File.WriteAllTextAsync(payload, "backup");
        byte[] content = System.Text.Encoding.UTF8.GetBytes("backup");
        var metadata = new
        {
            OriginalPath = Original,
            OriginalHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content)),
            CreationDate = DateTime.UtcNow.AddDays(-90),
            FileSize = content.Length,
            FileName = "old.txt",
            Encrypted = false
        };
        await File.WriteAllTextAsync(Path.Combine(backupDirectory, "metadata.json"),
            System.Text.Json.JsonSerializer.Serialize(metadata));
        string unrelated = Path.Combine(Backups, "unrelated");
        Directory.CreateDirectory(unrelated);
        await File.WriteAllTextAsync(Path.Combine(unrelated, "keep.txt"), "keep");
        using var manager = Manager();
        await manager.CleanupOldBackupsAsync(TimeSpan.FromDays(30));
        Assert.False(Directory.Exists(backupDirectory));
        Assert.Empty(await manager.GetAllBackupsAsync());
        Assert.True(File.Exists(Path.Combine(unrelated, "keep.txt")));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => manager.CleanupOldBackupsAsync(TimeSpan.Zero));
    }

    [Fact]
    public async Task CancelledDeleteKeepsFileAndPropagatesCancellation()
    {
        await File.WriteAllTextAsync(Original, "keep");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Guardian(Mock.Of<IBackupManager>()).SafeDeleteAsync(Original, ct: cancellation.Token));
        Assert.True(File.Exists(Original));
    }

    [Fact]
    public async Task JunctionCannotEscapeDirectoryCleaning()
    {
        if (!OperatingSystem.IsWindows()) return;
        string clean = Path.Combine(_root, "clean");
        string outside = Path.Combine(_root, "outside");
        string link = Path.Combine(clean, "junction");
        Directory.CreateDirectory(clean);
        Directory.CreateDirectory(outside);
        string sentinel = Path.Combine(outside, "keep.txt");
        await File.WriteAllTextAsync(sentinel, "keep");
        CreateJunction(link, outside);
        try
        {
            using var manager = Manager();
            var guardian = Guardian(manager);
            Assert.False((await guardian.SafeDeleteAsync(Path.Combine(link, "keep.txt"))).Success);
            Assert.True((await guardian.SafeCleanDirectoryAsync(clean, recursive: true)).Success);
            Assert.True(File.Exists(sentinel));
        }
        finally { Directory.Delete(link); }
    }

    [Theory]
    [InlineData(@"HKLM\SYSTEM\ControlSet001\Services\driver")]
    [InlineData(@"HKLM\SOFTWARE")]
    [InlineData(@"HKCU")]
    [InlineData(@"HKCU/Software/Microsoft/Windows/CurrentVersion/Run")]
    [InlineData(@"HKCU\\Software\test")]
    public void RegistryProtectionBlocksAliasesAncestorsAndMalformedPaths(string path) =>
        Assert.True(new RegistryGuardian(NullLogger<RegistryGuardian>.Instance).IsSystemCriticalKey(path));

    [Fact]
    public void RegistryExportsUseCorrectBinaryQwordAndUnicodeRepresentations()
    {
        var format = typeof(RegistryGuardian).GetMethod("FormatRegistryValue", BindingFlags.Static | BindingFlags.NonPublic)!;
        string Format(object value, RegistryValueKind kind) => (string)format.Invoke(null, ["Value", value, kind])!;
        Assert.Equal("\"Value\"=hex:01,ab,ff", Format(new byte[] { 1, 0xab, 0xff }, RegistryValueKind.Binary));
        Assert.Equal("\"Value\"=hex(b):08,07,06,05,04,03,02,01", Format(0x0102030405060708L, RegistryValueKind.QWord));
        Assert.Equal("\"Value\"=hex(2):ea,96,00,00", Format("雪", RegistryValueKind.ExpandString));
        Assert.Contains("25,00,54,00,45,00,4d,00,50,00,25,00", Format("%TEMP%", RegistryValueKind.ExpandString));
        Assert.Equal("\"Value\"=hex(7):ea,96,00,00,00,00", Format(new[] { "雪" }, RegistryValueKind.MultiString));
    }

    [Fact]
    public async Task PartialHashDistinguishesLargeFilesWithDifferentEdges()
    {
        string fileA = Path.Combine(_root, "largeA.bin");
        string fileB = Path.Combine(_root, "largeB.bin");
        string fileC = Path.Combine(_root, "largeC.bin");
        var bytesA = new byte[32 * 1024];
        Array.Fill(bytesA, (byte)'A');
        var bytesB = (byte[])bytesA.Clone();
        var bytesC = (byte[])bytesA.Clone();
        bytesC[^1] = (byte)'Z';
        await File.WriteAllBytesAsync(fileA, bytesA);
        await File.WriteAllBytesAsync(fileB, bytesB);
        await File.WriteAllBytesAsync(fileC, bytesC);

        string hashA = await OpenCleaner.Core.HashHelper.ComputePartialHashAsync(fileA);
        string hashB = await OpenCleaner.Core.HashHelper.ComputePartialHashAsync(fileB);
        string hashC = await OpenCleaner.Core.HashHelper.ComputePartialHashAsync(fileC);
        Assert.Equal(hashA, hashB);
        Assert.NotEqual(hashA, hashC);
    }

    private static void CreateJunction(string link, string target)
    {
        using var process = Process.Start(new ProcessStartInfo("cmd.exe")
        {
            Arguments = $"/d /c mklink /J \"{link}\" \"{target}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        })!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
