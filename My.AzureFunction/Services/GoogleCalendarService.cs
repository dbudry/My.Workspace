using Google.Apis.Auth;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Requests;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Microsoft.Extensions.Logging;
using My.DAL.Models;
using My.Shared.Rules;

namespace My.Functions.Services
{
    /// <summary>
    /// Handles OAuth and event CRUD against the user's primary Google calendar.
    /// </summary>
    public class GoogleCalendarService
    {
        public const string ExtendedPropSource = "source";
        public const string ExtendedPropSourceValue = "tyme";
        public const string ExtendedPropTymeTaskId = "tymeTaskId";

        private static readonly string[] CalendarScopes = new[]
        {
            CalendarService.Scope.Calendar,
            DriveService.Scope.DriveFile,
            DriveService.Scope.DriveReadonly,
            "https://www.googleapis.com/auth/userinfo.email",
            "openid"
        };

        private readonly GoogleTokenEncryptor encryptor;
        private readonly ILogger<GoogleCalendarService> logger;
        private readonly string clientId;
        private readonly string clientSecret;

        public GoogleCalendarService(GoogleTokenEncryptor encryptor, ILogger<GoogleCalendarService> logger)
        {
            this.encryptor = encryptor;
            this.logger = logger;
            clientId = Environment.GetEnvironmentVariable("Google__ClientId") ?? "";
            clientSecret = Environment.GetEnvironmentVariable("Google__ClientSecret") ?? "";
        }

        public bool IsConfigured => !string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(clientSecret);

        /// <summary>
        /// Builds the consent URL the user must visit to authorize Tyme to manage their calendar.
        /// Pass <paramref name="loginHint"/> (the user's email) to skip Google's account chooser
        /// and force the OAuth to bind to the same account they signed into the app with.
        /// Optional <c>hd</c> comes from configured allowed email domains when exactly one domain is set.
        /// </summary>
        public string BuildAuthorizationUrl(string redirectUri, string state, string? loginHint = null, string? allowedEmailDomains = null)
        {
            var flow = CreateFlow();
            var req = flow.CreateAuthorizationCodeRequest(redirectUri);
            req.State = state;
            // Force refresh_token issuance even on re-consent
            if (req is GoogleAuthorizationCodeRequestUrl google)
            {
                google.AccessType = "offline";
                google.Prompt = "consent";
            }
            var url = req.Build().ToString();

            // Append login_hint + optional hd to skip the account chooser. The Google client library
            // doesn't expose these on the request object, so we tack them on the built URL.
            var sep = url.Contains('?') ? "&" : "?";
            var hd = GoogleIdentityRules.GetSingleHostedDomainHint(
                allowedEmailDomains ?? GoogleIdentityRules.ResolveConfiguredDomains());
            if (!string.IsNullOrEmpty(hd))
            {
                url += $"{sep}hd={Uri.EscapeDataString(hd)}";
                sep = "&";
            }
            if (!string.IsNullOrEmpty(loginHint))
                url += $"{sep}login_hint={Uri.EscapeDataString(loginHint)}";

            return url;
        }

        /// <summary>Exchanges an auth code for tokens. Returns (refreshToken, email).</summary>
        public async Task<(string refreshToken, string? email)> ExchangeCodeAsync(
            string code, string redirectUri, CancellationToken ct = default)
        {
            var flow = CreateFlow();
            var token = await flow.ExchangeCodeForTokenAsync(
                userId: "unused",
                code: code,
                redirectUri: redirectUri,
                taskCancellationToken: ct);

            if (string.IsNullOrEmpty(token.RefreshToken))
                throw new InvalidOperationException(
                    "Google did not return a refresh_token. The user may have previously authorized without revoking — have them revoke access at myaccount.google.com and try again.");

            string? email = null;
            if (!string.IsNullOrEmpty(token.IdToken))
            {
                try
                {
                    var payload = await GoogleJsonWebSignature.ValidateAsync(token.IdToken,
                        new GoogleJsonWebSignature.ValidationSettings { Audience = new[] { clientId } });
                    email = payload.Email;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Could not validate Google id_token for email extraction.");
                }
            }

            return (token.RefreshToken, email);
        }

        /// <summary>Revokes the refresh token server-side (user stays logged in everywhere else).</summary>
        public async Task RevokeAsync(string encryptedRefreshToken)
        {
            try
            {
                var refresh = encryptor.Decrypt(encryptedRefreshToken);
                using var http = new HttpClient();
                await http.PostAsync($"https://oauth2.googleapis.com/revoke?token={Uri.EscapeDataString(refresh)}", null);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Token revoke failed (will clear linkage anyway).");
            }
        }

