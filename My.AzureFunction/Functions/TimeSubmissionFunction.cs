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
using My.Shared.Dtos.TimeSubmission;
using My.Shared.Rules;

namespace My.Functions
{
    public class TimeSubmissionFunction
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly ILogger<TimeSubmissionFunction> _logger;
        private readonly IValidator<CreateTimeSubmissionDto> _createValidator;

        public TimeSubmissionFunction(
            ApplicationDbContext dbContext,
            ILogger<TimeSubmissionFunction> logger,
            IValidator<CreateTimeSubmissionDto> createValidator)
        {
            _dbContext = dbContext;
            _logger = logger;
            _createValidator = createValidator;
        }

        [Function("GetTimeSubmissions")]
        public async Task<IActionResult> GetMineAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "timesubmissions")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, out var userId) is IActionResult unauth) return unauth;

            var rows = await _dbContext.TimeSubmissions
                .Where(s => s.UserId == userId)
                .OrderByDescending(s => s.Year).ThenByDescending(s => s.Month)
                .Select(s => new TimeSubmissionDto
                {
                    TimeSubmissionId = s.TimeSubmissionId,
                    UserId = s.UserId,
                    Year = s.Year,
                    Month = s.Month,
                    SubmittedAt = s.SubmittedAt,
                    SubmittedByUserId = s.SubmittedByUserId
                })
                .ToListAsync();

            return new OkObjectResult(rows);
        }

        [Function("GetOverdueTimeSubmissions")]
        public async Task<IActionResult> GetOverdueAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "timesubmissions/overdue")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, out var userId) is IActionResult unauth) return unauth;

            var overdue = await ComputeOverdueAsync(userId);
            return new OkObjectResult(overdue);
        }

        /// <summary>
        /// Manager team view: one row per (user × month) for users in the caller's scope
        /// where the user has tracked time in that past month, with submitted/unsubmitted
        /// status. Optional query string filters: ?status=submitted|unsubmitted|all (default
        /// "all"), ?userId, ?year, ?month. Used by the Submit page (full filters) and the
        /// Dashboard widget (pre-filtered to status=unsubmitted).
        /// </summary>
        [Function("GetTeamTimeSubmissions")]
        public async Task<IActionResult> GetTeamAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "timesubmissions/team")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            // Scoped-only: this is a Tyme module surface; global Admin doesn't qualify
            // by virtue of being a super-role. Caller must be Manager:Tyme or Admin:Tyme.
            if (AuthGates.RequireScopedTyme(principal, Constants.Roles.Manager) is IActionResult unauth) return unauth;

            var statusFilter = (req.Query["status"] ?? "all").ToLowerInvariant();
            var userIdFilter = req.Query["userId"];
            int? yearFilter = int.TryParse(req.Query["year"], out var y) ? y : null;
            int? monthFilter = int.TryParse(req.Query["month"], out var m) ? m : null;

            var nowUtc = DateTime.UtcNow;
            var currentMonthStart = new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);

            // (user × month) pairs derived from tracked-task history — i.e. months a user
            // actually worked. The submission table alone wouldn't surface unsubmitted months.
            var taskMonths = await _dbContext.TrackedTasks
                .Where(t => t.StartDate < currentMonthStart)
                .Select(t => new { t.UserId, t.StartDate.Year, t.StartDate.Month })
                .Distinct()
                .ToListAsync();

            var submissions = await _dbContext.TimeSubmissions
                .Select(s => new { s.TimeSubmissionId, s.UserId, s.Year, s.Month, s.SubmittedAt })
                .ToListAsync();
            var submissionByKey = submissions.ToDictionary(s => (s.UserId, s.Year, s.Month));

            // Union: every month a user worked (tracked tasks) plus every submission row,
            // de-duped. Submissions with no tracked tasks (rare but possible) still surface.
            var keys = new HashSet<(string UserId, int Year, int Month)>(
                taskMonths.Select(t => (t.UserId, t.Year, t.Month)));
            foreach (var s in submissions)
                keys.Add((s.UserId, s.Year, s.Month));

            // Restrict to users visible in manager Tyme team surfaces. Pre-fetch each
            // user's roles once (Managers see Tyme-scoped users; admins use IsVisibleTo).
            var userIds = keys.Select(k => k.UserId).Distinct().ToList();
            if (userIds.Count == 0)
                return new OkObjectResult(new List<TeamSubmissionRowDto>());

            var users = await _dbContext.ApplicationUsers
                .Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email })
                .ToListAsync();
            var userMap = users.ToDictionary(u => u.Id);

            var rolesByUser = await (from ur in _dbContext.UserRoles
                                     where userIds.Contains(ur.UserId)
                                     join r in _dbContext.Roles on ur.RoleId equals r.Id
                                     select new { ur.UserId, RoleName = r.Name! })
                                    .ToListAsync();
            var roleSetByUser = rolesByUser
                .GroupBy(x => x.UserId)
                .ToDictionary(g => g.Key, g => (IList<string>)g.Select(x => x.RoleName).ToList());

            var rows = new List<TeamSubmissionRowDto>(keys.Count);
            foreach (var key in keys)
            {
                if (!userMap.TryGetValue(key.UserId, out var u)) continue;

                // Scope gate: skip users the caller can't manage in their scope.
                var roles = roleSetByUser.TryGetValue(key.UserId, out var rs) ? rs : Array.Empty<string>();
                if (!Constants.Roles.IsVisibleInTymeTeamView(principal, roles)) continue;

                if (!string.IsNullOrEmpty(userIdFilter) && key.UserId != userIdFilter) continue;
                if (yearFilter.HasValue && key.Year != yearFilter.Value) continue;
                if (monthFilter.HasValue && key.Month != monthFilter.Value) continue;

                var submitted = submissionByKey.TryGetValue(key, out var s);
                if (statusFilter == "submitted" && !submitted) continue;
                if (statusFilter == "unsubmitted" && submitted) continue;

                rows.Add(new TeamSubmissionRowDto
                {
                    UserId = key.UserId,
                    UserName = UserDisplayNameRules.Resolve(u.FirstName, u.LastName, u.Email),
                    Year = key.Year,
                    Month = key.Month,
                    IsSubmitted = submitted,
                    SubmittedAt = submitted ? s!.SubmittedAt : null,
                    TimeSubmissionId = submitted ? s!.TimeSubmissionId : null
                });
            }

            // Default sort: newest months first, then user name. Predictable for both
            // consumers; the SPA can re-sort client-side if it wants something else.
            var ordered = rows
                .OrderByDescending(r => r.Year)
                .ThenByDescending(r => r.Month)
                .ThenBy(r => r.UserName)
                .ToList();

            return new OkObjectResult(ordered);
        }

        [Function("CreateTimeSubmission")]
        public async Task<IActionResult> CreateAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "timesubmissions")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, out var userId) is IActionResult unauth) return unauth;

            var (body, validationError) = await RequestValidator.ReadJsonAndValidateAsync(req, _createValidator);
            if (validationError != null)
                return validationError;

            var existing = await _dbContext.TimeSubmissions
                .FirstOrDefaultAsync(s => s.UserId == userId && s.Year == body!.Year && s.Month == body.Month);
            if (existing != null)
                return new BadRequestObjectResult("This month has already been submitted.");

            var nowUtc = DateTime.UtcNow;
            var entity = new TimeSubmission
            {
                UserId = userId,
                Year = body!.Year,
                Month = body.Month,
                SubmittedAt = nowUtc,
                SubmittedByUserId = userId
            };

            _dbContext.TimeSubmissions.Add(entity);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("User {UserId} submitted time for {Year}-{Month:00}.", userId, body.Year, body.Month);

            return new OkObjectResult(new TimeSubmissionDto
            {
                TimeSubmissionId = entity.TimeSubmissionId,
                UserId = entity.UserId,
                Year = entity.Year,
                Month = entity.Month,
                SubmittedAt = entity.SubmittedAt,
                SubmittedByUserId = entity.SubmittedByUserId
            });
        }

        [Function("GetTimeSubmissionCorrections")]
        public async Task<IActionResult> GetCorrectionsAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "timesubmissions/{id}/corrections")] HttpRequestData req,
            string id)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, Constants.Roles.Manager) is IActionResult unauth)
                return unauth;

            var submission = await _dbContext.TimeSubmissions.AsNoTracking()
                .FirstOrDefaultAsync(s => s.TimeSubmissionId == id);
            if (submission == null)
                return new NotFoundObjectResult("Time submission not found.");

            if (!await CanManageSubmissionUserAsync(principal, submission.UserId))
                return new ForbidResult();

            var monthStart = new DateTime(submission.Year, submission.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var monthEnd = monthStart.AddMonths(1).AddTicks(-1);

            var tasks = await _dbContext.TrackedTasks.AsNoTracking()
                .Include(t => t.Project)
                .Where(t => t.UserId == submission.UserId
                         && t.StartDate >= monthStart
                         && t.StartDate <= monthEnd)
                .ToListAsync();
            var taskIds = tasks.Select(t => t.TaskId).ToList();

            var aliases = await _dbContext.TrackedTaskAliases.AsNoTracking()
                .Include(a => a.Project)
                .Where(a => taskIds.Contains(a.TaskId))
                .ToListAsync();
            var audits = await _dbContext.TrackedTaskCorrectionAudits.AsNoTracking()
                .Where(a => taskIds.Contains(a.TaskId))
                .ToDictionaryAsync(a => a.TaskId);
            var taskById = tasks.ToDictionary(t => t.TaskId);

            var items = new List<TimeSubmissionCorrectionItemDto>();

            foreach (var alias in aliases)
            {
                if (!taskById.TryGetValue(alias.TaskId, out var task))
                    continue;

                items.Add(new TimeSubmissionCorrectionItemDto
                {
                    TaskId = alias.TaskId,
                    Kind = "Alias",
                    TaskName = task.Name,
                    OriginalName = task.Name,
                    OriginalStartDate = task.StartDate,
                    OriginalDurationSeconds = task.Duration.TotalSeconds,
                    OriginalProjectName = task.Project?.Name,
                    AdjustedName = alias.Name,
                    AdjustedStartDate = alias.StartDate,
                    AdjustedDurationSeconds = alias.Duration.TotalSeconds,
                    AdjustedProjectName = alias.Project?.Name
                });
            }

            foreach (var audit in audits.Values)
            {
                if (!taskById.TryGetValue(audit.TaskId, out var task))
                    continue;

                items.Add(new TimeSubmissionCorrectionItemDto
                {
                    TaskId = audit.TaskId,
                    Kind = "Direct",
                    TaskName = task.Name,
                    OriginalName = audit.PreviousName,
                    OriginalStartDate = audit.PreviousStartDate,
                    OriginalDurationSeconds = audit.PreviousDuration.TotalSeconds,
                    OriginalProjectName = null,
                    AdjustedName = audit.NewName,
                    AdjustedStartDate = audit.NewStartDate,
                    AdjustedDurationSeconds = audit.NewDuration.TotalSeconds,
                    AdjustedProjectName = task.Project?.Name
                });
            }

            return new OkObjectResult(new TimeSubmissionCorrectionsDto
            {
                Items = items.OrderBy(i => i.AdjustedStartDate).ToList(),
                DirectCorrectionCount = audits.Count
            });
        }

        [Function("UnsubmitTimeSubmission")]
        public async Task<IActionResult> UnsubmitWithReconciliationAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "timesubmissions/{id}/unsubmit")] HttpRequestData req,
            string id)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, Constants.Roles.Manager) is IActionResult unauth)
                return unauth;
            var actorId = principal.FindFirstValue(Constants.Claims.UserId);

            var existing = await _dbContext.TimeSubmissions.FirstOrDefaultAsync(s => s.TimeSubmissionId == id);
            if (existing == null)
                return new NotFoundObjectResult("Time submission not found.");

            if (!await CanManageSubmissionUserAsync(principal, existing.UserId))
                return new ForbidResult();

            UnsubmitTimeSubmissionDto? body = null;
            try
            {
                body = await req.ReadFromJsonAsync<UnsubmitTimeSubmissionDto>();
            }
            catch
            {
                // optional body
            }

            var monthStart = new DateTime(existing.Year, existing.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var monthEnd = monthStart.AddMonths(1).AddTicks(-1);

            var taskIdsInMonth = await _dbContext.TrackedTasks.AsNoTracking()
                .Where(t => t.UserId == existing.UserId
                         && t.StartDate >= monthStart
                         && t.StartDate <= monthEnd)
                .Select(t => t.TaskId)
                .ToListAsync();

            var aliases = await _dbContext.TrackedTaskAliases
                .Include(a => a.Project)
                .Where(a => taskIdsInMonth.Contains(a.TaskId))
                .ToListAsync();

            if (aliases.Count > 0)
            {
                var reconciliation = body?.AliasReconciliation;
                if (reconciliation != "ApplyToTasks" && reconciliation != "KeepOriginals")
                    return new BadRequestObjectResult("Alias corrections exist — choose ApplyToTasks or KeepOriginals.");

                var targetIds = ResolveAliasTaskIds(aliases, body?.DeleteAliasTaskIds);

                if (reconciliation == "ApplyToTasks")
                {
                    var tasks = await _dbContext.TrackedTasks
                        .Where(t => targetIds.Contains(t.TaskId))
                        .ToListAsync();
                    var taskById = tasks.ToDictionary(t => t.TaskId);

                    foreach (var alias in aliases.Where(a => targetIds.Contains(a.TaskId)))
                    {
                        if (!taskById.TryGetValue(alias.TaskId, out var task))
                            continue;

                        task.Name = alias.Name;
                        task.StartDate = alias.StartDate;
                        task.Duration = alias.Duration;
                        task.ProjectId = alias.ProjectId;
                        task.EndDate = alias.Duration > TimeSpan.Zero
                            ? alias.StartDate + alias.Duration
                            : null;
                    }

                    _dbContext.TrackedTaskAliases.RemoveRange(aliases.Where(a => targetIds.Contains(a.TaskId)));
                }
                else
                {
                    _dbContext.TrackedTaskAliases.RemoveRange(aliases.Where(a => targetIds.Contains(a.TaskId)));
                }
            }

            _dbContext.TimeSubmissions.Remove(existing);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("User {ActorId} unsubmitted {Year}-{Month:00} for user {TargetId}.",
                actorId, existing.Year, existing.Month, existing.UserId);

            return new NoContentResult();
        }

        [Function("DeleteTimeSubmission")]
        public async Task<IActionResult> DeleteAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "timesubmissions/{id}")] HttpRequestData req,
            string id)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, Constants.Roles.Manager) is IActionResult unauth)
                return unauth;

            var existing = await _dbContext.TimeSubmissions.FirstOrDefaultAsync(s => s.TimeSubmissionId == id);
            if (existing == null)
                return new NotFoundObjectResult("Time submission not found.");

            if (!await CanManageSubmissionUserAsync(principal, existing.UserId))
                return new ForbidResult();

            var monthStart = new DateTime(existing.Year, existing.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var monthEnd = monthStart.AddMonths(1).AddTicks(-1);
            var taskIdsInMonth = await _dbContext.TrackedTasks.AsNoTracking()
                .Where(t => t.UserId == existing.UserId
                         && t.StartDate >= monthStart
                         && t.StartDate <= monthEnd)
                .Select(t => t.TaskId)
                .ToListAsync();
            var hasAliases = await _dbContext.TrackedTaskAliases.AnyAsync(a => taskIdsInMonth.Contains(a.TaskId));
            if (hasAliases)
                return new BadRequestObjectResult("This month has alias corrections — use POST /timesubmissions/{id}/unsubmit with reconciliation options.");

            _dbContext.TimeSubmissions.Remove(existing);
            await _dbContext.SaveChangesAsync();

            return new NoContentResult();
        }

        private static HashSet<string> ResolveAliasTaskIds(
            IReadOnlyCollection<TrackedTaskAlias> aliases,
            IReadOnlyList<string>? deleteAliasTaskIds)
        {
            if (deleteAliasTaskIds is { Count: > 0 })
                return deleteAliasTaskIds.ToHashSet();

            return aliases.Select(a => a.TaskId).ToHashSet();
        }

        private async Task<bool> CanManageSubmissionUserAsync(ClaimsPrincipal principal, string userId)
        {
            var roleRows = await (from ur in _dbContext.UserRoles
                                  where ur.UserId == userId
                                  join r in _dbContext.Roles on ur.RoleId equals r.Id
                                  select r.Name!)
                                 .ToListAsync();
            return Constants.Roles.IsVisibleInTymeTeamView(principal, roleRows);
        }



        /// <summary>
        /// Returns the months for the given user where:
        ///   - the month is strictly before the current calendar month (UTC),
        ///   - the user has at least one tracked task that month, and
        ///   - no TimeSubmission row exists for that (user, year, month).
        /// </summary>
        private async Task<List<OverdueMonthDto>> ComputeOverdueAsync(string userId)
        {
            var nowUtc = DateTime.UtcNow;
            var currentMonthStart = new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);

            var taskMonths = await _dbContext.TrackedTasks
                .Where(t => t.UserId == userId && t.StartDate < currentMonthStart)
                .Select(t => new { t.StartDate.Year, t.StartDate.Month })
                .Distinct()
                .ToListAsync();

            if (taskMonths.Count == 0) return new List<OverdueMonthDto>();

            var submitted = await _dbContext.TimeSubmissions
                .Where(s => s.UserId == userId)
                .Select(s => new { s.Year, s.Month })
                .ToListAsync();

            var submittedSet = submitted
                .Select(x => (x.Year, x.Month))
                .ToHashSet();

            return taskMonths
                .Where(m => !submittedSet.Contains((m.Year, m.Month)))
                .OrderBy(m => m.Year).ThenBy(m => m.Month)
                .Select(m => new OverdueMonthDto { Year = m.Year, Month = m.Month })
                .ToList();
        }
    }
}
