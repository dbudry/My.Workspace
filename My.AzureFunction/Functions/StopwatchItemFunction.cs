using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using My.DAL.Data;
using My.DAL.Models;
using My.DAL.Models.Paging;
using My.DAL.Repository;
using My.Functions.Authorization;
using My.Functions.Helpers;
using My.Functions.Services;
using My.Shared.Constants;
using My.Shared.Dtos.Paging;
using My.Shared.Dtos.StopwatchItem;
using My.Shared.Dtos.TrackedTask;
using My.Shared.Rules;

namespace My.Functions
{
    public class StopwatchItemFunction
    {
        private readonly IRepository<StopwatchItem> itemRepository;
        private readonly IRepository<TrackedTask> taskRepository;
        private readonly IRepository<TimeSubmission> submissionRepository;
        private readonly IRepository<Project> projectRepository;
        private readonly ApplicationDbContext dbContext;
        private readonly AppMapper mapper;
        private readonly GoogleCalendarService googleCalendar;
        private readonly TeamAvailabilityPublisher teamAvailabilityPublisher;
        private readonly ILogger<StopwatchItemFunction> logger;
        private readonly IValidator<CreateStopwatchItemDto> createValidator;
        private readonly IValidator<UpdateStopwatchItemDto> updateValidator;

        public StopwatchItemFunction(
            IRepositoryFactory repositoryFactory,
            ApplicationDbContext dbContext,
            AppMapper mapper,
            GoogleCalendarService googleCalendar,
            TeamAvailabilityPublisher teamAvailabilityPublisher,
            ILogger<StopwatchItemFunction> logger,
            IValidator<CreateStopwatchItemDto> createValidator,
            IValidator<UpdateStopwatchItemDto> updateValidator)
        {
            this.dbContext = dbContext;
            this.mapper = mapper;
            this.googleCalendar = googleCalendar;
            this.teamAvailabilityPublisher = teamAvailabilityPublisher;
            this.logger = logger;
            this.createValidator = createValidator;
            this.updateValidator = updateValidator;
            itemRepository = repositoryFactory.GetRepository<StopwatchItem>();
            taskRepository = repositoryFactory.GetRepository<TrackedTask>();
            submissionRepository = repositoryFactory.GetRepository<TimeSubmission>();
            projectRepository = repositoryFactory.GetRepository<Project>();
        }