        public Task<Event> CreateEventAsync(string encryptedRefreshToken, string calendarId, TrackedTask task, string? projectSlug, string? timeZone, string? matchedColorId, string? unmatchedColorId, CancellationToken ct = default)
            => MutateAsync(encryptedRefreshToken, async svc =>
                await svc.Events.Insert(BuildEvent(task, projectSlug, timeZone, matchedColorId, unmatchedColorId), calendarId).ExecuteAsync(ct));

        public Task<Event> UpdateEventAsync(string encryptedRefreshToken, string calendarId, string eventId, TrackedTask task, string? projectSlug, string? timeZone, string? matchedColorId, string? unmatchedColorId, CancellationToken ct = default)
            => MutateAsync(encryptedRefreshToken, async svc =>
                await svc.Events.Update(BuildEvent(task, projectSlug, timeZone, matchedColorId, unmatchedColorId), calendarId, eventId).ExecuteAsync(ct));

        public Task DeleteEventAsync(string encryptedRefreshToken, string calendarId, string eventId, CancellationToken ct = default)
            => MutateAsync(encryptedRefreshToken, async svc =>
            {
                await svc.Events.Delete(calendarId, eventId).ExecuteAsync(ct);
                return (object?)null;
            });

        /// <summary>
        /// Creates the sanitized sister event on the workspace-shared Team Availability
        /// calendar. Title is "[&lt;DisplayName&gt;] &lt;ProjectName&gt;", color is fixed Sage,
        /// no task name / description / slug leaks through. <paramref name="displayName"/>
        /// must be pre-resolved by the caller (typically via <c>UserDisplayNameRules.Resolve</c>)
        /// so that an email never lands on the team calendar.
        /// </summary>
        public Task<Event> CreateTeamAvailabilityEventAsync(
            string encryptedRefreshToken, string calendarId, TrackedTask task,
            string displayName, string? projectName, string? timeZone,
            CancellationToken ct = default)
            => MutateAsync(encryptedRefreshToken, async svc =>
                await svc.Events.Insert(BuildTeamAvailabilityEvent(task, displayName, projectName, timeZone), calendarId).ExecuteAsync(ct));

        public Task<Event> UpdateTeamAvailabilityEventAsync(
            string encryptedRefreshToken, string calendarId, string eventId, TrackedTask task,
            string displayName, string? projectName, string? timeZone,
            CancellationToken ct = default)
            => MutateAsync(encryptedRefreshToken, async svc =>
                await svc.Events.Update(BuildTeamAvailabilityEvent(task, displayName, projectName, timeZone), calendarId, eventId).ExecuteAsync(ct));

        /// <summary>
        /// Registers a push channel on the user's primary calendar. Google caps channels at ~1 week;
        /// caller is expected to re-watch before expiration.
        /// </summary>
        public Task<Channel> StartWatchAsync(
            string encryptedRefreshToken, string calendarId,
            string channelId, string channelToken, string webhookUrl,
            CancellationToken ct = default)
            => MutateAsync(encryptedRefreshToken, async svc =>
            {
                var channel = new Channel
                {
                    Id = channelId,
                    Type = "web_hook",
                    Address = webhookUrl,
                    Token = channelToken
                };
                return await svc.Events.Watch(channel, calendarId).ExecuteAsync(ct);
            });

        public Task StopWatchAsync(
            string encryptedRefreshToken, string channelId, string resourceId,
            CancellationToken ct = default)
            => MutateAsync(encryptedRefreshToken, async svc =>
            {
                await svc.Channels.Stop(new Channel { Id = channelId, ResourceId = resourceId })
                    .ExecuteAsync(ct);
                return (object?)null;
            });

        // Bounds for the FIRST sync (no syncToken yet). With SingleEvents=true Google
        // expands every recurring rule into individual instances, so an unbounded query
        // on a user's primary calendar can return thousands of events spanning decades —
        // e.g. weekly "Home" reminders out to 2037. Each one costs a DB lookup downstream,
        // and at ~50ms per event the connect callback can take minutes and time the SPA
        // out (the original incident: OAuth code consumed by a too-long first attempt,
        // retry got invalid_grant). Bounding the initial window to a small slice around
        // "now" keeps connect responsive; subsequent syncs use the sync token and have
        // no time bound, so newly-created events come in via the webhook regardless of
        // their start date.
        private static readonly TimeSpan InitialSyncLookback = TimeSpan.FromDays(30);
        private static readonly TimeSpan InitialSyncLookahead = TimeSpan.FromDays(90);

