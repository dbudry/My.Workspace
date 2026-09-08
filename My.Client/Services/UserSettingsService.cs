using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using My.Shared.Constants;
using My.Shared.Dtos.GoogleCalendar;
using My.Shared.Dtos.UserSettings;
using My.Shared.Rules;

namespace My.Client.Services
{
    public class UserSettingsService
    {
        private readonly IHttpClientFactory _clientFactory;
        private readonly IJSRuntime _js;
        private readonly NavigationManager _navigation;
        private UserSettingsDto? _cachedSettings;
        private bool _autoDetectInProgress;

        public event Action? OnSettingsChanged;

        public UserSettingsService(IHttpClientFactory clientFactory, IJSRuntime js, NavigationManager navigation)
        {
            _clientFactory = clientFactory;
            _js = js;
            _navigation = navigation;
        }

        public bool Use24HourTime => _cachedSettings?.Use24HourTime ?? false;

        /// <summary>
        /// Wall-clock default start for new timed Tyme entries (user timezone). Defaults to 08:00.
        /// </summary>
        public TimeSpan DefaultStartTimeOfDay =>
            DefaultStartTimeRules.Resolve(_cachedSettings?.DefaultStartTimeMinutes);

        public string? TimeZone => _cachedSettings?.TimeZone;

        public bool IsGoogleCalendarConnected => _cachedSettings?.IsGoogleCalendarConnected ?? false;

        public bool IsGoogleDriveConnected => _cachedSettings?.IsGoogleDriveConnected ?? false;

        /// <summary>
        /// True after Settings → Disconnect. Sign-in must not start Calendar OAuth.
        /// </summary>
        public bool GoogleCalendarAutoConnectOptOut =>
            _cachedSettings?.GoogleCalendarAutoConnectOptOut ?? false;

        public string? GoogleCalendarEmail => _cachedSettings?.GoogleCalendarEmail;

        public bool PublishToGoogleCalendar => _cachedSettings?.PublishToGoogleCalendar ?? false;

        public bool ImportFromGoogleCalendar => _cachedSettings?.ImportFromGoogleCalendar ?? false;

        public ProjectColorSource ProjectColorSource => _cachedSettings?.ProjectColorSource ?? ProjectColorSource.GroupThenOrganization;

        public List<string> FavoriteIntranetPageIds => _cachedSettings?.FavoriteIntranetPageIds ?? new List<string>();

        public bool GoogleCalendarRemoveExcludedEvents => _cachedSettings?.GoogleCalendarRemoveExcludedEvents ?? false;

        public async Task<UserSettingsDto> GetSettingsAsync()
        {
            if (_cachedSettings == null)
            {
                var client = _clientFactory.CreateClient(Constants.API.ClientName);
                _cachedSettings = await client.GetFromJsonAsync<UserSettingsDto>(Constants.API.UserSettings.Get)
                    ?? new UserSettingsDto();
            }

            if (string.IsNullOrEmpty(_cachedSettings.TimeZone))
                await TryAutoDetectTimeZoneAsync();

            return _cachedSettings;
        }

        public async Task UpdateSettingsAsync(UpdateUserSettingsDto dto)
        {
            var client = _clientFactory.CreateClient(Constants.API.ClientName);
            var response = await client.PutAsJsonAsync(Constants.API.UserSettings.Update, dto);
            response.EnsureSuccessStatusCode();

            _cachedSettings = await response.Content.ReadFromJsonAsync<UserSettingsDto>();
            OnSettingsChanged?.Invoke();
        }

