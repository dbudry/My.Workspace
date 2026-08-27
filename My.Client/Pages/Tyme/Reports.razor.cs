using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using MudBlazor;
using System.Net.Http.Json;
using System.Text;
using My.Client.Extensions;
using My.Client.Models;
using My.Client.Models.Dashboard;
using My.Client.Models.Paging;
using My.Client.Services;
using My.Shared.Dtos.Analytics;
using My.Shared.Dtos.Paging;
using My.Shared.Dtos.Project;
using My.Shared.Dtos.TrackedTask;
using My.Shared.Constants;
using My.Shared.Rules;

namespace My.Client.Pages.Tyme
{
    public partial class Reports
    {
        private List<TrackedTask> allTasks = new();
        private List<TrackedTask> filteredTasks = new();
        private List<TrackedTask> taskDetailRows = new();
        private List<Project> projects = new();

        private DateTime? dateFrom;
        private DateTime? dateTo;
        private Project? selectedProject;
        private bool isLoading = true;
        private bool canViewTeamReports;
        private string currentUserId = string.Empty;
        private HashSet<string> selectedUserIds = new();
        private bool selectAllEmployeesChecked;
        private bool selectNoneEmployeesChecked;
        private string employeeSearchText = "";
        private List<UserOption> userOptions = new();
        private EmployeeTimeDisplayMode displayMode = EmployeeTimeDisplayMode.Both;

        private const int SummaryTabIndex = 0;
        private const int DetailsTabIndex = 1;
        private const string ActiveTabStorageKey = "reports.activeTab";

        private const string ProjectMixAxisStorageKey = "reports.projectMixAxis";
        private const string ProjectMixValueModeStorageKey = "reports.projectMixValueMode";
        private const string DailyHoursAxisStorageKey = "reports.dailyHoursAxis";
        private const string DailyHoursValueModeStorageKey = "reports.dailyHoursValueMode";

        /// <summary>0 = Summary (analytics), 1 = Details (grid). Matches Management's tab layout.
        /// Persisted per user via LocalStorage so the page reopens on whichever tab they left.</summary>
        private int activeReportTab;

        private string totalTimeFormatted = "00:00";
        private string topProjectName = "None";
        private string avgPerDayFormatted = "00:00";
        private string durationTotalFormatted = "0h";

        /// <summary>Flat per-project totals for the filtered range, built once per filter
        /// change in <see cref="BuildChartData"/>. Both the "Time by Project" doughnut and
        /// the Daily Hours breakdown re-pivot this (or, for Daily Hours, the raw tasks) via
        /// <see cref="ChartPivotRules"/> so all three group and color identically.</summary>
        private List<ProjectDataItem> rawProjectData = new();

        private ChartAxis _projectMixAxis = ChartAxis.Organization;
        private ChartAxis projectMixAxis
        {
            get => _projectMixAxis;
            set
            {
                if (_projectMixAxis == value) return;
                _projectMixAxis = value;
                _ = LocalStorage.SetItemAsync(ProjectMixAxisStorageKey, value.ToString());
                StateHasChanged();
            }
        }

        private ChartValueMode _projectMixValueMode = ChartValueMode.Percent;
        private ChartValueMode projectMixValueMode
        {
            get => _projectMixValueMode;
            set
            {
                if (_projectMixValueMode == value) return;
                _projectMixValueMode = value;
                _ = LocalStorage.SetItemAsync(ProjectMixValueModeStorageKey, value.ToString());
                StateHasChanged();
            }
        }

        private string ProjectMixAxisLabel => ChartPivotRules.AxisLabel(projectMixAxis);

        private List<ProjectDataItem> pivotedProjectChartData => ChartPivotRules.Pivot(rawProjectData, projectMixAxis);

        private string[]? ProjectMixPalette =>
            ChartPivotRules.Palette(pivotedProjectChartData, projectMixAxis, SettingsService.ProjectColorSource);

