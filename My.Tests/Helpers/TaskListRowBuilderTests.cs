using My.Client.Helpers;
using My.Client.Models;
using My.Shared.Dtos.Project;
using My.Shared.Dtos.StopwatchItem;
using My.Shared.Dtos.TrackedTask;
using My.Shared.Rules;
using Xunit;

namespace My.Tests.Helpers;

public class TaskListRowBuilderTests
{
    private static TrackedTask AdjustedTask()
    {
        var dto = new TrackedTaskDto
        {
            TaskId = "t1",
                    Details = "Original name",
            StartDate = new DateTime(2026, 6, 26, 10, 0, 0, DateTimeKind.Utc),
            Duration = TimeSpan.FromHours(8),
            EndDate = new DateTime(2026, 6, 26, 18, 0, 0, DateTimeKind.Utc),
            ProjectId = "p-orig",
            Project = new ProjectDto
            {
                ProjectId = "p-orig",
                Name = "Marketing",
                OrganizationName = "Acme Organization",
                OrganizationColor = "#123456"
            },
            UserId = "u1",
            IsManagerAdjusted = true,
            AdjustmentKind = "Direct",
            ManagerAdjustment = new ManagerAdjustmentDto
            {
                    Details = "Adjusted name",
                StartDate = new DateTime(2026, 6, 26, 5, 0, 0, DateTimeKind.Utc),
                Duration = TimeSpan.FromHours(8),
                ProjectId = null,
                ProjectName = null,
                OrganizationColor = null
            }
        };
        return new TrackedTask(dto);
    }

    [Fact]
    public void ExpandManualRows_Both_yields_original_and_overlay()
    {
        var rows = TaskListRowBuilder.ExpandManualRows(AdjustedTask(), EmployeeTimeDisplayMode.Both).ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal("Original name", rows[0].Details);
        Assert.Equal("Marketing", rows[0].ProjectName);
        Assert.Equal("Marketing", rows[0].ProjectDisplayName);
        Assert.Equal("Acme Organization", rows[0].OrganizationName);
        Assert.False(rows[0].IsOverlayRow);
        Assert.Equal("Adjusted name", rows[1].Details);
        Assert.Null(rows[1].ProjectDisplayName);
        Assert.True(rows[1].IsOverlayRow);
    }

    [Fact]
    public void ExpandManualRows_TheirTime_yields_original_only()
    {
        var rows = TaskListRowBuilder.ExpandManualRows(AdjustedTask(), EmployeeTimeDisplayMode.TheirTime).ToList();
        Assert.Single(rows);
        Assert.Equal("Original name", rows[0].Details);
        Assert.False(rows[0].IsOverlayRow);
    }

    [Fact]
    public void ExpandManualRows_Adjusted_yields_overlay_only()
    {
        var rows = TaskListRowBuilder.ExpandManualRows(AdjustedTask(), EmployeeTimeDisplayMode.Adjusted).ToList();
        Assert.Single(rows);
        Assert.Equal("Adjusted name", rows[0].Details);
        Assert.True(rows[0].IsOverlayRow);
        Assert.Equal("Adjusted name", rows[0].OverlayDetails);
    }

    [Fact]
    public void ExpandManualRows_unadjusted_is_single_row_in_every_mode()
    {
        var dto = new TrackedTaskDto
        {
            TaskId = "t2",
                    Details = "Plain",
            StartDate = DateTime.UtcNow,
            Duration = TimeSpan.FromHours(1),
            UserId = "u1"
        };
        var task = new TrackedTask(dto);

        foreach (var mode in Enum.GetValues<EmployeeTimeDisplayMode>())
        {
            var rows = TaskListRowBuilder.ExpandManualRows(task, mode).ToList();
            Assert.Single(rows);
            Assert.Equal("Plain", rows[0].Details);
        }
    }

    [Fact]
    public void Overlay_row_does_not_inherit_original_project_color()
    {
        var rows = TaskListRowBuilder.ExpandManualRows(AdjustedTask(), EmployeeTimeDisplayMode.Both).ToList();
        var overlay = rows[1];
        Assert.Null(overlay.OrganizationColor);
        Assert.Equal("#123456", rows[0].OrganizationColor);
    }

