using My.DAL.Models;
using My.Functions;
using My.Functions.Helpers;
using My.Shared.Dtos.TrackedTask;
using Xunit;

namespace My.Tests.Helpers;

/// <summary>
/// Pins employee-view restore for Direct corrections: main DTO shows original
/// values including a derived EndDate so locked read-only dialogs stay coherent.
/// </summary>
public class TrackedTaskAdjustmentEnricherTests
{
    private readonly AppMapper _mapper = new();

    [Fact]
    public void ApplyEmployeeView_Direct_restores_original_and_derives_EndDate()
    {
        var previousStart = new DateTime(2026, 6, 26, 10, 0, 0, DateTimeKind.Utc);
        var previousDuration = TimeSpan.FromHours(8);
        var correctedStart = new DateTime(2026, 6, 26, 5, 0, 0, DateTimeKind.Utc);

        var dto = new TrackedTaskDto
        {
            TaskId = "t1",
            Name = "Corrected name",
            StartDate = correctedStart,
            Duration = TimeSpan.FromHours(3),
            EndDate = correctedStart + TimeSpan.FromHours(3),
            ProjectId = null,
            UserId = "u1"
        };

        var audit = new TrackedTaskCorrectionAudit
        {
            TrackedTaskCorrectionAuditId = "a1",
            TaskId = "t1",
            CorrectedByUserId = "mgr",
            CorrectedAtUtc = DateTime.UtcNow,
            PreviousName = "Original name",
            PreviousStartDate = previousStart,
            PreviousDuration = previousDuration,
            PreviousProjectId = "p1",
            PreviousIsBillable = true,
            NewName = "Corrected name",
            NewStartDate = correctedStart,
            NewDuration = TimeSpan.FromHours(3),
            NewProjectId = null,
            NewIsBillable = false
        };

        var project = new Project
        {
            ProjectId = "p1",
            Name = "Marketing",
            Organization = new Organization { Name = "Org", Color = "#abc" }
        };

        var context = new TrackedTaskAdjustmentContext
        {
            Audits = new Dictionary<string, TrackedTaskCorrectionAudit> { ["t1"] = audit },
            ProjectsById = new Dictionary<string, Project> { ["p1"] = project }
        };

        TrackedTaskAdjustmentEnricher.ApplyEmployeeView(dto, alias: null, audit, context, _mapper);

        Assert.True(dto.IsManagerAdjusted);
        Assert.Equal("Direct", dto.AdjustmentKind);
        Assert.Equal("Original name", dto.Name);
        Assert.Equal(previousStart, dto.StartDate);
        Assert.Equal(previousDuration, dto.Duration);
        Assert.Equal("p1", dto.ProjectId);
        Assert.NotNull(dto.Project);
        Assert.Equal("Marketing", dto.Project!.Name);
        Assert.Equal(previousStart + previousDuration, dto.EndDate);

        Assert.NotNull(dto.ManagerAdjustment);
        Assert.Equal("Corrected name", dto.ManagerAdjustment!.Name);
        Assert.Null(dto.ManagerAdjustment.ProjectId);
        Assert.Null(dto.ManagerAdjustment.ProjectName);
    }

    [Fact]
    public void ApplyEmployeeView_Alias_leaves_original_on_dto_and_fills_adjustment()
    {
        var dto = new TrackedTaskDto
        {
            TaskId = "t2",
            Name = "Original",
            StartDate = new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc),
            Duration = TimeSpan.FromHours(8),
            EndDate = new DateTime(2026, 6, 1, 17, 0, 0, DateTimeKind.Utc),
            ProjectId = "p-orig",
            UserId = "u1"
        };

        var aliasProject = new Project
        {
            ProjectId = "p-adj",
            Name = "IT Support",
            Organization = new Organization { Name = "PP", Color = "#00ff00" },
            ProjectGroup = new ProjectGroup { Name = "Ops", Color = "#0000ff" }
        };

        var alias = new TrackedTaskAlias
        {
            TaskId = "t2",
            Name = "Aliased",
            StartDate = new DateTime(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc),
            Duration = TimeSpan.FromHours(7),
            ProjectId = "p-adj",
            Project = aliasProject
        };

        var context = new TrackedTaskAdjustmentContext
        {
            Aliases = new Dictionary<string, TrackedTaskAlias> { ["t2"] = alias },
            ProjectsById = new Dictionary<string, Project> { ["p-adj"] = aliasProject }
        };

        TrackedTaskAdjustmentEnricher.ApplyEmployeeView(dto, alias, audit: null, context, _mapper);

        Assert.Equal("Original", dto.Name);
        Assert.Equal("p-orig", dto.ProjectId);
        Assert.NotNull(dto.ManagerAdjustment);
        Assert.Equal("Aliased", dto.ManagerAdjustment!.Name);
        Assert.Equal("IT Support", dto.ManagerAdjustment.ProjectName);
        Assert.Equal("#00ff00", dto.ManagerAdjustment.OrganizationColor);
        Assert.Equal("#0000ff", dto.ManagerAdjustment.ProjectGroupColor);
    }
}