        private ChartAxis _dailyHoursAxis = ChartAxis.Organization;
        private ChartAxis dailyHoursAxis
        {
            get => _dailyHoursAxis;
            set
            {
                if (_dailyHoursAxis == value) return;
                _dailyHoursAxis = value;
                _ = LocalStorage.SetItemAsync(DailyHoursAxisStorageKey, value.ToString());
                StateHasChanged();
            }
        }

        private ChartValueMode _dailyHoursValueMode = ChartValueMode.Percent;
        private ChartValueMode dailyHoursValueMode
        {
            get => _dailyHoursValueMode;
            set
            {
                if (_dailyHoursValueMode == value) return;
                _dailyHoursValueMode = value;
                _ = LocalStorage.SetItemAsync(DailyHoursValueModeStorageKey, value.ToString());
                StateHasChanged();
            }
        }

        private string DailyHoursAxisLabel => ChartPivotRules.AxisLabel(dailyHoursAxis);

        /// <summary>
        /// Stacked-bar data for the Daily Hours chart: one series per category on the
        /// selected axis, one value per day (last 14 days with data). Percent mode stacks
        /// each day to 100% of that day's total; Duration mode stacks actual hours.
        /// Rebuilt on every access (cheap — the filtered range is small) rather than cached,
        /// so it always reflects the current axis/value-mode toggle without a manual rebuild.
        /// </summary>
        private (List<ChartSeries<double>> Series, string[] Labels, string[]? Palette) BuildDailyStackedData()
        {
            var days = filteredTasks
                .GroupBy(t => ToUserDate(t.StartDate))
                .OrderBy(g => g.Key)
                .Select(g => g.Key)
                .TakeLast(14)
                .ToList();

            if (days.Count == 0)
                return (new List<ChartSeries<double>>(), Array.Empty<string>(), null);

            var dayIndex = new Dictionary<DateTime, int>();
            for (int i = 0; i < days.Count; i++)
                dayIndex[days[i]] = i;

            // key -> (display name, per-day seconds)
            var categories = new Dictionary<string, (string Name, double[] Seconds)>();
            var categoryOrder = new List<string>();

            foreach (var task in filteredTasks)
            {
                var taskDate = ToUserDate(task.StartDate);
                if (!dayIndex.TryGetValue(taskDate, out var idx)) continue;

                var (key, name) = ChartPivotRules.CategoryKey(
                    task.Project?.ProjectId, task.Project?.Name,
                    task.Project?.OrganizationId, task.Project?.OrganizationName,
                    task.Project?.ProjectGroupId, task.Project?.ProjectGroupName,
                    dailyHoursAxis);

                if (!categories.TryGetValue(key, out var entry))
                {
                    entry = (name, new double[days.Count]);
                    categories[key] = entry;
                    categoryOrder.Add(key);
                }

                entry.Seconds[idx] += task.Duration.TotalSeconds;
            }

            // Stable order: biggest category first, so the stack and its legend read
            // consistently with the "Time by Project" doughnut above it.
            var orderedKeys = categoryOrder
                .OrderByDescending(k => categories[k].Seconds.Sum())
                .ToList();

            // Reuse the same Pivot+Palette used for the doughnut so a category (e.g. an
            // organization) gets the same color in both charts on this page.
            var pivoted = ChartPivotRules.Pivot(rawProjectData, dailyHoursAxis);
            var pivotPalette = ChartPivotRules.Palette(pivoted, dailyHoursAxis, SettingsService.ProjectColorSource);
            var colorByKey = new Dictionary<string, string>();
            if (pivotPalette != null)
            {
                for (int i = 0; i < pivoted.Count && i < pivotPalette.Length; i++)
                    colorByKey[pivoted[i].ProjectId] = pivotPalette[i];
            }

            double[] dayTotals = new double[days.Count];
            foreach (var key in orderedKeys)
            {
                var seconds = categories[key].Seconds;
                for (int i = 0; i < days.Count; i++)
                    dayTotals[i] += seconds[i];
            }

            var series = new List<ChartSeries<double>>();
            var palette = new List<string>();
            foreach (var key in orderedKeys)
            {
                var (name, seconds) = categories[key];
                double[] values = dailyHoursValueMode == ChartValueMode.Duration
                    ? seconds.Select(s => s / 3600.0).ToArray()
                    : seconds.Select((s, i) => dayTotals[i] > 0 ? s / dayTotals[i] * 100.0 : 0.0).ToArray();

                series.Add(new ChartSeries<double> { Name = name, Data = values });
                palette.Add(colorByKey.TryGetValue(key, out var color) ? color : ProjectColorRules.FallbackGray);
            }

            var labels = days.Select(d => d.ToString("MM/dd")).ToArray();
            return (series, labels, palette.ToArray());
        }

