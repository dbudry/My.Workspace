using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;
using System.Net.Http.Json;
using My.Client.Components.Projects;
using My.Client.Extensions;
using My.Client.Models;
using My.Client.Models.Paging;
using My.Client.Services;
using My.Shared.Dtos.Paging;
using My.Shared.Dtos.Organization;
using My.Shared.Dtos.Project;
using My.Shared.Dtos.ProjectGroup;
using My.Shared.Constants;
using My.Shared.Dtos;
using My.Shared.Helpers;

namespace My.Client.Pages.Tyme
{
    public partial class ProjectManager
    {
        List<Project> projectsList { get; set; } = new();
        List<ProjectGroup> projectGroupsList { get; set; } = new();
        List<Organization> organizationsList { get; set; } = new();
        MudTable<ProjectDisplayRow> table = null!;

        bool showArchived = false;
        bool showInactive = false;
        bool allowProjectDelete = false;
        // Whether the *current user* is allowed to create/edit/delete projects + groups.
        // The page is open to any user with a Tyme-scoped role for read-only viewing;
        // mutations are gated. Global-only Admins are blocked before the component loads.
        bool canManage = false;

        private const string GroupByStorageKey = "projects.groupBy";

        private GroupByMode _groupBy = GroupByMode.Organization;
        public GroupByMode groupBy
        {
            get => _groupBy;
            set => _groupBy = value;
        }

        HttpClient client = null!;

        string searchString = "";

        [CascadingParameter]
        private Task<AuthenticationState> AuthenticationStateTask { get; set; } = null!;

        [CascadingParameter(Name = "SetPageTitle")]
        private Action<string>? SetPageTitle { get; set; }

        #region Dependency Injection

        [Inject]
        protected NavigationManager Navigation { get; set; } = null!;

        [Inject]
        private IHttpClientFactory ClientFactory { get; set; } = null!;

        [Inject]
        private ISnackbar Snackbar { get; set; } = null!;

        [Inject]
        private IDialogService DialogService { get; set; } = null!;

        [Inject]
        private LocalStorageService LocalStorage { get; set; } = null!;

        [Inject]
        private ProjectsCache ProjectsCache { get; set; } = null!;

        [Inject]
        private OrganizationsCache OrganizationsCache { get; set; } = null!;

        [Inject]
        private AppSettingsCache AppSettingsCache { get; set; } = null!;

        [Inject]
        private UserSettingsService SettingsService { get; set; } = null!;

        #endregion

        protected override async Task OnInitializedAsync()
        {
            var authState = await AuthenticationStateTask;
            var user = authState.User;

            if (user.Identity != null && !user.Identity.IsAuthenticated)
                Navigation.NavigateTo($"{Navigation.BaseUri}auth/login", true);

            // Gate mutation UI on the user's *scoped* Tyme role. A global Admin without
            // any Tyme:* scope will have been denied by the [Authorize(Policy="Tyme:User:Scoped")]
            // attribute on the page (consistent with Intranet scoping).
            canManage = user.IsInRole(Constants.Roles.Scoped(Constants.Roles.Manager, Constants.Scopes.Tyme))
                     || user.IsInRole(Constants.Roles.Scoped(Constants.Roles.Admin, Constants.Scopes.Tyme));

            client = ClientFactory.CreateClient(Constants.API.ClientName);

            SetPageTitle?.Invoke(canManage ? "Manage Projects" : "Projects");

            // Restore the grouping mode the user last picked (defaults to Organization).
            var savedGroupBy = await LocalStorage.GetItemAsync<string>(GroupByStorageKey);
            if (!string.IsNullOrEmpty(savedGroupBy) && Enum.TryParse<GroupByMode>(savedGroupBy, out var parsedMode))
                _groupBy = parsedMode;

            await Task.WhenAll(
                LoadProjectGroups(),
                LoadAppSettings(),
                SettingsService.GetSettingsAsync());
        }

        private async Task LoadAppSettings()
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

        private async Task OnShowArchivedChanged(bool value)
        {
            showArchived = value;
            await ReloadTableAsync();
        }

        private async Task OnShowInactiveChanged(bool value)
        {
            showInactive = value;
            await ReloadTableAsync();
        }

