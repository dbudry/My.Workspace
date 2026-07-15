using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;
using System.Net.Http.Json;
using My.Client.Components.Availability;
using My.Client.Extensions;
using My.Client.Models;
using My.Client.Services;
using My.Shared.Constants;
using My.Shared.Dtos.Project;
using My.Shared.Dtos.ProjectGroup;
using My.Shared.Helpers;

namespace My.Client.Pages.Tyme
{
    public partial class AvailabilityManager
    {
        private const string DefaultGroupName = "Time Off";
        private const string DefaultGroupColor = "#5C8A6A"; // Sage to match the team-calendar event

        private List<Project> categories = new();
        private List<ProjectGroup> groups = new();
        private List<Project> rows = new();

        private bool isLoading = true;
        private bool showArchived;
        private bool showInactive;
        private bool allowProjectDelete;
        private string searchString = string.Empty;

        private HttpClient client = null!;

        [CascadingParameter]
        private Task<AuthenticationState> AuthenticationStateTask { get; set; } = null!;

        [CascadingParameter(Name = "SetPageTitle")]
        private Action<string>? SetPageTitle { get; set; }

        [Inject] private NavigationManager Navigation { get; set; } = null!;
        [Inject] private IHttpClientFactory ClientFactory { get; set; } = null!;
        [Inject] private ISnackbar Snackbar { get; set; } = null!;
        [Inject] private IDialogService DialogService { get; set; } = null!;
        [Inject] private ProjectsCache ProjectsCache { get; set; } = null!;
        [Inject] private OrganizationsCache OrganizationsCache { get; set; } = null!;
        [Inject] private AppSettingsCache AppSettingsCache { get; set; } = null!;

        private List<Organization> organizations = new();

        protected override async Task OnInitializedAsync()
        {
            var authState = await AuthenticationStateTask;
            if (authState.User.Identity is not { IsAuthenticated: true })
            {
                Navigation.NavigateTo($"{Navigation.BaseUri}auth/login", true);
                return;
            }

            client = ClientFactory.CreateClient(Constants.API.ClientName);
            SetPageTitle?.Invoke("Availability");

            await Task.WhenAll(LoadCategories(), LoadGroups(), LoadAppSettingsAsync());
            BuildRows();
            isLoading = false;
        }

        private async Task LoadAppSettingsAsync()
        {
            try
            {
                var settings = await AppSettingsCache.GetAsync();
                var val = settings.FirstOrDefault(s => s.Key == Constants.SettingKeys.AllowProjectDelete);
                if (val != null && bool.TryParse(val.Value, out var parsed))
                    allowProjectDelete = parsed;
            }
            catch { }
        }

        private async Task LoadCategories()
        {
            try
            {
                categories = (await ProjectsCache.LoadSharedAvailabilityAsync()).ToList();
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't load availability categories.");
            }
        }

        private async Task LoadGroups()
        {
            try
            {
                var dtos = await client.GetFromJsonAsync<List<ProjectGroupDto>>(Constants.API.ProjectGroup.Get);
                groups = dtos?.Select(g => new ProjectGroup(g)).ToList() ?? new();
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't load project groups.");
            }
        }