        /// <summary>
        /// Incremental pull of changed events. On first call (no syncToken) Google returns
        /// events from a bounded window around now plus a token for next time. Subsequent
        /// calls use the token and have no time bounds. Returns (changedEvents, nextSyncToken).
        /// </summary>
        public async Task<(List<Event> events, string? nextSyncToken)> SyncEventsAsync(
            string encryptedRefreshToken, string calendarId, string? syncToken,
            CancellationToken ct = default)
        {
            var svc = await CreateServiceAsync(encryptedRefreshToken);
            var results = new List<Event>();
            string? pageToken = null;
            string? nextSyncToken = syncToken;

            do
            {
                var req = svc.Events.List(calendarId);
                req.ShowDeleted = true;
                req.SingleEvents = true;
                req.MaxResults = 250;
                if (!string.IsNullOrEmpty(syncToken) && string.IsNullOrEmpty(pageToken))
                    req.SyncToken = syncToken;
                if (!string.IsNullOrEmpty(pageToken))
                    req.PageToken = pageToken;
                else if (string.IsNullOrEmpty(syncToken))
                {
                    req.TimeMinDateTimeOffset = DateTimeOffset.UtcNow - InitialSyncLookback;
                    req.TimeMaxDateTimeOffset = DateTimeOffset.UtcNow + InitialSyncLookahead;
                }

                Events page;
                try
                {
                    page = await req.ExecuteAsync(ct);
                }
                catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.Gone)
                {
                    // 410 = sync token invalidated; caller should retry without a token.
                    return (results, null);
                }

                if (page.Items != null) results.AddRange(page.Items);
                pageToken = page.NextPageToken;
                if (!string.IsNullOrEmpty(page.NextSyncToken)) nextSyncToken = page.NextSyncToken;
            }
            while (!string.IsNullOrEmpty(pageToken));

            return (results, nextSyncToken);
        }

        /// <summary>
        /// Hard cap on the number of events <see cref="ListEventsInRangeAsync"/> will
        /// pull through pagination. On Consumption plan the Function App's HTTP timeout
        /// is 230s and a year-range pull against a weekly recurring `[ooo]` standup can
        /// trivially expand past that — we want a friendly error in the DTO instead of
        /// a hard 504.
        /// </summary>
        public const int MaxRangeListResults = 5000;

        /// <summary>
        /// Full list of events in a closed time range, no sync token. Used by the
        /// admin/self-service "pull missed events from Google" path and the nightly
        /// self-heal timer — when the webhook channel expired or a single event was
        /// missed, callers need to ask "look at this date range and ingest anything
        /// I'm missing" without resetting incremental sync state.
        ///
        /// Returns ALL events including cancelled ones; callers filter. Single-events
        /// expansion is on so recurring events appear as their individual instances —
        /// otherwise a weekly "[ooo] Lunch break" would only import once.
        ///
        /// Throws <see cref="CalendarRangeTooLargeException"/> when total expanded
        /// instances exceed <see cref="MaxRangeListResults"/>. Callers should surface
        /// this as an actionable error (narrow the range or split into smaller pulls).
        /// </summary>
        public async Task<List<Event>> ListEventsInRangeAsync(
            string encryptedRefreshToken, string calendarId,
            DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
        {
            var svc = await CreateServiceAsync(encryptedRefreshToken);
            var results = new List<Event>();
            string? pageToken = null;

            do
            {
                var req = svc.Events.List(calendarId);
                req.ShowDeleted = true;
                req.SingleEvents = true;
                req.MaxResults = 250;
                req.TimeMinDateTimeOffset = new DateTimeOffset(DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc));
                req.TimeMaxDateTimeOffset = new DateTimeOffset(DateTime.SpecifyKind(toUtc, DateTimeKind.Utc));
                req.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;
                if (!string.IsNullOrEmpty(pageToken))
                    req.PageToken = pageToken;

                var page = await req.ExecuteAsync(ct);
                if (page.Items != null) results.AddRange(page.Items);

                // Stop *before* we'd run another page that's likely to push past the cap.
                // Throwing here (rather than truncating) keeps the contract honest: the
                // caller knows the range was too large rather than silently getting a
                // partial result and concluding "nothing to import."
                if (results.Count >= MaxRangeListResults)
                    throw new CalendarRangeTooLargeException(results.Count, MaxRangeListResults);

                pageToken = page.NextPageToken;
            }
            while (!string.IsNullOrEmpty(pageToken));

            return results;
        }