        private async Task<TableData<ProjectDisplayRow>> LoadServerData(TableState state, CancellationToken cancellationToken)
        {
            try
            {
                var query = new ListQueryParameters
                {
                    PageNumber = state.Page + 1,
                    PageSize = state.PageSize,
                    Search = searchString,
                    SortBy = string.IsNullOrWhiteSpace(state.SortLabel) ? "Name" : state.SortLabel,
                    SortDescending = state.SortDirection == SortDirection.Descending,
                    IncludeArchived = showArchived,
                    IncludeInactive = showInactive,
                    GroupBy = groupBy == GroupByMode.Organization
                        ? ProjectListGroupBy.Organization
                        : ProjectListGroupBy.ProjectGroup
                };

                var url = ListQueryUrlBuilder.Build(Constants.API.Project.Get, query);
                var response = await client.GetFromJsonAsync<PagedResponse<ProjectListRowDto>>(url, cancellationToken);

                var listRows = response?.Items?.ToList() ?? new List<ProjectListRowDto>();
                projectsList = listRows
                    .Where(r => r.Project != null)
                    .Select(r => new Project(r.Project!))
                    .GroupBy(p => p.ProjectId)
                    .Select(g => g.First())
                    .ToList();

                var displayRows = MapListRows(listRows);
                // Group headers (and empty groups) so "By Project Group" is never a flat list,
                // and every group remains editable even when it has no projects on this page.
                if (groupBy == GroupByMode.ProjectGroup)
                    displayRows = EnsureProjectGroupHeaders(displayRows, projectGroupsList, projectsList);

                return new TableData<ProjectDisplayRow>
                {
                    Items = displayRows,
                    TotalItems = response?.TotalCount ?? 0
                };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new TableData<ProjectDisplayRow> { Items = Array.Empty<ProjectDisplayRow>(), TotalItems = 0 };
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't load projects.");
                return new TableData<ProjectDisplayRow> { Items = Array.Empty<ProjectDisplayRow>(), TotalItems = 0 };
            }
        }

        private async Task ReloadTableAsync()
        {
            if (table != null)
                await table.ReloadServerData();
        }

        private async Task OnSearchChanged(string value)
        {
            searchString = value;
            await ReloadTableAsync();
        }

        private string GetEmptyMessage()
        {
            if (!string.IsNullOrWhiteSpace(searchString))
                return "No projects match your search.";
            if (showArchived)
                return "No archived projects match.";
            return "Add a project group or project to get started.";
        }

        private async Task LoadProjectGroups()
        {
            try
            {
                var groups = await client.GetFromJsonAsync<List<ProjectGroupDto>>(Constants.API.ProjectGroup.Get);
                if (groups != null)
                {
                    projectGroupsList = groups.Select(g => new ProjectGroup(g)).ToList();
                }
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't load project groups.");
            }
        }

        private static string FormatHeaderLabel(string name, ProjectDisplayRow row)
        {
            if (row.ProjectCount is not > 0)
                return name;

            return $"{name} ({row.ProjectCount:N0})";
        }

        private async Task OnGroupByChanged(GroupByMode value)
        {
            groupBy = value;
            await LocalStorage.SetItemAsync(GroupByStorageKey, value.ToString());
            await ReloadTableAsync();
        }

        private static List<ProjectDisplayRow> MapListRows(IEnumerable<ProjectListRowDto> listRows)
        {
            var rows = new List<ProjectDisplayRow>();
            foreach (var row in listRows)
            {
                // Prefer payload shape over Kind — enum deserialization can default to
                // Organization (0) and drop headers, leaving a flat project list.
                if (row.ProjectGroup != null
                    && (row.Kind == ProjectListRowKind.ProjectGroup || row.Project == null))
                {
                    rows.Add(ProjectDisplayRow.ForGroup(
                        new ProjectGroup
                        {
                            ProjectGroupId = row.ProjectGroup.ProjectGroupId,
                            Name = row.ProjectGroup.Name,
                            Color = row.ProjectGroup.Color ?? "#616161"
                        },
                        row.ProjectGroup.ProjectCount > 0 ? row.ProjectGroup.ProjectCount : row.ProjectCount ?? 0,
                        row.ProjectGroup.ProjectsTruncated || row.ProjectsTruncated));
                    continue;
                }

                if (row.Organization != null
                    && (row.Kind == ProjectListRowKind.Organization || row.Project == null))
                {
                    rows.Add(ProjectDisplayRow.ForOrganization(
                        new Organization
                        {
                            OrganizationId = row.Organization.OrganizationId,
                            Name = row.Organization.Name,
                            Color = row.Organization.Color
                        },
                        row.Organization.ProjectCount > 0 ? row.Organization.ProjectCount : row.ProjectCount ?? 0,
                        row.Organization.ProjectsTruncated || row.ProjectsTruncated));
                    continue;
                }

                if (row.Kind == ProjectListRowKind.UnassignedBucket
                    || (!string.IsNullOrEmpty(row.BucketLabel) && row.Project == null))
                {
                    rows.Add(ProjectDisplayRow.ForNoGroupHeader(
                        row.BucketLabel ?? "Unassigned",
                        row.ProjectCount,
                        row.ProjectsTruncated));
                    continue;
                }

                if (row.Project != null)
                    rows.Add(ProjectDisplayRow.ForProject(new Project(row.Project)));
            }

            return rows;
        }

