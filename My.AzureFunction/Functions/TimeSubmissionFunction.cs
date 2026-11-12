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
using My.Functions.Services;
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
        private readonly IValidator<CreateManagerTimeSubmissionDto> _createOnBehalfValidator;
        private readonly IValidator<UpdateTymeSubmitNotifySettingsDto> _notifySettingsValidator;
        private readonly SubmissionGridService _gridService;
        private readonly TymeSubmitNotifier _notifier;
        private readonly IValidator<SendUnsubmittedReminderDto> _reminderValidator;
        private readonly UnsubmittedReminderService _reminder;
        private readonly IValidator<TymeReminderSettingsDto> _reminderSettingsValidator;

        public TimeSubmissionFunction(
            ApplicationDbContext dbContext,
            ILogger<TimeSubmissionFunction> logger,
            IValidator<CreateTimeSubmissionDto> createValidator,
            IValidator<CreateManagerTimeSubmissionDto> createOnBehalfValidator,
            IValidator<UpdateTymeSubmitNotifySettingsDto> notifySettingsValidator,
            SubmissionGridService gridService,
            TymeSubmitNotifier notifier,
            IValidator<SendUnsubmittedReminderDto> reminderValidator,
            UnsubmittedReminderService reminder,
            IValidator<TymeReminderSettingsDto> reminderSettingsValidator)
        {
            _reminderSettingsValidator = reminderSettingsValidator;
            _dbContext = dbContext;
            _logger = logger;
            _createValidator = createValidator;
            _createOnBehalfValidator = createOnBehalfValidator;
            _notifySettingsValidator = notifySettingsValidator;
            _gridService = gridService;
            _notifier = notifier;
            _reminderValidator = reminderValidator;
            _reminder = reminder;
        }

        /// <summary>
        /// Preview of the "you may have forgotten to submit your time" email for one employee.
        /// Manager:Tyme, and only when the TymeUnsubmittedReminderEnabled app setting is on.
        /// </summary>
        [Function("GetUnsubmittedReminderPreview")]
        public async Task<IActionResult> GetReminderPreviewAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "timesubmissions/reminder")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, out var managerId, Constants.Roles.Manager) is IActionResult unauth)
                return unauth;

            var employeeId = (req.Query["userId"] ?? "").Trim();
            if (string.IsNullOrEmpty(employeeId))
                return new BadRequestObjectResult("Employee is required.");
            if (!int.TryParse(req.Query["year"], out var year) || !int.TryParse(req.Query["month"], out var month)
                || year < 2000 || year > 9999 || month < 1 || month > 12)
                return new BadRequestObjectResult("Invalid Year/Month.");

            var result = await PrepareReminderAsync(principal, managerId, employeeId, year, month);
            return result.Error != null ? result.Error : new OkObjectResult(result.Prepared!.Preview);
        }

        [Function("GetTymeReminderSettings")]
        public async Task<IActionResult> GetReminderSettingsAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "timesubmissions/reminder-settings")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, Constants.Roles.Manager) is IActionResult unauth) return unauth;
            return new OkObjectResult(await _reminder.LoadSettingsAsync());
        }

        [Function("UpdateTymeReminderSettings")]
        public async Task<IActionResult> UpdateReminderSettingsAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "timesubmissions/reminder-settings")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, Constants.Roles.Manager) is IActionResult unauth) return unauth;

            var (dto, validationError) = await RequestValidator.ReadJsonAndValidateAsync(req, _reminderSettingsValidator);
            if (validationError != null)
                return validationError;

            return new OkObjectResult(await _reminder.SaveSettingsAsync(dto!));
        }

        /// <summary>Sends the reminder email shown by <see cref="GetReminderPreviewAsync"/>.</summary>
        [Function("SendUnsubmittedReminder")]
        public async Task<IActionResult> SendReminderAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "timesubmissions/reminder")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, out var managerId, Constants.Roles.Manager) is IActionResult unauth)
                return unauth;

            var (body, validationError) = await RequestValidator.ReadJsonAndValidateAsync(req, _reminderValidator);
            if (validationError != null)
                return validationError;

            var result = await PrepareReminderAsync(principal, managerId, body!.UserId.Trim(), body.Year, body.Month);
            if (result.Error != null)
                return result.Error;

            var sendError = await _reminder.SendAsync(result.Prepared!);
            if (sendError != null)
                return new ObjectResult(sendError) { StatusCode = 502 };
            return new OkObjectResult(result.Prepared!.Preview);
        }

        private async Task<(UnsubmittedReminderService.Prepared? Prepared, IActionResult? Error)> PrepareReminderAsync(
            ClaimsPrincipal principal, string managerId, string employeeId, int year, int month)
        {
            if (!await _reminder.IsEnabledAsync())
                return (null, new BadRequestObjectResult(UnsubmittedReminderRules.DisabledMessage));
            if (!await CanManageSubmissionUserAsync(principal, employeeId))
                return (null, new ForbidResult());

            var (prepared, error) = await _reminder.PrepareAsync(
                employeeId, managerId, year, month,
                roles => Constants.Roles.IsVisibleInTymeTeamView(principal, roles), DateTime.UtcNow);
            return error != null ? (null, new BadRequestObjectResult(error)) : (prepared, null);
        }

        /// <summary>
        /// Manager team grid: every visible active Tyme employee × a month window. Query:
        /// ?from=yyyy-MM&amp;to=yyyy-MM&amp;userIds=a,b. Defaults: last month to next month, everyone.
        /// </summary>
        [Function("GetTimeSubmissionGrid")]
        public async Task<IActionResult> GetGridAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "timesubmissions/grid")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, Constants.Roles.Manager) is IActionResult unauth) return unauth;

            var nowUtc = DateTime.UtcNow;
            var (defaultFrom, defaultTo) = SubmissionGridRules.DefaultRange(nowUtc);
            var from = defaultFrom;
            var to = defaultTo;

            var fromText = req.Query["from"];
            var toText = req.Query["to"];
            if (!string.IsNullOrWhiteSpace(fromText))
            {
                var parsed = SubmissionGridRules.ParseMonth(fromText);
                if (parsed == null) return new BadRequestObjectResult("Invalid from month. Use yyyy-MM.");
                from = parsed.Value;
            }
            if (!string.IsNullOrWhiteSpace(toText))
            {
                var parsed = SubmissionGridRules.ParseMonth(toText);
                if (parsed == null) return new BadRequestObjectResult("Invalid to month. Use yyyy-MM.");
                to = parsed.Value;
            }
            if (!SubmissionGridRules.IsValidRange(from, to))
                return new BadRequestObjectResult(
                    $"Choose a range of 1 to {SubmissionGridRules.MaxMonths} months, start before end.");

            var userIds = (req.Query["userIds"] ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var grid = await _gridService.BuildAsync(
                from, to, userIds, roles => Constants.Roles.IsVisibleInTymeTeamView(principal, roles), nowUtc);
            return new OkObjectResult(grid);
        }

        [Function("GetTymeSubmitNotifySettings")]
        public async Task<IActionResult> GetNotifySettingsAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "timesubmissions/notify-settings")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, Constants.Roles.Manager) is IActionResult unauth) return unauth;
            return new OkObjectResult(await _notifier.LoadSettingsAsync());
        }

        [Function("UpdateTymeSubmitNotifySettings")]
        public async Task<IActionResult> UpdateNotifySettingsAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "timesubmissions/notify-settings")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, Constants.Roles.Manager) is IActionResult unauth) return unauth;

            var (dto, validationError) = await RequestValidator.ReadJsonAndValidateAsync(req, _notifySettingsValidator);
            if (validationError != null)
                return validationError;

            return new OkObjectResult(await _notifier.SaveSettingsAsync(dto!));
        }

        [Function("GetTymeSubmitNotifyDirectory")]
        public async Task<IActionResult> GetNotifyDirectoryAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "timesubmissions/notify-directory")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, Constants.Roles.Manager) is IActionResult unauth) return unauth;
            return new OkObjectResult(await _notifier.LoadDirectoryAsync());
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
        /// Every month the current user could submit right now: has tracked time, not
        /// already submitted. Unlike <see cref="GetOverdueAsync"/> this is NOT restricted
        /// to months that have already ended — it's what populates the Submit page's own
        /// list, so a user who pre-entered next month's vacation and wants to lock it in
        /// before going on leave can see and submit it early. The nav badge / dashboard
        /// "overdue" alerts intentionally keep using GetOverdueAsync above and are
        /// unaffected by this.
        /// </summary>
        [Function("GetEligibleTimeSubmissions")]
        public async Task<IActionResult> GetEligibleAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "timesubmissions/eligible")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, out var userId) is IActionResult unauth) return unauth;

            var eligible = await ComputeEligibleForSubmissionAsync(userId);
            return new OkObjectResult(eligible);
        }

        /// <summary>
        /// Manager team view: one row per (user × month) for users in the caller's scope
        /// where the user has tracked time in that past month, with submitted/unsubmitted
        /// status. Optional query string filters: ?status= a comma list of
        /// submitted, unsubmitted, open, notime (default all), ?userId, ?from and ?to (yyyy-MM). Used by the Submit page (full filters) and the
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

            // status is a comma-separated list (submitted, unsubmitted, open, notime) or "all".
            var statuses = SubmissionGridRules.ParseStatusFilter(req.Query["status"]);
            // userId may be one id or a comma-separated list (Employee multi-select).
            var userIdFilter = (req.Query["userId"] ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.Ordinal);
            // Optional month range (yyyy-MM, inclusive). Either end may be left off for open-ended.
            (int Year, int Month)? fromMonth = null;
            (int Year, int Month)? toMonth = null;
            var fromText = req.Query["from"];
            var toText = req.Query["to"];
            if (!string.IsNullOrWhiteSpace(fromText))
            {
                fromMonth = SubmissionGridRules.ParseMonth(fromText);
                if (fromMonth == null) return new BadRequestObjectResult("Invalid from month. Use yyyy-MM.");
            }
            if (!string.IsNullOrWhiteSpace(toText))
            {
                toMonth = SubmissionGridRules.ParseMonth(toText);
                if (toMonth == null) return new BadRequestObjectResult("Invalid to month. Use yyyy-MM.");
            }
            if (fromMonth.HasValue && toMonth.HasValue
                && !SubmissionGridRules.IsInRange(toMonth.Value.Year, toMonth.Value.Month, fromMonth, null))
                return new BadRequestObjectResult("The from month must not be after the to month.");

            var nowUtc = DateTime.UtcNow;
            var currentMonthStart = new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);

            // (user × month) pairs derived from tracked-task history — i.e. months a user
            // actually worked. The submission table alone wouldn't surface unsubmitted months.
            var taskMonths = await _dbContext.TrackedTasks
                .Where(t => t.StartDate < currentMonthStart
                    && (t.Project == null || t.Project.CountsAsTime))
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
            // user's roles once (Managers and scoped admins see Tyme-scoped users; global Admin sees everyone).
            var userIds = keys.Select(k => k.UserId).Distinct().ToList();

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

                if (userIdFilter.Count > 0 && !userIdFilter.Contains(key.UserId)) continue;
                if (!SubmissionGridRules.IsInRange(key.Year, key.Month, fromMonth, toMonth)) continue;

                var submitted = submissionByKey.TryGetValue(key, out var s);
                if (!statuses.Contains(submitted ? "submitted" : "unsubmitted")) continue;

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

            // "No time" rows (a past month with nothing tracked) and "Open" rows (current or future
            // month with time, not due yet) come from the Grid service. They need a month range
            // (there are endless such months otherwise), so the dashboard widget never gets them.
            // Capped at the latest MaxMonths of the range, same cap as the Grid.
            var wantNoTime = statuses.Contains("notime");
            var wantOpen = statuses.Contains("open");
            if ((wantNoTime || wantOpen) && fromMonth.HasValue && toMonth.HasValue)
            {
                var noTimeFrom = fromMonth.Value;
                if (SubmissionGridRules.CountMonths(noTimeFrom, toMonth.Value) > SubmissionGridRules.MaxMonths)
                    noTimeFrom = SubmissionGridRules.AddMonths(
                        toMonth.Value.Year, toMonth.Value.Month, -(SubmissionGridRules.MaxMonths - 1));

                var grid = await _gridService.BuildAsync(
                    noTimeFrom, toMonth.Value, userIdFilter.Count > 0 ? userIdFilter : null,
                    roles => Constants.Roles.IsVisibleInTymeTeamView(principal, roles), nowUtc);

                foreach (var gridRow in grid.Rows)
                {
                    for (var i = 0; i < grid.Months.Count; i++)
                    {
                        var month = grid.Months[i];
                        var cellStatus = gridRow.Cells[i].Status;
                        var isOpen = cellStatus == SubmissionCellStatus.Open;
                        var isNoTime = cellStatus == SubmissionCellStatus.NoData
                            && !TimeSubmissionRules.IsEarlySubmission(month.Year, month.Month, nowUtc);
                        if (!(isOpen && wantOpen) && !(isNoTime && wantNoTime)) continue;

                        rows.Add(new TeamSubmissionRowDto
                        {
                            UserId = gridRow.UserId,
                            UserName = gridRow.UserName,
                            Year = month.Year,
                            Month = month.Month,
                            Open = isOpen,
                            NoTime = isNoTime
                        });
                    }
                }
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

            // Early submission (current or future month) is allowed, but only for a month
            // the user has actually entered time in — e.g. pre-entered vacation before
            // going on leave. Without this, CreateTimeSubmissionDtoValidator no longer
            // blocks the current/future month at the shape layer, so this is the only
            // thing stopping a submission for a month with nothing in it.
            var monthStart = new DateTime(body!.Year, body.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var monthEnd = monthStart.AddMonths(1).AddTicks(-1);
            var hasTrackedTime = await _dbContext.TrackedTasks
                .AnyAsync(t => t.UserId == userId
                    && t.StartDate >= monthStart && t.StartDate <= monthEnd
                    && (t.Project == null || t.Project.CountsAsTime));
            if (!hasTrackedTime)
                return new BadRequestObjectResult("No tracked time for that month yet — nothing to submit.");

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

            var emailWarning = await _notifier.NotifyAsync(userId, body.Year, body.Month);

            return new OkObjectResult(new TimeSubmissionDto
            {
                TimeSubmissionId = entity.TimeSubmissionId,
                UserId = entity.UserId,
                Year = entity.Year,
                Month = entity.Month,
                SubmittedAt = entity.SubmittedAt,
                SubmittedByUserId = entity.SubmittedByUserId,
                EmailWarning = emailWarning
            });
        }

        /// <summary>
        /// Manager:Tyme+ submits a month for another employee when the workspace setting
        /// <c>TymeAllowManagerSubmitOnBehalf</c> is enabled. Admin:Tyme is included via the
        /// Manager role hierarchy. Sets <see cref="TimeSubmission.SubmittedByUserId"/> to the
        /// manager so audit distinguishes self-submit from on-behalf submit.
        /// </summary>
        [Function("CreateManagerTimeSubmission")]
        public async Task<IActionResult> CreateOnBehalfAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "timesubmissions/onbehalf")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, Constants.Roles.Manager) is IActionResult unauth)
                return unauth;
            var actorId = principal.FindFirstValue(Constants.Claims.UserId);
            if (string.IsNullOrEmpty(actorId))
                return new UnauthorizedResult();

            var (body, validationError) = await RequestValidator.ReadJsonAndValidateAsync(req, _createOnBehalfValidator);
            if (validationError != null)
                return validationError;

            var settingRow = await _dbContext.AppSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == Constants.SettingKeys.TymeAllowManagerSubmitOnBehalf);
            if (!ManagerSubmitOnBehalfRules.IsEnabled(settingRow?.Value))
                return new BadRequestObjectResult(ManagerSubmitOnBehalfRules.DisabledMessage);

            var targetUserId = body!.UserId.Trim();
            if (!await CanManageSubmissionUserAsync(principal, targetUserId))
                return new ForbidResult();

            var existing = await _dbContext.TimeSubmissions
                .FirstOrDefaultAsync(s => s.UserId == targetUserId && s.Year == body.Year && s.Month == body.Month);
            if (existing != null)
                return new BadRequestObjectResult("This month has already been submitted.");

            var monthStart = new DateTime(body.Year, body.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var monthEnd = monthStart.AddMonths(1).AddTicks(-1);
            var hasTrackedTime = await _dbContext.TrackedTasks
                .AnyAsync(t => t.UserId == targetUserId
                    && t.StartDate >= monthStart && t.StartDate <= monthEnd
                    && (t.Project == null || t.Project.CountsAsTime));
            if (!hasTrackedTime)
                return new BadRequestObjectResult("No tracked time for that month yet — nothing to submit.");

            var nowUtc = DateTime.UtcNow;
            var entity = new TimeSubmission
            {
                UserId = targetUserId,
                Year = body.Year,
                Month = body.Month,
                SubmittedAt = nowUtc,
                SubmittedByUserId = actorId
            };

            _dbContext.TimeSubmissions.Add(entity);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation(
                "User {ActorId} submitted time on behalf of {TargetId} for {Year}-{Month:00}.",
                actorId, targetUserId, body.Year, body.Month);

            var emailWarning = await _notifier.NotifyAsync(targetUserId, body.Year, body.Month);

            return new OkObjectResult(new TimeSubmissionDto
            {
                TimeSubmissionId = entity.TimeSubmissionId,
                UserId = entity.UserId,
                Year = entity.Year,
                Month = entity.Month,
                SubmittedAt = entity.SubmittedAt,
                SubmittedByUserId = entity.SubmittedByUserId,
                EmailWarning = emailWarning
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

            var workdayRow = await _dbContext.AppSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == Constants.SettingKeys.WorkdayHours);
            var workdayHours = AllDayEntryRules.ParseWorkdayHours(workdayRow?.Value);
            // task.Duration is 0 for all-day entries of 24h+ (SQL time cannot store that) —
            // recompute from the dates instead of reading the raw column.
            TimeSpan ActualDuration(TrackedTask t) => TeamAvailabilityHoursRules.HoursFor(
                t.Project?.CountsAsTime, t.IsAllDay, t.StartDate, t.EndDate, t.Duration, workdayHours);
            // Same idea for a Direct-correction audit's "previous" snapshot.
            TimeSpan ActualPreviousDuration(TrackedTaskCorrectionAudit a) => TeamAvailabilityHoursRules.HoursFor(
                null, a.PreviousIsAllDay, a.PreviousStartDate, a.PreviousEndDate, a.PreviousDuration, workdayHours);

            var items = new List<TimeSubmissionCorrectionItemDto>();

            foreach (var alias in aliases)
            {
                if (!taskById.TryGetValue(alias.TaskId, out var task))
                    continue;

                items.Add(new TimeSubmissionCorrectionItemDto
                {
                    TaskId = alias.TaskId,
                    Kind = "Alias",
                    TaskDetails = task.Details ?? string.Empty,
                    OriginalDetails = task.Details ?? string.Empty,
                    OriginalStartDate = task.StartDate,
                    OriginalDurationSeconds = ActualDuration(task).TotalSeconds,
                    OriginalProjectName = task.Project?.Name,
                    AdjustedDetails = alias.Details,
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
                    TaskDetails = task.Details ?? string.Empty,
                    OriginalDetails = audit.PreviousDetails,
                    OriginalStartDate = audit.PreviousStartDate,
                    OriginalDurationSeconds = ActualPreviousDuration(audit).TotalSeconds,
                    OriginalProjectName = null,
                    AdjustedDetails = audit.NewDetails,
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

                        task.Details = alias.Details;
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
                .Where(t => t.UserId == userId && t.StartDate < currentMonthStart
                    && (t.Project == null || t.Project.CountsAsTime))
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

        /// <summary>
        /// Same shape as <see cref="ComputeOverdueAsync"/> but with no "month must already
        /// be over" restriction — unsubmitted months with tracked time through next month
        /// (early submit). Far-future imported recurring meetings are not listed.
        /// </summary>
        private async Task<List<EligibleMonthDto>> ComputeEligibleForSubmissionAsync(string userId)
        {
            var nowUtc = DateTime.UtcNow;

            var taskMonths = await _dbContext.TrackedTasks
                .Where(t => t.UserId == userId
                    && (t.Project == null || t.Project.CountsAsTime))
                .Select(t => new { t.StartDate.Year, t.StartDate.Month })
                .Distinct()
                .ToListAsync();

            if (taskMonths.Count == 0) return new List<EligibleMonthDto>();

            var submitted = await _dbContext.TimeSubmissions
                .Where(s => s.UserId == userId)
                .Select(s => new { s.Year, s.Month })
                .ToListAsync();

            var submittedSet = submitted
                .Select(x => (x.Year, x.Month))
                .ToHashSet();

            return taskMonths
                .Where(m => !submittedSet.Contains((m.Year, m.Month))
                    && TimeSubmissionRules.IsListedOnSubmitPage(m.Year, m.Month, nowUtc))
                .OrderBy(m => m.Year).ThenBy(m => m.Month)
                .Select(m => new EligibleMonthDto
                {
                    Year = m.Year,
                    Month = m.Month,
                    IsEarly = TimeSubmissionRules.IsEarlySubmission(m.Year, m.Month, nowUtc)
                })
                .ToList();
        }
    }
}
