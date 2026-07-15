using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using MudBlazor;
using System.Net.Http.Json;
using My.Client.Extensions;
using My.Client.Services;
using My.Client.Theme;
using My.Shared.Constants;
using My.Shared.Dtos.GoogleCalendar;
using My.Shared.Dtos.UserSettings;
using My.Shared.Rules;

namespace My.Client.Pages.Settings
{
    public partial class Settings : IDisposable
    {
        private static readonly List<string> AllTimeZones =
            UserTimeZoneRules.GetSelectableTimeZoneIds().ToList();

        private bool isLoading = true;
        private bool isSaving;
        private bool use24HourTime;
        private string? selectedTimeZone;

        private bool isGoogleConnected;
        private string? googleEmail;
        private bool publishToGoogle;
        private bool importFromGoogle;
        private string? matchedColorId;
        private string? unmatchedColorId;
        private ProjectColorSource projectColorSource;
        private bool isGoogleBusy;
        private string? googleError;

        // "Pull missed events from Google" state — date range defaults to last 30 days.
        private DateTime? pullFromDate = DateTime.Today.AddDays(-30);
        private DateTime? pullToDate = DateTime.Today;
        private bool isPullingFromGoogle;
        private CalendarPullResultDto? pullResult;

        [CascadingParameter]
        private Task<AuthenticationState> AuthenticationStateTask { get; set; } = null!;

        [CascadingParameter(Name = "SetPageTitle")]
        private Action<string>? SetPageTitle { get; set; }

        [Inject]
        private UserSettingsService SettingsService { get; set; } = null!;

        [Inject]
        private NavigationManager Navigation { get; set; } = null!;

        [Inject]
        private IHttpClientFactory ClientFactory { get; set; } = null!;

        [Inject]
        private ISnackbar Snackbar { get; set; } = null!;

        [Inject]
        private IDialogService DialogService { get; set; } = null!;

        [Inject]
        private ThemeService Theme { get; set; } = null!;

        [Inject]
        private AppSettingsCache AppSettingsCache { get; set; } = null!;

        [Inject]
        private IJSRuntime JS { get; set; } = null!;

        protected override async Task OnInitializedAsync()
        {
            var authState = await AuthenticationStateTask;
            if (authState.User.Identity is not { IsAuthenticated: true })
            {
                Navigation.NavigateTo($"{Navigation.BaseUri}auth/login", true);
                return;
            }

            SetPageTitle?.Invoke("Settings");

            await Theme.InitAsync();
            Theme.Changed += OnThemeChanged;

            await RestoreGoogleConnectErrorFromSessionAsync();
            await HandleGoogleRedirectIfPresent();
            await LoadSettings();

            isLoading = false;
        }

        private async Task SetTheme(ThemeMode mode) => await Theme.SetAsync(mode);

        private void OnThemeChanged() => InvokeAsync(StateHasChanged);

        public void Dispose()
        {
            Theme.Changed -= OnThemeChanged;
        }

        private async Task LoadSettings()
        {
            try
            {
                SettingsService.InvalidateCache();
                var settings = await SettingsService.GetSettingsAsync();
                use24HourTime = settings.Use24HourTime;
                selectedTimeZone = settings.TimeZone;
                isGoogleConnected = settings.IsGoogleCalendarConnected;
                googleEmail = settings.GoogleCalendarEmail;
                publishToGoogle = settings.PublishToGoogleCalendar;
                importFromGoogle = settings.ImportFromGoogleCalendar;
                matchedColorId = settings.TymeEventColorId;
                unmatchedColorId = settings.TymeUnmatchedEventColorId;
                projectColorSource = settings.ProjectColorSource;
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't load your settings.");
            }
        }