        /// <summary>
        /// Ensures "By Project Group" shows real group headers (with Edit / Delete actions)
        /// and includes empty groups from the full group catalog. If the server returned a
        /// flat project list, regroups client-side.
        /// </summary>
        private static List<ProjectDisplayRow> EnsureProjectGroupHeaders(
            List<ProjectDisplayRow> rows,
            IReadOnlyList<ProjectGroup> allGroups,
            IReadOnlyList<Project> pageProjects)
        {
            var hasGroupHeaders = rows.Any(r => r.IsGroupRow || r.IsNoGroupHeaderRow);
            if (!hasGroupHeaders)
            {
                // Flat list from API — rebuild headers from groups + this page's projects.
                return BuildProjectGroupRows(allGroups, pageProjects);
            }

            // Headers present: still inject empty groups so they stay visible and editable.
            var presentGroupIds = rows
                .Where(r => r.IsGroupRow && r.Group != null)
                .Select(r => r.Group!.ProjectGroupId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var missingEmpty = allGroups
                .Where(g => !presentGroupIds.Contains(g.ProjectGroupId))
                .Where(g => !pageProjects.Any(p =>
                    string.Equals(p.ProjectGroupId, g.ProjectGroupId, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => ProjectDisplayRow.ForGroup(g, projectCount: 0))
                .ToList();

            if (missingEmpty.Count == 0)
                return rows;

            // Place empty groups after existing group sections, before a trailing "No Group"
            // bucket if present; otherwise append.
            var noGroupIndex = rows.FindIndex(r => r.IsNoGroupHeaderRow);
            if (noGroupIndex >= 0)
            {
                rows.InsertRange(noGroupIndex, missingEmpty);
                return rows;
            }

            rows.AddRange(missingEmpty);
            return rows;
        }

        private static List<ProjectDisplayRow> BuildProjectGroupRows(
            IReadOnlyList<ProjectGroup> allGroups,
            IReadOnlyList<Project> pageProjects)
        {
            var rows = new List<ProjectDisplayRow>();
            var byGroup = pageProjects
                .Where(p => !string.IsNullOrEmpty(p.ProjectGroupId))
                .GroupBy(p => p.ProjectGroupId!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList(), StringComparer.OrdinalIgnoreCase);

            var unassigned = pageProjects
                .Where(p => string.IsNullOrEmpty(p.ProjectGroupId))
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var group in allGroups.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
            {
                byGroup.TryGetValue(group.ProjectGroupId, out var projects);
                projects ??= new List<Project>();
                rows.Add(ProjectDisplayRow.ForGroup(group, projects.Count));
                foreach (var p in projects)
                    rows.Add(ProjectDisplayRow.ForProject(p));
            }

            // Projects whose group id is not in the catalog (orphans) — keep them under a bucket.
            var knownIds = allGroups.Select(g => g.ProjectGroupId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var orphanProjects = pageProjects
                .Where(p => !string.IsNullOrEmpty(p.ProjectGroupId) && !knownIds.Contains(p.ProjectGroupId!))
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (unassigned.Count > 0 || orphanProjects.Count > 0)
            {
                var bucket = unassigned.Concat(orphanProjects).ToList();
                rows.Add(ProjectDisplayRow.ForNoGroupHeader("No Group", bucket.Count));
                foreach (var p in bucket)
                    rows.Add(ProjectDisplayRow.ForProject(p));
            }

            return rows;
        }

        private async Task<IReadOnlyList<Organization>> LoadOrganizationsForProjectDialogAsync(
            string? search = null,
            string? linkedOrganizationId = null)
        {
            try
            {
                return await OrganizationsCache.LoadForProjectPickerAsync(search, linkedOrganizationId);
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't load organizations for this project.");
                return Array.Empty<Organization>();
            }
        }

        #region Project CRUD

        private Task AddProject() => AddProject(null);

        private async Task AddProject(ProjectGroup? preselectedGroup)
        {
            organizationsList = (await LoadOrganizationsForProjectDialogAsync()).ToList();

            var model = new Project
            {
                IsActive = true,
                ProjectGroupId = preselectedGroup?.ProjectGroupId
            };

            var parameters = new DialogParameters<ProjectDialog>
            {
                { x => x.Model, model },
                { x => x.Groups, projectGroupsList },
                { x => x.Organizations, organizationsList },
                { x => x.SubmitLabel, "Create" }
            };

            var dialog = await DialogService.ShowAsync<ProjectDialog>("New Project", parameters,
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
                    IsSharedAvailability = edited.IsSharedAvailability,
                    IsBillable = edited.IsBillable
                };
                var response = await client.PostAsJsonAsync(Constants.API.Project.Create, dto);
                if (!response.IsSuccessStatusCode)
                {
                    Snackbar.Add(await response.Content.ReadAsStringAsync(), Severity.Error);
                    return;
                }
                Snackbar.Add("Project created.", Severity.Success);
                ProjectsCache.Invalidate();
                await ReloadTableAsync();
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't create project.");
            }
        }

        private async Task EditProject(Project project)
        {
            organizationsList = (await LoadOrganizationsForProjectDialogAsync(
                project.OrganizationName,
                project.OrganizationId)).ToList();

            var model = new Project
            {
                ProjectId = project.ProjectId,
                Name = project.Name,
                TeamDisplayName = project.TeamDisplayName,
                Slug = project.Slug,
                OrganizationId = project.OrganizationId,
                OrganizationName = project.OrganizationName,
                DepartmentId = project.DepartmentId,
                DepartmentName = project.DepartmentName,
                ProjectGroupId = project.ProjectGroupId,
                ProjectGroupName = project.ProjectGroupName,
                IsActive = project.IsActive,
                IsArchived = project.IsArchived,
                IsSharedAvailability = project.IsSharedAvailability,
                IsBillable = project.IsBillable
            };

            var parameters = new DialogParameters<ProjectDialog>
            {
                { x => x.Model, model },
                { x => x.Groups, projectGroupsList },
                { x => x.Organizations, organizationsList },
                { x => x.SubmitLabel, "Save" }
            };

            var dialog = await DialogService.ShowAsync<ProjectDialog>("Edit Project", parameters,
                new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true });
            var result = await dialog.Result;
            if (result is null || result.Canceled) return;

            var edited = (Project)result.Data!;
            if (project.IsBillable && !edited.IsBillable)
            {
                if (!await ConfirmClearBillableTimeAsync(project.ProjectId))
                    return;
            }

            try
            {
                var dto = new UpdateProjectDto
                {
                    ProjectId = project.ProjectId,
                    Name = edited.Name,
                    DisplayName = edited.TeamDisplayName,
                    Slug = edited.Slug,
                    OrganizationId = edited.OrganizationId,
                    DepartmentId = edited.DepartmentId,
                    ProjectGroupId = edited.ProjectGroupId,
                    IsSharedAvailability = edited.IsSharedAvailability,
                    IsBillable = edited.IsBillable
                };
                var response = await client.PutAsJsonAsync(Constants.API.Project.Update, dto);
                if (!response.IsSuccessStatusCode)
                {
                    Snackbar.Add(await response.Content.ReadAsStringAsync(), Severity.Error);
                    return;
                }
                Snackbar.Add("Project updated.", Severity.Success);
                ProjectsCache.Invalidate();
                await ReloadTableAsync();
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't update project.");
            }
        }

        private async Task ToggleActiveProject(Project project)
        {
            var action = project.IsActive ? "deactivate" : "activate";
            var confirmed = await DialogService.ShowMessageBoxAsync(
                "Confirm", $"Are you sure you want to {action} \"{project.Name}\"?",
                yesText: "Yes", cancelText: "Cancel");
            if (confirmed != true) return;

            try
            {
                var response = await client.PostAsync($"{Constants.API.Project.SetActive}/{project.ProjectId}/setactive", null);
                response.EnsureSuccessStatusCode();
                Snackbar.Add($"Project {action}d.", Severity.Success);
                ProjectsCache.Invalidate();
                await ReloadTableAsync();
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, $"Couldn't {action} project.");
            }
        }

        private async Task ArchiveProject(Project project)
        {
            var action = project.IsArchived ? "unarchive" : "archive";
            var confirmed = await DialogService.ShowMessageBoxAsync(
                "Confirm", $"Are you sure you want to {action} \"{project.Name}\"?",
                yesText: "Yes", cancelText: "Cancel");
            if (confirmed != true) return;

            try
            {
                var response = await client.PostAsync($"{Constants.API.Project.Archive}/{project.ProjectId}/archive", null);
                response.EnsureSuccessStatusCode();
                Snackbar.Add($"Project {action}d.", Severity.Success);
                ProjectsCache.Invalidate();
                await ReloadTableAsync();
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, $"Couldn't {action} project.");
            }
        }

