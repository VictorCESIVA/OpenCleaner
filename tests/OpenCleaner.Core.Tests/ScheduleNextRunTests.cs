using OpenCleaner.Core.Scheduler;
using Xunit;

namespace OpenCleaner.Core.Tests;

public sealed class ScheduleNextRunTests
{
    private static ScheduleConfig Config(bool enabled, TimeSpan time, params DayOfWeek[] days) => new()
    {
        IsEnabled = enabled,
        TimeOfDay = time,
        Days = days
    };

    [Fact]
    public void DisabledOrEmptyScheduleHasNoNextRun()
    {
        Assert.Null(Config(false, TimeSpan.FromHours(3), DayOfWeek.Monday).GetNextRun(new DateTime(2026, 10, 26, 1, 0, 0)));
        Assert.Null(Config(true, TimeSpan.FromHours(3)).GetNextRun(new DateTime(2026, 10, 26, 1, 0, 0)));
    }

    [Fact]
    public void SameDayLaterTimeIsChosen()
    {
        DateTime now = new(2026, 10, 26, 2, 0, 0); // lundi
        Assert.Equal(new DateTime(2026, 10, 26, 3, 0, 0),
            Config(true, TimeSpan.FromHours(3), DayOfWeek.Monday).GetNextRun(now));
    }

    [Fact]
    public void SameDayPassedTimeMovesToNextSelectedDay()
    {
        DateTime now = new(2026, 10, 26, 4, 0, 0); // lundi après 03:00
        Assert.Equal(new DateTime(2026, 11, 2, 3, 0, 0),
            Config(true, TimeSpan.FromHours(3), DayOfWeek.Monday).GetNextRun(now));
    }

    [Fact]
    public void NextSelectedDayIsUsedAcrossTheWeek()
    {
        DateTime now = new(2026, 10, 30, 4, 0, 0); // vendredi après 03:00
        Assert.Equal(new DateTime(2026, 11, 2, 3, 0, 0),
            Config(true, TimeSpan.FromHours(3), DayOfWeek.Monday, DayOfWeek.Friday).GetNextRun(now));
    }

    [Fact]
    public void SundayEveningFindsMondayMorning()
    {
        DateTime now = new(2026, 11, 1, 23, 30, 0); // dimanche
        Assert.Equal(new DateTime(2026, 11, 2, 3, 0, 0),
            Config(true, TimeSpan.FromHours(3), DayOfWeek.Monday).GetNextRun(now));
    }

    [Fact]
    public void ExactScheduledInstantIsNotReturnedTwice()
    {
        DateTime now = new(2026, 10, 26, 3, 0, 0);
        Assert.Equal(new DateTime(2026, 11, 2, 3, 0, 0),
            Config(true, TimeSpan.FromHours(3), DayOfWeek.Monday).GetNextRun(now));
    }
}
