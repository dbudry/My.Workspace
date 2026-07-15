using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;
using My.Client.Models.Dashboard;
using My.Client.Services;
using My.Shared;
using My.Shared.Constants;
using My.Shared.Dtos.Dashboard;
using My.Shared.Dtos.Intranet;
using My.Shared.Dtos.TimeSubmission;
using My.Shared.Rules;

namespace My.Client.Components.Dashboard
{
    public enum ChartAxis
    {
        Organization,
        ProjectGroup
    }

    public partial class Dashboard : IDisposable
    {
        public ClaimsPrincipal User { get; set; } = null!;

        private const string Placeholder = "—";
        private const string AxisStorageKey = "dashboard.projectMixAxis";

        private string TopProjectLastMonth = Placeholder;
        private string TopProjectThisMonth = Placeholder;

        private List<ProjectDataItem> projectChartData = new();
        private List<OverdueMonthDto> overdueMonths = new();
        private bool canManageTyme;
        private bool hasTymeAccess;
        private bool hasIntranetAccess;
        private List<FavoriteIntranetPageLink> intranetFavoriteLinks = new();
        private bool isLoading = true;
        private string? loadError;
        /// <summary>
        /// Google identity is present but <c>POST /users/provision</c> never stamped
        /// <c>app_user_id</c> (cold start / SQL). Distinct from "no Tyme role".
        /// </summary>
        private bool profileLoadFailed;
        private bool isRetryingProfile;

        private ChartAxis _selectedAxis = ChartAxis.Organization;
        private ChartAxis selectedAxis
        {
            get => _selectedAxis;
            set
            {
                if (_selectedAxis == value) return;
                _selectedAxis = value;
                _ = LocalStorage.SetItemAsync(AxisStorageKey, value.ToString());
                StateHasChanged();
            }
        }

        private string AxisLabel => selectedAxis switch
        {
            ChartAxis.Organization => "organization",
            ChartAxis.ProjectGroup => "project group",
            _ => "organization"
        };

        /// <summary>
        /// Re-pivots the underlying per-project data into the currently selected axis,
        /// rolling unspecified parents into a single "Unspecified" bucket. The first
        /// project in each group contributes its parent's color so the palette matches
        /// what the user sees on other pages (Org pivot → org color, Group pivot → group
        /// color).
        /// </summary>
        private List<ProjectDataItem> pivotedChartData
        {
            get
            {
                IEnumerable<IGrouping<(string? Id, string? Name), ProjectDataItem>> grouped = selectedAxis switch
                {
                    ChartAxis.Organization => projectChartData.GroupBy(p => (p.OrganizationId, p.OrganizationName)),
                    ChartAxis.ProjectGroup => projectChartData.GroupBy(p => (p.ProjectGroupId, p.ProjectGroupName)),
                    _ => projectChartData.GroupBy(p => (p.OrganizationId, p.OrganizationName))
                };

                return grouped
                    .Select(g =>
                    {
                        var sample = g.First();
                        return new ProjectDataItem(
                            g.Key.Id ?? "Unspecified",
                            string.IsNullOrEmpty(g.Key.Name) ? "Unspecified" : g.Key.Name!,
                            TimeSpan.FromSeconds(g.Sum(p => p.Time.TotalSeconds)),
                            "")
                        {
                            // Carry the parent's native color forward so PivotPalette can
                            // assemble a per-segment palette without re-doing the join.
                            OrganizationColor = sample.OrganizationColor,
                            ProjectGroupColor = sample.ProjectGroupColor,
                        };
                    })
                    .OrderByDescending(p => p.Time)
                    .ToList();
            }
        }

        /// <summary>
        /// Per-segment palette aligned with <see cref="pivotedChartData"/>. Returns null
        /// when the user opted out — the chart then falls back to MudBlazor defaults.
        /// </summary>
        private string[]? PivotPalette
        {
            get
            {
                if (SettingsService.ProjectColorSource == ProjectColorSource.None)
                    return null;

                var data = pivotedChartData;
                return data.Select(item => selectedAxis switch
                {
                    ChartAxis.Organization => item.OrganizationColor,
                    ChartAxis.ProjectGroup => item.ProjectGroupColor,
                    _ => null
                })
                .Select(c => string.IsNullOrWhiteSpace(c) ? ProjectColorRules.FallbackGray : c)
                .ToArray();
            }
        }

        #region Dependency Injection

        [Inject]
        protected NavigationManager Navigation { get; set; } = null!;

        [Inject]
        private IHttpClientFactory ClientFactory { get; set; } = null!;