        private List<ChartSeries<double>> DailySeries => BuildDailyStackedData().Series;
        private string[] DailyLabels => BuildDailyStackedData().Labels;
        private ChartOptions DailyChartOptions
        {
            get
            {
                var palette = BuildDailyStackedData().Palette;
                return palette is { Length: > 0 } ? new ChartOptions { ChartPalette = palette } : new ChartOptions();
            }
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
        private UserSettingsService SettingsService { get; set; } = null!;

        [Inject]
        private ProjectsCache ProjectsCache { get; set; } = null!;

        [Inject]
        private TrackedTasksClient TrackedTasksClient { get; set; } = null!;

        [Inject]
        private AppSettingsCache AppSettingsCache { get; set; } = null!;

        [Inject]
        private IJSRuntime JS { get; set; } = null!;

        [Inject]
        private LocalStorageService LocalStorage { get; set; } = null!;

        #endregion

        protected override async Task OnInitializedAsync()
        {
            var authState = await AuthenticationStateTask;
            var user = authState.User;

            if (user.Identity != null && !user.Identity.IsAuthenticated)
                Navigation.NavigateTo($"{Navigation.BaseUri}auth/login", true);

            currentUserId = user.FindFirst(Constants.Claims.AppUserId)?.Value ?? string.Empty;

            client = ClientFactory.CreateClient(Constants.API.ClientName);

            SetPageTitle?.Invoke("Reports");

            await SettingsService.GetSettingsAsync();
            await LoadDisplayModeFromAppSettingsAsync();

            try
            {
                var savedTab = await LocalStorage.GetItemAsync<int?>(ActiveTabStorageKey);
                if (savedTab is SummaryTabIndex or DetailsTabIndex)
                    activeReportTab = savedTab.Value;
            }
            catch { /* default to Summary */ }

            // Restore the axis/value-mode the user last picked for each chart (default:
            // Organization / Percent), same pattern as the Dashboard's Project mix card.
            try
            {
                var savedProjectMixAxis = await LocalStorage.GetItemAsync<string>(ProjectMixAxisStorageKey);
                if (!string.IsNullOrEmpty(savedProjectMixAxis) && Enum.TryParse<ChartAxis>(savedProjectMixAxis, out var parsedProjectMixAxis))
                    _projectMixAxis = parsedProjectMixAxis;

                var savedProjectMixValueMode = await LocalStorage.GetItemAsync<string>(ProjectMixValueModeStorageKey);
                if (!string.IsNullOrEmpty(savedProjectMixValueMode) && Enum.TryParse<ChartValueMode>(savedProjectMixValueMode, out var parsedProjectMixValueMode))
                    _projectMixValueMode = parsedProjectMixValueMode;

                var savedDailyHoursAxis = await LocalStorage.GetItemAsync<string>(DailyHoursAxisStorageKey);
                if (!string.IsNullOrEmpty(savedDailyHoursAxis) && Enum.TryParse<ChartAxis>(savedDailyHoursAxis, out var parsedDailyHoursAxis))
                    _dailyHoursAxis = parsedDailyHoursAxis;

                var savedDailyHoursValueMode = await LocalStorage.GetItemAsync<string>(DailyHoursValueModeStorageKey);
                if (!string.IsNullOrEmpty(savedDailyHoursValueMode) && Enum.TryParse<ChartValueMode>(savedDailyHoursValueMode, out var parsedDailyHoursValueMode))
                    _dailyHoursValueMode = parsedDailyHoursValueMode;
            }
            catch { /* default to Organization / Percent */ }

            // Default to current month in the user's timezone
            var userToday = SettingsService.GetUserToday();
            dateFrom = new DateTime(userToday.Year, userToday.Month, 1);
            dateTo = userToday.ToDateTime(TimeOnly.MinValue);

            await LoadEmployeeOptionsAsync();
            await Task.WhenAll(LoadProjects(), LoadAllTasks());
            ApplyClientFilters();
            isLoading = false;
        }

        private IEnumerable<UserOption> FilteredEmployeeOptions =>
            string.IsNullOrWhiteSpace(employeeSearchText)
                ? userOptions
                : userOptions.Where(u =>
                    u.UserName.Contains(employeeSearchText, StringComparison.OrdinalIgnoreCase));

        private string EmployeeSelectionLabel
        {
            get
            {
                if (selectedUserIds.Count == 0)
                    return "No employees selected";
                if (userOptions.Count > 0 && userOptions.All(u => selectedUserIds.Contains(u.UserId)))
                    return "All employees";
                var nameById = userOptions.ToDictionary(u => u.UserId, u => u.UserName);
                return string.Join(", ", selectedUserIds
                    .Select(id => nameById.GetValueOrDefault(id, id))
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
            }
        }

        private async Task LoadEmployeeOptionsAsync()
        {
            try
            {
                var employees = await client.GetFromJsonAsync<List<ManageableEmployeeDto>>(
                    Constants.API.Analytics.GetManageableEmployees);
                userOptions = (employees ?? new())
                    .Select(e => new UserOption { UserId = e.UserId, UserName = e.UserName })
                    .OrderBy(u => u.UserName)
                    .ToList();
                canViewTeamReports = userOptions.Count > 0;
                if (canViewTeamReports)
                {
                    if (!string.IsNullOrEmpty(currentUserId)
                        && userOptions.Any(u => u.UserId == currentUserId))
                        selectedUserIds = new HashSet<string> { currentUserId };
                    else
                        selectedUserIds = new HashSet<string> { userOptions[0].UserId };
                    SyncEmployeeSelectionState();
                }
            }
            catch
            {
                // User:Tyme with the setting off used to 403; the API now returns [].
                // Any failure keeps Reports self-only.
                canViewTeamReports = false;
                userOptions = new();
            }
        }

        private void SyncEmployeeSelectionState()
        {
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
                selectNoneEmployeesChecked = false;
            }
        }

        private void OnSelectAllEmployeesChanged(bool value)
        {
            selectAllEmployeesChecked = value;
            if (value)
            {
                selectNoneEmployeesChecked = false;
                selectedUserIds = userOptions.Select(u => u.UserId).ToHashSet();
            }
            SyncEmployeeSelectionState();
        }

        private void OnSelectNoneEmployeesChanged(bool value)
        {
            selectNoneEmployeesChecked = value;
            if (value)
            {
                selectAllEmployeesChecked = false;
                selectedUserIds = new HashSet<string>();
            }
            SyncEmployeeSelectionState();
        }

        private void OnEmployeeCheckboxChanged(string userId, bool isChecked)
        {
            if (isChecked)
                selectedUserIds.Add(userId);
            else
                selectedUserIds.Remove(userId);
            SyncEmployeeSelectionState();
        }

        private sealed class UserOption
        {
            public string UserId { get; set; } = null!;
            public string UserName { get; set; } = null!;
        }

        /// <summary>Workspace mode from App Settings only — no per-user override.</summary>
        private async Task LoadDisplayModeFromAppSettingsAsync()
        {
            displayMode = EmployeeTimeDisplayMode.Both;
            try
            {
                var settings = await AppSettingsCache.GetAsync();
                displayMode = EmployeeTimeDisplayModeRules.FromAppSettings(
                    settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)));
            }
            catch
            {
                displayMode = EmployeeTimeDisplayMode.Both;
            }
        }