        private async Task<bool> ConfirmClearBillableTimeAsync(string projectId)
        {
            try
            {
                var impact = await client.GetFromJsonAsync<ProjectBillableImpactDto>(
                    $"{Constants.API.Project.BillableImpact}{projectId}/billableimpact");
                if (impact is not { HasBillableTime: true })
                    return true;

                var message =
                    $"This project has {impact.BillableTaskCount} billable time " +
                    $"{(impact.BillableTaskCount == 1 ? "entry" : "entries")}. " +
                    "Marking it non-billable will clear the billable flag on those entries. " +
                    "The time records will remain, but they will no longer count as billable.";

                var confirmed = await DialogService.ShowMessageBoxAsync(
                    "Clear billable time?",
                    message,
                    yesText: "Mark non-billable",
                    cancelText: "Cancel");
                return confirmed == true;
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't check billable time on this project.");
                return false;
            }
        }

        private async Task RemoveProject(Project project)
        {
            try
            {
                var impact = await client.GetFromJsonAsync<ProjectDeleteImpactDto>(
                    $"{Constants.API.Project.DeleteImpact}{project.ProjectId}/deleteimpact");
                if (impact is null || !impact.CanDelete)
                {
                    var reason = impact == null
                        ? "This project has logged time or calendar history and cannot be deleted."
                        : ProjectDeleteGuard.BuildBlockReason(impact);
                    await DialogService.ShowMessageBoxAsync("Cannot Delete", reason, yesText: "OK");
                    return;
                }
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't check whether this project can be deleted.");
                return;
            }

            var confirmed = await DialogService.ShowMessageBoxAsync(
                "Confirm Delete", $"Permanently delete \"{project.Name}\"? This cannot be undone.",
                yesText: "Delete", cancelText: "Cancel");
            if (confirmed != true) return;

            try
            {
                var response = await client.DeleteAsync($"{Constants.API.Project.Delete}/{project.ProjectId}");
                if (response.IsSuccessStatusCode)
                {
                    Snackbar.Add("Project removed.", Severity.Success);
                    ProjectsCache.Invalidate();
                    await ReloadTableAsync();
                }
                else
                {
                    Snackbar.Add(await response.Content.ReadAsStringAsync(), Severity.Error);
                }
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't delete project.");
            }
        }

