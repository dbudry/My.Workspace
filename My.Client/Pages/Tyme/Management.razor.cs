using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using MudBlazor;
using My.Client.Components.TrackedTasks;
using My.Client.Extensions;
using My.Client.Services;
using My.Shared.Constants;
using My.Shared.Dtos.Analytics;
using My.Shared.Dtos.Organization;
using My.Shared.Dtos.Paging;
using My.Shared.Dtos.ProjectGroup;
using My.Shared.Helpers;
using My.Shared.Rules;
using System.Text;
using System.Text.Json;
namespace My.Client.Pages.Tyme
{
    public partial class Management
    {
        private bool isLoading = true;
        private bool isRefreshing;
        private bool isAuthorized = false;

        private DateTime? dateFrom;
        private DateTime? dateTo;
        private HashSet<string> selectedUserIds = new();
        private bool selectAllEmployeesChecked;
        private bool selectNoneEmployeesChecked = true;
        private string employeeSearchText = "";
        private string? selectedOrganizationId;
        private string? selectedProjectGroupId;
        private string? selectedProjectId;
        private SubmissionFilter selectedSubmissionFilter = SubmissionFilter.All;
        private BillableFilter selectedBillableFilter = BillableFilter.All;

        // Column visibility for the Task Details table — persisted per user via LocalStorage
        // so the manager's last picked column set is restored on next visit.
        private const string ColumnPrefsStorageKey = "management.columns";

        private static readonly (string Key, string Label)[] availableColumns = new[]
        {
            ("date", "Date"),
            ("employee", "Employee"),
            ("task", "Task"),
            ("project", "Project"),
            ("organization", "Organization"),
            ("group", "Group"),
            ("duration", "Duration"),
            ("billable", "Billable"),
            ("status", "Status")
        };

        private HashSet<string> visibleColumns = availableColumns.Select(c => c.Key).ToHashSet();
        private const int TaskDetailsTabIndex = 1;
        private int activeReportTab;

        private bool IsColumnVisible(string key) => visibleColumns.Contains(key);

        private async Task ToggleColumn(string key, bool show)
        {
            if (show) visibleColumns.Add(key);
            else visibleColumns.Remove(key);
            await LocalStorage.SetItemAsync(ColumnPrefsStorageKey, visibleColumns.ToArray());
        }

        private List<AdminTaskItem> allTasks = new();
        private List<AdminTaskItem> filteredTasks = new();
        private List<UserSummaryItem> userSummaries = new();
        private List<UserOption> userOptions = new();
        private List<FilterOption> organizationOptions = new();
        private List<FilterOption> projectGroupOptions = new();
        private List<FilterOption> projectOptions = new();
        private Dictionary<string, bool> showingOriginalForTask = new();
        private ManagerCorrectionSettings correctionSettings = ManagerCorrectionSettings.Defaults;
        private bool canAliasCreateOrUpdate;
        private bool canDirectCreateOrUpdate;

        private List<FilterOption> filteredProjectOptions =>
            projectOptions.Where(p =>
                (string.IsNullOrEmpty(selectedOrganizationId) || p.ParentId == selectedOrganizationId) &&
                (string.IsNullOrEmpty(selectedProjectGroupId) || p.GroupId == selectedProjectGroupId))
            .ToList();

        private IEnumerable<UserOption> FilteredEmployeeOptions =>
            string.IsNullOrWhiteSpace(employeeSearchText)
                ? userOptions
                : userOptions.Where(u =>
                    u.UserName.Contains(employeeSearchText, StringComparison.OrdinalIgnoreCase));

        /// <summary>Project autocomplete model — pairs with selectedProjectId for the actual filter value.</summary>
        private FilterOption? selectedOrganizationOption =>
            string.IsNullOrEmpty(selectedOrganizationId)
                ? null
                : organizationOptions.FirstOrDefault(o => o.Id == selectedOrganizationId);

        private FilterOption? selectedProjectGroupOption =>
            string.IsNullOrEmpty(selectedProjectGroupId)
                ? null
                : projectGroupOptions.FirstOrDefault(g => g.Id == selectedProjectGroupId);

