using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using My.Shared.Constants;
using My.Shared.Dtos.Paging;
using My.Shared.Dtos.TrackedTask;
using My.Shared.Rules;
using My.DAL.Data;
using My.DAL.Models;
using My.DAL.Models.Paging;
using My.DAL.Repository;
using My.Functions.Authorization;
using My.Functions.Helpers;
using My.Functions.Services;

namespace My.Functions
{
    public class TrackedTaskFunctions
    {
        private readonly IRepository<TrackedTask> taskRepository;
        private readonly IRepository<UserSettings> settingsRepository;
        private readonly IRepository<TimeSubmission> submissionRepository;
        private readonly IRepository<Project> projectRepository;
        private readonly ApplicationDbContext dbContext;
        private readonly AppMapper mapper;
        private readonly GoogleCalendarService googleCalendar;
        private readonly TeamAvailabilityPublisher teamAvailabilityPublisher;
        private readonly ILogger<TrackedTaskFunctions> logger;
        private readonly IValidator<CreateTrackedTaskDto> createValidator;
        private readonly IValidator<UpdateTrackedTaskDto> updateValidator;
        private readonly IValidator<DuplicateTrackedTaskDto> duplicateValidator;

        public TrackedTaskFunctions(
            IRepositoryFactory repositoryFactory,
            ApplicationDbContext dbContext,
            AppMapper mapper,
            GoogleCalendarService googleCalendar,
            TeamAvailabilityPublisher teamAvailabilityPublisher,
            ILogger<TrackedTaskFunctions> logger,
            IValidator<CreateTrackedTaskDto> createValidator,
            IValidator<UpdateTrackedTaskDto> updateValidator,
            IValidator<DuplicateTrackedTaskDto> duplicateValidator)
        {
            this.dbContext = dbContext;
            this.mapper = mapper;
            this.googleCalendar = googleCalendar;
            this.teamAvailabilityPublisher = teamAvailabilityPublisher;
            this.logger = logger;
            this.createValidator = createValidator;
            this.updateValidator = updateValidator;
            this.duplicateValidator = duplicateValidator;
            taskRepository = repositoryFactory.GetRepository<TrackedTask>();
            settingsRepository = repositoryFactory.GetRepository<UserSettings>();
            submissionRepository = repositoryFactory.GetRepository<TimeSubmission>();
            projectRepository = repositoryFactory.GetRepository<Project>();
        }

        /// <summary>
        /// Reads <c>WorkdayHours</c> from AppSettings and parses it. Falls back to the
        /// helper's default if the row is missing or unparseable.
        /// </summary>
        private async Task<double> GetWorkdayHoursAsync()
        {
            var row = await dbContext.AppSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == Constants.SettingKeys.WorkdayHours);
            return AllDayEntryRules.ParseWorkdayHours(row?.Value);
        }