        #endregion

        #region Project Group CRUD

        private async Task AddProjectGroup()
        {
            var model = new ProjectGroup
            {
                Color = PickDefaultColor()
            };
            var parameters = new DialogParameters<ProjectGroupDialog>
            {
                { x => x.Model, model },
                { x => x.SubmitLabel, "Create" }
            };
            var dialog = await DialogService.ShowAsync<ProjectGroupDialog>("New Project Group", parameters,
                new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true });
            var result = await dialog.Result;
            if (result is null || result.Canceled) return;

            var edited = (ProjectGroup)result.Data!;
            try
            {
                var dto = new CreateProjectGroupDto
                {
                    Name = edited.Name.Trim(),
                    Color = edited.Color
                };
                var response = await client.PostAsJsonAsync(Constants.API.ProjectGroup.Create, dto);
                if (!response.IsSuccessStatusCode)
                {
                    Snackbar.Add(await response.Content.ReadAsStringAsync(), Severity.Error);
                    return;
                }
                Snackbar.Add("Project group created.", Severity.Success);
                ProjectsCache.Invalidate();
                await LoadProjectGroups();
                await ReloadTableAsync();
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't create project group.");
            }
        }

        private async Task EditProjectGroup(ProjectGroup group)
        {
            var model = new ProjectGroup
            {
                ProjectGroupId = group.ProjectGroupId,
                Name = group.Name,
                Color = group.Color
            };
            var parameters = new DialogParameters<ProjectGroupDialog>
            {
                { x => x.Model, model },
                { x => x.SubmitLabel, "Save" }
            };
            var dialog = await DialogService.ShowAsync<ProjectGroupDialog>("Edit Project Group", parameters,
                new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true });
            var result = await dialog.Result;
            if (result is null || result.Canceled) return;

