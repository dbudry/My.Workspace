using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;
using System.Net.Http.Json;
using My.Client.Components.Users;
using My.Client.Extensions;
using My.Client.Models;
using My.Client.Services;
using My.Shared.Constants;
using My.Shared.Dtos;
using My.Shared.Dtos.GoogleCalendar;
using My.Shared.Dtos.User;

namespace My.Client.Pages.Admin
{
    public partial class UserManager : IDisposable
    {
        private List<UserModel> usersList = new();
        private string searchString = string.Empty;
        private bool isLoading = true;
        private bool showArchived = false;
        private bool showInactive = false;
        private bool allowUserDelete = false;
        private int dataRetentionDays = 2555;
        private Dictionary<string, UserDataInfo> userDataInfoMap = new();
        private HttpClient client = null!;
        private bool isGlobalAdmin = false;
        private string? currentUserEmail;

        private class UserDataInfo
        {
            public int TaskCount { get; set; }
            public DateTime? MostRecentDate { get; set; }
        }

        [CascadingParameter]
        private Task<AuthenticationState> AuthenticationStateTask { get; set; } = null!;

        [CascadingParameter(Name = "SetPageTitle")]
        private Action<string>? SetPageTitle { get; set; }

        [Inject]
        protected NavigationManager Navigation { get; set; } = null!;

        [Inject]
        private IHttpClientFactory ClientFactory { get; set; } = null!;

        [Inject]
        private ISnackbar Snackbar { get; set; } = null!;

        [Inject]
        private IDialogService DialogService { get; set; } = null!;

        [Inject]
        private UserSettingsService SettingsService { get; set; } = null!;

        [Inject]
        private AppSettingsCache AppSettingsCache { get; set; } = null!;

        [Inject]
        private IUserRoleRefreshService RoleRefresh { get; set; } = null!;

        [Inject]
        private ImpersonationAuthStateProvider AuthStateProvider { get; set; } = null!;

        private IReadOnlyList<string> AvailableRoles { get; set; } = Constants.Roles.Assignable();

        private IEnumerable<UserModel> FilteredUsers
        {
            get
            {
                var visible = usersList.Where(u => u.IsArchived || u.IsActive || showInactive);
                return string.IsNullOrWhiteSpace(searchString)
                    ? visible
                    : visible.Where(MatchesSearch);
            }
        }

