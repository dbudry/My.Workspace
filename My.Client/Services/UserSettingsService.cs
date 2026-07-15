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

        public string? TimeZone => _cachedSettings?.TimeZone;

        public bool IsGoogleCalendarConnected => _cachedSettings?.IsGoogleCalendarConnected ?? false;

        public string? GoogleCalendarEmail => _cachedSettings?.GoogleCalendarEmail;

        public bool PublishToGoogleCalendar => _cachedSettings?.PublishToGoogleCalendar ?? false;

        public bool ImportFromGoogleCalendar => _cachedSettings?.ImportFromGoogleCalendar ?? false;

        public ProjectColorSource ProjectColorSource => _cachedSettings?.ProjectColorSource ?? ProjectColorSource.GroupThenOrganization;

        public List<string> FavoriteIntranetPageIds => _cachedSettings?.FavoriteIntranetPageIds ?? new List<string>();

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
        /// Converts a UTC DateTime to the user's configured timezone.
        /// </summary>
        public DateTime ConvertToUserTime(DateTime utcDateTime)
        {
            return TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc),
                GetTimeZoneInfo());
        }

        /// <summary>
        /// Gets today's date in the user's configured timezone.
        /// </summary>
        public DateOnly GetUserToday()
        {
            var userNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, GetTimeZoneInfo());
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
                    TimeZone = browserTz.Trim(),
                    PublishToGoogleCalendar = _cachedSettings.PublishToGoogleCalendar,
                    ImportFromGoogleCalendar = _cachedSettings.ImportFromGoogleCalendar,
                    TymeEventColorId = _cachedSettings.TymeEventColorId,
                    TymeUnmatchedEventColorId = _cachedSettings.TymeUnmatchedEventColorId,
                    ProjectColorSource = _cachedSettings.ProjectColorSource
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
                TimeZone = current.TimeZone,
                PublishToGoogleCalendar = current.PublishToGoogleCalendar,
                ImportFromGoogleCalendar = current.ImportFromGoogleCalendar,
                TymeEventColorId = current.TymeEventColorId,
                TymeUnmatchedEventColorId = current.TymeUnmatchedEventColorId,
                ProjectColorSource = current.ProjectColorSource,
                FavoriteIntranetPageIds = list
            };

            await UpdateSettingsAsync(dto);
        }

        /// <summary>
        /// Starts the Google Calendar + Drive connect flow (the same one used from Settings).
        /// This is the mechanism that makes the integration "automatic" after OIDC login for route 2.
        /// Optionally stores a return URL so that after the consent callback + backfill we can
        /// send the user back to where they were (e.g. dashboard or editor) instead of leaving them on /settings.
        /// </summary>
        public async Task InitiateGoogleConnectAsync(string? returnUrlAfterConnect = null)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(returnUrlAfterConnect))
                {
                    await _js.InvokeVoidAsync("localStorage.setItem", "postGoogleConnectReturnUrl", returnUrlAfterConnect);
                }

                var client = _clientFactory.CreateClient(Constants.API.ClientName);
                var settingsRedirect = $"{_navigation.BaseUri.TrimEnd('/')}/settings";
                var resp = await client.GetFromJsonAsync<AuthUrlResponse>(
                    $"{Constants.API.GoogleCalendar.GetAuthUrl}?redirectUri={Uri.EscapeDataString(settingsRedirect)}");

                if (resp != null && !string.IsNullOrWhiteSpace(resp.Url))
                {
                    _navigation.NavigateTo(resp.Url, forceLoad: true);
                }
            }
            catch (Exception)
            {
                // Let the caller surface a message if desired. Silent failure here is acceptable
                // because the user can still go to Settings manually.
            }
        }

        private class AuthUrlResponse
        {
            public string? Url { get; set; }
        }
    }
}
