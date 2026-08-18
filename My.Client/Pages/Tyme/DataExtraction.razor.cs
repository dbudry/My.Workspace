using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using MudBlazor;
using My.Client.Extensions;
using My.Shared.Constants;
using My.Shared.Dtos.Analytics;
using My.Shared.Dtos.Organization;
using My.Shared.Dtos.Project;
using My.Shared.Dtos.ProjectGroup;
using My.Shared.Dtos.Paging;
using My.Shared.Helpers;
using My.Shared.Rules;
using MiniExcelLibs;

namespace My.Client.Pages.Tyme;

public partial class DataExtraction
{
    private bool isLoading = true;
    private bool isExporting;
    private bool isAuthorized;

    private DateTime? dateFrom;
    private DateTime? dateTo;
    private bool includeArchived;
    private string? validationMessage;

    private HashSet<string> selectedEntities = new(StringComparer.Ordinal);
    private HashSet<string> selectedUserIds = new(StringComparer.Ordinal);
    private bool selectAllEmployeesChecked;
    private bool selectNoneEmployeesChecked = true;
    private string employeeSearchText = "";

    private List<FilterOption> organizationOptions = [];
    private List<FilterOption> projectGroupOptions = [];
    private List<FilterOption> projectOptions = [];
    private List<UserOption> userOptions = [];

    private static readonly (string Key, string Label)[] referenceEntities =
    [
        (TymeDataExtractionRules.ApplicationUsers, "Application Users"),
        (TymeDataExtractionRules.Organizations, "Organizations"),
        (TymeDataExtractionRules.ProjectGroups, "Project Groups"),
        (TymeDataExtractionRules.Projects, "Projects")
    ];

    private static readonly (string Key, string Label)[] transactionalEntities =
    [
        (TymeDataExtractionRules.TrackedTasks, "Tracked Tasks"),
        (TymeDataExtractionRules.TrackedTaskAliases, "Tracked Task Aliases"),
        (TymeDataExtractionRules.TrackedTaskCorrectionAudits, "Correction Audits"),
        (TymeDataExtractionRules.TimeSubmissions, "Time Submissions")
    ];

    private bool requiresDateRange => TymeDataExtractionRules.RequiresDateRange(selectedEntities);

    private bool dateRangeFilterEnabled => requiresDateRange;

    private bool employeesFilterEnabled =>
        TymeDataExtractionRules.SupportsEmployeesFilter(selectedEntities);

    private bool includeArchivedFilterEnabled =>
        TymeDataExtractionRules.SupportsIncludeArchivedFilter(selectedEntities);

    private bool organizationFilterEnabled =>
        TymeDataExtractionRules.SupportsOrganizationFilter(selectedEntities);

    private bool projectFilterEnabled =>
        TymeDataExtractionRules.SupportsProjectFilter(selectedEntities);

    private bool projectGroupFilterEnabled =>
        TymeDataExtractionRules.SupportsProjectGroupFilter(selectedEntities);

    private FilterOption? selectedOrganizationOption
    {
        get => string.IsNullOrEmpty(selectedOrganizationId)
            ? null
            : organizationOptions.FirstOrDefault(o => o.Id == selectedOrganizationId);
        set => selectedOrganizationId = value?.Id;
    }

    private FilterOption? selectedProjectGroupOption
    {
        get => string.IsNullOrEmpty(selectedProjectGroupId)
            ? null
            : projectGroupOptions.FirstOrDefault(g => g.Id == selectedProjectGroupId);
        set => selectedProjectGroupId = value?.Id;
    }

    private FilterOption? selectedProjectOption
    {
        get => string.IsNullOrEmpty(selectedProjectId)
            ? null
            : projectOptions.FirstOrDefault(p => p.Id == selectedProjectId);
        set => selectedProjectId = value?.Id;
    }

    private string? selectedOrganizationId;
    private string? selectedProjectGroupId;
    private string? selectedProjectId;

    private IEnumerable<UserOption> FilteredEmployeeOptions =>
        string.IsNullOrWhiteSpace(employeeSearchText)
            ? userOptions
            : userOptions.Where(u =>
                u.UserName.Contains(employeeSearchText, StringComparison.OrdinalIgnoreCase));

    private string EmployeeSelectionLabel =>
        selectedUserIds.Count switch
        {
            0 => "All employees",
            1 => userOptions.FirstOrDefault(u => selectedUserIds.Contains(u.UserId))?.UserName ?? "1 employee",
            _ => $"{selectedUserIds.Count} employees"
        };

    private HttpClient client = null!;

    [CascadingParameter]
    private Task<AuthenticationState> AuthenticationStateTask { get; set; } = null!;

    [CascadingParameter(Name = "SetPageTitle")]
    private Action<string>? SetPageTitle { get; set; }