        [Inject]
        private ILogger<Dashboard> Logger { get; set; } = null!;

        [Inject]
        private LocalStorageService LocalStorage { get; set; } = null!;

        [Inject]
        private UserSettingsService SettingsService { get; set; } = null!;

        [Inject]
        private AuthenticationStateProvider AuthStateProvider { get; set; } = null!;

        [Inject]
        private IUserRoleRefreshService RoleRefresh { get; set; } = null!;

        #endregion

        [CascadingParameter]
        private Task<AuthenticationState> AuthenticationStateTask { get; set; } = null!;

        protected override void OnInitialized()
        {
            AuthStateProvider.AuthenticationStateChanged += OnAuthStateChanged;
        }

        public void Dispose()
        {
            AuthStateProvider.AuthenticationStateChanged -= OnAuthStateChanged;
        }

        private void OnAuthStateChanged(Task<AuthenticationState> t) => _ = RefreshForAuthChangeAsync();

        private async Task RefreshForAuthChangeAsync()
        {
            var authState = await AuthStateProvider.GetAuthenticationStateAsync();
            await ApplyUserGatesAsync(authState.User);
            await InvokeAsync(StateHasChanged);
        }

        protected override async Task OnInitializedAsync()
        {
            var authState = await AuthenticationStateTask;
            User = authState.User;

            if (User.Identity != null && !User.Identity.IsAuthenticated)
                Navigation.NavigateTo($"{Navigation.BaseUri}auth/login", true);

            // Restore the axis the user last picked (defaults to Organization).
            var savedAxis = await LocalStorage.GetItemAsync<string>(AxisStorageKey);
            if (!string.IsNullOrEmpty(savedAxis) && Enum.TryParse<ChartAxis>(savedAxis, out var parsed))
                _selectedAxis = parsed;

            await ApplyUserGatesAsync(User);
        }

        private async Task ApplyUserGatesAsync(ClaimsPrincipal user)
        {
            User = user;

            // Provision never attached app_user_id — show retry UI instead of an empty shell.
            // Users with a real profile but no module roles still have app_user_id.
            if (CustomAccountFactory.IsMissingAppProfile(user))
            {
                profileLoadFailed = true;
                isLoading = false;
                loadError = null;
                hasTymeAccess = false;
                hasIntranetAccess = false;
                canManageTyme = false;
                overdueMonths.Clear();
                projectChartData.Clear();
                intranetFavoriteLinks.Clear();
                return;
            }

            profileLoadFailed = false;

            // Tyme panels are for users who have been explicitly given a Tyme-scoped role.
            // A pure global Admin is system-focused (Users, AppSettings, Logs) and does
            // not automatically get Tyme (or Intranet). Gate strictly on scoped roles.
            // If they need to act in Tyme they use the impersonation feature.
            var prevManageTyme = canManageTyme;
            var prevTymeAccess = hasTymeAccess;
            var prevIntranetAccess = hasIntranetAccess;

            canManageTyme = Constants.Roles.HasScopedAccess(user, Constants.Scopes.Tyme, Constants.Roles.Manager);

            hasTymeAccess = Constants.Roles.HasScopedAccess(user, Constants.Scopes.Tyme);
            hasIntranetAccess = Constants.Roles.HasScopedAccess(user, Constants.Scopes.Intranet);

            var settings = await SettingsService.GetSettingsAsync();
            var favoriteIds = settings?.FavoriteIntranetPageIds ?? new();

            if (hasIntranetAccess && favoriteIds.Count > 0)
                await LoadFavoritePageLinksAsync(favoriteIds);
            else
                intranetFavoriteLinks.Clear();

            if (hasTymeAccess && (!prevTymeAccess || prevManageTyme != canManageTyme))
                await LoadDashboard();
            else if (!hasTymeAccess)
            {
                isLoading = false;
                loadError = null;
                overdueMonths.Clear();
            }
        }

        private sealed class FavoriteIntranetPageLink
        {
            public string PageId { get; init; } = "";
            public string? Slug { get; init; }
            public string Title { get; init; } = "";
        }

