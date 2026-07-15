using Microsoft.EntityFrameworkCore;
using My.DAL.Data;
using My.DAL.Models;
using My.DAL.Repository;
using My.Shared.Constants;
using My.Shared.Dtos.Analytics;
using My.Shared.Rules;

namespace My.Functions.Helpers;

/// <summary>
/// Builds <see cref="TymeDataExportDto"/> slices for Tyme:Admin data extraction.
/// </summary>
public sealed class TymeDataExtractionBuilder
{
    private readonly ApplicationDbContext dbContext;
    private readonly IRepository<Project> projectRepository;
    private readonly IRepository<TrackedTask> trackedTaskRepository;
    private readonly IRepository<TrackedTaskAlias> aliasRepository;
    private readonly IRepository<TimeSubmission> submissionRepository;

    public TymeDataExtractionBuilder(
        ApplicationDbContext dbContext,
        IRepositoryFactory repositoryFactory)
    {
        this.dbContext = dbContext;
        projectRepository = repositoryFactory.GetRepository<Project>();
        trackedTaskRepository = repositoryFactory.GetRepository<TrackedTask>();
        aliasRepository = repositoryFactory.GetRepository<TrackedTaskAlias>();
        submissionRepository = repositoryFactory.GetRepository<TimeSubmission>();
    }

    public async Task<TymeDataExportDto> BuildAsync(TymeDataExtractionRequest request)
    {
        var export = new TymeDataExportDto();
        var entities = request.Entities;

        List<TrackedTask>? scopedTasks = null;
        if (entities.Contains(TymeDataExtractionRules.TrackedTasks)
            || entities.Contains(TymeDataExtractionRules.TrackedTaskAliases)
            || entities.Contains(TymeDataExtractionRules.TrackedTaskCorrectionAudits))
        {
            scopedTasks = await LoadScopedTasksAsync(request);
        }

        if (entities.Contains(TymeDataExtractionRules.Organizations))
            export.Organizations = await LoadOrganizationsAsync(request);

        if (entities.Contains(TymeDataExtractionRules.ProjectGroups))
            export.ProjectGroups = await LoadProjectGroupsAsync(request);

        if (entities.Contains(TymeDataExtractionRules.Projects))
            export.Projects = await LoadProjectsAsync(request);

        if (entities.Contains(TymeDataExtractionRules.ApplicationUsers))
            export.ApplicationUsers = await LoadApplicationUsersAsync(request);

        if (entities.Contains(TymeDataExtractionRules.TrackedTasks))
            export.TrackedTasks = MapTrackedTasks(scopedTasks ?? [], await LoadSubmittedSetAsync(scopedTasks));

        if (entities.Contains(TymeDataExtractionRules.TrackedTaskAliases))
            export.TrackedTaskAliases = await LoadAliasesAsync(request, scopedTasks);

        if (entities.Contains(TymeDataExtractionRules.TrackedTaskCorrectionAudits))
            export.TrackedTaskCorrectionAudits = await LoadAuditsAsync(request, scopedTasks);

        if (entities.Contains(TymeDataExtractionRules.TimeSubmissions))
            export.TimeSubmissions = await LoadTimeSubmissionsAsync(request);

        return export;
    }

    private async Task<List<TrackedTask>> LoadScopedTasksAsync(TymeDataExtractionRequest request)
    {
        var from = request.FromUtc;
        var to = request.ToUtc;

        var tasks = (await trackedTaskRepository.Get(
            filter: task =>
                (from == null || task.StartDate >= from) &&
                (to == null || task.StartDate <= to) &&
                (request.UserIds.Count == 0 || request.UserIds.Contains(task.UserId)) &&
                (string.IsNullOrEmpty(request.ProjectId) || task.ProjectId == request.ProjectId),
            includeProperties: "Project.Organization,Project.ProjectGroup,User")).ToList();

        if (!string.IsNullOrEmpty(request.OrganizationId))
            tasks = tasks.Where(t => t.Project?.OrganizationId == request.OrganizationId).ToList();

        if (!string.IsNullOrEmpty(request.ProjectGroupId))
            tasks = tasks.Where(t => t.Project?.ProjectGroupId == request.ProjectGroupId).ToList();

        return tasks;
    }