        /// <summary>
        /// Reads <c>TeamAvailabilityCalendarId</c> from AppSettings. Empty string disables
        /// the dual-publish path (returned as null).
        /// </summary>
        private async Task<string?> GetTeamAvailabilityCalendarIdAsync()
        {
            var row = await dbContext.AppSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == Constants.SettingKeys.TeamAvailabilityCalendarId);
            return string.IsNullOrWhiteSpace(row?.Value) ? null : row.Value;
        }

        /// <summary>
        /// Normalizes an all-day task's start/end to date-only at 00:00 UTC and derives
        /// <c>Duration</c> from <c>WorkdayHours × workday span</c>. No-op for timed entries.
        /// The previous version stamped EndDate at 23:59:59 of the last day so calendar
        /// queries would render the inclusive range — that worked, but combined with the
        /// client's <c>ToLocalTime()</c> on read it shifted the start day back by one for
        /// users west of UTC. Date-only on both ends round-trips cleanly without timezone
        /// drama.
        /// </summary>
        private static DateTime? TryParseUtcDateQuery(string? value, bool endOfDay)
        {
            if (string.IsNullOrWhiteSpace(value) || !DateTime.TryParse(value, out var parsed))
                return null;

            var day = DateTime.SpecifyKind(parsed.Date, DateTimeKind.Utc);
            return endOfDay ? day.AddDays(1).AddTicks(-1) : day;
        }

        private async Task ApplyAllDayDerivationsAsync(TrackedTask task)
        {
            if (!task.IsAllDay) return;

            var startDay = task.StartDate.Date;
            var lastDay = (task.EndDate ?? task.StartDate).Date;
            if (lastDay < startDay) lastDay = startDay;

            task.StartDate = DateTime.SpecifyKind(startDay, DateTimeKind.Utc);
            task.EndDate = DateTime.SpecifyKind(lastDay, DateTimeKind.Utc);

            var hours = await GetWorkdayHoursAsync();
            task.Duration = AllDayEntryRules.DurationFor(task.StartDate, task.EndDate, hours);
        }

        /// <summary>
        /// Returns null if the project (and its org/department) is OK to log time against,
        /// otherwise an error message explaining what's blocking it. Empty/null projectId
        /// is allowed — tasks may have no project.
        /// </summary>
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

        /// <summary>True if the given user has already submitted the (year, month) period.</summary>
        private async Task<bool> IsMonthSubmittedAsync(string userId, int year, int month)
        {
            var existing = await submissionRepository.Get(
                s => s.UserId == userId && s.Year == year && s.Month == month);
            return existing.Any();
        }

        /// <summary>Returns the set of submitted (year, month) for the given user.</summary>
        private async Task<HashSet<(int Year, int Month)>> GetSubmittedMonthsAsync(string userId)
        {
            var rows = await dbContext.TimeSubmissions.AsNoTracking()
                .Where(s => s.UserId == userId)
                .Select(s => new { s.Year, s.Month })
                .ToListAsync();
            return rows.Select(s => (s.Year, s.Month)).ToHashSet();
        }

        private TrackedTaskDto ToDtoWithSubmissionFlag(TrackedTask task, HashSet<(int Year, int Month)> submitted)
        {
            var dto = mapper.TrackedTaskToDto(task);
            dto.IsMonthSubmitted = submitted.Contains((task.StartDate.Year, task.StartDate.Month));
            return dto;
        }

        private async Task EnrichEmployeeViewAsync(IReadOnlyList<TrackedTaskDto> dtos, IReadOnlyList<TrackedTask> tasks)
        {
            if (dtos.Count == 0)
                return;

            var taskIds = tasks.Select(t => t.TaskId).ToList();
            var adjustmentContext = await TrackedTaskAdjustmentEnricher.LoadForTasksAsync(dbContext, taskIds);
            for (var i = 0; i < dtos.Count; i++)
            {
                adjustmentContext.Aliases.TryGetValue(tasks[i].TaskId, out var alias);
                adjustmentContext.Audits.TryGetValue(tasks[i].TaskId, out var audit);
                TrackedTaskAdjustmentEnricher.ApplyEmployeeView(dtos[i], alias, audit, adjustmentContext, mapper);
            }
        }

        private async Task<UserSettings?> GetSettingsAsync(string userId) =>
            (await settingsRepository.Get(s => s.UserId == userId)).FirstOrDefault();

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
                logger.LogWarning(ex, "Failed to push TrackedTask {TaskId} to Google.", task.TaskId);
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
                    try
                    {
                        await googleCalendar.UpdateEventAsync(s.GoogleRefreshToken, s.GoogleCalendarId, task.GoogleEventId, task, project?.Slug, s.TimeZone, s.TymeEventColorId, s.TymeUnmatchedEventColorId);
                        await TryPushTeamAvailabilityAsync(task, s);
                        return;
                    }
                    catch (Google.GoogleApiException ex) when (
                        ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound ||
                        ex.HttpStatusCode == System.Net.HttpStatusCode.Gone)
                    {
                        // Stored event id no longer exists on Google (most often: user disconnected
                        // and reconnected with a different account, so the linkage points at events
                        // in the old account). Drop the stale id and fall through to create fresh.
                        logger.LogInformation(
                            "Stale Google event {EventId} for TrackedTask {TaskId} — recreating.",
                            task.GoogleEventId, task.TaskId);
                        task.GoogleEventId = null;
                    }
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
                logger.LogWarning(ex, "Failed to update TrackedTask {TaskId} in Google.", task.TaskId);
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
                    logger.LogWarning(ex, "Failed to delete Google event for TrackedTask {TaskId}.", task.TaskId);
                }
            }

            // Single code path for team-calendar cleanup whether the delete comes from the
            // UI or from a Google cancellation webhook — keeps the two delete callers in
            // step with each other.
            await teamAvailabilityPublisher.DeleteSisterEventAsync(task, s);
        }

        private Task TryPushTeamAvailabilityAsync(TrackedTask task, UserSettings? settings) =>
            teamAvailabilityPublisher.PublishAsync(task, settings);

        [Function("GetTrackedTasks")]
        public async Task<IActionResult> GetTrackedTasksAsync([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "trackedtasks")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, out var userId) is IActionResult unauth) return unauth;

            var listQuery = HttpListQueryParser.ParseListQuery(req);
            var parameters = HttpListQueryParser.ToPagingParameters(listQuery);

            DateTime? from = TryParseUtcDateQuery(req.Query["From"], endOfDay: false);
            DateTime? to = TryParseUtcDateQuery(req.Query["To"], endOfDay: true);
            var excludeStopwatchSessions = string.Equals(
                req.Query["excludeStopwatchSessions"], "true", StringComparison.OrdinalIgnoreCase);

            var filter = TrackedTaskListFilters.Build(userId, parameters.Search, from, to, excludeStopwatchSessions);
            var orderBy = TrackedTaskListFilters.OrderBy(parameters.SortBy, parameters.SortDescending);

            var pagedTrackedTaskList = await taskRepository.GetPaged(
                parameters,
                filter: filter,
                orderBy: orderBy,
                includeProperties: "Project.ProjectGroup,Project.Organization");

            var submitted = await GetSubmittedMonthsAsync(userId);
            var taskList = pagedTrackedTaskList.ToList();
            var dtos = taskList.Select(t => ToDtoWithSubmissionFlag(t, submitted)).ToList();
            await EnrichEmployeeViewAsync(dtos, taskList);
            return new OkObjectResult(new PagedResponse<TrackedTaskDto>
            {
                Items = dtos,
                TotalCount = pagedTrackedTaskList.TotalCount,
                PageSize = pagedTrackedTaskList.PageSize,
                CurrentPage = pagedTrackedTaskList.CurrentPage,
                TotalPages = pagedTrackedTaskList.TotalPages,
                HasNext = pagedTrackedTaskList.HasNext,
                HasPrevious = pagedTrackedTaskList.HasPrevious
            });
        }

        /// <summary>
        /// Returns every task row in a date window in one response. Calendar and reports
        /// use this instead of paging through GET /trackedtasks (which ran COUNT + data
        /// per page — brutal when hundreds of stopwatch sessions exist).
        /// </summary>
        [Function("GetTrackedTasksRange")]
        public async Task<IActionResult> GetTrackedTasksRangeAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "trackedtasks/range")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, out var userId) is IActionResult unauth) return unauth;

            DateTime? from = TryParseUtcDateQuery(req.Query["From"], endOfDay: false);
            DateTime? to = TryParseUtcDateQuery(req.Query["To"], endOfDay: true);
            var search = req.Query["Search"];
            var excludeStopwatchSessions = string.Equals(
                req.Query["excludeStopwatchSessions"], "true", StringComparison.OrdinalIgnoreCase);

            var tasks = await TrackedTaskRangeQuery.LoadAsync(
                dbContext, userId, from, to, search, excludeStopwatchSessions);

            var submitted = await GetSubmittedMonthsAsync(userId);
            var dtos = tasks.Select(t => ToDtoWithSubmissionFlag(t, submitted)).ToList();
            await EnrichEmployeeViewAsync(dtos, tasks);
            return new OkObjectResult(dtos);
        }

        [Function("GetActiveTrackedTasks")]
        public async Task<IActionResult> GetActiveTrackedTasksAsync([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "trackedtasks/active")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, out var userId) is IActionResult unauth) return unauth;

            var activeTasks = await taskRepository.Get(
                filter: t => t.UserId == userId && t.EndDate == null,
                orderBy: q => q.OrderByDescending(t => t.StartDate),
                // Same as the paged Get: Organization + ProjectGroup are required to fill
                // the color fields on ProjectDto, which the stopwatch's color bar reads.
                includeProperties: "Project.ProjectGroup,Project.Organization");

            var activeTaskList = activeTasks.ToList();
            var submitted = await GetSubmittedMonthsAsync(userId);
            var dtos = activeTaskList.Select(t => ToDtoWithSubmissionFlag(t, submitted)).ToList();
            await EnrichEmployeeViewAsync(dtos, activeTaskList);
            return new OkObjectResult(dtos);
        }

        // The {id} constraint excludes the literal GET siblings 'range' and 'active'. Without it,
        // Azure Functions matches this single-segment {id} route for GET /trackedtasks/range (and
        // /active), so GetById("range") returns 404 and those endpoints never reach their own
        // functions. Keep the Route a plain string literal — a Constants reference here silently 404s.
        [Function("GetTrackedTask")]
        public async Task<IActionResult> GetTrackedTaskAsync([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "trackedtasks/{id:regex(^(?!range$|active$).+$)}")] HttpRequestData req, string id)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, out var userId) is IActionResult unauth) return unauth;

            var trackedTask = await taskRepository.GetById(id);
            if (trackedTask == null)
                return new NotFoundObjectResult("Tracked task not found!");

            var dto = mapper.TrackedTaskToDto(trackedTask);
            dto.IsMonthSubmitted = await IsMonthSubmittedAsync(
                trackedTask.UserId, trackedTask.StartDate.Year, trackedTask.StartDate.Month);
            await EnrichEmployeeViewAsync(new[] { dto }, new[] { trackedTask });
            return new OkObjectResult(dto);
        }

        [Function("CreateTrackedTask")]
        public async Task<IActionResult> CreateTrackedTaskAsync([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "trackedtasks")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, out var userId) is IActionResult unauth) return unauth;

            var (trackedTask, validationError) = await RequestValidator.ReadJsonAndValidateAsync(req, createValidator);
            if (validationError != null)
                return validationError;

            var newTrackedTask = mapper.DtoToTrackedTask(trackedTask!);
            newTrackedTask.UserId = userId;
            newTrackedTask.ProjectId = string.IsNullOrEmpty(trackedTask!.ProjectId) ? null : trackedTask.ProjectId;

            // All-day entries are date-only — the client sends them stamped Kind=Utc so the
            // wire format is "YYYY-MM-DDT00:00:00Z" with no offset shifts. Don't run them
            // through ToUniversalTime() (which would no-op on a UTC server but would shift
            // the date for a Local DateTime that wasn't already UTC, depending on serializer
            // settings). Timed entries still need the normalization because the client
            // serializes them with whatever Kind MudDatePicker hands us.
            newTrackedTask.StartDate = newTrackedTask.IsAllDay
                ? DateTime.SpecifyKind(trackedTask.StartDate.Date, DateTimeKind.Utc)
                : trackedTask.StartDate.ToUniversalTime();

            var createDecision = SubmissionRules.Evaluate(
                SubmissionRules.Operation.Create,
                await IsMonthSubmittedAsync(userId, newTrackedTask.StartDate.Year, newTrackedTask.StartDate.Month));
            if (!createDecision.IsAllowed) return new BadRequestObjectResult(createDecision.Reason!);

            var projectIssue = await ValidateProjectIsLoggable(newTrackedTask.ProjectId);
            if (projectIssue != null)
                return new BadRequestObjectResult(projectIssue);

            if (newTrackedTask.IsAllDay)
            {
                // For all-day, EndDate comes from the client too (date-only Utc-stamped).
                // Skip ToUniversalTime — see comment above. Derivations normalize to 00:00 UTC.
                if (trackedTask.EndDate.HasValue)
                    newTrackedTask.EndDate = DateTime.SpecifyKind(trackedTask.EndDate.Value.Date, DateTimeKind.Utc);
                await ApplyAllDayDerivationsAsync(newTrackedTask);
            }
            else if (newTrackedTask.Duration > TimeSpan.Zero)
            {
                // If Duration is non-zero this is a finished timed entry.
                newTrackedTask.EndDate = newTrackedTask.StartDate + newTrackedTask.Duration;
            }
            else
            {
                // Timer start — leave EndDate null to mark as active.
                newTrackedTask.EndDate = null;
            }

            newTrackedTask.IsBillable = await TrackedTaskBillableResolver.ResolveAsync(dbContext, newTrackedTask.ProjectId);

            await taskRepository.Insert(newTrackedTask);
            await TryPushCreateAsync(newTrackedTask);

            var createdDto = mapper.TrackedTaskToDto(newTrackedTask);
            createdDto.IsMonthSubmitted = false;
            return new OkObjectResult(createdDto);
        }

        [Function("DeleteTrackedTask")]
        public async Task<IActionResult> DeleteTrackedTaskAsync([HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "trackedtasks/{id}")] HttpRequestData req, string id)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, out var userId) is IActionResult unauth) return unauth;

            var trackedTaskToDelete = await taskRepository.Find(id);
            if (trackedTaskToDelete == null)
            {
                logger.LogError("Tracked task was not found");
                return new NotFoundObjectResult("Tracked task not found!");
            }

            if (trackedTaskToDelete.UserId != userId)
                return new UnauthorizedResult();

            var deleteDecision = SubmissionRules.Evaluate(
                SubmissionRules.Operation.Delete,
                await IsMonthSubmittedAsync(userId, trackedTaskToDelete.StartDate.Year, trackedTaskToDelete.StartDate.Month));
            if (!deleteDecision.IsAllowed) return new BadRequestObjectResult(deleteDecision.Reason!);

            await TryPushDeleteAsync(trackedTaskToDelete);
            await taskRepository.Delete(id);
            logger.LogInformation($"Tracked task with Id {id} was deleted.");
            return new NoContentResult();
        }

        [Function("DuplicateTrackedTask")]
        public async Task<IActionResult> DuplicateTrackedTaskAsync([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "trackedtasks/{id}/duplicate")] HttpRequestData req, string id)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, out var userId) is IActionResult unauth) return unauth;

            var source = await taskRepository.Find(id);
            if (source == null)
                return new NotFoundObjectResult("Tracked task not found!");

            if (source.UserId != userId)
                return new UnauthorizedResult();

            DateTime? newStart = null;
            try
            {
                var body = await req.ReadFromJsonAsync<DuplicateTrackedTaskDto>();
                if (body != null)
                {
                    if (await RequestValidator.BadRequestIfInvalidAsync(duplicateValidator, body) is { } dupError)
                        return dupError;
                    if (body.StartDate.HasValue)
                        newStart = body.StartDate.Value.ToUniversalTime();
                }
            }
            catch { }

            var start = newStart ?? source.StartDate.AddDays(1);

            var dupDecision = SubmissionRules.Evaluate(
                SubmissionRules.Operation.Duplicate,
                await IsMonthSubmittedAsync(userId, start.Year, start.Month));
            if (!dupDecision.IsAllowed) return new BadRequestObjectResult(dupDecision.Reason!);

            var dupProjectIssue = await ValidateProjectIsLoggable(source.ProjectId);
            if (dupProjectIssue != null)
                return new BadRequestObjectResult(dupProjectIssue);

            var clone = new TrackedTask
            {
                UserId = userId,
                Name = source.Name,
                ProjectId = source.ProjectId,
                StartDate = start,
                Duration = source.Duration,
                IsAllDay = source.IsAllDay,
                IsBillable = await TrackedTaskBillableResolver.ResolveAsync(dbContext, source.ProjectId)
            };

            if (source.IsAllDay)
            {
                // Preserve the original calendar-day span on the new start date.
                var spanDays = AllDayEntryRules.InclusiveDaySpan(source.StartDate, source.EndDate);
                clone.EndDate = start.Date.AddDays(spanDays - 1);
                await ApplyAllDayDerivationsAsync(clone);
            }
            else
            {
                clone.EndDate = source.Duration > TimeSpan.Zero ? start + source.Duration : null;
            }

            await taskRepository.Insert(clone);
            await TryPushCreateAsync(clone);
            logger.LogInformation("Tracked task {SourceId} duplicated to {NewId}.", id, clone.TaskId);

            var cloneDto = mapper.TrackedTaskToDto(clone);
            cloneDto.IsMonthSubmitted = false;
            return new OkObjectResult(cloneDto);
        }

        [Function("UpdateTrackedTask")]
        public async Task<IActionResult> UpdateTrackedTaskAsync([HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "trackedtasks")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, out var userId) is IActionResult unauth) return unauth;

            var (trackedTask, validationError) = await RequestValidator.ReadJsonAndValidateAsync(req, updateValidator);
            if (validationError != null)
                return validationError;

            var foundTrackedTask = await taskRepository.Find(trackedTask!.TaskId);
            if (foundTrackedTask == null)
            {
                logger.LogError("Tracked task was not found");
                return new NotFoundObjectResult("Tracked task not found!");
            }

            if (foundTrackedTask.UserId != userId)
                return new UnauthorizedResult();

            // Reject if the task currently lives in a submitted month — even before applying changes.
            var editDecision = SubmissionRules.Evaluate(
                SubmissionRules.Operation.Edit,
                await IsMonthSubmittedAsync(userId, foundTrackedTask.StartDate.Year, foundTrackedTask.StartDate.Month));
            if (!editDecision.IsAllowed) return new BadRequestObjectResult(editDecision.Reason!);

            mapper.UpdateTrackedTaskFromDto(trackedTask, foundTrackedTask);

            // Date-only handling for all-day; see CreateTrackedTask for the rationale.
            foundTrackedTask.StartDate = foundTrackedTask.IsAllDay
                ? DateTime.SpecifyKind(foundTrackedTask.StartDate.Date, DateTimeKind.Utc)
                : foundTrackedTask.StartDate.ToUniversalTime();

            // Reject if the (possibly new) StartDate moves the task into a submitted month.
            var moveDecision = SubmissionRules.Evaluate(
                SubmissionRules.Operation.Move,
                await IsMonthSubmittedAsync(userId, foundTrackedTask.StartDate.Year, foundTrackedTask.StartDate.Month));
            if (!moveDecision.IsAllowed) return new BadRequestObjectResult(moveDecision.Reason!);

            var updateProjectIssue = await ValidateProjectIsLoggable(foundTrackedTask.ProjectId);
            if (updateProjectIssue != null)
                return new BadRequestObjectResult(updateProjectIssue);

            if (foundTrackedTask.IsAllDay)
            {
                if (foundTrackedTask.EndDate.HasValue)
                    foundTrackedTask.EndDate = DateTime.SpecifyKind(foundTrackedTask.EndDate.Value.Date, DateTimeKind.Utc);
                await ApplyAllDayDerivationsAsync(foundTrackedTask);
            }
            else if (foundTrackedTask.EndDate.HasValue)
            {
                foundTrackedTask.EndDate = foundTrackedTask.EndDate.Value.ToUniversalTime();
                foundTrackedTask.Duration = foundTrackedTask.EndDate.Value - foundTrackedTask.StartDate;
            }
            else if (trackedTask.Duration.HasValue)
            {
                // Active task — save accumulated duration without setting EndDate
                foundTrackedTask.Duration = trackedTask.Duration.Value;
            }

            foundTrackedTask.IsBillable = await TrackedTaskBillableResolver.ResolveAsync(dbContext, foundTrackedTask.ProjectId);

            await taskRepository.Update(foundTrackedTask);
            await TryPushUpdateAsync(foundTrackedTask);
            return new OkObjectResult(mapper.TrackedTaskToDto(foundTrackedTask));
        }

    }
}
