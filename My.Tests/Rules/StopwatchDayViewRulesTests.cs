using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class StopwatchDayViewRulesTests
{
    [Fact]
    public void GroupByDayThenItem_splits_the_same_work_item_across_days()
    {
        var monday = new DateTime(2026, 9, 14, 9, 0, 0);
        var tuesday = new DateTime(2026, 9, 15, 10, 0, 0);
        var sessions = new[]
        {
            Slice("t1", "sw-1", monday, TimeSpan.FromHours(1)),
            Slice("t2", "sw-1", monday.AddHours(2), TimeSpan.FromMinutes(30)),
            Slice("t3", "sw-1", tuesday, TimeSpan.FromHours(2)),
            Slice("t4", "sw-2", monday.AddHours(1), TimeSpan.FromMinutes(15)),
        };

        var days = StopwatchDayViewRules.GroupByDayThenItem(sessions);

        Assert.Equal(2, days.Count);
        Assert.Equal(tuesday.Date, days[0].Day);
        Assert.Equal(monday.Date, days[1].Day);

        var mondayRows = days[1].Items;
        Assert.Equal(2, mondayRows.Count);
        var sw1 = mondayRows.Single(r => r.StopwatchItemId == "sw-1");
        Assert.Equal(2, sw1.Sessions.Count);
        Assert.Equal(TimeSpan.FromHours(1.5), sw1.CompletedDuration);

        var tuesdayRows = days[0].Items;
        Assert.Single(tuesdayRows);
        Assert.Equal(TimeSpan.FromHours(2), tuesdayRows[0].CompletedDuration);
    }

    [Fact]
    public void GroupByDayThenItem_does_not_count_running_duration_as_completed()
    {
        var day = new DateTime(2026, 9, 17, 8, 0, 0);
        var sessions = new[]
        {
            Slice("done", "sw-1", day, TimeSpan.FromMinutes(10), isRunning: false),
            Slice("live", "sw-1", day.AddHours(1), TimeSpan.Zero, isRunning: true),
        };

        var row = StopwatchDayViewRules.GroupByDayThenItem(sessions).Single().Items.Single();
        Assert.Equal(TimeSpan.FromMinutes(10), row.CompletedDuration);
        Assert.Contains(row.Sessions, s => s.IsRunning);
    }

    [Fact]
    public void TryValidateRange_rejects_inverted_or_too_long()
    {
        var from = new DateTime(2026, 9, 14, 0, 0, 0, DateTimeKind.Utc);
        Assert.False(StopwatchDayViewRules.TryValidateRange(from, from, out _));
        Assert.False(StopwatchDayViewRules.TryValidateRange(from, from.AddDays(11), out _));
        Assert.True(StopwatchDayViewRules.TryValidateRange(from, from.AddDays(7), out var ok));
        Assert.Null(ok);
    }

    [Fact]
    public void TryParseUtcRange_keeps_Z_as_utc_and_does_not_relabel_local_ticks()
    {
        Assert.True(StopwatchDayViewRules.TryParseUtcRange(
            "2026-09-14T04:00:00.0000000Z",
            "2026-09-21T04:00:00.0000000Z",
            out var fromUtc, out var toUtc, out var error));
        Assert.Null(error);
        Assert.Equal(DateTimeKind.Utc, fromUtc.Kind);
        Assert.Equal(new DateTime(2026, 9, 14, 4, 0, 0, DateTimeKind.Utc), fromUtc);
        Assert.Equal(new DateTime(2026, 9, 21, 4, 0, 0, DateTimeKind.Utc), toUtc);
    }

    private static StopwatchDayViewRules.SessionSlice Slice(
        string taskId, string itemId, DateTime start, TimeSpan duration, bool isRunning = false)
        => new()
        {
            TaskId = taskId,
            StopwatchItemId = itemId,
            Day = start.Date,
            StartLocal = start,
            StartUtc = DateTime.SpecifyKind(start, DateTimeKind.Utc),
            EndLocal = isRunning ? null : start.Add(duration),
            Duration = duration,
            IsLocked = false,
            IsRunning = isRunning
        };
}