        private async Task LoadProjects()
        {
            // Used only for empty-search suggestions (first page of active projects).
            // Typed search hits the server via SearchProjects so projects past page 1 still appear.
            try
            {
                projects = (await ProjectsCache.LookupActiveAsync()).ToList();
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't load projects.");
            }
        }

        private async Task LoadAllTasks()
        {
            try
            {
                if (canViewTeamReports)
                {
                    allTasks = await LoadTeamReportTasksAsync();
                    return;
                }

                allTasks = await TrackedTasksClient.LoadRangeAsync(dateFrom, dateTo);
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't load tracked tasks.");
            }
        }

        private async Task<List<TrackedTask>> LoadTeamReportTasksAsync()
        {
            if (selectedUserIds.Count == 0)
                return new List<TrackedTask>();

            var url = Constants.API.Analytics.ConstructUrlForTeamReports(dateFrom, dateTo, selectedUserIds);
            var dtos = await client.GetFromJsonAsync<List<TrackedTaskDto>>(url)
                ?? new List<TrackedTaskDto>();
            try
            {
                await SettingsService.GetSettingsAsync();
            }
            catch
            {
                // Tests / offline: fall back to UTC.
            }

            var tz = SettingsService.GetTimeZoneInfo();
            return dtos.Select(d => new TrackedTask(d, tz)).ToList();
        }