        private void BuildRows()
        {
            var query = categories.AsEnumerable();
            if (!showArchived) query = query.Where(c => !c.IsArchived);
            if (!showInactive) query = query.Where(c => c.IsActive);

            if (!string.IsNullOrWhiteSpace(searchString))
            {
                var needle = searchString.Trim();
                query = query.Where(c =>
                    c.Name.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                    (c.TeamDisplayName?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (c.Slug?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (c.OrganizationName?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (c.DepartmentName?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false));
            }

            rows = query
                .OrderBy(c => c.IsArchived).ThenBy(c => !c.IsActive).ThenBy(c => c.Name)
                .ToList();
        }

        private void OnShowArchivedChanged(bool value)
        {
            showArchived = value;
            BuildRows();
        }

        private void OnShowInactiveChanged(bool value)
        {
            showInactive = value;
            BuildRows();
        }

        /// <summary>
        /// Finds the "Time Off" project group, creating it if absent. Used as a sensible
        /// default so PTO categories cluster on reports without forcing the manager to
        /// set up grouping by hand.
        /// </summary>
        private async Task<ProjectGroup?> EnsureDefaultGroupAsync()
        {
            var existing = groups.FirstOrDefault(g => string.Equals(g.Name, DefaultGroupName, StringComparison.OrdinalIgnoreCase));
            if (existing != null) return existing;

            try
            {
                var dto = new CreateProjectGroupDto { Name = DefaultGroupName, Color = DefaultGroupColor };
                var response = await client.PostAsJsonAsync(Constants.API.ProjectGroup.Create, dto);
                if (!response.IsSuccessStatusCode) return null;

                var created = await response.Content.ReadFromJsonAsync<ProjectGroupDto>();
                if (created == null) return null;

                var group = new ProjectGroup(created);
                groups.Add(group);
                return group;
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, $"Couldn't auto-create the '{DefaultGroupName}' group.");
                return null;
            }
        }

        private async Task<IReadOnlyList<Organization>> LoadOrganizationsForDialogAsync(
            string? search = null,
            string? linkedOrganizationId = null)
        {
            try
            {
                return await OrganizationsCache.LoadForProjectPickerAsync(search, linkedOrganizationId);
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't load organizations for this category.");
                return Array.Empty<Organization>();
            }
        }

        private async Task AddCategory()
        {
            var defaultGroup = await EnsureDefaultGroupAsync();
            organizations = (await LoadOrganizationsForDialogAsync()).ToList();

            // Pre-fill the team-calendar display name so a manager who creates "PTO" /
            // "Vacation" / "Sick" without thinking about the override doesn't end up
            // with the internal name leaking onto the public team calendar. They can
            // still type something else before saving — this is just the default.
            // The same default is also used as the fallback at publish time for any
            // existing IsSharedAvailability project that has DisplayName=null.
            var model = new Project
            {
                IsSharedAvailability = true,
                ProjectGroupId = defaultGroup?.ProjectGroupId,
                TeamDisplayName = My.Shared.Rules.TeamAvailabilityEventRules.DefaultDisplayName
            };

            var parameters = new DialogParameters<AvailabilityDialog>
            {
                { x => x.Model, model },
                { x => x.Groups, groups },
                { x => x.Organizations, organizations },
                { x => x.SubmitLabel, "Create" }
            };

            var dialog = await DialogService.ShowAsync<AvailabilityDialog>("New Availability Category", parameters,
                new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true });
            var result = await dialog.Result;
            if (result is null || result.Canceled) return;

            var edited = (Project)result.Data!;
            try
            {
                var dto = new CreateProjectDto
                {
                    Name = edited.Name,
                    DisplayName = edited.TeamDisplayName,
                    Slug = edited.Slug,
                    OrganizationId = edited.OrganizationId,
                    DepartmentId = edited.DepartmentId,
                    ProjectGroupId = edited.ProjectGroupId,
                    IsSharedAvailability = true
                };
                var response = await client.PostAsJsonAsync(Constants.API.Project.Create, dto);
                if (!response.IsSuccessStatusCode)
                {
                    Snackbar.Add(await response.Content.ReadAsStringAsync(), Severity.Error);
                    return;
                }
                Snackbar.Add("Availability category created.", Severity.Success);
                ProjectsCache.Invalidate();
                await LoadCategories();
                BuildRows();
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't create availability category.");
            }
        }

        private async Task EditCategory(Project category)
        {
            organizations = (await LoadOrganizationsForDialogAsync(
                category.OrganizationName,
                category.OrganizationId)).ToList();

            var model = new Project
            {
                ProjectId = category.ProjectId,
                Name = category.Name,
                TeamDisplayName = category.TeamDisplayName,
                Slug = category.Slug,
                OrganizationId = category.OrganizationId,
                OrganizationName = category.OrganizationName,
                DepartmentId = category.DepartmentId,
                DepartmentName = category.DepartmentName,
                ProjectGroupId = category.ProjectGroupId,
                ProjectGroupName = category.ProjectGroupName,
                IsActive = category.IsActive,
                IsArchived = category.IsArchived,
                IsSharedAvailability = true
            };

            var parameters = new DialogParameters<AvailabilityDialog>
            {
                { x => x.Model, model },
                { x => x.Groups, groups },
                { x => x.Organizations, organizations },
                { x => x.SubmitLabel, "Save" }
            };

            var dialog = await DialogService.ShowAsync<AvailabilityDialog>("Edit Availability Category", parameters,
                new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true });
            var result = await dialog.Result;
            if (result is null || result.Canceled) return;

            var edited = (Project)result.Data!;
            try
            {
                var dto = new UpdateProjectDto
                {
                    ProjectId = category.ProjectId,
                    Name = edited.Name,
                    DisplayName = edited.TeamDisplayName,
                    Slug = edited.Slug,
                    OrganizationId = edited.OrganizationId,
                    DepartmentId = edited.DepartmentId,
                    ProjectGroupId = edited.ProjectGroupId,
                    IsSharedAvailability = true
                };
                var response = await client.PutAsJsonAsync(Constants.API.Project.Update, dto);
                if (!response.IsSuccessStatusCode)
                {
                    Snackbar.Add(await response.Content.ReadAsStringAsync(), Severity.Error);
                    return;
                }
                Snackbar.Add("Availability category updated.", Severity.Success);
                ProjectsCache.Invalidate();
                await LoadCategories();
                BuildRows();
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't update availability category.");
            }
        }

