using My.Client.Helpers;
using My.Client.Models;
using My.Shared.Dtos.StopwatchItem;
using Xunit;

namespace My.Tests.Helpers;

public class TaskListRowBuilderTests
{
    [Fact]
    public void FromStopwatch_uses_last_worked_for_sort_and_display_date()
    {
        var lastWorked = new DateTime(2026, 7, 1, 18, 0, 0, DateTimeKind.Utc);
        var row = TaskListRowBuilder.FromStopwatch(new StopwatchItemDto
        {
            StopwatchItemId = "sw-1",
            Name = "Test",
            LastWorkedAt = lastWorked,
            TotalDuration = TimeSpan.FromMinutes(5)
        });

        Assert.Equal(TaskListRowKind.Stopwatch, row.Kind);
        Assert.Equal(lastWorked, row.SortDate);
        Assert.Equal(lastWorked.ToLocalTime(), row.DisplayDate);
    }

    [Fact]
    public void FromManual_uses_start_date_for_sort_and_display_date()
    {
        var start = new DateTime(2026, 6, 25, 9, 0, 0);
        var row = TaskListRowBuilder.FromManual(new TrackedTask
        {
            TaskId = "t-1",
            Name = "Manual",
            StartDate = start,
            Duration = TimeSpan.FromHours(1)
        });

        Assert.Equal(TaskListRowKind.Manual, row.Kind);
        Assert.Equal(start, row.SortDate);
        Assert.Equal(start, row.DisplayDate);
    }
}