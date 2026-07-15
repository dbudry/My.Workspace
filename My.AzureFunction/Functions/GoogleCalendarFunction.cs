using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using System.Text.RegularExpressions;
using My.Functions.Helpers;
using My.Shared.Constants;
using My.Shared.Dtos.GoogleCalendar;
using My.Shared.Dtos.Query;
using My.Shared.Rules;
using My.Shared.Validation;
using My.DAL.Data;
using My.DAL.Models;
using My.DAL.Repository;
using My.Functions.Services;

namespace My.Functions
{
    public class GoogleCalendarFunction
    {
        private const string SyncResourceState = "sync";
        private const string ExistsResourceState = "exists";

        // Google's reserved alias for the authenticated user's own primary calendar.
        // We bind every Tyme integration to this instead of creating a dedicated "Tyme"
        // sub-calendar — see docs/initiatives/personal-calendar-migration.md.
        private const string PrimaryCalendarId = "primary";

        /// <summary>
        /// Matches a bracketed project-slug tag anywhere in the event title, e.g. "[bw]".
        /// Single token; group 1 is the project slug. Case-insensitive.
        /// </summary>
        private static readonly Regex SlugTagPattern =
            new(@"\[\s*([a-z0-9]+)\s*\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly IRepository<UserSettings> settingsRepository;
        private readonly IRepository<TrackedTask> taskRepository;
        private readonly IRepository<Project> projectRepository;
        private readonly IRepository<TimeSubmission> submissionRepository;
        private readonly ApplicationDbContext dbContext;
        private readonly GoogleCalendarService google;
        private readonly GoogleTokenEncryptor encryptor;
        private readonly TeamAvailabilityPublisher teamAvailabilityPublisher;
        private readonly ILogger<GoogleCalendarFunction> logger;
        private readonly RedirectUriQueryValidator redirectUriValidator;
        private readonly IValidator<GoogleCalendarCallbackDto> callbackValidator;
        private readonly IValidator<DateRangeQueryDto> dateRangeValidator;

        public GoogleCalendarFunction(
            IRepositoryFactory repositoryFactory,
            ApplicationDbContext dbContext,
            GoogleCalendarService google,
            GoogleTokenEncryptor encryptor,
            TeamAvailabilityPublisher teamAvailabilityPublisher,
            ILogger<GoogleCalendarFunction> logger,
            RedirectUriQueryValidator redirectUriValidator,
            IValidator<GoogleCalendarCallbackDto> callbackValidator,
            IValidator<DateRangeQueryDto> dateRangeValidator)
        {
            settingsRepository = repositoryFactory.GetRepository<UserSettings>();
            taskRepository = repositoryFactory.GetRepository<TrackedTask>();
            projectRepository = repositoryFactory.GetRepository<Project>();
            submissionRepository = repositoryFactory.GetRepository<TimeSubmission>();
            this.dbContext = dbContext;
            this.google = google;
            this.encryptor = encryptor;
            this.teamAvailabilityPublisher = teamAvailabilityPublisher;
            this.logger = logger;
            this.redirectUriValidator = redirectUriValidator;
            this.callbackValidator = callbackValidator;
            this.dateRangeValidator = dateRangeValidator;
        }

        private async Task<bool> IsMonthSubmittedAsync(string userId, int year, int month)
        {
            var rows = await submissionRepository.Get(s => s.UserId == userId && s.Year == year && s.Month == month);
            return rows.Any();
        }

        private async Task<double> GetWorkdayHoursAsync()
        {
            var row = await dbContext.AppSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == Constants.SettingKeys.WorkdayHours);
            return AllDayEntryRules.ParseWorkdayHours(row?.Value);
        }