        private async Task ApplyFilters()
        {
            isLoading = true;
            await LoadAllTasks();
            ApplyClientFilters();
            isLoading = false;
            StateHasChanged();
        }

        private DateTime ToUserDate(DateTime dt)
        {
            return SettingsService.ConvertToUserTime(dt.ToUniversalTime()).Date;
        }

        private void ApplyClientFilters()
        {
            filteredTasks = allTasks.Where(t =>
            {
                var taskDate = ToUserDate(t.StartDate);
                if (dateFrom.HasValue && taskDate < dateFrom.Value.Date) return false;
                if (dateTo.HasValue && taskDate > dateTo.Value.Date) return false;
                if (selectedProject != null && t.ProjectId != selectedProject.ProjectId) return false;
                return true;
            }).OrderByDescending(t => t.StartDate).ToList();

            taskDetailRows = BuildTaskDetailRows(filteredTasks)
                .Where(RowDateInFilter)
                .ToList();

            CalculateSummary();
            BuildChartData();
        }

        private bool RowDateInFilter(TrackedTask row)
        {
            var taskDate = ToUserDate(row.StartDate);
            if (dateFrom.HasValue && taskDate < dateFrom.Value.Date) return false;
            if (dateTo.HasValue && taskDate > dateTo.Value.Date) return false;
            return true;
        }

        private string FormatReportDate(DateTime dt)
        {
            var utc = dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime();
            return SettingsService.ConvertToUserTime(utc).ToString("MM/dd/yyyy");
        }