        private async Task SaveSettings()
        {
            isSaving = true;
            try
            {
                await SettingsService.UpdateSettingsAsync(new UpdateUserSettingsDto
                {
                    Use24HourTime = use24HourTime,
                    TimeZone = selectedTimeZone,
                    PublishToGoogleCalendar = publishToGoogle,
                    ImportFromGoogleCalendar = importFromGoogle,
                    TymeEventColorId = matchedColorId,
                    TymeUnmatchedEventColorId = unmatchedColorId,
                    ProjectColorSource = projectColorSource
                });
                Snackbar.Add("Settings saved.", Severity.Success);
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't save settings.");
            }
            isSaving = false;
        }

        private Task<IEnumerable<string>> SearchTimeZones(string? value, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(value))
                return Task.FromResult(AllTimeZones.AsEnumerable());

            var results = AllTimeZones
                .Where(tz => tz.Contains(value, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(results);
        }

        private string BuildRedirectUri() => $"{Navigation.BaseUri.TrimEnd('/')}/settings";

        private async Task ConnectGoogle()
        {
            isGoogleBusy = true;
            googleError = null;
            try
            {
                // Delegate to the shared service implementation. This is the same flow
                // that is triggered automatically after login for new users (route 2).
                // We pass the settings page as a reasonable return target if this was a manual reconnect.
                await SettingsService.InitiateGoogleConnectAsync(Navigation.Uri);
            }
            catch (Exception ex)
            {
                googleError = ex.Message;
            }
            isGoogleBusy = false;
        }

        private async Task DisconnectGoogle()
        {
            var parameters = new DialogParameters<DisconnectGoogleDialog>();
            var dialog = await DialogService.ShowAsync<DisconnectGoogleDialog>("Disconnect Google Calendar", parameters,
                new DialogOptions { MaxWidth = MaxWidth.ExtraSmall, FullWidth = true });
            var result = await dialog.Result;
            if (result is null || result.Canceled)
                return;

            isGoogleBusy = true;
            googleError = null;
            try
            {
                var client = ClientFactory.CreateClient(Constants.API.ClientName);
                var resp = await client.PostAsync(Constants.API.GoogleCalendar.Disconnect, null);
                if (!resp.IsSuccessStatusCode)
                {
                    googleError = await resp.Content.ReadAsStringAsync();
                }
                else
                {
                    Snackbar.Add("Disconnected from Google Calendar.", Severity.Success);
                    await LoadSettings();
                }
            }
            catch (Exception ex)
            {
                googleError = ex.Message;
            }
            isGoogleBusy = false;
        }

        /// <summary>
        /// Self-service "fix-it" path: re-scan the user's primary Google calendar over a
        /// date range and import any slug-tagged events Tyme missed (typically because
        /// a webhook delivery was lost). Idempotent — already-imported events are
        /// updated in place. The endpoint defaults to the caller, so no userId param.
        /// </summary>
        private async Task PullFromGoogle()
        {
            if (pullFromDate is null || pullToDate is null) return;
            if (pullToDate.Value.Date < pullFromDate.Value.Date)
            {
                Snackbar.Add("'To' must be on or after 'From'.", Severity.Warning);
                return;
            }

            isPullingFromGoogle = true;
            pullResult = null;
            StateHasChanged();

            try
            {
                var client = ClientFactory.CreateClient(Constants.API.ClientName);
                var url = Constants.API.GoogleCalendar.ConstructPullFromGoogle(pullFromDate.Value, pullToDate.Value);
                var resp = await client.PostAsync(url, null);
                if (resp.IsSuccessStatusCode)
                {
                    pullResult = await resp.Content.ReadFromJsonAsync<CalendarPullResultDto>();
                }
                else
                {
                    pullResult = new CalendarPullResultDto
                    {
                        Error = await resp.Content.ReadAsStringAsync()
                    };
                }
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't pull missed events.");
            }
            isPullingFromGoogle = false;
        }

        private async Task HandleGoogleRedirectIfPresent()
        {
            var code = GetQueryParam("code");
            if (string.IsNullOrEmpty(code)) return;

            bool navigateToSettings = true;

            try
            {
                var client = ClientFactory.CreateClient(Constants.API.ClientName);
                var resp = await client.PostAsJsonAsync(Constants.API.GoogleCalendar.Callback, new
                {
                    code,
                    redirectUri = BuildRedirectUri()
                });

                if (resp.IsSuccessStatusCode)
                {
                    Snackbar.Add("Google Calendar connected.", Severity.Success);
                    await RunBackfillIfConfiguredAsync(client);

                    // Route 2: if we were auto-triggered from dashboard/editor/etc., return the user
                    // to their original destination instead of stranding them on the Settings page.
                    try
                    {
                        var returnUrl = await JS.InvokeAsync<string?>("localStorage.getItem", "postGoogleConnectReturnUrl");
                        if (!string.IsNullOrWhiteSpace(returnUrl))
                        {
                            await JS.InvokeVoidAsync("localStorage.removeItem", "postGoogleConnectReturnUrl");
                            navigateToSettings = false;
                            // Use replace so the browser history is clean.
                            Navigation.NavigateTo(returnUrl, replace: true);
                        }
                    }
                    catch { /* ignore, fall through to normal settings landing */ }
                }
                else
                {
                    googleError = await resp.Content.ReadAsStringAsync();
                }
            }
            catch (Exception ex)
            {
                googleError = ex.Message;
            }
            finally
            {
                if (navigateToSettings)
                {
                    // Strip OAuth params so a refresh doesn't re-submit them. Persist any
                    // error first — this navigation recreates the component and would
                    // otherwise drop googleError before the user sees it.
                    if (!string.IsNullOrEmpty(googleError))
                    {
                        try
                        {
                            await JS.InvokeVoidAsync("sessionStorage.setItem", "googleConnectError", googleError);
                        }
                        catch { /* non-fatal */ }
                    }

                    Navigation.NavigateTo($"{Navigation.BaseUri.TrimEnd('/')}/settings", replace: true);
                }
            }
        }

        private async Task RestoreGoogleConnectErrorFromSessionAsync()
        {
            try
            {
                var stored = await JS.InvokeAsync<string?>("sessionStorage.getItem", "googleConnectError");
                if (!string.IsNullOrWhiteSpace(stored))
                {
                    googleError = stored;
                    await JS.InvokeVoidAsync("sessionStorage.removeItem", "googleConnectError");
                }
            }
            catch { /* JS interop may not be ready during prerender */ }
        }

        /// <summary>
        /// Reads the workspace AppSettings (TymeCalendarBackfillDefaultDays + …PromptUser).
        /// If prompt is enabled, asks the user for a date range; if disabled, runs the
        /// backfill silently with the default lookback. A 0-day default skips backfill
        /// entirely. Failures are surfaced as a snackbar but never block the connect.
        /// </summary>
        private async Task RunBackfillIfConfiguredAsync(HttpClient client)
        {
            SettingsService.InvalidateCache();
            var userSettings = await SettingsService.GetSettingsAsync();
            if (userSettings.CalendarBackfillPromptAcknowledged)
                return;

            int defaultDays = 30;
            bool promptUser = true;
            try
            {
                var settings = await AppSettingsCache.GetAsync();
                var daysSetting = settings.FirstOrDefault(s => s.Key == Constants.SettingKeys.TymeCalendarBackfillDefaultDays);
                if (daysSetting != null && int.TryParse(daysSetting.Value, out var d) && d >= 0) defaultDays = d;

                var promptSetting = settings.FirstOrDefault(s => s.Key == Constants.SettingKeys.TymeCalendarBackfillPromptUser);
                if (promptSetting != null && bool.TryParse(promptSetting.Value, out var p)) promptUser = p;
            }
            catch
            {
                // If AppSettings fail to load, fall through to defaults — backfill should
                // be a courtesy, never a connect blocker.
            }

            if (defaultDays <= 0 && !promptUser) return;

            DateTime fromDate;
            DateTime toDate;
            if (promptUser)
            {
                var parameters = new DialogParameters<CalendarBackfillDialog>
                {
                    { x => x.DefaultDays, defaultDays }
                };
                var dialog = await DialogService.ShowAsync<CalendarBackfillDialog>("Sync your tasks?", parameters,
                    new DialogOptions { MaxWidth = MaxWidth.ExtraSmall, FullWidth = true });
                var result = await dialog.Result;
                if (result is null || result.Canceled)
                    return;

                if (result.Data is not CalendarBackfillDialog.BackfillRange range)
                {
                    await AcknowledgeBackfillPromptAsync(client);
                    return;
                }

                fromDate = range.From;
                toDate = range.To;
            }
            else
            {
                fromDate = DateTime.Today.AddDays(-defaultDays);
                toDate = DateTime.Today;
            }

            var snackbarKey = Snackbar.Add("Pushing your tracked tasks to your Google calendar…", Severity.Info,
                cfg => { cfg.RequireInteraction = true; cfg.HideTransitionDuration = 0; });
            try
            {
                var url = $"{Constants.API.GoogleCalendar.Backfill}?from={fromDate:yyyy-MM-dd}&to={toDate:yyyy-MM-dd}";
                var resp = await client.PostAsync(url, null);
                if (snackbarKey != null) Snackbar.Remove(snackbarKey);
                if (resp.IsSuccessStatusCode)
                {
                    var summary = await resp.Content.ReadFromJsonAsync<CalendarBackfillResultDto>();
                    if (summary != null)
                    {
                        var msg = summary.Failed > 0
                            ? $"Synced {summary.Created} tasks (skipped {summary.Skipped}, {summary.Failed} failed)."
                            : $"Synced {summary.Created} tasks (skipped {summary.Skipped}).";
                        Snackbar.Add(msg, summary.Failed > 0 ? Severity.Warning : Severity.Success);
                    }

                    await AcknowledgeBackfillPromptAsync(client);
                }
                else
                {
                    Snackbar.Add("Calendar backfill failed: " + await resp.Content.ReadAsStringAsync(), Severity.Warning);
                }
            }
            catch (Exception ex)
            {
                if (snackbarKey != null) Snackbar.Remove(snackbarKey);
                Snackbar.AddApiError(ex, "Calendar backfill failed.");
            }
        }

        private async Task AcknowledgeBackfillPromptAsync(HttpClient client)
        {
            try
            {
                var resp = await client.PostAsync(Constants.API.GoogleCalendar.AcknowledgeBackfillPrompt, null);
                resp.EnsureSuccessStatusCode();
                SettingsService.InvalidateCache();
            }
            catch
            {
                // Non-fatal — worst case the user sees the prompt again on a future reconnect.
            }
        }

        private string? GetQueryParam(string key)
        {
            var uri = Navigation.ToAbsoluteUri(Navigation.Uri);
            if (string.IsNullOrEmpty(uri.Query)) return null;
            foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var idx = pair.IndexOf('=');
                var k = idx > 0 ? pair.Substring(0, idx) : pair;
                if (string.Equals(k, key, StringComparison.Ordinal))
                    return idx > 0 ? Uri.UnescapeDataString(pair.Substring(idx + 1)) : string.Empty;
            }
            return null;
        }

        private class AuthUrlResponse
        {
            public string? Url { get; set; }
        }

        private static string ProjectColorSourceCaption(ProjectColorSource source) => source switch
        {
            ProjectColorSource.GroupThenOrganization =>
                "Use the project group's color. Falls back to the organization's color when the project has no group.",
            ProjectColorSource.Organization =>
                "Always use the organization's color, regardless of project group.",
            ProjectColorSource.ProjectGroup =>
                "Always use the project group's color. Ungrouped projects get no color.",
            ProjectColorSource.None =>
                "Hide project color indicators everywhere.",
            _ => string.Empty,
        };
    }
}