    [Fact]
    public void FromWeekTasks_includes_manuals_and_stopwatch_sessions_sorted_by_start()
    {
        var earlier = new TrackedTask(new TrackedTaskDto
        {
            TaskId = "m1",
                    Details = "Zebra",
            StartDate = new DateTime(2026, 8, 3, 9, 0, 0, DateTimeKind.Local),
            Duration = TimeSpan.FromHours(1),
            UserId = "u1"
        });
        var laterManual = new TrackedTask(new TrackedTaskDto
        {
            TaskId = "m2",
                    Details = "Alpha",
            StartDate = new DateTime(2026, 8, 4, 9, 0, 0, DateTimeKind.Local),
            Duration = TimeSpan.FromHours(2),
            UserId = "u1"
        });
        var session = new TrackedTask(new TrackedTaskDto
        {
            TaskId = "s1",
                    Details = "Session work",
            StartDate = new DateTime(2026, 8, 3, 14, 0, 0, DateTimeKind.Local),
            Duration = TimeSpan.FromHours(0.5),
            UserId = "u1",
            StopwatchItemId = "sw1"
        });

        var rows = TaskListRowBuilder.FromWeekTasks(
            new[] { laterManual, session, earlier },
            EmployeeTimeDisplayMode.Both);

        Assert.Equal(3, rows.Count);
        Assert.Equal("Zebra", rows[0].Details);
        Assert.Equal("Session work", rows[1].Details);
        Assert.Equal("Alpha", rows[2].Details);
        Assert.NotNull(rows[1].ManualTask);
        Assert.Equal("sw1", rows[1].ManualTask!.StopwatchItemId);
    }

    [Fact]
    public void FromWeekTasks_expands_adjusted_manuals_in_Both_mode()
    {
        var plain = new TrackedTask(new TrackedTaskDto
        {
            TaskId = "p1",
                    Details = "Plain",
            StartDate = new DateTime(2026, 8, 5, 9, 0, 0, DateTimeKind.Local),
            Duration = TimeSpan.FromHours(1),
            UserId = "u1"
        });
        var adjusted = AdjustedTask();

        var rows = TaskListRowBuilder.FromWeekTasks(
            new[] { plain, adjusted },
            EmployeeTimeDisplayMode.Both);

        // plain (1) + adjusted original + overlay (2) = 3
        Assert.Equal(3, rows.Count);
        Assert.Contains(rows, r => r.Details == "Plain" && !r.IsOverlayRow);
        Assert.Contains(rows, r => r.Details == "Original name" && !r.IsOverlayRow);
        Assert.Contains(rows, r => r.Details == "Adjusted name" && r.IsOverlayRow);
    }

    [Fact]
    public void FromWeekTasks_empty_input_is_empty()
    {
        Assert.Empty(TaskListRowBuilder.FromWeekTasks(Array.Empty<TrackedTask>()));
    }

    private static readonly TimeZoneInfo Eastern =
        TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "Eastern Standard Time" : "America/New_York");

    private static StopwatchItemDto RunningStopwatch(DateTime lastWorkedUtc, DateTime activeSessionStartUtc) =>
        new()
        {
            StopwatchItemId = "sw1",
                    Details = "Live session",
            TotalDuration = TimeSpan.FromMinutes(30),
            IsRunning = true,
            ActiveSessionId = "as1",
            ActiveSessionStartDate = activeSessionStartUtc,
            LastWorkedAt = lastWorkedUtc,
        };

    [Fact]
    public void FromStopwatch_converts_LastWorkedAt_to_the_users_time_zone()
    {
        // 2026-01-15 is outside DST, so Eastern = UTC-5.
        var lastWorkedUtc = new DateTime(2026, 1, 15, 14, 0, 0, DateTimeKind.Utc);
        var item = RunningStopwatch(lastWorkedUtc, lastWorkedUtc);

        var row = TaskListRowBuilder.FromStopwatch(item, Eastern);

        Assert.Equal(new DateTime(2026, 1, 15, 9, 0, 0), row.SortDate);
        Assert.Equal(row.SortDate, row.DisplayDate);
        Assert.Equal(TaskListRowKind.Stopwatch, row.Kind);
    }

    [Fact]
    public void FromStopwatch_defaults_to_utc_when_no_time_zone_given()
    {
        var lastWorkedUtc = new DateTime(2026, 1, 15, 14, 0, 0, DateTimeKind.Utc);
        var item = RunningStopwatch(lastWorkedUtc, lastWorkedUtc);

        var row = TaskListRowBuilder.FromStopwatch(item);

        Assert.Equal(lastWorkedUtc, row.SortDate);
    }

    [Fact]
    public void FromStopwatch_adds_elapsed_active_session_time_to_total_duration()
    {
        var now = DateTime.UtcNow;
        var item = RunningStopwatch(now, now.AddMinutes(-10));

        var row = TaskListRowBuilder.FromStopwatch(item, Eastern);

        // TotalDuration (30m) + ~10m elapsed on the active session.
        Assert.True(row.Duration > TimeSpan.FromMinutes(39) && row.Duration < TimeSpan.FromMinutes(41));
    }

    [Fact]
    public void FromStopwatch_not_running_uses_TotalDuration_as_is()
    {
        var lastWorkedUtc = new DateTime(2026, 1, 15, 14, 0, 0, DateTimeKind.Utc);
        var item = new StopwatchItemDto
        {
            StopwatchItemId = "sw2",
                    Details = "Paused",
            TotalDuration = TimeSpan.FromHours(2),
            IsRunning = false,
            LastWorkedAt = lastWorkedUtc,
        };

        var row = TaskListRowBuilder.FromStopwatch(item, Eastern);

        Assert.Equal(TimeSpan.FromHours(2), row.Duration);
        Assert.False(row.IsRunning);
    }
}