        [Function("GetStopwatchItems")]
        public async Task<IActionResult> GetStopwatchItemsAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "stopwatchitems")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, out var userId) is IActionResult unauth) return unauth;

            var listQuery = HttpListQueryParser.ParseListQuery(req);
            var parameters = HttpListQueryParser.ToPagingParameters(listQuery);
            if (string.IsNullOrWhiteSpace(parameters.SortBy))
            {
                parameters.SortBy = "LastWorkedAt";
                parameters.SortDescending = true;
            }

            var filter = (System.Linq.Expressions.Expression<Func<StopwatchItem, bool>>)(i => i.UserId == userId);
            var orderBy = OrderStopwatchItems(parameters.SortBy, parameters.SortDescending);

            var paged = await itemRepository.GetPaged(
                parameters,
                filter: filter,
                orderBy: orderBy,
                includeProperties: "Project.ProjectGroup,Project.Organization");

            var itemIds = paged.Select(i => i.StopwatchItemId).ToList();
            var sessions = itemIds.Count == 0
                ? new List<TrackedTask>()
                : await dbContext.TrackedTasks.AsNoTracking()
                    .Where(t => t.StopwatchItemId != null && itemIds.Contains(t.StopwatchItemId))
                    .ToListAsync();

            var sessionsByItem = sessions.GroupBy(t => t.StopwatchItemId!).ToDictionary(g => g.Key, g => g.ToList());
            var dtos = paged.Select(i => ToListDto(i, sessionsByItem.GetValueOrDefault(i.StopwatchItemId) ?? new List<TrackedTask>())).ToList();

            return new OkObjectResult(new PagedResponse<StopwatchItemDto>
            {
                Items = dtos,
                TotalCount = paged.TotalCount,
                PageSize = paged.PageSize,
                CurrentPage = paged.CurrentPage,
                TotalPages = paged.TotalPages,
                HasNext = paged.HasNext,
                HasPrevious = paged.HasPrevious
            });
        }

        [Function("CreateStopwatchItem")]
        public async Task<IActionResult> CreateStopwatchItemAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "stopwatchitems")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, out var userId) is IActionResult unauth) return unauth;

            var dto = await req.ReadFromJsonAsync<CreateStopwatchItemDto>();
            if (await RequestValidator.BadRequestIfInvalidAsync(createValidator, dto) is { } validationError)
                return validationError;

            var projectIssue = await ValidateProjectIsLoggable(dto!.ProjectId);
            if (projectIssue != null)
                return new BadRequestObjectResult(projectIssue);

            var now = DateTime.UtcNow;
            var item = new StopwatchItem
            {
                UserId = userId,
                Name = dto.Name.Trim(),
                ProjectId = dto.ProjectId,
                CreatedAt = now,
                LastWorkedAt = now
            };

            await itemRepository.Insert(item);

            var loaded = (await itemRepository.Get(
                i => i.StopwatchItemId == item.StopwatchItemId,
                includeProperties: "Project.ProjectGroup,Project.Organization")).First();

            return new OkObjectResult(ToListDto(loaded, new List<TrackedTask>()));
        }

        [Function("CreateAndStartStopwatchItem")]
        public async Task<IActionResult> CreateAndStartStopwatchItemAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "stopwatchitems/create-and-start")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, out var userId) is IActionResult unauth) return unauth;

            var dto = await req.ReadFromJsonAsync<CreateStopwatchItemDto>();
            if (await RequestValidator.BadRequestIfInvalidAsync(createValidator, dto) is { } validationError)
                return validationError;

            var projectIssue = await ValidateProjectIsLoggable(dto!.ProjectId);
            if (projectIssue != null)
                return new BadRequestObjectResult(projectIssue);

            var now = DateTime.UtcNow;
            var createDecision = SubmissionRules.Evaluate(
                SubmissionRules.Operation.Create,
                await IsMonthSubmittedAsync(userId, now.Year, now.Month));
            if (!createDecision.IsAllowed) return new BadRequestObjectResult(createDecision.Reason!);

            var item = new StopwatchItem
            {
                UserId = userId,
                Name = dto.Name.Trim(),
                ProjectId = dto.ProjectId,
                CreatedAt = now,
                LastWorkedAt = now
            };

            await itemRepository.Insert(item);
            return await StartItemCoreAsync(item.StopwatchItemId, userId, now);
        }

        [Function("UpdateStopwatchItem")]
        public async Task<IActionResult> UpdateStopwatchItemAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "stopwatchitems")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, out var userId) is IActionResult unauth) return unauth;

            var dto = await req.ReadFromJsonAsync<UpdateStopwatchItemDto>();
            if (await RequestValidator.BadRequestIfInvalidAsync(updateValidator, dto) is { } validationError)
                return validationError;

            var projectIssue = await ValidateProjectIsLoggable(dto!.ProjectId);
            if (projectIssue != null)
                return new BadRequestObjectResult(projectIssue);

            var item = await FindOwnedItemAsync(dto.StopwatchItemId, userId);
            if (item == null) return new NotFoundObjectResult("Stopwatch item not found.");

            item.Name = dto.Name.Trim();
            item.ProjectId = dto.ProjectId;
            await itemRepository.Update(item);

            var active = await dbContext.TrackedTasks
                .FirstOrDefaultAsync(t => t.StopwatchItemId == dto.StopwatchItemId && t.EndDate == null);
            if (active != null)
            {
                active.Name = item.Name;
                active.ProjectId = dto.ProjectId;
                await taskRepository.Update(active);
            }

            var loaded = (await itemRepository.Get(
                i => i.StopwatchItemId == dto.StopwatchItemId,
                includeProperties: "Project.ProjectGroup,Project.Organization")).First();
            var sessions = await dbContext.TrackedTasks.AsNoTracking()
                .Where(t => t.StopwatchItemId == dto.StopwatchItemId)
                .ToListAsync();

            return new OkObjectResult(ToListDto(loaded, sessions));
        }

        [Function("StartStopwatchItem")]
        public async Task<IActionResult> StartStopwatchItemAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "stopwatchitems/{id}/start")] HttpRequestData req,
            string id)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, out var userId) is IActionResult unauth) return unauth;

            var item = await FindOwnedItemAsync(id, userId);
            if (item == null) return new NotFoundObjectResult("Stopwatch item not found.");

            var now = DateTime.UtcNow;
            return await StartItemCoreAsync(id, userId, now);
        }

        private async Task<IActionResult> StartItemCoreAsync(string id, string userId, DateTime now)
        {
            var item = await FindOwnedItemAsync(id, userId);
            if (item == null) return new NotFoundObjectResult("Stopwatch item not found.");

            var createDecision = SubmissionRules.Evaluate(
                SubmissionRules.Operation.Create,
                await IsMonthSubmittedAsync(userId, now.Year, now.Month));
            if (!createDecision.IsAllowed) return new BadRequestObjectResult(createDecision.Reason!);

            if (string.IsNullOrWhiteSpace(item.ProjectId))
                return new BadRequestObjectResult("A project is required to log time. Edit the work item and assign a project first.");

            var projectIssue = await ValidateProjectIsLoggable(item.ProjectId);
            if (projectIssue != null)
                return new BadRequestObjectResult(projectIssue);

            var existingActive = await dbContext.TrackedTasks
                .Where(t => t.UserId == userId && t.EndDate == null && !t.IsAllDay)
                .ToListAsync();

            // DB-only auto-stop — calendar sync happens when the user explicitly stops.
            foreach (var active in existingActive)
                await StopSessionAsync(active, now, syncCalendar: false);

            var activeOnItem = await dbContext.TrackedTasks
                .FirstOrDefaultAsync(t => t.StopwatchItemId == id && t.EndDate == null);
            if (activeOnItem != null)
                return new BadRequestObjectResult("This work item is already running.");

            var session = new TrackedTask
            {
                UserId = userId,
                StopwatchItemId = id,
                Name = item.Name,
                ProjectId = item.ProjectId,
                StartDate = now,
                Duration = TimeSpan.Zero,
                EndDate = null,
                IsBillable = await TrackedTaskBillableResolver.ResolveAsync(dbContext, item.ProjectId)
            };

            await taskRepository.Insert(session);

            item.LastWorkedAt = now;
            await itemRepository.Update(item);

            var loaded = (await itemRepository.Get(
                i => i.StopwatchItemId == id,
                includeProperties: "Project.ProjectGroup,Project.Organization")).First();
            var sessions = await dbContext.TrackedTasks.AsNoTracking()
                .Where(t => t.StopwatchItemId == id)
                .ToListAsync();

            return new OkObjectResult(ToListDto(loaded, sessions));
        }

        [Function("StopStopwatchItem")]
        public async Task<IActionResult> StopStopwatchItemAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "stopwatchitems/{id}/stop")] HttpRequestData req,
            string id)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, out var userId) is IActionResult unauth) return unauth;

            var item = await FindOwnedItemAsync(id, userId);
            if (item == null) return new NotFoundObjectResult("Stopwatch item not found.");

            var session = await dbContext.TrackedTasks
                .FirstOrDefaultAsync(t => t.StopwatchItemId == id && t.EndDate == null);
            if (session == null)
                return new BadRequestObjectResult("No active session to stop.");

            var now = DateTime.UtcNow;
            await StopSessionAsync(session, now, syncCalendar: true);

            item.LastWorkedAt = now;
            await itemRepository.Update(item);

            var loaded = (await itemRepository.Get(
                i => i.StopwatchItemId == id,
                includeProperties: "Project.ProjectGroup,Project.Organization")).First();
            var sessions = await dbContext.TrackedTasks.AsNoTracking()
                .Where(t => t.StopwatchItemId == id)
                .ToListAsync();

            return new OkObjectResult(ToListDto(loaded, sessions));
        }

        [Function("GetStopwatchItemSessions")]
        public async Task<IActionResult> GetStopwatchItemSessionsAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "stopwatchitems/{id}/sessions")] HttpRequestData req,
            string id)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, out var userId) is IActionResult unauth) return unauth;

            var item = await FindOwnedItemAsync(id, userId);
            if (item == null) return new NotFoundObjectResult("Stopwatch item not found.");

            var sessions = await taskRepository.Get(
                t => t.StopwatchItemId == id,
                orderBy: q => q.OrderByDescending(t => t.StartDate),
                includeProperties: "Project.ProjectGroup,Project.Organization");

            var submitted = await GetSubmittedMonthsAsync(userId);
            var dtos = sessions.Select(t =>
            {
                var dto = mapper.TrackedTaskToDto(t);
                dto.IsMonthSubmitted = submitted.Contains((t.StartDate.Year, t.StartDate.Month));
                return dto;
            }).ToList();

            return new OkObjectResult(dtos);
        }

        [Function("DeleteStopwatchItem")]
        public async Task<IActionResult> DeleteStopwatchItemAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "stopwatchitems/{id}")] HttpRequestData req,
            string id)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, out var userId) is IActionResult unauth) return unauth;

            var item = await itemRepository.Find(id);
            if (item == null) return new NotFoundObjectResult("Stopwatch item not found.");
            if (item.UserId != userId) return new UnauthorizedResult();

            var sessions = await dbContext.TrackedTasks.Where(t => t.StopwatchItemId == id).ToListAsync();
            foreach (var session in sessions)
            {
                var deleteDecision = SubmissionRules.Evaluate(
                    SubmissionRules.Operation.Delete,
                    await IsMonthSubmittedAsync(userId, session.StartDate.Year, session.StartDate.Month));
                if (!deleteDecision.IsAllowed)
                    return new BadRequestObjectResult($"Cannot delete: a session in {session.StartDate:MMMM yyyy} has been submitted.");
            }

            // Push each deletion to Google individually (external per-event calls), then
            // remove the session rows in a single DELETE rather than one round-trip each.
            foreach (var session in sessions)
                await TryPushDeleteAsync(session);

            await dbContext.TrackedTasks.Where(t => t.StopwatchItemId == id).ExecuteDeleteAsync();
            await itemRepository.Delete(id);
            return new NoContentResult();
        }

        private static Func<IQueryable<StopwatchItem>, IOrderedQueryable<StopwatchItem>> OrderStopwatchItems(
            string? sortBy, bool descending)
        {
            return sortBy?.ToLowerInvariant() switch
            {
                "name" => descending
                    ? q => q.OrderByDescending(i => i.Name)
                    : q => q.OrderBy(i => i.Name),
                "createdat" => descending
                    ? q => q.OrderByDescending(i => i.CreatedAt)
                    : q => q.OrderBy(i => i.CreatedAt),
                _ => descending
                    ? q => q.OrderByDescending(i => i.LastWorkedAt)
                    : q => q.OrderBy(i => i.LastWorkedAt)
            };
        }

        private StopwatchItemDto ToListDto(StopwatchItem item, List<TrackedTask> sessions)
        {
            var active = sessions.FirstOrDefault(t => t.EndDate == null);
            var completedTotal = sessions
                .Where(t => t.EndDate != null)
                .Aggregate(TimeSpan.Zero, (sum, t) => sum + t.Duration);

            return new StopwatchItemDto
            {
                StopwatchItemId = item.StopwatchItemId,
                Name = item.Name,
                ProjectId = item.ProjectId,
                Project = item.Project == null ? null : mapper.ProjectToDto(item.Project),
                TotalDuration = completedTotal,
                IsRunning = active != null,
                ActiveSessionId = active?.TaskId,
                ActiveSessionStartDate = active?.StartDate,
                LastWorkedAt = item.LastWorkedAt,
                CreatedAt = item.CreatedAt
            };
        }

        private async Task<StopwatchItem?> FindOwnedItemAsync(string id, string userId)
        {
            var item = await itemRepository.Find(id);
            return item != null && item.UserId == userId ? item : null;
        }

        private async Task StopSessionAsync(TrackedTask session, DateTime endUtc, bool syncCalendar)
        {
            session.EndDate = endUtc;
            var elapsed = StopwatchRules.ElapsedForActiveSession(session.StartDate, endUtc);
            session.Duration = StopwatchRules.RoundUpToMinute(elapsed);
            await taskRepository.Update(session);
            if (syncCalendar)
                await TryPushCreateAsync(session);
        }

        private async Task<bool> IsMonthSubmittedAsync(string userId, int year, int month)
        {
            var existing = await submissionRepository.Get(s => s.UserId == userId && s.Year == year && s.Month == month);
            return existing.Any();
        }

        private async Task<HashSet<(int Year, int Month)>> GetSubmittedMonthsAsync(string userId)
        {
            var rows = await submissionRepository.Get(s => s.UserId == userId);
            return rows.Select(s => (s.Year, s.Month)).ToHashSet();
        }

        private async Task<string?> ValidateProjectIsLoggable(string? projectId)
        {
            if (string.IsNullOrEmpty(projectId)) return null;

            var project = (await projectRepository.Get(
                p => p.ProjectId == projectId,
                includeProperties: $"{nameof(Project.Organization)},{nameof(Project.Department)}"))
                .FirstOrDefault();

            if (project == null) return "The selected project no longer exists.";
            if (project.IsArchived) return "Cannot log time against an archived project.";
            if (!project.IsActive) return "Cannot log time against an inactive project.";
            if (project.Organization is { IsArchived: true })
                return "Cannot log time against a project whose organization is archived.";
            if (project.Organization is { IsActive: false })
                return "Cannot log time against a project whose organization is inactive.";
            if (project.Department is { IsArchived: true })
                return "Cannot log time against a project whose department is archived.";
            if (project.Department is { IsActive: false })
                return "Cannot log time against a project whose department is inactive.";
            return null;
        }

        private async Task<UserSettings?> GetSettingsAsync(string userId) =>
            (await dbContext.UserSettings.AsNoTracking().Where(s => s.UserId == userId).ToListAsync()).FirstOrDefault();

        private async Task<Project?> GetProjectAsync(string? projectId)
        {
            if (string.IsNullOrEmpty(projectId)) return null;
            return await projectRepository.GetById(projectId);
        }

        private async Task TryPushCreateAsync(TrackedTask task)
        {
            try
            {
                var s = await GetSettingsAsync(task.UserId);
                if (s == null || !s.PublishToGoogleCalendar
                    || string.IsNullOrEmpty(s.GoogleRefreshToken) || string.IsNullOrEmpty(s.GoogleCalendarId))
                {
                    await TryPushTeamAvailabilityAsync(task, s);
                    return;
                }

                var project = await GetProjectAsync(task.ProjectId);
                var ev = await googleCalendar.CreateEventAsync(s.GoogleRefreshToken, s.GoogleCalendarId, task, project?.Slug, s.TimeZone, s.TymeEventColorId, s.TymeUnmatchedEventColorId);
                task.GoogleEventId = ev.Id;
                await taskRepository.Update(task);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to push stopwatch session {TaskId} to Google.", task.TaskId);
            }

            await TryPushTeamAvailabilityAsync(task, await GetSettingsAsync(task.UserId));
        }

        private async Task TryPushUpdateAsync(TrackedTask task)
        {
            try
            {
                var s = await GetSettingsAsync(task.UserId);
                if (s == null || string.IsNullOrEmpty(s.GoogleRefreshToken) || string.IsNullOrEmpty(s.GoogleCalendarId))
                {
                    await TryPushTeamAvailabilityAsync(task, s);
                    return;
                }

                var project = await GetProjectAsync(task.ProjectId);
                if (!string.IsNullOrEmpty(task.GoogleEventId))
                {
                    if (!s.PublishToGoogleCalendar)
                    {
                        await TryPushTeamAvailabilityAsync(task, s);
                        return;
                    }

                    await googleCalendar.UpdateEventAsync(s.GoogleRefreshToken, s.GoogleCalendarId, task.GoogleEventId, task, project?.Slug, s.TimeZone, s.TymeEventColorId, s.TymeUnmatchedEventColorId);
                    await TryPushTeamAvailabilityAsync(task, s);
                    return;
                }

                if (s.PublishToGoogleCalendar)
                {
                    var ev = await googleCalendar.CreateEventAsync(s.GoogleRefreshToken, s.GoogleCalendarId, task, project?.Slug, s.TimeZone, s.TymeEventColorId, s.TymeUnmatchedEventColorId);
                    task.GoogleEventId = ev.Id;
                    await taskRepository.Update(task);
                }

                await TryPushTeamAvailabilityAsync(task, s);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to update stopwatch session {TaskId} in Google.", task.TaskId);
            }
        }

        private async Task TryPushDeleteAsync(TrackedTask task)
        {
            var s = await GetSettingsAsync(task.UserId);

            if (!string.IsNullOrEmpty(task.GoogleEventId))
            {
                try
                {
                    if (s != null && !string.IsNullOrEmpty(s.GoogleRefreshToken) && !string.IsNullOrEmpty(s.GoogleCalendarId))
                        await googleCalendar.DeleteEventAsync(s.GoogleRefreshToken, s.GoogleCalendarId, task.GoogleEventId);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to delete Google event for stopwatch session {TaskId}.", task.TaskId);
                }
            }

            await teamAvailabilityPublisher.DeleteSisterEventAsync(task, s);
        }

        private Task TryPushTeamAvailabilityAsync(TrackedTask task, UserSettings? settings) =>
            teamAvailabilityPublisher.PublishAsync(task, settings);
    }
}