        private FilterOption? selectedProjectOption =>
            string.IsNullOrEmpty(selectedProjectId)
                ? null
                : projectOptions.FirstOrDefault(p => p.Id == selectedProjectId);

        private void OnSelectedOrganizationChanged(FilterOption? option)
        {
            selectedOrganizationId = option?.Id;
            ApplyClientFilters();
        }

        private void OnSelectedProjectGroupChanged(FilterOption? option)
        {
            selectedProjectGroupId = option?.Id;
            ApplyClientFilters();
        }

        private void OnSelectedProjectChanged(FilterOption? option)
        {
            selectedProjectId = option?.Id;
            ApplyClientFilters();
        }

        private static Task<IEnumerable<FilterOption>> SearchFilterOptions(
            IEnumerable<FilterOption> pool, string? value, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(value))
                return Task.FromResult(pool);

            return Task.FromResult(pool.Where(o =>
                o.Name.Contains(value, StringComparison.OrdinalIgnoreCase)));
        }

        private Task<IEnumerable<FilterOption>> SearchOrganizationOptions(string? value, CancellationToken token) =>
            SearchFilterOptions(organizationOptions, value, token);

        private Task<IEnumerable<FilterOption>> SearchProjectGroupOptions(string? value, CancellationToken token) =>
            SearchFilterOptions(projectGroupOptions, value, token);

        /// <summary>
        /// Autocomplete search: matches typed text against project name or slug.
        /// Respects the cascading filter (Organization / Project Group) the manager
        /// has set so they can narrow to "projects under Org X" then type.
        /// </summary>
        private Task<IEnumerable<FilterOption>> SearchProjectOptions(string? value, CancellationToken token)
        {
            var pool = (IEnumerable<FilterOption>)filteredProjectOptions;
            if (string.IsNullOrWhiteSpace(value))
                return Task.FromResult(pool);

            return Task.FromResult(pool.Where(p =>
                p.SearchText.Contains(value, StringComparison.OrdinalIgnoreCase)));
        }

        HttpClient client = null!;

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
        private IJSRuntime JS { get; set; } = null!;

        [Inject]
        private UserSettingsService SettingsService { get; set; } = null!;

        [Inject]
        private IDialogService DialogService { get; set; } = null!;

        [Inject]
        private LocalStorageService LocalStorage { get; set; } = null!;

        [Inject]
        private AppSettingsCache AppSettingsCache { get; set; } = null!;

        #endregion

        protected override async Task OnInitializedAsync()
        {
            var authState = await AuthenticationStateTask;
            var user = authState.User;

            if (user.Identity != null && !user.Identity.IsAuthenticated)
            {
                Navigation.NavigateTo($"{Navigation.BaseUri}auth/login", true);
                return;
            }

            client = ClientFactory.CreateClient(Constants.API.ClientName);

            SetPageTitle?.Invoke("Management");

            await SettingsService.GetSettingsAsync();
            SetDefaultDateRange();

            try
            {
                var savedColumns = await LocalStorage.GetItemAsync<string[]>(ColumnPrefsStorageKey);
                if (savedColumns is { Length: > 0 })
                    visibleColumns = savedColumns.ToHashSet();
            }
            catch { /* fall back to "show all" defaults */ }

            await LoadCorrectionSettingsAsync();
            await Task.WhenAll(LoadEmployeeOptionsAsync(), LoadFilterOptionsAsync());
            await LoadTaskDataAsync();
            isLoading = false;
        }

