using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class StopwatchCalendarRulesTests
{
    [Fact]
    public void GroupByWorkItemAndDay_sums_duration_and_counts_sessions()
    {
        var day = new DateTime(2026, 6, 15);
        var sessions = new[]
        {
            new StopwatchCalendarRules.SessionSlice
            {
                TaskId = "t1",
                StopwatchItemId = "sw-1",
                Name = "Feature work",
                StartDate = day.AddHours(9),
                EndDate = day.AddHours(9).AddMinutes(5),
                Duration = TimeSpan.FromMinutes(1),
                IsLocked = false
            },
            new StopwatchCalendarRules.SessionSlice
            {
                TaskId = "t2",
                StopwatchItemId = "sw-1",
                Name = "Feature work",
                StartDate = day.AddHours(14),
                EndDate = day.AddHours(14).AddMinutes(10),
                Duration = TimeSpan.FromMinutes(2),
                IsLocked = false
            },
            new StopwatchCalendarRules.SessionSlice
            {
                TaskId = "t3",
                StopwatchItemId = "sw-2",
                Name = "Other task",
                StartDate = day.AddHours(10),
                EndDate = day.AddHours(10).AddMinutes(30),
                Duration = TimeSpan.FromMinutes(30),
                IsLocked = false
            }
        };

        var grouped = StopwatchCalendarRules.GroupByWorkItemAndDay(sessions).OrderBy(g => g.Name).ToList();

        Assert.Equal(2, grouped.Count);

        var feature = grouped.First(g => g.StopwatchItemId == "sw-1");
        Assert.Equal(2, feature.SessionCount);
        Assert.Equal(TimeSpan.FromMinutes(3), feature.TotalDuration);
        Assert.Equal(day, feature.Day);
        Assert.Equal(day.AddHours(9), feature.Start);
    }

    [Fact]
    public void GetSessionDisplayEnd_uses_billed_duration_when_rounded_up()
    {
        var start = new DateTime(2026, 6, 15, 9, 0, 5);
        var end = start.AddSeconds(10);

        var displayEnd = StopwatchCalendarRules.GetSessionDisplayEnd(start, end, TimeSpan.FromMinutes(1));

        Assert.Equal(start.AddMinutes(1), displayEnd);
    }
}