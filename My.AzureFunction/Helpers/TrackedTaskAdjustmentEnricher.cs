using Microsoft.EntityFrameworkCore;
using My.DAL.Data;
using My.DAL.Models;
using My.Shared.Dtos.Project;
using My.Shared.Dtos.TrackedTask;

namespace My.Functions.Helpers;

internal sealed class TrackedTaskAdjustmentContext
{
    public Dictionary<string, TrackedTaskAlias> Aliases { get; init; } = new();
    public Dictionary<string, TrackedTaskCorrectionAudit> Audits { get; init; } = new();
    public Dictionary<string, Project> ProjectsById { get; init; } = new();
}

internal static class TrackedTaskAdjustmentEnricher
{
    public static async Task<TrackedTaskAdjustmentContext> LoadForTasksAsync(
        ApplicationDbContext db,
        IReadOnlyCollection<string> taskIds)
    {
        if (taskIds.Count == 0)
            return new TrackedTaskAdjustmentContext();

        var aliases = await db.TrackedTaskAliases.AsNoTracking()
            .Include(a => a.Project)
            .Where(a => taskIds.Contains(a.TaskId))
            .ToDictionaryAsync(a => a.TaskId);

        var audits = await db.TrackedTaskCorrectionAudits.AsNoTracking()
            .Where(a => taskIds.Contains(a.TaskId))
            .ToDictionaryAsync(a => a.TaskId);

        var projectIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var alias in aliases.Values)
        {
            if (!string.IsNullOrEmpty(alias.ProjectId))
                projectIds.Add(alias.ProjectId);
        }

        foreach (var audit in audits.Values)
        {
            if (!string.IsNullOrEmpty(audit.PreviousProjectId))
                projectIds.Add(audit.PreviousProjectId);
            if (!string.IsNullOrEmpty(audit.NewProjectId))
                projectIds.Add(audit.NewProjectId);
        }

        var projectsById = projectIds.Count == 0
            ? new Dictionary<string, Project>()
            : await db.Projects.AsNoTracking()
                .Include(p => p.Organization)
                .Include(p => p.Department)
                .Include(p => p.ProjectGroup)
                .Where(p => projectIds.Contains(p.ProjectId))
                .ToDictionaryAsync(p => p.ProjectId);

        return new TrackedTaskAdjustmentContext
        {
            Aliases = aliases,
            Audits = audits,
            ProjectsById = projectsById
        };
    }

    /// <summary>
    /// Employee-facing enrichment: originals stay on the DTO; corrected values go in
    /// <see cref="TrackedTaskDto.ManagerAdjustment"/> for both alias and direct modes.
    /// </summary>
    public static void ApplyEmployeeView(
        TrackedTaskDto dto,
        TrackedTaskAlias? alias,
        TrackedTaskCorrectionAudit? audit,
        TrackedTaskAdjustmentContext context,
        AppMapper mapper)
    {
        if (alias != null)
        {
            dto.IsManagerAdjusted = true;
            dto.AdjustmentKind = "Alias";
            dto.ManagerAdjustment = BuildAdjustment(
                alias.Name,
                alias.StartDate,
                alias.Duration,
                alias.ProjectId,
                context,
                fallbackProject: alias.Project);
            return;
        }

        if (audit == null)
            return;

        dto.IsManagerAdjusted = true;
        dto.AdjustmentKind = "Direct";
        dto.ManagerAdjustment = BuildAdjustment(
            audit.NewName,
            audit.NewStartDate,
            audit.NewDuration,
            audit.NewProjectId,
            context,
            fallbackProject: null);

        // Task row in DB holds corrected values; restore the employee's original submission
        // on the main DTO (same shape as alias mode).
        dto.Name = audit.PreviousName;
        dto.StartDate = audit.PreviousStartDate;
        dto.Duration = audit.PreviousDuration;
        dto.ProjectId = audit.PreviousProjectId;
        dto.Project = ResolveProjectDto(audit.PreviousProjectId, context.ProjectsById, mapper);
        // EndDate is not stored on the audit — re-derive so read-only dialogs don't show the
        // corrected end against the restored original start/duration.
        dto.EndDate = audit.PreviousDuration > TimeSpan.Zero
            ? audit.PreviousStartDate + audit.PreviousDuration
            : null;
    }

    private static ManagerAdjustmentDto BuildAdjustment(
        string name,
        DateTime startDate,
        TimeSpan duration,
        string? projectId,
        TrackedTaskAdjustmentContext context,
        Project? fallbackProject)
    {
        Project? project = null;
        if (!string.IsNullOrEmpty(projectId) && context.ProjectsById.TryGetValue(projectId, out var byId))
            project = byId;
        project ??= fallbackProject;

        return new ManagerAdjustmentDto
        {
            Name = name,
            StartDate = startDate,
            Duration = duration,
            ProjectId = projectId,
            ProjectName = project?.Name,
            OrganizationName = project?.Organization?.Name,
            OrganizationColor = project?.Organization?.Color,
            ProjectGroupName = project?.ProjectGroup?.Name,
            ProjectGroupColor = project?.ProjectGroup?.Color
        };
    }

    private static ProjectDto? ResolveProjectDto(
        string? projectId,
        IReadOnlyDictionary<string, Project> projectsById,
        AppMapper mapper)
    {
        if (string.IsNullOrEmpty(projectId))
            return null;

        return projectsById.TryGetValue(projectId, out var project)
            ? mapper.ProjectToDto(project)
            : null;
    }
}