        private bool MatchesSearch(UserModel user)
        {
            var query = searchString.Trim();
            return (user.FirstName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
                || (user.LastName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
                || (user.Email?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
                || user.Roles.Any(r => r.Contains(query, StringComparison.OrdinalIgnoreCase))
                || GetStatusLabel(user).Contains(query, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetStatusLabel(UserModel user)
        {
            if (user.IsArchived) return "Archived";
            if (!user.IsActive) return "Inactive";
            return "Active";
        }

        private static string GetStatusSortKey(UserModel user) =>
            user.IsArchived ? "2" : !user.IsActive ? "1" : "0";

        private static string GetRolesSortKey(UserModel user) =>
            string.Join(", ", user.Roles.OrderBy(r => r, StringComparer.OrdinalIgnoreCase));

        private string GetEmptyUsersMessage()
        {
            if (!string.IsNullOrWhiteSpace(searchString))
                return "No users match your search.";
            if (showArchived)
                return "No archived users match.";
            if (showInactive)
                return "No users match.";
            return "Add a user to get started.";
        }

        private Task OnShowInactiveChanged(bool value)
        {
            showInactive = value;
            return InvokeAsync(StateHasChanged);
        }

        protected override async Task OnInitializedAsync()
        {
            var authState = await AuthenticationStateTask;
            var user = authState.User;

            if (user.Identity != null && !user.Identity.IsAuthenticated)
                Navigation.NavigateTo($"{Navigation.BaseUri}auth/login", true);

            client = ClientFactory.CreateClient(Constants.API.ClientName);

            SetPageTitle?.Invoke("Manage Users");

            AvailableRoles = Constants.Roles.AssignableFor(user);
            isGlobalAdmin = Constants.Roles.IsGlobalAdmin(user);
            currentUserEmail = user.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                ?? user.Identity?.Name;

            AppSettingsCache.Changed += OnAppSettingsChanged;

            await SettingsService.GetSettingsAsync();
            await LoadAppSettings();
            await LoadUsers();
        }

        public void Dispose() => AppSettingsCache.Changed -= OnAppSettingsChanged;

        private async void OnAppSettingsChanged()
        {
            await InvokeAsync(async () =>
            {
                await LoadAppSettings();
                await LoadUsers();
            });
        }

        private async Task LoadAppSettings()
        {
            try
            {
                var settings = await AppSettingsCache.GetAsync();
                allowUserDelete = false;
                var deleteVal = settings.FirstOrDefault(s => s.Key == Constants.SettingKeys.AllowUserDelete);
                if (deleteVal != null && bool.TryParse(deleteVal.Value, out var parsed))
                    allowUserDelete = parsed;

                var retentionVal = settings.FirstOrDefault(s => s.Key == Constants.SettingKeys.DataRetentionDays);
                if (retentionVal != null && int.TryParse(retentionVal.Value, out var days))
                    dataRetentionDays = days;
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't load app settings.");
            }
        }

        private async Task LoadUsers()
        {
            isLoading = true;
            try
            {
                var url = showArchived
                    ? $"{Constants.API.User.Get}?includeArchived=true"
                    : Constants.API.User.Get;
                var response = await client.GetFromJsonAsync<UserDto[]>(url);
                if (response != null)
                {
                    usersList = response.Select(u => new UserModel(u)).ToList();

                    if (allowUserDelete && isGlobalAdmin)
                        await LoadUserDataInfo();
                }
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't load users.");
            }

            isLoading = false;
            await InvokeAsync(StateHasChanged);
        }

        private async Task LoadUserDataInfo()
        {
            userDataInfoMap.Clear();
            foreach (var user in usersList)
            {
                try
                {
                    var info = await client.GetFromJsonAsync<UserDataInfo>(Constants.API.User.ActionPath(user.Id, "datainfo"));
                    if (info != null)
                        userDataInfoMap[user.Id] = info;
                }
                catch { /* non-critical */ }
            }
        }

        private async Task HandleUserAdded(UserModel user)
        {
            await LoadUsers();
        }

        private async Task OnShowArchivedChanged(bool value)
        {
            showArchived = value;
            await LoadUsers();
        }

        /// <summary>
        /// Opens the edit dialog for a single user. Inline editing didn't have room for
        /// the multi-select Roles picker or the full email/name fields, and pre-validation
        /// errors trapped the user inside the row. The dialog gives every field room and
        /// runs its own validation before saving.
        /// </summary>
        private async Task OpenEditDialog(UserModel user)
        {
            var editedUserId = user.Id;
            var editedEmail = user.Email;
            var parameters = new DialogParameters<EditUserDialog>
            {
                { x => x.User, user },
                { x => x.AvailableRoles, AvailableRoles }
            };
            var dialog = await DialogService.ShowAsync<EditUserDialog>(
                $"Edit {user.FirstName} {user.LastName}",
                parameters,
                new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true });
            var result = await dialog.Result;
            if (result is { Canceled: false })
            {
                await LoadUsers();

                // Self-edit: bust client-side role caches (provision task, OIDC 60s principal
                // cache) so nav/dashboard/ScopedAuthorizeView see the new roles immediately.
                // Use the real principal — not the impersonation-filtered one.
                var currentUser = await AuthStateProvider.GetRealUserAsync();
                if (IsCurrentUser(currentUser, editedUserId, editedEmail))
                {
                    // In-session refresh updates nav/dashboard without a full reload. Only
                    // forceLoad as a fallback when the OIDC cache cannot be bust in-process
                    // (framework private fields changed) — never both, which double-flashed.
                    var refreshed = await RoleRefresh.RefreshAfterSelfRoleChangeAsync();
                    if (!refreshed)
                        Navigation.NavigateTo(Navigation.Uri, forceLoad: true);
                }
            }
        }

        private static bool IsCurrentUser(
            System.Security.Claims.ClaimsPrincipal current,
            string editedUserId,
            string? editedEmail)
        {
            var appUserId = current.FindFirst(Constants.Claims.AppUserId)?.Value;
            if (!string.IsNullOrWhiteSpace(appUserId)
                && !string.IsNullOrWhiteSpace(editedUserId)
                && string.Equals(appUserId, editedUserId, StringComparison.Ordinal))
                return true;

            var currentEmail = current.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                ?? current.Identity?.Name;
            return !string.IsNullOrWhiteSpace(currentEmail)
                && !string.IsNullOrWhiteSpace(editedEmail)
                && string.Equals(currentEmail, editedEmail, StringComparison.OrdinalIgnoreCase);
        }

        private static string FormatApiError(HttpResponseMessage response, string body, string fallback) =>
            string.IsNullOrWhiteSpace(body) ? $"{fallback} (HTTP {(int)response.StatusCode})." : body;

        private async Task ToggleActive(UserModel user)
        {
            var newState = user.IsActive ? "inactive" : "active";
            var message = user.IsActive
                ? $"Are you sure you want to set \"{user.FirstName} {user.LastName}\" as inactive? They will no longer be able to log in."
                : $"Are you sure you want to reactivate \"{user.FirstName} {user.LastName}\"?";

            var result = await DialogService.ShowMessageBoxAsync(
                $"Set {newState}?",
                message,
                yesText: "Yes", cancelText: "Cancel");

            if (result != true) return;

            try
            {
                var response = await client.PostAsync(Constants.API.User.ActionPath(user.Id, "setactive"), null);
                if (response.IsSuccessStatusCode)
                {
                    await LoadUsers();
                    Snackbar.Add($"User is now {newState}.", Severity.Success);
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    Snackbar.Add(FormatApiError(response, error, "Couldn't change user's active status."), Severity.Error);
                }
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't change user's active status.");
            }
        }

        private async Task RestoreUser(UserModel user)
        {
            if (!isGlobalAdmin)
            {
                Snackbar.Add("Only global Admin users can restore archived accounts.", Severity.Warning);
                return;
            }

            var result = await DialogService.ShowMessageBoxAsync(
                "Restore user?",
                $"Restore \"{user.FirstName} {user.LastName}\"?\n\n" +
                "They will be unarchived and can sign in again. Their tracked time, submissions, " +
                "and personal settings were not deleted — only workspace access was suspended.",
                yesText: "Restore", cancelText: "Cancel");

            if (result != true) return;

            try
            {
                var response = await client.PostAsync(Constants.API.User.ActionPath(user.Id, "unarchive"), null);
                if (response.IsSuccessStatusCode)
                {
                    await LoadUsers();
                    Snackbar.Add("User was restored.", Severity.Success);
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    Snackbar.Add(FormatApiError(response, error, "Couldn't restore user."), Severity.Error);
                }
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't restore user.");
            }
        }

        private async Task ArchiveUser(UserModel user)
        {
            var result = await DialogService.ShowMessageBoxAsync(
                "Confirm Archive",
                $"Are you sure you want to archive user \"{user.FirstName} {user.LastName}\"? They will be deactivated and hidden from all views except this page.",
                yesText: "Archive", cancelText: "Cancel");

            if (result != true) return;

            try
            {
                var response = await client.PostAsync(Constants.API.User.ActionPath(user.Id, "archive"), null);
                if (response.IsSuccessStatusCode)
                {
                    await LoadUsers();
                    Snackbar.Add("User was archived.", Severity.Success);
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    Snackbar.Add(FormatApiError(response, error, "Couldn't archive user."), Severity.Error);
                }
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't archive user.");
            }
        }

        private bool IsCurrentUser(UserModel user) =>
            !string.IsNullOrWhiteSpace(currentUserEmail)
            && !string.IsNullOrWhiteSpace(user.Email)
            && string.Equals(currentUserEmail, user.Email, StringComparison.OrdinalIgnoreCase);

        private bool ShowDeleteControls => allowUserDelete && isGlobalAdmin;

        /// <summary>
        /// Null when delete is allowed; otherwise a short reason for a disabled button tooltip.
        /// </summary>
        private string? GetDeleteBlockReason(UserModel user)
        {
            if (!ShowDeleteControls)
                return null;

            if (IsCurrentUser(user))
                return "You cannot delete your own account.";

            if (!userDataInfoMap.TryGetValue(user.Id, out var info))
                return "Loading user data… refresh if this persists.";

            if (info.TaskCount == 0 || info.MostRecentDate == null)
                return null;

            var cutoff = DateTime.UtcNow.AddDays(-dataRetentionDays);
            if (info.MostRecentDate.Value < cutoff)
                return null;

            return $"Most recent tracked task is within the {dataRetentionDays}-day retention period.";
        }

        private async Task DeleteUser(UserModel user)
        {
            userDataInfoMap.TryGetValue(user.Id, out var info);
            var hasData = info != null && info.TaskCount > 0;

            string message;
            if (hasData)
                message = $"This user has {info!.TaskCount} tracked task(s). Deleting \"{user.FirstName} {user.LastName}\" will permanently remove the user AND all their data. Are you sure?";
            else
                message = $"Are you sure you want to delete user \"{user.FirstName} {user.LastName}\"?";

            var result = await DialogService.ShowMessageBoxAsync(
                "Confirm Delete",
                message,
                yesText: "Delete", cancelText: "Cancel");

            if (result != true) return;

            try
            {
                var response = await client.DeleteAsync(Constants.API.User.ById(user.Id));
                if (response.IsSuccessStatusCode)
                {
                    await LoadUsers();
                    Snackbar.Add("User and all associated data deleted.", Severity.Success);
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    Snackbar.Add(FormatApiError(response, error, "Couldn't delete user."), Severity.Error);
                }
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't delete user.");
            }
        }

        private static Color GetRoleColor(string role)
        {
            if (role.StartsWith("Admin")) return Color.Error;
            if (role.StartsWith("Manager")) return Color.Warning;
            return Color.Info;
        }

        private async Task PurgeToken(UserModel user)
        {
            var result = await DialogService.ShowMessageBoxAsync(
                "Force re-sign-in?",
                $"This will end \"{user.FirstName} {user.LastName}\"'s current sign-in. Their next API call will fail and they'll be redirected to sign in again. This is not a lockout — they can sign right back in.",
                yesText: "Force re-sign-in", cancelText: "Cancel");

            if (result != true) return;

            try
            {
                var response = await client.PostAsync(Constants.API.User.ActionPath(user.Id, "purge-token"), null);
                if (response.IsSuccessStatusCode)
                {
                    Snackbar.Add($"{user.FirstName}'s sign-in was reset.", Severity.Success);
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    Snackbar.Add(FormatApiError(response, error, "Couldn't purge user's sign-in."), Severity.Error);
                }
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't purge user's sign-in.");
            }
        }

        /// <summary>
        /// Opens the pull-from-Google dialog for a target user, then calls the API to
        /// re-scan their Google Calendar over the chosen range. Global Admin only —
        /// the endpoint enforces this too, but gating the UI prevents accidental
        /// "permission denied" toasts for non-admins.
        /// </summary>
        private async Task PullEventsFromGoogle(UserModel user)
        {
            var parameters = new DialogParameters<PullFromGoogleDialog>
            {
                { x => x.TargetEmail, user.Email ?? $"{user.FirstName} {user.LastName}" },
                { x => x.DefaultDays, 30 }
            };
            var dialog = await DialogService.ShowAsync<PullFromGoogleDialog>(
                $"Pull missed events for {user.FirstName} {user.LastName}",
                parameters,
                new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true });
            var dialogResult = await dialog.Result;
            if (dialogResult is null || dialogResult.Canceled
                || dialogResult.Data is not PullFromGoogleDialog.PullRange range)
                return;

            try
            {
                var url = Constants.API.GoogleCalendar.ConstructPullFromGoogle(range.From, range.To, user.Id);
                var response = await client.PostAsync(url, null);
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<CalendarPullResultDto>();
                    if (result?.Error != null)
                    {
                        Snackbar.Add($"Pull skipped: {result.Error}", Severity.Warning);
                    }
                    else if (result != null)
                    {
                        Snackbar.Add(
                            $"Scanned {result.Scanned}: {result.Created} imported, {result.Updated} updated, {result.Cancelled} removed, {result.Failed} failed.",
                            Severity.Success);
                    }
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    Snackbar.Add(error, Severity.Error);
                }
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't pull events from Google for this user.");
            }
        }

        private async Task PurgePermissions(UserModel user)
        {
            var result = await DialogService.ShowMessageBoxAsync(
                "Revoke Google permissions?",
                $"This revokes \"{user.FirstName} {user.LastName}\"'s Google grant: calendar sync stops, their stored event IDs are cleared, and they'll need to sign in again. This is not a lockout — they can reconnect their calendar afterwards.",
                yesText: "Revoke", cancelText: "Cancel");

            if (result != true) return;

            try
            {
                var response = await client.PostAsync(Constants.API.User.ActionPath(user.Id, "purge-permissions"), null);
                if (response.IsSuccessStatusCode)
                {
                    Snackbar.Add($"Revoked Google permissions for {user.FirstName}.", Severity.Success);
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    Snackbar.Add(FormatApiError(response, error, "Couldn't revoke user's Google permissions."), Severity.Error);
                }
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't revoke user's Google permissions.");
            }
        }
    }
}