    [Inject] private NavigationManager Navigation { get; set; } = null!;
    [Inject] private IHttpClientFactory ClientFactory { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IJSRuntime JS { get; set; } = null!;

    protected override async Task OnInitializedAsync()
    {
        var authState = await AuthenticationStateTask;
        if (authState.User.Identity is not { IsAuthenticated: true })
        {
            Navigation.NavigateTo($"{Navigation.BaseUri}auth/login", true);
            return;
        }

        isAuthorized = Constants.Roles.HasScopedAccess(authState.User, Constants.Scopes.Tyme, Constants.Roles.Admin);
        client = ClientFactory.CreateClient(Constants.API.ClientName);
        SetPageTitle?.Invoke("Data Extraction");
        SetDefaultDateRange();

        if (isAuthorized)
            await Task.WhenAll(LoadFilterOptionsAsync(), LoadEmployeeOptionsAsync());

        isLoading = false;
    }

    private void SetDefaultDateRange()
    {
        var today = DateTime.Today;
        dateFrom = new DateTime(today.Year, today.Month, 1);
        dateTo = today;
    }

    private void OnSelectedOrganizationChanged(FilterOption? option) =>
        selectedOrganizationId = option?.Id;

    private void OnSelectedProjectGroupChanged(FilterOption? option) =>
        selectedProjectGroupId = option?.Id;

    private void OnSelectedProjectChanged(FilterOption? option) =>
        selectedProjectId = option?.Id;

    private void ToggleEntity(string key, bool selected)
    {
        if (selected)
            selectedEntities.Add(key);
        else
            selectedEntities.Remove(key);

        validationMessage = null;
    }

    private async Task LoadFilterOptionsAsync()
    {
        try
        {
            var orgQuery = new ListQueryParameters
            {
                PageNumber = 1,
                PageSize = ListQueryParameters.MaxPageSize,
                SortBy = "Name",
                IncludeArchived = true,
                IncludeInactive = true
            };
            var orgUrl = ListQueryUrlBuilder.Build(Constants.API.Organization.Get, orgQuery);
            var orgResponse = await client.GetFromJsonAsync<PagedResponse<OrganizationDto>>(orgUrl);
            organizationOptions = (orgResponse?.Items ?? [])
                .Select(o => new FilterOption { Id = o.OrganizationId, Name = o.Name })
                .OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var groups = await client.GetFromJsonAsync<List<ProjectGroupDto>>(Constants.API.ProjectGroup.Get);
            projectGroupOptions = (groups ?? [])
                .Select(g => new FilterOption { Id = g.ProjectGroupId, Name = g.Name })
                .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var projectQuery = new ListQueryParameters
            {
                PageNumber = 1,
                PageSize = ListQueryParameters.MaxPageSize,
                SortBy = "Name",
                IncludeArchived = true,
                IncludeInactive = true
            };
            var projectUrl = ListQueryUrlBuilder.Build(Constants.API.Project.Get, projectQuery);
            var projectResponse = await client.GetFromJsonAsync<PagedResponse<ProjectDto>>(projectUrl);
            projectOptions = (projectResponse?.Items ?? [])
                .Select(p => new FilterOption
                {
                    Id = p.ProjectId,
                    Name = p.Name,
                    Slug = p.Slug,
                    ParentId = p.OrganizationId,
                    GroupId = p.ProjectGroupId
                })
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            Snackbar.AddApiError(ex, "Couldn't load filter options.");
        }
    }

    private async Task LoadEmployeeOptionsAsync()
    {
        try
        {
            var response = await client.GetAsync(Constants.API.Analytics.GetManageableEmployees);
            response.EnsureSuccessStatusCode();
            var employees = await response.Content.ReadFromJsonAsync<List<ManageableEmployeeDto>>();

            userOptions = (employees ?? [])
                .Select(e => new UserOption { UserId = e.UserId, UserName = e.UserName })
                .OrderBy(u => u.UserName)
                .ToList();
        }
        catch (Exception ex)
        {
            Snackbar.AddApiError(ex, "Couldn't load employees.");
        }
    }

    private async Task ExportExcelAsync()
    {
        validationMessage = TymeDataExtractionRules.ValidateRequest(selectedEntities, dateFrom, dateTo);
        if (validationMessage is not null)
            return;

        isExporting = true;
        try
        {
            var userIds = selectedUserIds.Count > 0 ? selectedUserIds : null;
            var url = Constants.API.Analytics.ConstructUrlForTymeDataExtraction(
                selectedEntities,
                dateFrom,
                dateTo,
                includeArchived,
                selectedOrganizationId,
                selectedProjectGroupId,
                selectedProjectId,
                userIds);

            var response = await client.GetAsync($"{client.BaseAddress}{url}");
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                validationMessage = string.IsNullOrWhiteSpace(error) ? "Export request failed." : error.Trim('"');
                return;
            }

            var exportData = await response.Content.ReadFromJsonAsync<TymeDataExportDto>()
                ?? new TymeDataExportDto();

            var sheets = new Dictionary<string, object>();
            if (selectedEntities.Contains(TymeDataExtractionRules.ApplicationUsers))
                sheets["ApplicationUsers"] = exportData.ApplicationUsers;
            if (selectedEntities.Contains(TymeDataExtractionRules.Organizations))
                sheets["Organizations"] = exportData.Organizations;
            if (selectedEntities.Contains(TymeDataExtractionRules.ProjectGroups))
                sheets["ProjectGroups"] = exportData.ProjectGroups;
            if (selectedEntities.Contains(TymeDataExtractionRules.Projects))
                sheets["Projects"] = exportData.Projects;
            if (selectedEntities.Contains(TymeDataExtractionRules.TrackedTasks))
                sheets["TrackedTasks"] = exportData.TrackedTasks;
            if (selectedEntities.Contains(TymeDataExtractionRules.TrackedTaskAliases))
                sheets["TrackedTaskAliases"] = exportData.TrackedTaskAliases;
            if (selectedEntities.Contains(TymeDataExtractionRules.TrackedTaskCorrectionAudits))
                sheets["TrackedTaskCorrectionAudits"] = exportData.TrackedTaskCorrectionAudits;
            if (selectedEntities.Contains(TymeDataExtractionRules.TimeSubmissions))
                sheets["TimeSubmissions"] = exportData.TimeSubmissions;

            using var stream = new MemoryStream();
            await stream.SaveAsAsync(sheets);

            var base64 = Convert.ToBase64String(stream.ToArray());
            var dateSuffix = requiresDateRange
                ? $"_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}"
                : $"_{DateTime.Today:yyyyMMdd}";
            var fileName = $"TymeDataExtraction{dateSuffix}.xlsx";

            await JS.InvokeVoidAsync("eval",
                $"var a=document.createElement('a');" +
                $"a.href='data:application/vnd.openxmlformats-officedocument.spreadsheetml.sheet;base64,{base64}';" +
                $"a.download='{fileName}';" +
                $"document.body.appendChild(a);a.click();document.body.removeChild(a);");

            Snackbar.Add("Export ready.", Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.AddApiError(ex, "Couldn't export data.");
        }
        finally
        {
            isExporting = false;
        }
    }

    private void OnSelectAllEmployeesChanged(bool value)
    {
        if (!value) return;
        selectedUserIds = userOptions.Select(u => u.UserId).ToHashSet();
        selectAllEmployeesChecked = true;
        selectNoneEmployeesChecked = false;
    }

    private void OnSelectNoneEmployeesChanged(bool value)
    {
        if (!value) return;
        selectedUserIds.Clear();
        selectAllEmployeesChecked = false;
        selectNoneEmployeesChecked = true;
    }

    private void OnEmployeeCheckboxChanged(string userId, bool selected)
    {
        if (selected)
            selectedUserIds.Add(userId);
        else
            selectedUserIds.Remove(userId);

        selectAllEmployeesChecked = selectedUserIds.Count == userOptions.Count && userOptions.Count > 0;
        selectNoneEmployeesChecked = selectedUserIds.Count == 0;
    }

    private Task<IEnumerable<FilterOption>> SearchOrganizationOptions(string? value, CancellationToken token) =>
        SearchFilterOptions(organizationOptions, value);

    private Task<IEnumerable<FilterOption>> SearchProjectGroupOptions(string? value, CancellationToken token) =>
        SearchFilterOptions(projectGroupOptions, value);

    private Task<IEnumerable<FilterOption>> SearchProjectOptions(string? value, CancellationToken token)
    {
        var pool = projectOptions.Where(p =>
            (string.IsNullOrEmpty(selectedOrganizationId) || p.ParentId == selectedOrganizationId) &&
            (string.IsNullOrEmpty(selectedProjectGroupId) || p.GroupId == selectedProjectGroupId));

        return SearchFilterOptions(pool, value);
    }

    private static Task<IEnumerable<FilterOption>> SearchFilterOptions(IEnumerable<FilterOption> pool, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Task.FromResult(pool);

        return Task.FromResult(pool.Where(o =>
            o.SearchText.Contains(value, StringComparison.OrdinalIgnoreCase)));
    }

    private sealed class FilterOption
    {
        public string Id { get; init; } = null!;
        public string Name { get; init; } = null!;
        public string? Slug { get; init; }
        public string? ParentId { get; init; }
        public string? GroupId { get; init; }
        public string SearchText => string.IsNullOrEmpty(Slug) ? Name : $"{Name} {Slug}";
    }

    private sealed class UserOption
    {
        public string UserId { get; init; } = null!;
        public string UserName { get; init; } = null!;
    }
}