    private async Task<HashSet<(string UserId, int Year, int Month)>> LoadSubmittedSetAsync(List<TrackedTask>? tasks)
    {
        if (tasks is null || tasks.Count == 0)
            return [];

        var userIds = tasks.Select(t => t.UserId).Distinct().ToList();
        var submissions = (await submissionRepository.Get(s => userIds.Contains(s.UserId))).ToList();
        return submissions.Select(s => (s.UserId, s.Year, s.Month)).ToHashSet();
    }

    private async Task<List<OrganizationExportRow>> LoadOrganizationsAsync(TymeDataExtractionRequest request)
    {
        var query = dbContext.Organizations.AsNoTracking().AsQueryable();
        if (!request.IncludeArchived)
            query = query.Where(o => !o.IsArchived);
        if (!string.IsNullOrEmpty(request.OrganizationId))
            query = query.Where(o => o.OrganizationId == request.OrganizationId);

        return await query
            .OrderBy(o => o.Name)
            .Select(o => new OrganizationExportRow
            {
                OrganizationId = o.OrganizationId,
                Name = o.Name,
                Address = o.Address,
                City = o.City,
                State = o.State,
                PostalCode = o.PostalCode,
                Country = o.Country,
                Note = o.Note,
                Color = o.Color,
                IsActive = o.IsActive,
                IsArchived = o.IsArchived
            })
            .ToListAsync();
    }

    private async Task<List<ProjectGroupExportRow>> LoadProjectGroupsAsync(TymeDataExtractionRequest request)
    {
        var query = dbContext.ProjectGroups.AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(request.ProjectGroupId))
            query = query.Where(g => g.ProjectGroupId == request.ProjectGroupId);