        /// <summary>
        /// Converts Google's real-UTC moment into the user's local wall-clock and tags
        /// it Kind=Utc — matching the convention dialog-created TrackedTasks use so
        /// downstream <c>GoogleEventTimeRules.FormatForGoogle</c> can rebroadcast it
        /// with the user's IANA zone without shifting the displayed time.
        /// </summary>
        private static DateTime ConvertUtcToLocalWallClock(DateTime utc, string? timeZone)
        {
            if (string.IsNullOrEmpty(timeZone))
                return DateTime.SpecifyKind(utc, DateTimeKind.Utc);

            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZone);
                var local = TimeZoneInfo.ConvertTimeFromUtc(
                    DateTime.SpecifyKind(utc, DateTimeKind.Utc), tz);
                return DateTime.SpecifyKind(local, DateTimeKind.Utc);
            }
            catch (TimeZoneNotFoundException)
            {
                // Bad TZ id — fall back to leaving the UTC value untouched. The team
                // calendar will be off by the offset but the task still imports.
                return DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            }
        }

        [Function("GetGoogleCalendarAuthUrl")]
        public async Task<IActionResult> GetAuthUrlAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "googlecalendar/authurl")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            var userId = principal.FindFirstValue(Constants.Claims.UserId);
            if (string.IsNullOrEmpty(userId))
                return new UnauthorizedResult();

            if (!google.IsConfigured)
                return new ObjectResult("Google Calendar integration is not configured on the server.") { StatusCode = 503 };

            var redirectUri = req.Query["redirectUri"];
            if (RequestValidator.BadRequestIfInvalid(redirectUriValidator, redirectUri) is { } redirectError)
                return redirectError;

            // Bind the OAuth to the same Google account the user signed into the app
            // with — passing login_hint skips the account chooser and prevents the user
            // from picking a different (e.g. personal) Gmail account by accident.
            var email = principal.FindFirstValue(System.Security.Claims.ClaimTypes.Email);
            var url = google.BuildAuthorizationUrl(redirectUri!, state: userId, loginHint: email);
            return await Task.FromResult<IActionResult>(new OkObjectResult(new { url }));
        }

        [Function("CompleteGoogleCalendarCallback")]
        public async Task<IActionResult> CompleteCallbackAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "googlecalendar/callback")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            var userId = principal.FindFirstValue(Constants.Claims.UserId);
            if (string.IsNullOrEmpty(userId))
                return new UnauthorizedResult();

            var (body, validationError) = await RequestValidator.ReadJsonAndValidateAsync(req, callbackValidator);
            if (validationError != null)
                return validationError;

            try
            {
                var (refresh, email) = await google.ExchangeCodeAsync(body!.Code!, body.RedirectUri!);
                var encrypted = encryptor.Encrypt(refresh);

                var settings = (await settingsRepository.Get(s => s.UserId == userId)).FirstOrDefault()
                               ?? await InsertNewSettingsAsync(userId);

                settings.GoogleRefreshToken = encrypted;
                settings.GoogleCalendarId = PrimaryCalendarId;
                settings.GoogleCalendarEmail = email;
                // Connecting implies "I want sync." Enable both directions so the very next
                // task edit flows out to the calendar, instead of silently no-op'ing because
                // an old toggle was still off. Users can still flip these off in /settings
                // afterwards if they want one-way sync only.
                settings.PublishToGoogleCalendar = true;
                settings.ImportFromGoogleCalendar = true;
                await settingsRepository.Update(settings);

                await TryStartWatchAsync(settings, req);

                return new OkObjectResult(new { connected = true, email });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Google Calendar callback failed for {UserId}.", userId);
                return new BadRequestObjectResult(ApiErrorMessages.GoogleCalendarConnectFailed);
            }
        }

        [Function("DisconnectGoogleCalendar")]
        public async Task<IActionResult> DisconnectAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "googlecalendar/disconnect")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            var userId = principal.FindFirstValue(Constants.Claims.UserId);
            if (string.IsNullOrEmpty(userId))
                return new UnauthorizedResult();

            var settings = (await settingsRepository.Get(s => s.UserId == userId)).FirstOrDefault();
            if (settings == null || string.IsNullOrEmpty(settings.GoogleRefreshToken))
                return new NoContentResult();

            await TryStopWatchAsync(settings);

            // Intentionally KEEP the GoogleEventId on each TrackedTask. Earlier we cleared it
            // here, but that caused a reconnect-with-the-same-account flow to duplicate every
            // event: the previously-published events were still on Google, and backfill saw
            // every task as never-synced and pushed a fresh copy. Leaving the linkage intact
            // means a same-account reconnect picks back up where it left off; a different-account
            // reconnect produces stale ids that will 404 on the next update, which
            // TryPushUpdateAsync handles by dropping the linkage and creating fresh.

            // Intentionally NOT calling /revoke on the refresh_token. Google's revoke endpoint
            // invalidates every access_token in the grant chain — including the SPA's sign-in
            // access_token when scopes overlap (we share openid+profile+email between OIDC
            // sign-in and the calendar OAuth flow under the same client_id). Revoking here
            // killed the user's session ~5 min later (after the AuthMiddleware tokeninfo cache
            // expired) and produced a redirect-loop they couldn't escape. Forgetting the
            // refresh_token client-side is a sufficient disconnect; users who want a full
            // grant revocation can do it at myaccount.google.com → Connections.
            settings.GoogleRefreshToken = null;
            settings.GoogleCalendarId = null;
            settings.GoogleCalendarEmail = null;
            settings.GoogleChannelId = null;
            settings.GoogleChannelToken = null;
            settings.GoogleResourceId = null;
            settings.GoogleChannelExpiresAt = null;
            settings.GoogleSyncToken = null;
            settings.CalendarBackfillAcknowledgedUtc = null;
            await settingsRepository.Update(settings);

            return new NoContentResult();
        }

        [Function("AcknowledgeCalendarBackfillPrompt")]
        public async Task<IActionResult> AcknowledgeBackfillPromptAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "googlecalendar/backfill/acknowledge")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            var userId = principal.FindFirstValue(Constants.Claims.UserId);
            if (string.IsNullOrEmpty(userId))
                return new UnauthorizedResult();

            var settings = (await settingsRepository.Get(s => s.UserId == userId)).FirstOrDefault()
                           ?? await InsertNewSettingsAsync(userId);

            settings.CalendarBackfillAcknowledgedUtc = DateTime.UtcNow;
            await settingsRepository.Update(settings);

            return new NoContentResult();
        }

        /// <summary>
        /// Push the user's existing tracked tasks in [from, to] onto their primary Google
        /// calendar so they can see their historical tracked time alongside their meetings.
        /// Idempotent — tasks already on the calendar (have GoogleEventId) are skipped, so
        /// the user can safely retry or call this with overlapping ranges.
        ///
        /// Synchronous on purpose: a 30-day window is typically &lt; 50s of wall time at
        /// Google's per-user write quota (~6/sec) and shows a progress snackbar in the
        /// SPA. If teams start using long lookbacks this should move to a background job.
        /// </summary>
        [Function("BackfillGoogleCalendar")]
        public async Task<IActionResult> BackfillAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "googlecalendar/backfill")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            var userId = principal.FindFirstValue(Constants.Claims.UserId);
            if (string.IsNullOrEmpty(userId))
                return new UnauthorizedResult();

            var (dateRange, rangeError) = RequestValidator.ParseDateRangeQuery(req, dateRangeValidator);
            if (rangeError != null)
                return rangeError;

            var fromUtc = dateRange!.From.ToUniversalTime();
            var toUtc = dateRange.To.ToUniversalTime().AddDays(1).AddTicks(-1);

            var settings = (await settingsRepository.Get(s => s.UserId == userId)).FirstOrDefault();
            if (settings == null
                || string.IsNullOrEmpty(settings.GoogleRefreshToken)
                || string.IsNullOrEmpty(settings.GoogleCalendarId))
                return new BadRequestObjectResult("Google Calendar is not connected.");

            var tasks = (await taskRepository.Get(t =>
                    t.UserId == userId
                    && t.StartDate >= fromUtc
                    && t.StartDate <= toUtc))
                .ToList();

            // Batch the project lookups instead of one-per-task. Slug prefixes the event
            // title (`[slug] task name`) so inbound sync recognizes our own pushes via
            // tag rather than seeing them as untagged and flagging red.
            var projectIds = tasks
                .Where(t => !string.IsNullOrEmpty(t.ProjectId))
                .Select(t => t.ProjectId!)
                .Distinct()
                .ToList();
            var slugByProjectId = projectIds.Count == 0
                ? new Dictionary<string, string?>()
                : (await projectRepository.Get(p => projectIds.Contains(p.ProjectId)))
                    .ToDictionary(p => p.ProjectId, p => p.Slug);

            var result = new Shared.Dtos.GoogleCalendar.CalendarBackfillResultDto();

            foreach (var t in tasks)
            {
                if (!string.IsNullOrEmpty(t.GoogleEventId))
                {
                    result.Skipped++;
                    continue;
                }

                try
                {
                    string? slug = null;
                    if (!string.IsNullOrEmpty(t.ProjectId))
                        slugByProjectId.TryGetValue(t.ProjectId, out slug);

                    var ev = await google.CreateEventAsync(settings.GoogleRefreshToken, settings.GoogleCalendarId, t, slug, settings.TimeZone, settings.TymeEventColorId, settings.TymeUnmatchedEventColorId);
                    t.GoogleEventId = ev.Id;
                    await taskRepository.Update(t);
                    result.Created++;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Backfill failed for TrackedTask {TaskId}; continuing.", t.TaskId);
                    result.Failed++;
                }
            }

            logger.LogInformation(
                "Backfill complete for user {UserId}: created={Created}, skipped={Skipped}, failed={Failed}.",
                userId, result.Created, result.Skipped, result.Failed);

            return new OkObjectResult(result);
        }

        /// <summary>
        /// Pull missed events from the user's Google Calendar into Tyme. The fix-it path
        /// for webhook misses — when a push channel expired, a transient error swallowed
        /// a webhook delivery, or a user typed a `[slug]` event before their Google
        /// integration was reconnected.
        ///
        /// Self-service by default (uses the caller's calendar). Global Admin can target
        /// another user with <c>?userId=</c> to fix issues on behalf of a colleague.
        ///
        /// Mechanism: lists all events in the range via <c>EventsResource.List</c> (no
        /// sync token) and runs each through the same <see cref="TryImportEventAsync"/>
        /// helper the webhook uses — so the behavior is identical, just triggered on
        /// demand instead of by push.
        /// </summary>
        [Function("PullFromGoogleCalendar")]
        public async Task<IActionResult> PullFromGoogleAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "googlecalendar/pullfromgoogle")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            var callerUserId = principal.FindFirstValue(Constants.Claims.UserId);
            if (string.IsNullOrEmpty(callerUserId))
                return new UnauthorizedResult();

            // Admin can target another user via ?userId=; absent or self → self-service.
            var targetUserId = req.Query["userId"];
            if (!string.IsNullOrEmpty(targetUserId) && targetUserId != callerUserId)
            {
                if (!Constants.Roles.IsGlobalAdmin(principal))
                    return new ObjectResult("Only global Admin can pull events for another user.") { StatusCode = 403 };
            }
            else
            {
                targetUserId = callerUserId;
            }

            var (dateRange, rangeError) = RequestValidator.ParseDateRangeQuery(req, dateRangeValidator);
            if (rangeError != null)
                return rangeError;

            var fromUtc = dateRange!.From.Date.ToUniversalTime();
            var toUtc = dateRange.To.Date.ToUniversalTime().AddDays(1).AddTicks(-1);

            var result = new My.Shared.Dtos.GoogleCalendar.CalendarPullResultDto();

            var settings = (await settingsRepository.Get(s => s.UserId == targetUserId)).FirstOrDefault();
            if (settings == null
                || string.IsNullOrEmpty(settings.GoogleRefreshToken)
                || string.IsNullOrEmpty(settings.GoogleCalendarId))
            {
                result.Error = "Google Calendar is not connected for this user.";
                return new OkObjectResult(result);
            }

            List<Google.Apis.Calendar.v3.Data.Event> events;
            try
            {
                events = await google.ListEventsInRangeAsync(
                    settings.GoogleRefreshToken, settings.GoogleCalendarId, fromUtc, toUtc, req.FunctionContext.CancellationToken);
            }
            catch (CalendarRangeTooLargeException ex)
            {
                // Friendly error: the user picked a too-broad window. Surfacing the
                // actionable hint (narrow the range) is more useful than a stack trace.
                logger.LogWarning(ex, "Pull-from-Google: range too large for user {UserId}.", targetUserId);
                result.Error = ex.Message;
                return new OkObjectResult(result);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Pull-from-Google failed to list events for user {UserId}.", targetUserId);
                result.Error = ApiErrorMessages.GoogleCalendarListFailed;
                return new OkObjectResult(result);
            }

            result.Scanned = events.Count;

            foreach (var ev in events)
            {
                try
                {
                    var outcome = await TryImportEventAsync(ev, settings);
                    switch (outcome)
                    {
                        case EventImportOutcome.Created: result.Created++; break;
                        case EventImportOutcome.Updated: result.Updated++; break;
                        case EventImportOutcome.Cancelled: result.Cancelled++; break;
                        case EventImportOutcome.SkippedOurs: result.SkippedOurs++; break;
                        case EventImportOutcome.SkippedNoDates: result.SkippedNoDates++; break;
                        case EventImportOutcome.SkippedNoTag: result.SkippedNoTag++; break;
                        case EventImportOutcome.SkippedUnresolvedTag: result.SkippedUnresolvedTag++; break;
                        case EventImportOutcome.SkippedDeclinedInvite: result.SkippedDeclinedInvite++; break;
                        case EventImportOutcome.SkippedMonthSubmitted: result.SkippedMonthSubmitted++; break;
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "Pull-from-Google: failed to import event {EventId} for user {UserId}; continuing.",
                        ev.Id, targetUserId);
                    result.Failed++;
                }
            }

            logger.LogInformation(
                "Pull-from-Google complete for user {UserId} ({CallerUserId}): scanned={Scanned}, created={Created}, updated={Updated}, cancelled={Cancelled}, failed={Failed}.",
                targetUserId, callerUserId, result.Scanned, result.Created, result.Updated, result.Cancelled, result.Failed);

            return new OkObjectResult(result);
        }

        /// <summary>
        /// Nightly self-heal: for every user with Google Calendar connected and import
        /// enabled, runs the same per-event import loop the webhook uses over a rolling
        /// [-7d, +7d] window. The pulled-by-hand endpoint above is the surgical version
        /// of this — the timer is what actually answers "no daily calls" by closing
        /// webhook gaps before anyone notices them.
        ///
        /// Schedule rationale: 07:00 UTC — one hour after <see cref="GoogleCalendarWatchRenewalFunction"/>
        /// (06:00 UTC) so a freshly-renewed channel doesn't go through this loop
        /// pointlessly. Watch renewal does NOT itself replay missed events, so the gap
        /// between channel expiry and next renewal is only closed by this pull.
        ///
        /// Window rationale: 7 days back covers webhook drops from missed deliveries
        /// and watch-channel gaps; 7 days forward catches vacations entered last week
        /// but starting next week, so the team calendar is populated before the user
        /// is actually out.
        ///
        /// Per-user try/catch is mandatory: one user's revoked refresh token or quota
        /// hit must not break the loop for everyone else.
        /// </summary>
        [Function("PullMissedEventsNightly")]
        public async Task PullMissedEventsNightlyAsync([TimerTrigger("0 0 7 * * *")] TimerInfo timer)
        {
            var windowFromUtc = DateTime.UtcNow.Date.AddDays(-7);
            var windowToUtc = DateTime.UtcNow.Date.AddDays(7).AddTicks(-1);

            var users = await settingsRepository.Get(s =>
                !string.IsNullOrEmpty(s.GoogleRefreshToken)
                && !string.IsNullOrEmpty(s.GoogleCalendarId)
                && s.ImportFromGoogleCalendar);

            int totalUsers = 0, succeeded = 0, failed = 0;
            int totalCreated = 0, totalUpdated = 0, totalCancelled = 0;

            foreach (var settings in users)
            {
                totalUsers++;
                try
                {
                    var events = await google.ListEventsInRangeAsync(
                        settings.GoogleRefreshToken!, settings.GoogleCalendarId!,
                        windowFromUtc, windowToUtc);

                    int created = 0, updated = 0, cancelled = 0;
                    foreach (var ev in events)
                    {
                        try
                        {
                            var outcome = await TryImportEventAsync(ev, settings);
                            switch (outcome)
                            {
                                case EventImportOutcome.Created: created++; break;
                                case EventImportOutcome.Updated: updated++; break;
                                case EventImportOutcome.Cancelled: cancelled++; break;
                            }
                        }
                        catch (Exception ex)
                        {
                            // One event's failure shouldn't poison the rest of this user's loop.
                            logger.LogWarning(ex,
                                "Nightly self-heal: failed to import event {EventId} for user {UserId}; continuing.",
                                ev.Id, settings.UserId);
                        }
                    }

                    totalCreated += created;
                    totalUpdated += updated;
                    totalCancelled += cancelled;
                    succeeded++;

                    if (created + updated + cancelled > 0)
                    {
                        logger.LogInformation(
                            "Nightly self-heal for user {UserId}: created={Created}, updated={Updated}, cancelled={Cancelled} from {Total} events.",
                            settings.UserId, created, updated, cancelled, events.Count);
                    }
                }
                catch (CalendarRangeTooLargeException ex)
                {
                    // A 14-day window blowing past 5000 events means the user has an
                    // extreme recurring-event pattern. Log + skip — they'll have to use
                    // the manual surgical pull with a narrower range if they need it.
                    logger.LogWarning(ex,
                        "Nightly self-heal: range too large for user {UserId}; skipping.", settings.UserId);
                    failed++;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex,
                        "Nightly self-heal: failed for user {UserId}; continuing with next user.", settings.UserId);
                    failed++;
                }
            }

            logger.LogInformation(
                "Nightly self-heal complete: users={TotalUsers} (ok={Succeeded}, failed={Failed}); created={TotalCreated}, updated={TotalUpdated}, cancelled={TotalCancelled}.",
                totalUsers, succeeded, failed, totalCreated, totalUpdated, totalCancelled);
        }

        /// <summary>
        /// Webhook receiver. Google calls this (unauthenticated) whenever the user's primary calendar changes.
        /// We find the user by channelId (echoed in X-Goog-Channel-Id) and pull the delta.
        /// </summary>
        [Function("GoogleCalendarWebhook")]
        public async Task<IActionResult> WebhookAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "googlecalendar/webhook")] HttpRequestData req)
        {
            string? channelId = HeaderOrNull(req, "X-Goog-Channel-Id");
            string? channelToken = HeaderOrNull(req, "X-Goog-Channel-Token");
            string? resourceState = HeaderOrNull(req, "X-Goog-Resource-State");

            if (string.IsNullOrEmpty(channelId))
                return new BadRequestObjectResult("Missing X-Goog-Channel-Id.");

            var settings = (await settingsRepository.Get(s => s.GoogleChannelId == channelId)).FirstOrDefault();
            if (settings == null)
            {
                logger.LogInformation("Google webhook for unknown channel {ChannelId}; ignoring.", channelId);
                return new OkResult();
            }

            if (!GoogleCalendarWebhookRules.IsChannelTokenValid(channelToken, settings.GoogleChannelToken))
            {
                logger.LogWarning("Google webhook rejected for channel {ChannelId}: invalid or missing channel token.", channelId);
                return new OkResult();
            }

            // "sync" is Google's "channel-is-live" handshake; no real change to process.
            if (string.Equals(resourceState, SyncResourceState, StringComparison.OrdinalIgnoreCase))
                return new OkResult();

            if (!settings.ImportFromGoogleCalendar
                || string.IsNullOrEmpty(settings.GoogleRefreshToken)
                || string.IsNullOrEmpty(settings.GoogleCalendarId))
                return new OkResult();

            if (!string.Equals(resourceState, ExistsResourceState, StringComparison.OrdinalIgnoreCase))
                return new OkResult();

            try
            {
                await ImportChangesAsync(settings);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Google webhook import failed for user {UserId}.", settings.UserId);
                return new ObjectResult("Import failed.") { StatusCode = 500 };
            }

            return new OkResult();
        }

        private async Task ImportChangesAsync(UserSettings settings)
        {
            var (events, nextSync) = await google.SyncEventsAsync(
                settings.GoogleRefreshToken!, settings.GoogleCalendarId!, settings.GoogleSyncToken);

            foreach (var ev in events)
                await TryImportEventAsync(ev, settings);

            if (!string.IsNullOrEmpty(nextSync) && nextSync != settings.GoogleSyncToken)
            {
                settings.GoogleSyncToken = nextSync;
                await settingsRepository.Update(settings);
            }
        }

        /// <summary>
        /// Outcomes for a single Google → Tyme event import attempt. Returned so the
        /// "pull missed events" endpoint can roll up counters; the webhook path just
        /// ignores the return.
        /// </summary>
        private enum EventImportOutcome
        {
            Created,
            Updated,
            Cancelled,
            SkippedOurs,
            SkippedNoDates,
            SkippedNoTag,
            SkippedUnresolvedTag,
            SkippedDeclinedInvite,
            SkippedMonthSubmitted,
        }

        /// <summary>
        /// The full per-event import decision tree, shared by the webhook (incremental
        /// sync) and the pull-missed-events endpoint (full range list). Pulled out so
        /// both entry points produce identical behavior — when a single event was
        /// missed by the webhook, an admin resync re-runs exactly the same logic
        /// rather than a parallel reimplementation.
        ///
        /// Returns the outcome so callers that want per-event counts can report them.
        /// The webhook path discards the return — it logs internally.
        /// </summary>
        private async Task<EventImportOutcome> TryImportEventAsync(
            Google.Apis.Calendar.v3.Data.Event ev, UserSettings settings)
        {
            string? source = null;
            ev.ExtendedProperties?.Private__?.TryGetValue(GoogleCalendarService.ExtendedPropSource, out source);
            var isOurs = string.Equals(source, GoogleCalendarService.ExtendedPropSourceValue, StringComparison.Ordinal);

            // Cancellations are processed regardless of source: when the user deletes a
            // Tyme-pushed event from Google Calendar, the cancellation webhook still has
            // our extended property attached, but the user's intent is clear — the matching
            // Tyme task should also disappear. (If we ourselves deleted the event via
            // TryPushDeleteAsync, the Tyme task is already gone and this becomes a no-op.)
            if (string.Equals(ev.Status, "cancelled", StringComparison.OrdinalIgnoreCase))
            {
                var toDelete = (await taskRepository.Get(t => t.UserId == settings.UserId && t.GoogleEventId == ev.Id)).FirstOrDefault();
                if (toDelete != null
                    && !await IsMonthSubmittedAsync(settings.UserId, toDelete.StartDate.Year, toDelete.StartDate.Month))
                {
                    // Clean up the team-availability sister event *before* deleting the
                    // TrackedTask — once the row is gone we've lost the
                    // TeamAvailabilityEventId reference. Without this step a user
                    // deleting [vac] from their primary calendar would see the team
                    // calendar still show their availability entry.
                    await teamAvailabilityPublisher.DeleteSisterEventAsync(toDelete, settings);

                    await taskRepository.Delete(toDelete.TaskId);
                    logger.LogInformation(
                        "Deleted TrackedTask {TaskId} for user {UserId} after Google cancelled event {EventId}.",
                        toDelete.TaskId, settings.UserId, ev.Id);
                }
                return EventImportOutcome.Cancelled;
            }

            // Non-cancellation echoes from our own Tyme → Google pushes: skip to avoid
            // re-importing what we just sent.
            if (isOurs) return EventImportOutcome.SkippedOurs;

            // Parse start/end via the pure CalendarEventDateRules helper so the logic
            // can be unit-tested without the Google SDK. Returns null when neither shape
            // (timed datetime or all-day date strings) was usable — log and skip.
            var parsed = CalendarEventDateRules.Parse(
                ev.Start?.DateTimeDateTimeOffset?.UtcDateTime,
                ev.End?.DateTimeDateTimeOffset?.UtcDateTime,
                ev.Start?.Date,
                ev.End?.Date);
            if (parsed == null)
            {
                logger.LogInformation(
                    "Skipping Google event {EventId} for user {UserId}: no usable start/end " +
                    "(summary='{Summary}').",
                    ev.Id, settings.UserId, ev.Summary);
                return EventImportOutcome.SkippedNoDates;
            }

            DateTime startDate = parsed.StartDate;
            DateTime endDate = parsed.EndDate;
            bool isAllDay = parsed.IsAllDay;

            // Timed events arrive as Google's real-UTC truth; dialog-created TrackedTasks
            // store wall-clock-tagged-Kind=Utc (intentional, so GoogleEventTimeRules can
            // rebroadcast with the user's IANA zone). Convert here so the conventions match
            // — without this, a 10am ET inbound shows as 2pm on the team calendar (4h shift).
            // All-day stays untouched: it's date-only, no zone math.
            if (!isAllDay)
            {
                startDate = ConvertUtcToLocalWallClock(startDate, settings.TimeZone);
                endDate = ConvertUtcToLocalWallClock(endDate, settings.TimeZone);
            }

            var rawSummary = ev.Summary ?? string.Empty;
            var (matchedProjectId, cleanSummary, tagHandling) = await ResolveSlugTagAsync(rawSummary, settings.UserId);

            var existing = (await taskRepository.Get(t => t.UserId == settings.UserId && t.GoogleEventId == ev.Id)).FirstOrDefault();

            // Primary-calendar rules (see docs/initiatives/personal-calendar-migration.md):
            //   - Untagged event → never imported. The primary calendar holds personal events;
            //     persisting them would be a privacy violation.
            //   - Unresolved slug `[xyz]` where no project has slug "xyz" → not imported either,
            //     but logged so the user can spot typos in their calendar copy.
            //   - Matched slug → import (create or update).
            if (tagHandling != TagHandling.MatchedTag)
            {
                if (tagHandling == TagHandling.UnresolvedTag)
                {
                    logger.LogInformation(
                        "Skipping Google event {EventId} for user {UserId}: slug present but did not resolve to any active project (summary='{Summary}').",
                        ev.Id, settings.UserId, rawSummary);
                    return EventImportOutcome.SkippedUnresolvedTag;
                }
                // If we previously imported this event and the user has since removed/changed
                // its slug, leave the existing task alone — we don't want to delete data the
                // user already committed to. They can delete it from Tyme directly.
                return EventImportOutcome.SkippedNoTag;
            }

            // Invite policy lives in CalendarImportRules.EvaluateInvite — pure helper
            // so it can be unit-tested without the Google SDK. Google marks the calendar
            // owner's attendee row with Self=true (more reliable than email matching
            // when aliases are involved); a missing Self row means the event is
            // self-organized or has no attendees and always imports.
            var selfAttendee = ev.Attendees?.FirstOrDefault(a => a.Self == true);
            var inviteDecision = CalendarImportRules.EvaluateInvite(selfAttendee?.ResponseStatus);
            if (inviteDecision == CalendarImportRules.InviteImportDecision.Skip)
            {
                if (existing != null
                    && !await IsMonthSubmittedAsync(settings.UserId, existing.StartDate.Year, existing.StartDate.Month))
                {
                    await taskRepository.Delete(existing.TaskId);
                }
                logger.LogInformation(
                    "Skipping Google event {EventId} for user {UserId}: invite responseStatus='{Status}' (need accepted or tentative).",
                    ev.Id, settings.UserId, selfAttendee?.ResponseStatus);
                return EventImportOutcome.SkippedDeclinedInvite;
            }

            var finalName = string.IsNullOrWhiteSpace(cleanSummary) ? "(Untitled)" : cleanSummary;

            // Duration: workday-derived for all-day, raw elapsed for timed. Matches what
            // a manual save through TrackedTaskFunction produces, so reports look the same
            // whether the entry came from the in-app dialog or from a Google event.
            var derivedDuration = isAllDay
                ? AllDayEntryRules.DurationFor(startDate, endDate, await GetWorkdayHoursAsync())
                : endDate - startDate;

            TrackedTask saved;
            EventImportOutcome outcome;
            if (existing != null)
            {
                if (await IsMonthSubmittedAsync(settings.UserId, existing.StartDate.Year, existing.StartDate.Month))
                    return EventImportOutcome.SkippedMonthSubmitted;
                existing.Name = finalName;
                existing.StartDate = startDate;
                existing.EndDate = endDate;
                existing.Duration = derivedDuration;
                existing.IsAllDay = isAllDay;
                existing.ProjectId = matchedProjectId;
                existing.IsBillable = await TrackedTaskBillableResolver.ResolveAsync(dbContext, matchedProjectId);
                await taskRepository.Update(existing);
                saved = existing;
                outcome = EventImportOutcome.Updated;
            }
            else
            {
                saved = new TrackedTask
                {
                    UserId = settings.UserId,
                    Name = finalName,
                    StartDate = startDate,
                    EndDate = endDate,
                    Duration = derivedDuration,
                    IsAllDay = isAllDay,
                    ProjectId = matchedProjectId,
                    GoogleEventId = ev.Id,
                    IsBillable = await TrackedTaskBillableResolver.ResolveAsync(dbContext, matchedProjectId)
                };
                await taskRepository.Insert(saved);
                outcome = EventImportOutcome.Created;
            }

            // Dual-publish to the workspace Team Availability calendar if the matched
            // project has IsSharedAvailability=true. Without this call, a [ooo] event
            // typed into the primary calendar imports into Tyme but never reaches the
            // shared calendar — the team's view goes stale.
            await teamAvailabilityPublisher.PublishAsync(saved, settings);
            return outcome;
        }

        private enum TagHandling { NoTag, MatchedTag, UnresolvedTag }

        /// <summary>
        /// Looks for a [slug] tag in the event title. The slug is workspace-unique, so it
        /// routes to a single project regardless of which user owns the calendar. If the
        /// tag is present but doesn't resolve, returns null projectId and leaves the tag
        /// in place so the user can see what they typed.
        /// </summary>
        private async Task<(string? projectId, string cleanedSummary, TagHandling handling)> ResolveSlugTagAsync(string summary, string userId)
        {
            var match = SlugTagPattern.Match(summary);
            if (!match.Success)
                return (null, summary.Trim(), TagHandling.NoTag);

            var projectSlug = match.Groups[1].Value.ToLowerInvariant();

            var candidates = await projectRepository.Get(
                p => p.Slug == projectSlug
                     && !p.IsArchived
                     && p.IsActive);
            var project = candidates.FirstOrDefault();

            if (project == null)
                return (null, summary.Trim(), TagHandling.UnresolvedTag);

            var cleaned = SlugTagPattern.Replace(summary, string.Empty).Trim();
            return (project.ProjectId, cleaned, TagHandling.MatchedTag);
        }

        private async Task TryStartWatchAsync(UserSettings settings, HttpRequestData req)
        {
            if (string.IsNullOrEmpty(settings.GoogleRefreshToken) || string.IsNullOrEmpty(settings.GoogleCalendarId))
                return;

            var webhookUrl = ResolveWebhookUrl(req);
            if (string.IsNullOrEmpty(webhookUrl))
            {
                logger.LogWarning("No webhook URL available; push channel will not be registered.");
                return;
            }

            var channelId = Guid.NewGuid().ToString("N");
            var channelToken = Guid.NewGuid().ToString("N");

            try
            {
                var ch = await google.StartWatchAsync(
                    settings.GoogleRefreshToken, settings.GoogleCalendarId,
                    channelId, channelToken, webhookUrl);

                settings.GoogleChannelId = ch.Id ?? channelId;
                settings.GoogleChannelToken = channelToken;
                settings.GoogleResourceId = ch.ResourceId;
                settings.GoogleChannelExpiresAt = ch.Expiration.HasValue
                    ? DateTimeOffset.FromUnixTimeMilliseconds(ch.Expiration.Value).UtcDateTime
                    : null;
                settings.GoogleSyncToken = null;
                await settingsRepository.Update(settings);

                // Initial pull so existing calendar events land in Tyme immediately,
                // and so subsequent webhooks have a syncToken to diff from.
                if (settings.ImportFromGoogleCalendar)
                {
                    try { await ImportChangesAsync(settings); }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Initial Google Calendar import failed for {UserId}; webhook will retry.", settings.UserId);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "StartWatch failed for user {UserId} — inbound sync disabled until next connect.", settings.UserId);
            }
        }

        private async Task TryStopWatchAsync(UserSettings settings)
        {
            if (string.IsNullOrEmpty(settings.GoogleRefreshToken)
                || string.IsNullOrEmpty(settings.GoogleChannelId)
                || string.IsNullOrEmpty(settings.GoogleResourceId))
                return;

            try
            {
                await google.StopWatchAsync(settings.GoogleRefreshToken,
                    settings.GoogleChannelId, settings.GoogleResourceId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "StopWatch failed for channel {ChannelId}.", settings.GoogleChannelId);
            }
        }

        private static string? ResolveWebhookUrl(HttpRequestData req)
        {
            var configured = Environment.GetEnvironmentVariable("Google__WebhookUrl");
            if (!string.IsNullOrEmpty(configured))
                return configured;

            var host = HeaderOrNull(req, "X-Forwarded-Host") ?? req.Url.Host;
            var scheme = HeaderOrNull(req, "X-Forwarded-Proto") ?? "https";
            return $"{scheme}://{host}/api/{Constants.API.GoogleCalendar.Webhook}";
        }

        private static string? HeaderOrNull(HttpRequestData req, string name)
        {
            if (req.Headers.TryGetValues(name, out var vals))
            {
                var first = vals.FirstOrDefault();
                if (!string.IsNullOrEmpty(first)) return first;
            }
            return null;
        }

        private async Task<UserSettings> InsertNewSettingsAsync(string userId)
        {
            var s = new UserSettings { UserId = userId };
            try
            {
                await settingsRepository.Insert(s);
            }
            catch (DbUpdateException)
            {
                // Concurrent creation race – reload the one that won
                var reloaded = await settingsRepository.Get(x => x.UserId == userId);
                s = reloaded.FirstOrDefault() ?? s;
            }
            return s;
        }

    }
}