        private async Task ToggleActive(Project category)
        {
            var action = category.IsActive ? "deactivate" : "activate";
            var confirmed = await DialogService.ShowMessageBoxAsync(
                "Confirm", $"Are you sure you want to {action} \"{category.Name}\"?",
                yesText: "Yes", cancelText: "Cancel");
            if (confirmed != true) return;

            try
            {
                var response = await client.PostAsync($"{Constants.API.Project.SetActive}/{category.ProjectId}/setactive", null);
                response.EnsureSuccessStatusCode();
                Snackbar.Add($"Category {action}d.", Severity.Success);
                ProjectsCache.Invalidate();
                await LoadCategories();
                BuildRows();
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, $"Couldn't {action} category.");
            }
        }

        private async Task RemoveCategory(Project category)
        {
            try
            {
                var impact = await client.GetFromJsonAsync<ProjectDeleteImpactDto>(
                    $"{Constants.API.Project.DeleteImpact}{category.ProjectId}/deleteimpact");
                if (impact is null || !impact.CanDelete)
                {
                    var reason = impact == null
                        ? "This category has logged time or calendar history and cannot be deleted."
                        : ProjectDeleteGuard.BuildBlockReason(impact, "category");
                    await DialogService.ShowMessageBoxAsync("Cannot Delete", reason, yesText: "OK");
                    return;
                }
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't check whether this category can be deleted.");
                return;
            }

            var confirmed = await DialogService.ShowMessageBoxAsync(
                "Confirm Delete",
                $"Permanently delete \"{category.Name}\"? This cannot be undone.",
                yesText: "Delete",
                cancelText: "Cancel");
            if (confirmed != true) return;

            try
            {
                var response = await client.DeleteAsync($"{Constants.API.Project.Delete}/{category.ProjectId}");
                if (response.IsSuccessStatusCode)
                {
                    Snackbar.Add("Availability category deleted.", Severity.Success);
                    ProjectsCache.Invalidate();
                    await LoadCategories();
                    BuildRows();
                }
                else
                {
                    Snackbar.Add(await response.Content.ReadAsStringAsync(), Severity.Error);
                }
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't delete availability category.");
            }
        }

        private async Task ArchiveCategory(Project category)
        {
            var action = category.IsArchived ? "unarchive" : "archive";
            var confirmed = await DialogService.ShowMessageBoxAsync(
                "Confirm", $"Are you sure you want to {action} \"{category.Name}\"?",
                yesText: "Yes", cancelText: "Cancel");
            if (confirmed != true) return;

            try
            {
                var response = await client.PostAsync($"{Constants.API.Project.Archive}/{category.ProjectId}/archive", null);
                response.EnsureSuccessStatusCode();
                Snackbar.Add($"Category {action}d.", Severity.Success);
                ProjectsCache.Invalidate();
                await LoadCategories();
                BuildRows();
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, $"Couldn't {action} category.");
            }
        }
    }
}
