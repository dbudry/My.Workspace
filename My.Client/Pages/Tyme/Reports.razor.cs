using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;
using System.Net.Http.Json;
using My.Client.Extensions;
using My.Client.Models;
using My.Client.Models.Paging;
using My.Client.Services;
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

        private string totalTimeFormatted = "00:00";
        private string topProjectName = "None";
        private string avgPerDayFormatted = "00:00";

        private List<(string Name, double Seconds, string Color)> projectChartData = new();
        private List<(string Date, double Hours)> dailyChartData = new();

        private List<ChartSeries<double>> ProjectSeries => new()
        {
            new ChartSeries<double> { Name = "Hours", Data = projectChartData.Select(p => p.Seconds / 3600.0).ToArray() }
        };
        // Show "<name> (NN%)" so the donut legend carries meaning. Falls back to bare
        // names when the total rounds to zero (empty filter result mid-render) to avoid
        // printing "NaN%".
        private string[] ProjectLabels
        {
            get
            {
                var total = projectChartData.Sum(p => p.Seconds);
                if (total <= 0)
                    return projectChartData.Select(p => p.Name).ToArray();
                return projectChartData.Select(p => $"{p.Name} ({p.Seconds / total:P0})").ToArray();
            }
        }

        /// <summary>Per-segment colors for the donut chart — same color the user sees
        /// on a project's row in the Tasks list, so the report ties visually back to it.
        /// Falls back to neutral gray when the user's preference is None or no source
        /// color is set.</summary>
        private ChartOptions ProjectChartOptions => new()
        {
            ChartPalette = projectChartData.Select(p => p.Color).ToArray()
        };

        private List<ChartSeries<double>> DailySeries => new()
        {
            new ChartSeries<double> { Name = "Hours", Data = dailyChartData.Select(d => d.Hours).ToArray() }
        };
        private string[] DailyLabels => dailyChartData.Select(d => d.Date).ToArray();

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

        #endregion

        protected override async Task OnInitializedAsync()
        {
            var authState = await AuthenticationStateTask;
            var user = authState.User;

            if (user.Identity != null && !user.Identity.IsAuthenticated)
                Navigation.NavigateTo($"{Navigation.BaseUri}auth/login", true);

            client = ClientFactory.CreateClient(Constants.API.ClientName);

            SetPageTitle?.Invoke("Reports");

            await SettingsService.GetSettingsAsync();

            // Default to current month in the user's timezone
            var userToday = SettingsService.GetUserToday();
            dateFrom = new DateTime(userToday.Year, userToday.Month, 1);
            dateTo = userToday.ToDateTime(TimeOnly.MinValue);

            await Task.WhenAll(LoadProjects(), LoadAllTasks());
            ApplyClientFilters();
            isLoading = false;
        }

        private async Task LoadProjects()
        {
            try
            {
                projects = (await ProjectsCache.LookupAsync()).ToList();
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
                allTasks = await TrackedTasksClient.LoadRangeAsync(dateFrom, dateTo);
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't load tracked tasks.");
            }
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

            taskDetailRows = BuildTaskDetailRows(filteredTasks);

            CalculateSummary();
            BuildChartData();
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
        }

        private void BuildChartData()
        {
            // Project breakdown. Resolve each segment's color via the user's preference
            // using the first task's project in the group (all tasks in a group share the
            // same project, so any one of them suffices). Empty/null colors fall back to
            // the neutral gray so the chart segment still renders distinctly.
            var source = SettingsService.ProjectColorSource;
            projectChartData = filteredTasks
                .GroupBy(t => t.Project?.Name ?? "None")
                .Select(g =>
                {
                    var sample = g.First().Project;
                    var color = ProjectColorRules.ResolveOrFallback(
                        sample?.OrganizationColor, sample?.ProjectGroupColor, source);
                    return (Name: g.Key, Seconds: g.Sum(t => t.Duration.TotalSeconds), Color: color);
                })
                .OrderByDescending(x => x.Seconds)
                .ToList();

            // Daily breakdown (last 14 days max for readability)
            dailyChartData = filteredTasks
                .GroupBy(t => ToUserDate(t.StartDate))
                .OrderBy(g => g.Key)
                .TakeLast(14)
                .Select(g => (Date: g.Key.ToString("MM/dd"), Hours: g.Sum(t => t.Duration.TotalSeconds) / 3600.0))
                .ToList();
        }

        /// <summary>Color bar shown next to the project name in the Task Details rows.</summary>
        private string? GetRowColor(TrackedTask task)
            => ProjectColorRules.Resolve(
                task.Project?.OrganizationColor,
                task.Project?.ProjectGroupColor,
                SettingsService.ProjectColorSource);

        /// <summary>Tooltip naming the Org/Group whose color is drawn on the row.</summary>
        private string GetRowLabel(TrackedTask task)
            => ProjectColorRules.ResolveLabel(
                task.Project?.OrganizationName,
                task.Project?.ProjectGroupName,
                task.Project?.OrganizationColor,
                task.Project?.ProjectGroupColor,
                SettingsService.ProjectColorSource) ?? string.Empty;

        private async Task ClearFilters()
        {
            var userToday = SettingsService.GetUserToday();
            dateFrom = new DateTime(userToday.Year, userToday.Month, 1);
            dateTo = userToday.ToDateTime(TimeOnly.MinValue);
            selectedProject = null;
            await ApplyFilters();
        }

        private Task<IEnumerable<Project>> SearchProjects(string? value, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(value))
                return Task.FromResult(projects.AsEnumerable());

            var results = projects.Where(p =>
                p.SearchText.Contains(value, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(results);
        }

        private static List<TrackedTask> BuildTaskDetailRows(IEnumerable<TrackedTask> tasks)
        {
            var rows = new List<TrackedTask>();
            foreach (var task in tasks)
            {
                rows.Add(task);
                if (task.ManagerAdjustment == null || task.AdjustmentKind is not ("Alias" or "Direct"))
                    continue;

                var adjustment = task.ManagerAdjustment;
                var isAlias = task.AdjustmentKind == "Alias";
                rows.Add(new TrackedTask
                {
                    TaskId = task.TaskId,
                    Name = adjustment.Name,
                    Duration = adjustment.Duration,
                    StartDate = adjustment.StartDate.ToLocalTime(),
                    ProjectId = adjustment.ProjectId,
                    IsMonthSubmitted = task.IsMonthSubmitted,
                    UserId = task.UserId,
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
    }
}