        private void CalculateSummary()
        {
            var totalSeconds = filteredTasks.Sum(t => t.Duration.TotalSeconds);
            totalTimeFormatted = FormatTime(totalSeconds);

            // Top project
            var topProject = filteredTasks
                .Where(t => t.Project != null)
                .GroupBy(t => t.Project!.Name)
                .OrderByDescending(g => g.Sum(t => t.Duration.TotalSeconds))
                .FirstOrDefault();
            topProjectName = topProject?.Key ?? "None";

            // Average per day
            var distinctDays = filteredTasks.Select(t => ToUserDate(t.StartDate)).Distinct().Count();
            if (distinctDays > 0)
            {
                avgPerDayFormatted = FormatTime(totalSeconds / distinctDays);
            }
            else
            {
                avgPerDayFormatted = "00:00";
            }

            // Details-tab footer total.
            durationTotalFormatted = WeekEntryGridRules.FormatDuration(TimeSpan.FromSeconds(
                taskDetailRows.Sum(t => t.Duration.TotalSeconds)));
        }

        private void BuildChartData()
        {
            // Flat per-project totals for the filtered range. Both the "Time by Project"
            // doughnut and the Daily Hours breakdown re-pivot this via ChartPivotRules so
            // Org/Project/Group grouping and coloring stay identical between the two charts
            // (and match the Dashboard's equivalent card).
            rawProjectData = filteredTasks
                .GroupBy(t => t.Project?.ProjectId ?? "None")
                .Select(g =>
                {
                    var sample = g.First().Project;
                    return new ProjectDataItem(
                        sample?.ProjectId ?? "None",
                        sample?.Name ?? "None",
                        TimeSpan.FromSeconds(g.Sum(t => t.Duration.TotalSeconds)),
                        "")
                    {
                        OrganizationId = sample?.OrganizationId,
                        OrganizationName = sample?.OrganizationName,
                        OrganizationColor = sample?.OrganizationColor,
                        ProjectGroupId = sample?.ProjectGroupId,
                        ProjectGroupName = sample?.ProjectGroupName,
                        ProjectGroupColor = sample?.ProjectGroupColor
                    };
                })
                .ToList();
        }

        private async Task ClearFilters()
        {
            var userToday = SettingsService.GetUserToday();
            dateFrom = new DateTime(userToday.Year, userToday.Month, 1);
            dateTo = userToday.ToDateTime(TimeOnly.MinValue);
            selectedProject = null;
            if (canViewTeamReports)
            {
                if (!string.IsNullOrEmpty(currentUserId)
                    && userOptions.Any(u => u.UserId == currentUserId))
                    selectedUserIds = new HashSet<string> { currentUserId };
                else if (userOptions.Count > 0)
                    selectedUserIds = new HashSet<string> { userOptions[0].UserId };
                else
                    selectedUserIds = new HashSet<string>();
                SyncEmployeeSelectionState();
            }
            await ApplyFilters();
        }

        private async Task<IEnumerable<Project>> SearchProjects(string? value, CancellationToken token)
        {
            // Must query the server — Lookup is capped at 25 rows. Filtering the
            // preloaded list made projects like "IT Support" invisible when they
            // fall outside the first page alphabetically.
            if (string.IsNullOrWhiteSpace(value))
                return projects;

            try
            {
                return await ProjectsCache.LookupActiveAsync(search: value);
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't search projects.");
                return Enumerable.Empty<Project>();
            }
        }