        private async Task LoadCorrectionSettingsAsync()
        {
            try
            {
                var settings = await AppSettingsCache.GetAsync();
                correctionSettings = ManagerCorrectionRules.Parse(
                    settings.ToDictionary(s => s.Key, s => s.Value ?? string.Empty, StringComparer.OrdinalIgnoreCase));
                canAliasCreateOrUpdate = correctionSettings.CanCreateOrUpdateAlias;
                canDirectCreateOrUpdate = correctionSettings.CanCreateOrUpdateDirect;
            }
            catch
            {
                correctionSettings = ManagerCorrectionSettings.Defaults;
                canAliasCreateOrUpdate = true;
                canDirectCreateOrUpdate = false;
            }
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
                    IncludeArchived = false,
                    IncludeInactive = false
                };
                var orgUrl = ListQueryUrlBuilder.Build(Constants.API.Organization.Get, orgQuery);
                var orgResponse = await client.GetFromJsonAsync<PagedResponse<OrganizationDto>>(orgUrl);
                organizationOptions = (orgResponse?.Items ?? Array.Empty<OrganizationDto>())
                    .Select(o => new FilterOption { Id = o.OrganizationId, Name = o.Name })
                    .OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var groups = await client.GetFromJsonAsync<List<ProjectGroupDto>>(Constants.API.ProjectGroup.Get);
                projectGroupOptions = (groups ?? new())
                    .Select(g => new FilterOption { Id = g.ProjectGroupId, Name = g.Name })
                    .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't load report filter options.");
            }
        }

        private async Task LoadEmployeeOptionsAsync()
        {
            try
            {
                var response = await client.GetAsync(Constants.API.Analytics.GetManageableEmployees);
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                    response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    isAuthorized = false;
                    return;
                }

                response.EnsureSuccessStatusCode();
                var employees = await response.Content.ReadFromJsonAsync<List<ManageableEmployeeDto>>();

                userOptions = (employees ?? new())
                    .Select(e => new UserOption { UserId = e.UserId, UserName = e.UserName })
                    .OrderBy(u => u.UserName)
                    .ToList();

                SyncEmployeeSelectionState();
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't load employees.");
            }
        }

        private async Task LoadTaskDataAsync()
        {
            try
            {
                var url = Constants.API.Analytics.ConstructUrlForAllUsersTasks(dateFrom, dateTo);
                var response = await client.GetAsync($"{client.BaseAddress}{url}");

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                    response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    isAuthorized = false;
                    return;
                }

                isAuthorized = true;

                if (response.IsSuccessStatusCode)
                {
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var json = await response.Content.ReadAsStringAsync();
                    allTasks = JsonSerializer.Deserialize<List<AdminTaskItem>>(json, options) ?? new();

                    MergeTaskUsersIntoEmployeeOptions();

                    // Build project options (with parent org and group for cascading)
                    projectOptions = allTasks
                        .Where(t => !string.IsNullOrEmpty(t.ProjectId))
                        .Select(t => new FilterOption
                        {
                            Id = t.ProjectId!,
                            Name = t.ProjectName,
                            Slug = t.ProjectSlug,
                            ParentId = t.OrganizationId,
                            GroupId = t.ProjectGroupId
                        })
                        .DistinctBy(p => p.Id)
                        .OrderBy(p => p.Name)
                        .ToList();

                    ApplyClientFilters();
                }
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't load management data.");
            }
        }

        private string EmployeeSelectionLabel =>
            FormatSelectedEmployees(selectedUserIds.ToList());

        private void OnSelectAllEmployeesChanged(bool value)
        {
            selectAllEmployeesChecked = value;
            if (value)
            {
                selectNoneEmployeesChecked = false;
                if (string.IsNullOrWhiteSpace(employeeSearchText))
                    selectedUserIds = userOptions.Select(u => u.UserId).ToHashSet();
                else
                {
                    foreach (var u in FilteredEmployeeOptions)
                        selectedUserIds.Add(u.UserId);
                    selectedUserIds = selectedUserIds.ToHashSet();
                }
            }

            SyncEmployeeSelectionState();
            ApplyClientFilters();
        }

        private void OnSelectNoneEmployeesChanged(bool value)
        {
            selectNoneEmployeesChecked = value;
            if (value)
            {
                selectAllEmployeesChecked = false;
                selectedUserIds = new HashSet<string>();
            }

            ApplyClientFilters();
        }

        private void OnEmployeeCheckboxChanged(string userId, bool isChecked)
        {
            if (isChecked && selectNoneEmployeesChecked)
                selectNoneEmployeesChecked = false;

            if (isChecked)
                selectedUserIds.Add(userId);
            else
                selectedUserIds.Remove(userId);

            if (!isChecked && selectAllEmployeesChecked)
                selectAllEmployeesChecked = false;

            if (userOptions.Count > 0 && userOptions.All(u => selectedUserIds.Contains(u.UserId)))
            {
                selectAllEmployeesChecked = true;
                selectNoneEmployeesChecked = false;
            }
            else if (selectedUserIds.Count == 0)
            {
                selectNoneEmployeesChecked = true;
                selectAllEmployeesChecked = false;
            }
            else
            {
                selectAllEmployeesChecked = false;
            }

            selectedUserIds = selectedUserIds.ToHashSet();
            ApplyClientFilters();
        }

        private void MergeTaskUsersIntoEmployeeOptions()
        {
            var known = userOptions.Select(u => u.UserId).ToHashSet();
            foreach (var taskUser in allTasks
                         .Select(t => new UserOption { UserId = t.UserId, UserName = t.UserName })
                         .DistinctBy(u => u.UserId))
            {
                if (known.Add(taskUser.UserId))
                    userOptions.Add(taskUser);
            }

            userOptions = userOptions.OrderBy(u => u.UserName).ToList();
            SyncEmployeeSelectionState();
        }

        private void SyncEmployeeSelectionState()
        {
            var validIds = userOptions.Select(u => u.UserId).ToHashSet();
            selectedUserIds = selectedUserIds.Where(id => validIds.Contains(id)).ToHashSet();

            if (selectedUserIds.Count == 0)
            {
                selectAllEmployeesChecked = false;
                selectNoneEmployeesChecked = true;
            }
            else if (userOptions.Count > 0 && userOptions.All(u => selectedUserIds.Contains(u.UserId)))
            {
                selectAllEmployeesChecked = true;
                selectNoneEmployeesChecked = false;
            }
            else
            {
                selectAllEmployeesChecked = false;
                selectNoneEmployeesChecked = false;
            }
        }

        private string FormatSelectedEmployees(IReadOnlyList<string> selectedIds)
        {
            if (selectedIds.Count == 0)
                return "No employees selected";

            if (userOptions.Count > 0 && userOptions.All(u => selectedIds.Contains(u.UserId)))
                return "All employees";

            var nameById = userOptions.ToDictionary(u => u.UserId, u => u.UserName);
            return string.Join(", ", selectedIds
                .Select(id => nameById.GetValueOrDefault(id, id))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
        }

        private void ApplyClientFilters()
        {
            filteredTasks = allTasks.Where(t =>
            {
                if (selectedUserIds.Count == 0) return false;
                if (!selectedUserIds.Contains(t.UserId)) return false;
                if (!string.IsNullOrEmpty(selectedOrganizationId) && t.OrganizationId != selectedOrganizationId) return false;
                if (!string.IsNullOrEmpty(selectedProjectGroupId) && t.ProjectGroupId != selectedProjectGroupId) return false;
                if (!string.IsNullOrEmpty(selectedProjectId) && t.ProjectId != selectedProjectId) return false;
                if (selectedSubmissionFilter == SubmissionFilter.Submitted && !t.IsMonthSubmitted) return false;
                if (selectedSubmissionFilter == SubmissionFilter.Unsubmitted && t.IsMonthSubmitted) return false;
                if (selectedBillableFilter == BillableFilter.Billable && !t.IsBillable) return false;
                if (selectedBillableFilter == BillableFilter.NonBillable && t.IsBillable) return false;
                return true;
            }).ToList();

            // Build user summaries
            userSummaries = filteredTasks
                .GroupBy(t => new { t.UserId, t.UserName })
                .Select(g => new UserSummaryItem
                {
                    UserId = g.Key.UserId,
                    UserName = g.Key.UserName,
                    TaskCount = g.Count(),
                    TotalTimeSeconds = g.Sum(t => t.DurationSeconds),
                    TotalTimeFormatted = FormatDuration(g.Sum(t => t.DurationSeconds))
                })
                .OrderByDescending(u => u.TotalTimeSeconds)
                .ToList();
        }

        private async Task ApplyFilters()
        {
            isRefreshing = true;
            try
            {
                await LoadTaskDataAsync();
            }
            finally
            {
                isRefreshing = false;
            }
        }

        private void SetDefaultDateRange()
        {
            var lastMonth = SettingsService.GetUserToday().AddMonths(-1);
            dateFrom = new DateTime(lastMonth.Year, lastMonth.Month, 1);
            dateTo = new DateTime(lastMonth.Year, lastMonth.Month, DateTime.DaysInMonth(lastMonth.Year, lastMonth.Month));
        }

        private async Task ClearFiltersAsync()
        {
            SetDefaultDateRange();
            selectedOrganizationId = null;
            selectedProjectGroupId = null;
            selectedProjectId = null;
            selectedSubmissionFilter = SubmissionFilter.All;
            selectedBillableFilter = BillableFilter.All;

            selectedUserIds = new HashSet<string>();
            selectAllEmployeesChecked = false;
            selectNoneEmployeesChecked = true;
            employeeSearchText = "";

            isRefreshing = true;
            try
            {
                await LoadTaskDataAsync();
            }
            finally
            {
                isRefreshing = false;
            }
        }

        private void OnSubmissionFilterChanged(SubmissionFilter value)
        {
            selectedSubmissionFilter = value;
            ApplyClientFilters();
        }

        private void OnBillableFilterChanged(BillableFilter value)
        {
            selectedBillableFilter = value;
            ApplyClientFilters();
        }

        public enum SubmissionFilter
        {
            All,
            Submitted,
            Unsubmitted
        }

        public enum BillableFilter
        {
            All,
            Billable,
            NonBillable
        }

        private async Task DownloadCsvAsync()
        {
            var sb = new StringBuilder();

            if (activeReportTab == TaskDetailsTabIndex)
            {
                var columns = availableColumns.Where(c => IsColumnVisible(c.Key)).ToList();
                sb.AppendLine(string.Join(",", columns.Select(c => $"\"{Escape(c.Label)}\"")));

                foreach (var task in filteredTasks)
                {
                    var row = GetTaskRowDisplay(task);
                    var cells = columns.Select(c => $"\"{Escape(GetExportCellValue(row, c.Key))}\"");
                    sb.AppendLine(string.Join(",", cells));
                }
            }
            else
            {
                sb.AppendLine("\"Employee\",\"Tasks\",\"Total Time\"");
                foreach (var summary in userSummaries)
                {
                    sb.AppendLine(
                        $"\"{Escape(summary.UserName)}\"," +
                        $"{summary.TaskCount}," +
                        $"\"{Escape(summary.TotalTimeFormatted)}\"");
                }
            }

            await TriggerCsvDownloadAsync(sb.ToString());
        }

        private async Task TriggerCsvDownloadAsync(string csv)
        {
            var bytes = Encoding.UTF8.GetBytes(csv);
            var base64 = Convert.ToBase64String(bytes);
            var fileName = $"Management_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.csv";

            await JS.InvokeVoidAsync("eval",
                $"var a=document.createElement('a');" +
                $"a.href='data:text/csv;base64,{base64}';" +
                $"a.download='{fileName}';" +
                $"document.body.appendChild(a);a.click();document.body.removeChild(a);");
        }

        private static string? GetExportCellValue(TaskRowDisplay row, string columnKey) =>
            columnKey switch
            {
                "date" => row.Date,
                "employee" => row.Employee,
                "task" => row.Task,
                "project" => row.Project,
                "organization" => row.Organization,
                "group" => row.Group,
                "duration" => row.Duration,
                "billable" => row.Billable,
                "status" => row.Status,
                _ => null
            };

        private TaskRowDisplay GetTaskRowDisplay(AdminTaskItem task)
        {
            var showOriginal = showingOriginalForTask.GetValueOrDefault(task.TaskId, false);
            var isCorrected = task.IsAliased || task.IsDirectCorrected;

            var displayDate = isCorrected && showOriginal && task.OriginalStartDate.HasValue
                ? task.OriginalStartDate.Value
                : task.StartDate;
            var displayProject = isCorrected && showOriginal && task.OriginalProjectName != null
                ? task.OriginalProjectName
                : task.ProjectName;
            var displayDuration = isCorrected && showOriginal && task.OriginalDurationSeconds.HasValue
                ? task.OriginalDurationSeconds.Value
                : task.DurationSeconds;
            var displayName = isCorrected && showOriginal && task.OriginalName != null
                ? task.OriginalName
                : task.Name;
            var displayIsBillable = isCorrected && showOriginal && task.OriginalIsBillable.HasValue
                ? task.OriginalIsBillable.Value
                : task.IsBillable;

            var statusParts = new List<string>
            {
                task.IsMonthSubmitted ? "Submitted" : "Unsubmitted"
            };
            if (task.IsAliased)
                statusParts.Add(showOriginal ? "Original" : "Altered");
            if (task.IsDirectCorrected)
                statusParts.Add(showOriginal ? "Original" : "Adjusted");

            return new TaskRowDisplay
            {
                Date = displayDate.ToLocalTime().ToString("MM/dd/yyyy"),
                Employee = task.UserName,
                Task = displayName,
                Project = displayProject,
                Organization = task.OrganizationName,
                Group = task.ProjectGroupName,
                Duration = FormatDuration(displayDuration),
                Billable = displayIsBillable ? "Yes" : "No",
                Status = string.Join(", ", statusParts)
            };
        }

        private static string Escape(string? value) =>
            value?.Replace("\"", "\"\"") ?? "";

        private void ToggleOriginal(string taskId)
        {
            showingOriginalForTask[taskId] = !showingOriginalForTask.GetValueOrDefault(taskId, false);
        }

        private async Task OpenAliasDialog(AdminTaskItem item)
        {
            var parameters = new DialogParameters
            {
                ["TaskId"] = item.TaskId,
                ["OriginalUserName"] = item.UserName,
                ["InitialName"] = item.IsAliased ? item.Name : (item.OriginalName ?? item.Name),
                ["InitialStartDate"] = item.StartDate,
                ["InitialDuration"] = TimeSpan.FromSeconds(item.DurationSeconds),
                ["InitialProjectId"] = item.ProjectId,
                ["InitialIsBillable"] = item.IsBillable,
                ["IsExisting"] = item.IsAliased
            };
            var dialog = await DialogService.ShowAsync<EditAliasDialog>(
                "Update (Alias)",
                parameters,
                new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true });

            var result = await dialog.Result;
            if (result is { Canceled: false })
            {
                Snackbar.Add("Alias saved.", Severity.Success);
                await ApplyFilters();
            }
        }

        private async Task OpenDirectDialog(AdminTaskItem item)
        {
            var parameters = new DialogParameters
            {
                ["TaskId"] = item.TaskId,
                ["OriginalUserName"] = item.UserName,
                ["InitialName"] = item.Name,
                ["InitialStartDate"] = item.StartDate,
                ["InitialDuration"] = TimeSpan.FromSeconds(item.DurationSeconds),
                ["InitialProjectId"] = item.ProjectId,
                ["InitialIsBillable"] = item.IsBillable,
                ["IsExisting"] = item.IsDirectCorrected
            };
            var dialog = await DialogService.ShowAsync<EditDirectDialog>(
                "Update (Direct)",
                parameters,
                new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true });

            var result = await dialog.Result;
            if (result is { Canceled: false })
            {
                Snackbar.Add("Correction saved.", Severity.Success);
                await ApplyFilters();
            }
        }

        private async Task RevertDirectCorrectionAsync(AdminTaskItem item)
        {
            var confirm = await DialogService.ShowMessageBoxAsync(
                "Revert correction?",
                "This will restore the employee's original values for this entry.",
                yesText: "Revert", cancelText: "Cancel");
            if (confirm != true) return;

            try
            {
                var resp = await client.DeleteAsync(
                    $"{Constants.API.TrackedTask.DeleteManagerCorrection}{item.TaskId}/manager-correction");
                resp.EnsureSuccessStatusCode();
                Snackbar.Add("Correction reverted.", Severity.Success);
                showingOriginalForTask.Remove(item.TaskId);
                await ApplyFilters();
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't revert correction.");
            }
        }

        private async Task DeleteAliasAsync(AdminTaskItem item)
        {
            var confirm = await DialogService.ShowMessageBoxAsync(
                "Delete alias?",
                "This will revert this time entry to the user's original values.",
                yesText: "Delete", cancelText: "Cancel");
            if (confirm != true) return;

            try
            {
                var resp = await client.DeleteAsync($"{Constants.API.TrackedTaskAlias.Delete}{item.TaskId}");
                resp.EnsureSuccessStatusCode();
                Snackbar.Add("Alias removed.", Severity.Success);
                showingOriginalForTask.Remove(item.TaskId);
                await ApplyFilters();
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't remove alias.");
            }
        }

        private static string FormatDuration(double totalSeconds)
        {
            var ts = TimeSpan.FromSeconds(totalSeconds);
            int hours = (ts.Days * 24) + ts.Hours;
            return $"{hours:00}:{ts.Minutes:00}";
        }

        private sealed class TaskRowDisplay
        {
            public string Date { get; init; } = "";
            public string Employee { get; init; } = "";
            public string Task { get; init; } = "";
            public string Project { get; init; } = "";
            public string? Organization { get; init; }
            public string? Group { get; init; }
            public string Duration { get; init; } = "";
            public string Billable { get; init; } = "";
            public string Status { get; init; } = "";
        }

        public class AdminTaskItem
        {
            public string TaskId { get; set; } = null!;
            public string Name { get; set; } = null!;
            public double DurationSeconds { get; set; }
            public double Duration { get => DurationSeconds; set => DurationSeconds = value; }
            public DateTime StartDate { get; set; }
            public DateTime? EndDate { get; set; }
            public string ProjectName { get; set; } = "None";
            public string? ProjectSlug { get; set; }
            public string? ProjectId { get; set; }
            public string? OrganizationName { get; set; }
            public string? OrganizationId { get; set; }
            public string? ProjectGroupName { get; set; }
            public string? ProjectGroupId { get; set; }
            public string UserName { get; set; } = "Unknown";
            public string UserId { get; set; } = null!;
            public bool IsAliased { get; set; }
            public bool IsDirectCorrected { get; set; }
            public bool IsMonthSubmitted { get; set; }
            public bool IsBillable { get; set; }
            public bool? OriginalIsBillable { get; set; }
            public DateTime? OriginalStartDate { get; set; }
            public double? OriginalDurationSeconds { get; set; }
            public string? OriginalProjectId { get; set; }
            public string? OriginalProjectName { get; set; }
            public string? OriginalName { get; set; }
        }

        public class UserSummaryItem
        {
            public string UserId { get; set; } = null!;
            public string UserName { get; set; } = null!;
            public int TaskCount { get; set; }
            public double TotalTimeSeconds { get; set; }
            public string TotalTimeFormatted { get; set; } = null!;
        }

        public class UserOption
        {
            public string UserId { get; set; } = null!;
            public string UserName { get; set; } = null!;
        }

        public class FilterOption
        {
            public string Id { get; set; } = null!;
            public string Name { get; set; } = null!;
            public string? Slug { get; set; }
            public string? ParentId { get; set; }
            public string? GroupId { get; set; }

            /// <summary>What we match against when the user types in the autocomplete.</summary>
            public string SearchText => string.IsNullOrEmpty(Slug) ? Name : $"{Name} {Slug}";
        }
    }
}