        /// <summary>
        /// Updates only project color source (org / group / etc.), preserving other settings.
        /// Used from Tasks toolbar so users can switch the color bar without opening Settings.
        /// </summary>
        public async Task UpdateProjectColorSourceAsync(ProjectColorSource source)
        {
            var current = await GetSettingsAsync();
            await UpdateSettingsAsync(new UpdateUserSettingsDto
            {
                Use24HourTime = current.Use24HourTime,
                DefaultStartTimeMinutes = DefaultStartTimeRules.ClampMinutes(current.DefaultStartTimeMinutes),
                TimeZone = current.TimeZone,
                PublishToGoogleCalendar = current.PublishToGoogleCalendar,
                ImportFromGoogleCalendar = current.ImportFromGoogleCalendar,
                GoogleCalendarAvailabilityOnly = current.GoogleCalendarAvailabilityOnly,
                GoogleCalendarRemoveExcludedEvents = current.GoogleCalendarRemoveExcludedEvents,
                TymeEventColorId = current.TymeEventColorId,
                TymeUnmatchedEventColorId = current.TymeUnmatchedEventColorId,
                ProjectColorSource = source,
                FavoriteIntranetPageIds = current.FavoriteIntranetPageIds ?? new List<string>()
            });
        }

        /// <summary>
        /// Updates only the two Google Calendar sync toggles, preserving other settings.
        /// Used by the post-connect backfill dialog so a first-time user can turn off
        /// PublishToGoogleCalendar / ImportFromGoogleCalendar (both default true) before
        /// sync ever starts, instead of discovering them later in Settings.
        /// </summary>
        public async Task UpdateGoogleCalendarSyncPreferencesAsync(
            bool publishToGoogleCalendar, bool importFromGoogleCalendar, bool availabilityOnly = false)
        {
            var current = await GetSettingsAsync();
            await UpdateSettingsAsync(new UpdateUserSettingsDto
            {
                Use24HourTime = current.Use24HourTime,
                DefaultStartTimeMinutes = DefaultStartTimeRules.ClampMinutes(current.DefaultStartTimeMinutes),
                TimeZone = current.TimeZone,
                PublishToGoogleCalendar = publishToGoogleCalendar,
                ImportFromGoogleCalendar = importFromGoogleCalendar,
                GoogleCalendarAvailabilityOnly = availabilityOnly,
                GoogleCalendarRemoveExcludedEvents = current.GoogleCalendarRemoveExcludedEvents,
                TymeEventColorId = current.TymeEventColorId,
                TymeUnmatchedEventColorId = current.TymeUnmatchedEventColorId,
                ProjectColorSource = current.ProjectColorSource,
                FavoriteIntranetPageIds = current.FavoriteIntranetPageIds ?? new List<string>()
            });
        }

        /// <summary>Forces the next GetSettingsAsync to fetch from the server.</summary>
        public void InvalidateCache()
        {
            _cachedSettings = null;
        }

        /// <summary>
        /// Returns the user's configured timezone as a TimeZoneInfo, falling back to UTC.
        /// </summary>
        public TimeZoneInfo GetTimeZoneInfo() =>
            UserTimeZoneRules.Resolve(_cachedSettings?.TimeZone);

        /// <summary>
        /// Converts a UTC DateTime (DB/API) to the user's configured timezone wall clock.
        /// </summary>
        public DateTime ConvertToUserTime(DateTime utcDateTime) =>
            Helpers.DateTimeWire.ToUserTime(utcDateTime, GetTimeZoneInfo());

        /// <summary>
        /// Converts a wall-clock DateTime in the user's configured timezone to UTC for the API.
        /// </summary>
        public DateTime ConvertFromUserTime(DateTime userWallClock) =>
            Helpers.DateTimeWire.ToUtc(userWallClock, GetTimeZoneInfo());

        /// <summary>
        /// Gets today's date in the user's configured timezone.
        /// </summary>
        public DateOnly GetUserToday()
        {
            var userNow = ConvertToUserTime(DateTime.UtcNow);
            return DateOnly.FromDateTime(userNow);
        }

        public string FormatTime(DateTime dateTime)
        {
            return Use24HourTime
                ? dateTime.ToString("HH:mm")
                : dateTime.ToString("h:mm tt");
        }

        public string FormatDateTime(DateTime dateTime)
        {
            return Use24HourTime
                ? dateTime.ToString("MM/dd/yyyy HH:mm")
                : dateTime.ToString("MM/dd/yyyy h:mm tt");
        }