        private async Task LoadFavoritePageLinksAsync(List<string> favoriteIds)
        {
            try
            {
                var client = ClientFactory.CreateClient(Constants.API.ClientName);
                using var response = await client.GetAsync(Constants.API.Intranet.Pages.Get);
                if (!response.IsSuccessStatusCode)
                {
                    intranetFavoriteLinks = favoriteIds
                        .Select(id => new FavoriteIntranetPageLink { PageId = id, Title = "Favorite page" })
                        .ToList();
                    return;
                }

                var pages = await response.Content.ReadFromJsonAsync<List<IntranetPageSummaryDto>>() ?? new();
                var pageById = pages.ToDictionary(p => p.PageId);

                intranetFavoriteLinks = favoriteIds
                    .Select(id =>
                    {
                        pageById.TryGetValue(id, out var page);
                        return new FavoriteIntranetPageLink
                        {
                            PageId = id,
                            Slug = page?.Slug,
                            Title = page is { Title: { Length: > 0 } title } ? title : "Untitled page"
                        };
                    })
                    .ToList();
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to load intranet favorite page titles; showing IDs as fallback.");
                intranetFavoriteLinks = favoriteIds
                    .Select(id => new FavoriteIntranetPageLink { PageId = id, Title = "Favorite page" })
                    .ToList();
            }
        }

        private async Task LoadDashboard()
        {
            isLoading = true;
            loadError = null;
            StateHasChanged();

            var client = ClientFactory.CreateClient(Constants.API.ClientName);

            // Side-load the per-user overdue reminder in parallel. The manager-side
            // unsubmitted view is now rendered by <TeamSubmissionsView /> which
            // self-fetches, so no separate manager-overdue load here.
            _ = LoadOverdueAsync(client);

            try
            {
                var dashboard = await client.GetFromJsonAsync<DashboardDto>(Constants.API.Analytics.GetDashboard);

                if (dashboard != null)
                {
                    TopProjectThisMonth = $"{dashboard.ThisMonth.TopProject} - {dashboard.ThisMonth.TopProjectAmounTimeText}";
                    TopProjectLastMonth = $"{dashboard.LastMonth.TopProject} - {dashboard.LastMonth.TopProjectAmounTimeText}";

                    projectChartData = dashboard.ProjectChart
                        .Select(p => new ProjectDataItem(p.ProjectId, p.ProjectName, p.Time, "")
                        {
                            OrganizationId = p.OrganizationId,
                            OrganizationName = p.OrganizationName,
                            OrganizationColor = p.OrganizationColor,
                            ProjectGroupId = p.ProjectGroupId,
                            ProjectGroupName = p.ProjectGroupName,
                            ProjectGroupColor = p.ProjectGroupColor
                        })
                        .ToList();
                }
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                Logger.LogWarning(ex, "Dashboard load returned 401; auth handler will redirect to sign-in.");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Dashboard load failed");
                loadError = BuildLoadErrorMessage(ex);
            }
            finally
            {
                isLoading = false;
                StateHasChanged();
            }
        }

        private async Task ReloadAsync()
        {
            await LoadDashboard();
        }

        /// <summary>
        /// Re-runs provision (via role-refresh) after a cold-start profile miss, then
        /// re-applies gates so dashboard data loads once roles are present.
        /// </summary>
        private async Task RetryProfileLoadAsync()
        {
            if (isRetryingProfile) return;

            isRetryingProfile = true;
            isLoading = true;
            profileLoadFailed = false;
            StateHasChanged();

            try
            {
                var cacheReset = await RoleRefresh.RefreshAfterSelfRoleChangeAsync();
                if (!cacheReset)
                {
                    // OIDC private cache fields changed — hard reload is the only reliable recovery.
                    Navigation.NavigateTo(Navigation.Uri, forceLoad: true);
                    return;
                }

                var authState = await AuthStateProvider.GetAuthenticationStateAsync();
                await ApplyUserGatesAsync(authState.User);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Profile retry failed");
                profileLoadFailed = true;
                isLoading = false;
            }
            finally
            {
                isRetryingProfile = false;
                StateHasChanged();
            }
        }

        private async Task LoadOverdueAsync(HttpClient client)
        {
            try
            {
                var list = await client.GetFromJsonAsync<List<OverdueMonthDto>>(Constants.API.TimeSubmission.GetOverdue);
                overdueMonths = list ?? new();
                StateHasChanged();
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to load overdue submissions; suppressing.");
            }
        }

        private static string BuildLoadErrorMessage(Exception ex) => ex switch
        {
            HttpRequestException httpEx when (int?)httpEx.StatusCode >= 500 =>
                "Couldn't load dashboard data. The server may be unavailable — try again in a moment.",
            HttpRequestException httpEx =>
                $"Couldn't load dashboard data ({(int?)httpEx.StatusCode} {httpEx.StatusCode}).",
            JsonException =>
                "Couldn't load dashboard data — got an unexpected response from the server.",
            TaskCanceledException =>
                "Couldn't load dashboard data — the request timed out.",
            _ => "Couldn't load dashboard data."
        };
    }
}