        return await query
            .OrderBy(g => g.Name)
            .Select(g => new ProjectGroupExportRow
            {
                ProjectGroupId = g.ProjectGroupId,
                Name = g.Name,
                Color = g.Color
            })
            .ToListAsync();
    }

    private async Task<List<ProjectExportRow>> LoadProjectsAsync(TymeDataExtractionRequest request)
    {
        var query = dbContext.Projects.AsNoTracking().AsQueryable();
        if (!request.IncludeArchived)
            query = query.Where(p => !p.IsArchived);
        if (!string.IsNullOrEmpty(request.OrganizationId))
            query = query.Where(p => p.OrganizationId == request.OrganizationId);
        if (!string.IsNullOrEmpty(request.ProjectGroupId))
            query = query.Where(p => p.ProjectGroupId == request.ProjectGroupId);
        if (!string.IsNullOrEmpty(request.ProjectId))
            query = query.Where(p => p.ProjectId == request.ProjectId);

        return await query
            .OrderBy(p => p.Name)
            .Select(p => new ProjectExportRow
            {
                ProjectId = p.ProjectId,
                Name = p.Name,
                DisplayName = p.DisplayName,
                Slug = p.Slug,
                OrganizationId = p.OrganizationId,
                DepartmentId = p.DepartmentId,
                ProjectGroupId = p.ProjectGroupId,
                IsActive = p.IsActive,
                IsArchived = p.IsArchived,
                IsSharedAvailability = p.IsSharedAvailability,
                IsBillable = p.IsBillable
            })
            .ToListAsync();
    }

    private async Task<List<ApplicationUserExportRow>> LoadApplicationUsersAsync(TymeDataExtractionRequest request)
    {
        var tymeRoleIds = await dbContext.Roles.AsNoTracking()
            .Where(r => r.Name!.EndsWith(":" + Constants.Scopes.Tyme))
            .Select(r => r.Id)
            .ToListAsync();

        var tymeUserIds = await dbContext.UserRoles.AsNoTracking()
            .Where(ur => tymeRoleIds.Contains(ur.RoleId))
            .Select(ur => ur.UserId)
            .Distinct()
            .ToListAsync();

        if (request.UserIds.Count > 0)
            tymeUserIds = tymeUserIds.Where(id => request.UserIds.Contains(id)).ToList();

        if (tymeUserIds.Count == 0)
            return [];

        var query = dbContext.ApplicationUsers.AsNoTracking()
            .Where(u => tymeUserIds.Contains(u.Id));

        if (!request.IncludeArchived)
            query = query.Where(u => !u.IsArchived);

        return await query
            .OrderBy(u => u.LastName)
            .ThenBy(u => u.FirstName)
            .Select(u => new ApplicationUserExportRow
            {
                Id = u.Id,
                FirstName = u.FirstName,
                LastName = u.LastName,
                Email = u.Email,
                UserName = u.UserName,
                IsActive = u.IsActive,
                IsArchived = u.IsArchived,
                LastLoginDate = u.LastLoginDate,
                LastSignInAt = u.LastSignInAt
            })
            .ToListAsync();
    }

    private static List<TrackedTaskExportRow> MapTrackedTasks(
        List<TrackedTask> tasks,
        HashSet<(string UserId, int Year, int Month)> submittedSet) =>
        tasks
            .Select(task => new TrackedTaskExportRow
            {
                TaskId = task.TaskId,
                Name = task.Name,
                DurationSeconds = task.Duration.TotalSeconds,
                StartDate = task.StartDate,
                EndDate = task.EndDate,
                IsBillable = task.IsBillable,
                ProjectId = task.ProjectId,
                UserId = task.UserId,
                StopwatchItemId = task.StopwatchItemId,
                GoogleEventId = task.GoogleEventId,
                IsAllDay = task.IsAllDay,
                TeamAvailabilityEventId = task.TeamAvailabilityEventId,
                OrganizationId = task.Project?.OrganizationId,
                ProjectGroupId = task.Project?.ProjectGroupId,
                IsMonthSubmitted = submittedSet.Contains((task.UserId, task.StartDate.Year, task.StartDate.Month))
            })
            .OrderByDescending(t => t.StartDate)
            .ToList();

    private async Task<List<TrackedTaskAliasExportRow>> LoadAliasesAsync(
        TymeDataExtractionRequest request,
        List<TrackedTask>? scopedTasks)
    {
        List<TrackedTaskAlias> aliases;

        if (request.Entities.Contains(TymeDataExtractionRules.TrackedTasks) && scopedTasks is not null)
        {
            var taskIds = scopedTasks.Select(t => t.TaskId).ToList();
            if (taskIds.Count == 0)
                return [];

            aliases = (await aliasRepository.Get(
                a => taskIds.Contains(a.TaskId),
                includeProperties: "Project")).ToList();
        }
        else
        {
            var from = request.FromUtc;
            var to = request.ToUtc;
            aliases = (await aliasRepository.Get(
                a => (from == null || a.StartDate >= from) && (to == null || a.StartDate <= to),
                includeProperties: "Project")).ToList();

            if (request.UserIds.Count > 0)
            {
                var taskIdsForUsers = await dbContext.TrackedTasks.AsNoTracking()
                    .Where(t => request.UserIds.Contains(t.UserId))
                    .Select(t => t.TaskId)
                    .ToListAsync();
                aliases = aliases.Where(a => taskIdsForUsers.Contains(a.TaskId)).ToList();
            }

            if (!string.IsNullOrEmpty(request.ProjectId))
                aliases = aliases.Where(a => a.ProjectId == request.ProjectId).ToList();
            else if (!string.IsNullOrEmpty(request.OrganizationId))
                aliases = aliases.Where(a => a.Project?.OrganizationId == request.OrganizationId).ToList();
            else if (!string.IsNullOrEmpty(request.ProjectGroupId))
                aliases = aliases.Where(a => a.Project?.ProjectGroupId == request.ProjectGroupId).ToList();
        }

        return aliases
            .Select(alias => new TrackedTaskAliasExportRow
            {
                TrackedTaskAliasId = alias.TrackedTaskAliasId,
                TaskId = alias.TaskId,
                Name = alias.Name,
                StartDate = alias.StartDate,
                DurationSeconds = alias.Duration.TotalSeconds,
                ProjectId = alias.ProjectId,
                IsBillable = alias.IsBillable,
                CreatedByUserId = alias.CreatedByUserId,
                CreatedAt = alias.CreatedAt,
                UpdatedAt = alias.UpdatedAt
            })
            .OrderBy(a => a.TaskId)
            .ToList();
    }

    private async Task<List<TrackedTaskCorrectionAuditExportRow>> LoadAuditsAsync(
        TymeDataExtractionRequest request,
        List<TrackedTask>? scopedTasks)
    {
        IQueryable<TrackedTaskCorrectionAudit> query = dbContext.TrackedTaskCorrectionAudits.AsNoTracking();

        if (request.Entities.Contains(TymeDataExtractionRules.TrackedTasks) && scopedTasks is not null)
        {
            var taskIds = scopedTasks.Select(t => t.TaskId).ToList();
            if (taskIds.Count == 0)
                return [];

            query = query.Where(a => taskIds.Contains(a.TaskId));
        }
        else
        {
            var from = request.FromUtc;
            var to = request.ToUtc;
            if (from.HasValue)
                query = query.Where(a => a.CorrectedAtUtc >= from.Value);
            if (to.HasValue)
                query = query.Where(a => a.CorrectedAtUtc <= to.Value);
        }

        var audits = await query.ToListAsync();

        if (request.UserIds.Count > 0)
        {
            var taskIdsForUsers = await dbContext.TrackedTasks.AsNoTracking()
                .Where(t => request.UserIds.Contains(t.UserId))
                .Select(t => t.TaskId)
                .ToListAsync();
            audits = audits.Where(a => taskIdsForUsers.Contains(a.TaskId)).ToList();
        }

        return audits
            .Select(audit => new TrackedTaskCorrectionAuditExportRow
            {
                TrackedTaskCorrectionAuditId = audit.TrackedTaskCorrectionAuditId,
                TaskId = audit.TaskId,
                CorrectedByUserId = audit.CorrectedByUserId,
                CorrectedAtUtc = audit.CorrectedAtUtc,
                PreviousName = audit.PreviousName,
                PreviousStartDate = audit.PreviousStartDate,
                PreviousDurationSeconds = audit.PreviousDuration.TotalSeconds,
                PreviousProjectId = audit.PreviousProjectId,
                PreviousIsBillable = audit.PreviousIsBillable,
                NewName = audit.NewName,
                NewStartDate = audit.NewStartDate,
                NewDurationSeconds = audit.NewDuration.TotalSeconds,
                NewProjectId = audit.NewProjectId,
                NewIsBillable = audit.NewIsBillable
            })
            .OrderBy(a => a.TaskId)
            .ToList();
    }

    private async Task<List<TimeSubmissionExportRow>> LoadTimeSubmissionsAsync(TymeDataExtractionRequest request)
    {
        var submissions = (await submissionRepository.Get(_ => true)).ToList();

        if (request.UserIds.Count > 0)
            submissions = submissions.Where(s => request.UserIds.Contains(s.UserId)).ToList();

        if (request.FromUtc.HasValue && request.ToUtc.HasValue)
        {
            submissions = submissions
                .Where(s => TymeDataExtractionRules.SubmissionMonthOverlapsRange(
                    s.Year, s.Month, request.FromUtc.Value, request.ToUtc.Value))
                .ToList();
        }

        return submissions
            .Select(s => new TimeSubmissionExportRow
            {
                TimeSubmissionId = s.TimeSubmissionId,
                UserId = s.UserId,
                Year = s.Year,
                Month = s.Month,
                SubmittedAt = s.SubmittedAt,
                SubmittedByUserId = s.SubmittedByUserId
            })
            .OrderBy(s => s.UserId)
            .ThenBy(s => s.Year)
            .ThenBy(s => s.Month)
            .ToList();
    }
}

public sealed class TymeDataExtractionRequest
{
    public required HashSet<string> Entities { get; init; }
    public DateTime? FromUtc { get; init; }
    public DateTime? ToUtc { get; init; }
    public bool IncludeArchived { get; init; }
    public string? OrganizationId { get; init; }
    public string? ProjectGroupId { get; init; }
    public string? ProjectId { get; init; }
    public HashSet<string> UserIds { get; init; } = new(StringComparer.Ordinal);
}