            var edited = (ProjectGroup)result.Data!;
            try
            {
                var dto = new UpdateProjectGroupDto
                {
                    ProjectGroupId = edited.ProjectGroupId,
                    Name = edited.Name.Trim(),
                    Color = edited.Color
                };
                var response = await client.PutAsJsonAsync(Constants.API.ProjectGroup.Update, dto);
                if (!response.IsSuccessStatusCode)
                {
                    Snackbar.Add(await response.Content.ReadAsStringAsync(), Severity.Error);
                    return;
                }
                Snackbar.Add("Project group updated.", Severity.Success);
                ProjectsCache.Invalidate();
                await LoadProjectGroups();
                await ReloadTableAsync();
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't update project group.");
            }
        }

        private async Task RemoveProjectGroup(ProjectGroup group)
        {
            var assignedCount = projectsList.Count(p => p.ProjectGroupId == group.ProjectGroupId);
            var confirmed = await DialogService.ShowMessageBoxAsync(
                "Confirm",
                assignedCount > 0
                    ? $"\"{group.Name}\" is assigned to {assignedCount} project(s). Removing it will ungroup them. Continue?"
                    : $"Remove group \"{group.Name}\"?",
                yesText: "Remove", cancelText: "Cancel");
            if (confirmed != true) return;

            try
            {
                var response = await client.DeleteAsync($"{Constants.API.ProjectGroup.Delete}/{group.ProjectGroupId}");
                if (response.IsSuccessStatusCode)
                {
                    Snackbar.Add("Project group removed.", Severity.Success);
                    ProjectsCache.Invalidate();
                    await LoadProjectGroups();
                    await ReloadTableAsync();
                }
                else
                {
                    Snackbar.Add(await response.Content.ReadAsStringAsync(), Severity.Error);
                }
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't delete project group.");
            }
        }

        private static readonly string[] DefaultGroupColors = new[]
        {
            "#1976d2", "#388e3c", "#e64a19", "#7b1fa2", "#00838f",
            "#c62828", "#f9a825", "#4527a0", "#00695c", "#ad1457"
        };

        private string PickDefaultColor() =>
            DefaultGroupColors[(projectGroupsList.Count + 1) % DefaultGroupColors.Length];

        #endregion
    }

    public enum GroupByMode
    {
        ProjectGroup,
        Organization
    }

    /// <summary>
    /// Row model for the unified table: a parent header (group or organization), a
    /// "No Group" / "No Organization" bucket header, or a project under one of those.
    /// </summary>
    public class ProjectDisplayRow
    {
        public ProjectGroup? Group { get; }
        public Organization? Organization { get; }
        public Project? Project { get; }
        public string? NoGroupHeaderLabel { get; }
        public int? ProjectCount { get; }
        public bool ProjectsTruncated { get; }

        public bool IsGroupRow => Group != null;
        public bool IsOrganizationRow => Organization != null;
        public bool IsNoGroupHeaderRow => Group == null && Organization == null && Project == null && NoGroupHeaderLabel != null;

        private ProjectDisplayRow(
            ProjectGroup? group,
            Organization? organization,
            Project? project,
            string? noGroupLabel,
            int? projectCount = null,
            bool projectsTruncated = false)
        {
            Group = group;
            Organization = organization;
            Project = project;
            NoGroupHeaderLabel = noGroupLabel;
            ProjectCount = projectCount;
            ProjectsTruncated = projectsTruncated;
        }

        public static ProjectDisplayRow ForGroup(ProjectGroup g, int projectCount = 0, bool projectsTruncated = false) =>
            new(g, null, null, null, projectCount, projectsTruncated);

        public static ProjectDisplayRow ForOrganization(Organization o, int projectCount = 0, bool projectsTruncated = false) =>
            new(null, o, null, null, projectCount, projectsTruncated);

        public static ProjectDisplayRow ForProject(Project p) => new(null, null, p, null);
        public static ProjectDisplayRow ForNoGroupHeader(string label, int? projectCount = null, bool projectsTruncated = false) =>
            new(null, null, null, label, projectCount, projectsTruncated);
    }
}