        private static Event BuildEvent(TrackedTask task, string? projectSlug, string? timeZone, string? matchedColorId, string? unmatchedColorId)
        {
            // Prefix the title with the project slug in [brackets] so the event reads
            // round-trip correctly: inbound sync (or a user manually editing in Google
            // Calendar) can re-route via the same tag.
            var hasSlug = !string.IsNullOrEmpty(projectSlug);
            var summary = hasSlug ? $"[{projectSlug}] {task.Name}" : task.Name;

            var ev = new Event
            {
                Summary = summary,
                ExtendedProperties = new Event.ExtendedPropertiesData
                {
                    Private__ = new Dictionary<string, string>
                    {
                        [ExtendedPropSource] = ExtendedPropSourceValue,
                        [ExtendedPropTymeTaskId] = task.TaskId
                    }
                }
            };

            ApplyTimeBounds(ev, task, timeZone);

            // Apply the user-configured color for the event's slug state. Defaults:
            //   - Matched events: null → omit ColorId → Google uses the calendar's default color.
            //   - Unmatched events: "11" (Tomato) → visual "needs a project" flag.
            // We deliberately don't emit an empty string for ColorId — Google's Insert endpoint
            // rejects "" as an invalid color value, which previously broke backfill. The .NET
            // client only serializes set properties, so leaving the field unassigned omits it
            // from the request body; on Update, omission clears any stale color via PUT semantics.
            var chosenColor = GoogleEventColors.NormalizeOrNull(
                hasSlug ? matchedColorId : unmatchedColorId ?? GoogleEventColors.DefaultUnmatchedColorId);
            if (chosenColor != null)
                ev.ColorId = chosenColor;

            return ev;
        }

        private static Event BuildTeamAvailabilityEvent(TrackedTask task, string displayName, string? projectName, string? timeZone)
        {
            var ev = new Event
            {
                Summary = TeamAvailabilityEventRules.BuildTitle(displayName, projectName),
                ColorId = TeamAvailabilityEventRules.FixedColorId,
                ExtendedProperties = new Event.ExtendedPropertiesData
                {
                    Private__ = new Dictionary<string, string>
                    {
                        [ExtendedPropSource] = ExtendedPropSourceValue,
                        [ExtendedPropTymeTaskId] = task.TaskId
                    }
                }
            };

            ApplyTimeBounds(ev, task, timeZone);
            return ev;
        }

        /// <summary>
        /// Stamps Start/End on the event from the TrackedTask. For all-day entries Google's
        /// API expects <c>EventDateTime.Date</c> with end exclusive; for timed entries we
        /// use wall-clock + IANA tz (handled by <see cref="GoogleEventTimeRules"/>).
        /// </summary>
        private static void ApplyTimeBounds(Event ev, TrackedTask task, string? timeZone)
        {
            if (task.IsAllDay)
            {
                var (startDate, endDateExclusive) = AllDayEntryRules.FormatAllDayForGoogle(task.StartDate, task.EndDate);
                ev.Start = new EventDateTime { Date = startDate };
                ev.End = new EventDateTime { Date = endDateExclusive };
                return;
            }

            var start = task.StartDate;
            var end = task.EndDate ?? start.Add(task.Duration > TimeSpan.Zero ? task.Duration : TimeSpan.FromMinutes(15));

            var startParts = GoogleEventTimeRules.FormatForGoogle(start, timeZone);
            var endParts = GoogleEventTimeRules.FormatForGoogle(end, timeZone);

            ev.Start = new EventDateTime { DateTimeRaw = startParts.DateTimeRaw, TimeZone = startParts.TimeZone };
            ev.End = new EventDateTime { DateTimeRaw = endParts.DateTimeRaw, TimeZone = endParts.TimeZone };
        }

        private async Task<T> MutateAsync<T>(string encryptedRefreshToken, Func<CalendarService, Task<T>> work)
        {
            var svc = await CreateServiceAsync(encryptedRefreshToken);
            return await work(svc);
        }

        private async Task<CalendarService> CreateServiceAsync(string encryptedRefreshToken)
        {
            var refresh = encryptor.Decrypt(encryptedRefreshToken);
            var flow = CreateFlow();

            var token = new TokenResponse { RefreshToken = refresh };
            var credential = new UserCredential(flow, "user", token);

            // Force-refresh so we always have a valid access token
            await credential.RefreshTokenAsync(CancellationToken.None);

            return new CalendarService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "Tyme"
            });
        }

        private GoogleAuthorizationCodeFlow CreateFlow()
        {
            if (!IsConfigured)
                throw new InvalidOperationException("Google__ClientId / Google__ClientSecret not configured.");

            return new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets { ClientId = clientId, ClientSecret = clientSecret },
                Scopes = CalendarScopes
            });
        }
    }
}