        private async Task TryAutoDetectTimeZoneAsync()
        {
            if (_autoDetectInProgress || _cachedSettings == null)
                return;

            _autoDetectInProgress = true;
            try
            {
                var browserTz = await _js.InvokeAsync<string>("getBrowserTimeZone");

                if (string.IsNullOrWhiteSpace(browserTz))
                    return;

                // Echo every field back — the update endpoint replaces the row wholesale,
                // so omitting a field here would silently reset it to its DTO default.
                await UpdateSettingsAsync(new UpdateUserSettingsDto
                {
                    Use24HourTime = _cachedSettings.Use24HourTime,
                    DefaultStartTimeMinutes = DefaultStartTimeRules.ClampMinutes(
                        _cachedSettings.DefaultStartTimeMinutes),
                    TimeZone = browserTz.Trim(),
                    PublishToGoogleCalendar = _cachedSettings.PublishToGoogleCalendar,
                    ImportFromGoogleCalendar = _cachedSettings.ImportFromGoogleCalendar,
                    GoogleCalendarAvailabilityOnly = _cachedSettings.GoogleCalendarAvailabilityOnly,
                    GoogleCalendarRemoveExcludedEvents = _cachedSettings.GoogleCalendarRemoveExcludedEvents,
                    TymeEventColorId = _cachedSettings.TymeEventColorId,
                    TymeUnmatchedEventColorId = _cachedSettings.TymeUnmatchedEventColorId,
                    ProjectColorSource = _cachedSettings.ProjectColorSource,
                    FavoriteIntranetPageIds = _cachedSettings.FavoriteIntranetPageIds ?? new List<string>()
                });
            }
            catch
            {
                // JS interop may not be available yet (e.g. during prerendering)
            }
            finally
            {
                _autoDetectInProgress = false;
            }
        }

        /// <summary>
        /// Toggles the given Intranet page in/out of the current user's favorites.
        /// The list is stored in UserSettings and will appear on the dashboard for
        /// any user with Intranet scope.
        /// </summary>
        public async Task ToggleIntranetFavoriteAsync(string pageId)
        {
            if (string.IsNullOrWhiteSpace(pageId)) return;

            var current = await GetSettingsAsync();
            var list = (current.FavoriteIntranetPageIds ?? new List<string>()).ToList();

            if (list.Contains(pageId))
                list.Remove(pageId);
            else
                list.Add(pageId);

            var dto = new UpdateUserSettingsDto
            {
                Use24HourTime = current.Use24HourTime,
                DefaultStartTimeMinutes = DefaultStartTimeRules.ClampMinutes(current.DefaultStartTimeMinutes),
                TimeZone = current.TimeZone,
                PublishToGoogleCalendar = current.PublishToGoogleCalendar,
                ImportFromGoogleCalendar = current.ImportFromGoogleCalendar,
                GoogleCalendarAvailabilityOnly = current.GoogleCalendarAvailabilityOnly,
                GoogleCalendarRemoveExcludedEvents = current.GoogleCalendarRemoveExcludedEvents,
                TymeEventColorId = current.TymeEventColorId,
                TymeUnmatchedEventColorId = current.TymeUnmatchedEventColorId,
                ProjectColorSource = current.ProjectColorSource,
                FavoriteIntranetPageIds = list
            };

            await UpdateSettingsAsync(dto);
        }

        /// <summary>
        /// Browser flag that blocks auto Google connect on next dashboard load.
        /// Set to "connected" after a live link, "disconnected" after an explicit
        /// disconnect (so Index does not immediately start OAuth again), or a timestamp
        /// after a cancelled first-time prompt. Cleared on logout.
        /// </summary>
        public const string GoogleAutoConnectAttemptedKey = "googleAutoConnectAttempted";
        public const string GoogleAutoConnectDisconnectedValue = "disconnected";

