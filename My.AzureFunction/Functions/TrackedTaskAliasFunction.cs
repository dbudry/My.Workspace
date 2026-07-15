using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using My.DAL.Data;
using My.DAL.Models;
using My.Functions.Authorization;
using My.Functions.Helpers;
using My.Shared.Constants;
using My.Shared.Dtos.TrackedTaskAlias;
using My.Shared.Rules;

namespace My.Functions
{
    public class TrackedTaskAliasFunction
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly ILogger<TrackedTaskAliasFunction> _logger;
        private readonly IValidator<UpsertTrackedTaskAliasDto> _upsertValidator;

        public TrackedTaskAliasFunction(
            ApplicationDbContext dbContext,
            ILogger<TrackedTaskAliasFunction> logger,
            IValidator<UpsertTrackedTaskAliasDto> upsertValidator)
        {
            _dbContext = dbContext;
            _logger = logger;
            _upsertValidator = upsertValidator;
        }

        [Function("GetTrackedTaskAliases")]
        public async Task<IActionResult> GetAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "trackedtaskaliases")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, Constants.Roles.Manager) is IActionResult unauth) return unauth;

            var filterUser = req.Query["userId"];
            DateTime? from = DateTime.TryParse(req.Query["from"], out var f) ? f.ToUniversalTime() : null;
            DateTime? to = DateTime.TryParse(req.Query["to"], out var t) ? t.ToUniversalTime().AddDays(1).AddTicks(-1) : null;

            var q = _dbContext.TrackedTaskAliases
                .Include(a => a.Project)
                .Include(a => a.Task)
                .AsQueryable();

            if (!string.IsNullOrEmpty(filterUser))
                q = q.Where(a => a.Task.UserId == filterUser);
            if (from.HasValue)
                q = q.Where(a => a.StartDate >= from.Value);
            if (to.HasValue)
                q = q.Where(a => a.StartDate <= to.Value);

            var aliases = await q.ToListAsync();
            var ownerIds = aliases.Select(a => a.Task!.UserId).Distinct().ToList();
            var rolesByOwner = await LoadRolesByUserAsync(ownerIds);

            var rows = aliases
                .Where(a =>
                {
                    var roles = rolesByOwner.TryGetValue(a.Task!.UserId, out var rs)
                        ? rs
                        : Array.Empty<string>();
                    return Constants.Roles.IsVisibleInTymeTeamView(principal, roles);
                })
                .Select(a => new TrackedTaskAliasDto
                {
                    TrackedTaskAliasId = a.TrackedTaskAliasId,
                    TaskId = a.TaskId,
                    Name = a.Name,
                    StartDate = a.StartDate,
                    Duration = a.Duration,
                    ProjectId = a.ProjectId,
                    ProjectName = a.Project?.Name,
                    IsBillable = a.IsBillable,
                    CreatedByUserId = a.CreatedByUserId,
                    CreatedAt = a.CreatedAt,
                    UpdatedAt = a.UpdatedAt
                })
                .ToList();

            return new OkObjectResult(rows);
        }

        [Function("UpsertTrackedTaskAlias")]
        public async Task<IActionResult> UpsertAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "trackedtaskaliases/{taskId}")] HttpRequestData req,
            string taskId)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, out var actorId, Constants.Roles.Manager) is IActionResult unauth) return unauth;

            var (body, validationError) = await RequestValidator.ReadJsonAndValidateAsync(req, _upsertValidator);
            if (validationError != null)
                return validationError;

            var correctionSettings = await ManagerCorrectionSettingsLoader.LoadAsync(_dbContext);

            var task = await _dbContext.TrackedTasks.FirstOrDefaultAsync(t => t.TaskId == taskId);
            if (task == null)
                return new NotFoundObjectResult("Tracked task not found.");

            if (!await CanManageTaskOwnerAsync(principal, task.UserId))
                return new ForbidResult();

            // Aliases can only be created for tasks in a currently submitted month —
            // this is the manager's correction tool *after* a user has submitted.
            // Updates to an existing alias don't require re-checking submission state
            // (the original alias guarantees the month was submitted at creation time).
            var taskMonthSubmitted = await _dbContext.TimeSubmissions
                .AnyAsync(s => s.UserId == task.UserId
                            && s.Year == task.StartDate.Year
                            && s.Month == task.StartDate.Month);

            var existing = await _dbContext.TrackedTaskAliases
                .FirstOrDefaultAsync(a => a.TaskId == taskId);

            if (existing == null && await _dbContext.TrackedTaskCorrectionAudits.AnyAsync(a => a.TaskId == taskId))
                return new BadRequestObjectResult("This task has a direct correction. Alias overlays are not allowed on the same task.");

            var settingsDecision = ManagerCorrectionRules.Evaluate(
                ManagerCorrectionRules.CorrectionMode.Alias,
                existing == null
                    ? ManagerCorrectionRules.CorrectionAction.Create
                    : ManagerCorrectionRules.CorrectionAction.Update,
                correctionSettings);
            if (!settingsDecision.IsAllowed)
                return new BadRequestObjectResult(settingsDecision.Reason!);

            if (existing == null)
            {
                var aliasDecision = SubmissionRules.Evaluate(SubmissionRules.Operation.Alias, taskMonthSubmitted);
                if (!aliasDecision.IsAllowed) return new BadRequestObjectResult(aliasDecision.Reason!);
            }

            // Validate ProjectId if provided — must point to an active, non-archived project
            // and live under an active org/department too.
            if (!string.IsNullOrEmpty(body!.ProjectId))
            {
                var project = await _dbContext.Projects
                    .Include(p => p.Organization)
                    .Include(p => p.Department)
                    .FirstOrDefaultAsync(p => p.ProjectId == body.ProjectId);
                if (project == null)
                    return new BadRequestObjectResult("Project not found.");
                if (project.IsArchived) return new BadRequestObjectResult("Cannot alias to an archived project.");
                if (!project.IsActive) return new BadRequestObjectResult("Cannot alias to an inactive project.");
                if (project.Organization is { IsArchived: true })
                    return new BadRequestObjectResult("Cannot alias to a project whose organization is archived.");
                if (project.Organization is { IsActive: false })
                    return new BadRequestObjectResult("Cannot alias to a project whose organization is inactive.");
                if (project.Department is { IsArchived: true })
                    return new BadRequestObjectResult("Cannot alias to a project whose department is archived.");
                if (project.Department is { IsActive: false })
                    return new BadRequestObjectResult("Cannot alias to a project whose department is inactive.");
            }

            var startUtc = body.StartDate.Kind == DateTimeKind.Utc ? body.StartDate : body.StartDate.ToUniversalTime();
            var nowUtc = DateTime.UtcNow;

            if (existing == null)
            {
                existing = new TrackedTaskAlias
                {
                    TaskId = taskId,
                    Name = body.Name.Trim(),
                    StartDate = startUtc,
                    Duration = body.Duration,
                    ProjectId = string.IsNullOrEmpty(body.ProjectId) ? null : body.ProjectId,
                    IsBillable = body.IsBillable,
                    CreatedByUserId = actorId,
                    CreatedAt = nowUtc,
                    UpdatedAt = nowUtc
                };
                _dbContext.TrackedTaskAliases.Add(existing);
                _logger.LogInformation("User {ActorId} created alias for task {TaskId}.", actorId, taskId);
            }
            else
            {
                existing.Name = body.Name.Trim();
                existing.StartDate = startUtc;
                existing.Duration = body.Duration;
                existing.ProjectId = string.IsNullOrEmpty(body.ProjectId) ? null : body.ProjectId;
                existing.IsBillable = body.IsBillable;
                existing.UpdatedAt = nowUtc;
                _logger.LogInformation("User {ActorId} updated alias for task {TaskId}.", actorId, taskId);
            }

            await _dbContext.SaveChangesAsync();

            // Reload with Project included for the response
            await _dbContext.Entry(existing).Reference(a => a.Project).LoadAsync();

            return new OkObjectResult(new TrackedTaskAliasDto
            {
                TrackedTaskAliasId = existing.TrackedTaskAliasId,
                TaskId = existing.TaskId,
                Name = existing.Name,
                StartDate = existing.StartDate,
                Duration = existing.Duration,
                ProjectId = existing.ProjectId,
                ProjectName = existing.Project?.Name,
                IsBillable = existing.IsBillable,
                CreatedByUserId = existing.CreatedByUserId,
                CreatedAt = existing.CreatedAt,
                UpdatedAt = existing.UpdatedAt
            });
        }

        [Function("DeleteTrackedTaskAlias")]
        public async Task<IActionResult> DeleteAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "trackedtaskaliases/{taskId}")] HttpRequestData req,
            string taskId)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, out var actorId, Constants.Roles.Manager) is IActionResult unauth) return unauth;

            var existing = await _dbContext.TrackedTaskAliases
                .Include(a => a.Task)
                .FirstOrDefaultAsync(a => a.TaskId == taskId);
            if (existing == null)
                return new NotFoundObjectResult("Alias not found.");

            if (!await CanManageTaskOwnerAsync(principal, existing.Task!.UserId))
                return new ForbidResult();

            _dbContext.TrackedTaskAliases.Remove(existing);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("User {ActorId} removed alias for task {TaskId}.", actorId, taskId);
            return new NoContentResult();
        }

        private async Task<bool> CanManageTaskOwnerAsync(ClaimsPrincipal principal, string taskUserId)
        {
            var roleRows = await (from ur in _dbContext.UserRoles
                                  where ur.UserId == taskUserId
                                  join r in _dbContext.Roles on ur.RoleId equals r.Id
                                  select r.Name!)
                                 .ToListAsync();
            return Constants.Roles.IsVisibleInTymeTeamView(principal, roleRows);
        }

        private async Task<Dictionary<string, IList<string>>> LoadRolesByUserAsync(IReadOnlyCollection<string> userIds)
        {
            if (userIds.Count == 0)
                return new Dictionary<string, IList<string>>();

            var roleRows = await (from ur in _dbContext.UserRoles
                                  where userIds.Contains(ur.UserId)
                                  join r in _dbContext.Roles on ur.RoleId equals r.Id
                                  select new { ur.UserId, RoleName = r.Name! })
                                 .ToListAsync();

            return roleRows
                .GroupBy(r => r.UserId)
                .ToDictionary(g => g.Key, g => (IList<string>)g.Select(r => r.RoleName).ToList());
        }
    }
}
