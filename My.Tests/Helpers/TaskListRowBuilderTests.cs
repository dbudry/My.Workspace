using My.Client.Helpers;
using My.Client.Models;
using My.Shared.Dtos.Project;
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
            Name = "Original name",
            StartDate = new DateTime(2026, 6, 26, 10, 0, 0, DateTimeKind.Utc),
            Duration = TimeSpan.FromHours(8),
            EndDate = new DateTime(2026, 6, 26, 18, 0, 0, DateTimeKind.Utc),
            ProjectId = "p-orig",
            Project = new ProjectDto
            {
                ProjectId = "p-orig",
                Name = "Marketing",
                OrganizationName = "Profit Point Inc.",
                OrganizationColor = "#123456"
            },
            UserId = "u1",
            IsManagerAdjusted = true,
            AdjustmentKind = "Direct",
            ManagerAdjustment = new ManagerAdjustmentDto
            {
                Name = "Adjusted name",
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
        Assert.Equal("Original name", rows[0].Name);
        Assert.Equal("Marketing", rows[0].ProjectDisplayName);
        Assert.False(rows[0].IsOverlayRow);
        Assert.Equal("Adjusted name", rows[1].Name);
        Assert.Null(rows[1].ProjectDisplayName);
        Assert.True(rows[1].IsOverlayRow);
    }

    [Fact]
    public void ExpandManualRows_TheirTime_yields_original_only()
    {
        var rows = TaskListRowBuilder.ExpandManualRows(AdjustedTask(), EmployeeTimeDisplayMode.TheirTime).ToList();
        Assert.Single(rows);
        Assert.Equal("Original name", rows[0].Name);
        Assert.False(rows[0].IsOverlayRow);
    }

    [Fact]
    public void ExpandManualRows_Adjusted_yields_overlay_only()
    {
        var rows = TaskListRowBuilder.ExpandManualRows(AdjustedTask(), EmployeeTimeDisplayMode.Adjusted).ToList();
        Assert.Single(rows);
        Assert.Equal("Adjusted name", rows[0].Name);
        Assert.True(rows[0].IsOverlayRow);
        Assert.Equal("Adjusted name", rows[0].OverlayName);
    }

    [Fact]
    public void ExpandManualRows_unadjusted_is_single_row_in_every_mode()
    {
        var dto = new TrackedTaskDto
        {
            TaskId = "t2",
            Name = "Plain",
            StartDate = DateTime.UtcNow,
            Duration = TimeSpan.FromHours(1),
            UserId = "u1"
        };
        var task = new TrackedTask(dto);

        foreach (var mode in Enum.GetValues<EmployeeTimeDisplayMode>())
        {
            var rows = TaskListRowBuilder.ExpandManualRows(task, mode).ToList();
            Assert.Single(rows);
            Assert.Equal("Plain", rows[0].Name);
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
}
