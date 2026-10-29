using OpenCleaner.Core.Scheduler;
using Xunit;

namespace OpenCleaner.Core.Tests;

public sealed class ScheduledRunJournalTests : IDisposable
{
    private readonly string _directory;
    private readonly string _journal;

    public ScheduledRunJournalTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "OpenCleaner-Runs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _journal = Path.Combine(_directory, "scheduled-runs.jsonl");
    }

    [Fact]
    public void MissingJournalReturnsNothing() =>
        Assert.Empty(ScheduledRunJournal.ReadLatest(5, Path.Combine(_directory, "absent.jsonl")));

    [Fact]
    public void RecordsAreReadNewestFirstAndLimited()
    {
        DateTime baseTime = new(2026, 10, 29, 3, 0, 0, DateTimeKind.Utc);
        ScheduledRunJournal.Append(new ScheduledRunRecord(baseTime, 10, 1000, 0), _journal);
        ScheduledRunJournal.Append(new ScheduledRunRecord(baseTime.AddDays(1), 20, 2000, 1), _journal);
        ScheduledRunJournal.Append(new ScheduledRunRecord(baseTime.AddDays(2), 30, 3000, 0), _journal);

        var records = ScheduledRunJournal.ReadLatest(2, _journal);

        Assert.Equal(2, records.Count);
        Assert.Equal(30, records[0].FilesCleaned);
        Assert.Equal(20, records[1].FilesCleaned);
        Assert.False(records[1].Success);
        Assert.Equal(3000, records[0].BytesProcessed);
    }

    [Fact]
    public void CorruptLinesAreIgnored()
    {
        File.WriteAllLines(_journal,
        [
            "pas du json",
            """{"CompletedAt":"2026-10-29T03:00:00Z","FilesCleaned":4,"BytesProcessed":400,"Failures":0}"""
        ]);

        var record = Assert.Single(ScheduledRunJournal.ReadLatest(5, _journal));
        Assert.Equal(4, record.FilesCleaned);
        Assert.True(record.Success);
    }

    [Fact]
    public void JournalIsTrimmedAndContainsNoFilePath()
    {
        DateTime baseTime = new(2026, 10, 29, 3, 0, 0, DateTimeKind.Utc);
        for (int index = 0; index < 205; index++)
            ScheduledRunJournal.Append(new ScheduledRunRecord(baseTime.AddMinutes(index), index, index, 0), _journal);

        Assert.Equal(200, File.ReadAllLines(_journal).Length);
        Assert.DoesNotContain(@"C:\", File.ReadAllText(_journal), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(200, ScheduledRunJournal.ReadLatest(500, _journal).Count);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