        /// <summary>
        /// Starts Calendar-only Google consent (Settings and post-login auto-connect).
        /// Drive is requested separately from Intranet via <see cref="InitiateGoogleDriveConnectAsync"/>.
        /// </summary>
        public Task InitiateGoogleConnectAsync(string? returnUrlAfterConnect = null) =>
            InitiateGoogleOAuthAsync(
                Constants.API.GoogleCalendar.GetAuthUrl,
                GoogleOAuthConnectKindRules.Calendar,
                "Couldn't start Google Calendar connect",
                "Server did not return a Google Calendar sign-in URL.",
                returnUrlAfterConnect);

        /// <summary>
        /// Starts Drive-only Google consent for Intranet browse/attach. Incremental on
        /// an existing Calendar grant so Calendar is not dropped.
        /// </summary>
        public Task InitiateGoogleDriveConnectAsync(string? returnUrlAfterConnect = null) =>
            InitiateGoogleOAuthAsync(
                Constants.API.GoogleDrive.GetAuthUrl,
                GoogleOAuthConnectKindRules.Drive,
                "Couldn't start Google Drive connect",
                "Server did not return a Google Drive sign-in URL.",
                returnUrlAfterConnect);

        private async Task InitiateGoogleOAuthAsync(
            string authUrlRoute,
            string connectKind,
            string startFailedPrefix,
            string missingUrlMessage,
            string? returnUrlAfterConnect)
        {
            await _js.InvokeVoidAsync("localStorage.setItem", GoogleOAuthConnectKindRules.LocalStorageKey, connectKind);
            if (!string.IsNullOrWhiteSpace(returnUrlAfterConnect))
            {
                await _js.InvokeVoidAsync("localStorage.setItem", "postGoogleConnectReturnUrl", returnUrlAfterConnect);
            }

            var client = _clientFactory.CreateClient(Constants.API.ClientName);
            var settingsRedirect = $"{_navigation.BaseUri.TrimEnd('/')}/settings";
            var url = $"{authUrlRoute}?redirectUri={Uri.EscapeDataString(settingsRedirect)}";

            HttpResponseMessage response;
            try
            {
                response = await client.GetAsync(url);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"{startFailedPrefix}. Try again in a moment.", ex);
            }

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                var detail = string.IsNullOrWhiteSpace(body) ? response.ReasonPhrase : body.Trim();
                throw new InvalidOperationException(
                    $"{startFailedPrefix} ({(int)response.StatusCode}). {detail}".Trim());
            }

            AuthUrlResponse? resp;
            try
            {
                resp = await response.Content.ReadFromJsonAsync<AuthUrlResponse>();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Server returned an unexpected response when starting Google connect.", ex);
            }

            if (resp == null || string.IsNullOrWhiteSpace(resp.Url))
                throw new InvalidOperationException(missingUrlMessage);

            _navigation.NavigateTo(resp.Url, forceLoad: true);
        }

        /// <summary>
        /// After an explicit Disconnect, keep a flag so the dashboard does not
        /// auto-start Google OAuth on the next load (that reconnect + cancelled
        /// Google copies was deleting Tyme rows).
        /// </summary>
        public async Task MarkGoogleCalendarDisconnectedAsync()
        {
            try
            {
                await _js.InvokeVoidAsync("localStorage.setItem",
                    GoogleAutoConnectAttemptedKey, GoogleAutoConnectDisconnectedValue);
            }
            catch
            {
                // Best-effort; Settings connect still works.
            }
        }

        /// <summary>
        /// Clears the browser flag set after a connect attempt or successful connection so
        /// a first-time auto-connect (or a failed attempt) can run again.
        /// </summary>
        public async Task ClearGoogleAutoConnectAttemptedAsync()
        {
            try
            {
                await _js.InvokeVoidAsync("localStorage.removeItem", GoogleAutoConnectAttemptedKey);
            }
            catch
            {
                // JS unavailable (tests / prerender) — ignore.
            }
        }

        private class AuthUrlResponse
        {
            public string? Url { get; set; }
        }
    }
}