        private List<TrackedTask> BuildTaskDetailRows(IEnumerable<TrackedTask> tasks)
        {
            var rows = new List<TrackedTask>();
            foreach (var task in tasks)
            {
                var hasAdj = task.ManagerAdjustment != null
                    && task.AdjustmentKind is "Alias" or "Direct";

                if (EmployeeTimeDisplayModeRules.IncludeOriginal(displayMode, hasAdj))
                    rows.Add(task);

                if (!EmployeeTimeDisplayModeRules.IncludeAdjustmentOverlay(displayMode, hasAdj)
                    || task.ManagerAdjustment == null)
                    continue;

                var adjustment = task.ManagerAdjustment;
                var isAlias = task.AdjustmentKind == "Alias";
                var startUtc = adjustment.StartDate.Kind == DateTimeKind.Utc
                    ? adjustment.StartDate
                    : adjustment.StartDate.ToUniversalTime();

                rows.Add(new TrackedTask
                {
                    TaskId = task.TaskId,
                    Details = adjustment.Details,
                    Duration = adjustment.Duration,
                    StartDate = startUtc,
                    EndDate = adjustment.Duration > TimeSpan.Zero
                        ? startUtc + adjustment.Duration
                        : null,
                    ProjectId = adjustment.ProjectId,
                    Project = string.IsNullOrEmpty(adjustment.ProjectId) && string.IsNullOrEmpty(adjustment.ProjectName)
                        ? null
                        : new Project
                        {
                            ProjectId = adjustment.ProjectId ?? string.Empty,
                            Name = adjustment.ProjectName ?? "None",
                            OrganizationName = adjustment.OrganizationName,
                            OrganizationColor = adjustment.OrganizationColor,
                            ProjectGroupName = adjustment.ProjectGroupName,
                            ProjectGroupColor = adjustment.ProjectGroupColor
                        },
                    IsMonthSubmitted = task.IsMonthSubmitted,
                    UserId = task.UserId,
                    UserName = task.UserName,
                    IsManagerAdjusted = !isAlias,
                    AdjustmentKind = isAlias ? "AliasOverlay" : "DirectOverlay"
                });
            }

            return rows;
        }

        private static string FormatTime(double totalSeconds)
        {
            var ts = TimeSpan.FromSeconds(totalSeconds);
            int hours = (ts.Days * 24) + ts.Hours;
            return $"{hours:00}:{ts.Minutes:00}";
        }

        private async Task OnActiveReportTabChanged(int index)
        {
            activeReportTab = index;
            try
            {
                await LocalStorage.SetItemAsync(ActiveTabStorageKey, activeReportTab);
            }
            catch { /* non-fatal — just won't be remembered next visit */ }
        }

        /// <summary>Exports the Details grid. The Download button only renders on that tab —
        /// Summary is charts/stats, not a row export.</summary>
        private async Task DownloadCsvAsync()
        {
            var sb = new StringBuilder();

            if (canViewTeamReports)
                sb.AppendLine("\"Date\",\"Employee\",\"Project\",\"Details\",\"Duration\"");
            else
                sb.AppendLine("\"Date\",\"Project\",\"Details\",\"Duration\"");
            foreach (var task in taskDetailRows)
            {
                var duration = $"{(int)task.Duration.TotalHours:00}:{task.Duration.Minutes:00}";
                var project = task.Project?.DisplayName ?? task.Project?.Name ?? "None";
                if (canViewTeamReports)
                {
                    sb.AppendLine(
                        $"\"{Escape(FormatReportDate(task.StartDate))}\"," +
                        $"\"{Escape(task.UserName ?? "Unknown")}\"," +
                        $"\"{Escape(project)}\"," +
                        $"\"{Escape(task.Details)}\"," +
                        $"\"{Escape(duration)}\"");
                }
                else
                {
                    sb.AppendLine(
                        $"\"{Escape(FormatReportDate(task.StartDate))}\"," +
                        $"\"{Escape(project)}\"," +
                        $"\"{Escape(task.Details)}\"," +
                        $"\"{Escape(duration)}\"");
                }
            }

            await TriggerCsvDownloadAsync(sb.ToString());
        }

        private async Task TriggerCsvDownloadAsync(string csv)
        {
            var bytes = Encoding.UTF8.GetBytes(csv);
            var base64 = Convert.ToBase64String(bytes);
            var fileName = $"Reports_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.csv";

            await JS.InvokeVoidAsync("eval",
                $"var a=document.createElement('a');" +
                $"a.href='data:text/csv;base64,{base64}';" +
                $"a.download='{fileName}';" +
                $"document.body.appendChild(a);a.click();document.body.removeChild(a);");
        }

        private static string Escape(string? value) =>
            value?.Replace("\"", "\"\"") ?? "";
    }
}
