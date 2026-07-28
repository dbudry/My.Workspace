using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;
using My.Client.Components.TrackedTasks;
using My.Client.Extensions;
using My.Client.Helpers;
using My.Client.Models;
using My.Client.Services;
using My.Shared.Dtos.Paging;
using My.Shared.Dtos.StopwatchItem;
using My.Shared.Dtos.TaskList;
using My.Shared.Constants;
using My.Shared.Rules;

namespace My.Client.Pages.Tyme
{
    public partial class Tasks
    {
        private MudTable<TaskListRow> table = null!;

        string searchString = "";
        private EmployeeTimeDisplayMode displayMode = EmployeeTimeDisplayMode.Both;

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
        private IDialogService DialogService { get; set; } = null!;

        [Inject]
        private UserSettingsService SettingsService { get; set; } = null!;

        [Inject]
        private TrackedTasksClient TrackedTasksClient { get; set; } = null!;

        [Inject]
        private AppSettingsCache AppSettingsCache { get; set; } = null!;

        #endregion

        protected override async Task OnInitializedAsync()
        {
            var authState = await AuthenticationStateTask;
            var user = authState.User;

            if (user.Identity != null && !user.Identity.IsAuthenticated)
                Navigation.NavigateTo($"{Navigation.BaseUri}auth/login", true);

            client = ClientFactory.CreateClient(Constants.API.ClientName);

            SetPageTitle?.Invoke("Tasks");

            await SettingsService.GetSettingsAsync();
            await LoadDisplayModeFromAppSettingsAsync();
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

        /// <summary>
        /// One page of the unified list, merged/sorted/paged on the server. MudTable calls this per
        /// page and per sort/search change — no more pulling every row up front.
        /// </summary>
        private async Task<TableData<TaskListRow>> LoadServerData(TableState state, CancellationToken cancellationToken)
        {
            try
            {
                var query = new ListQueryParameters
                {
                    PageNumber = state.Page + 1,
                    PageSize = state.PageSize,
                    Search = searchString,
                    SortBy = state.SortLabel ?? TaskListRules.SortDate,
                    SortDescending = state.SortDirection == SortDirection.Descending
                };

                var response = await TrackedTasksClient.LoadTaskListAsync(query, cancellationToken);
                var rows = new List<TaskListRow>();
                foreach (var dto in response.Items)
                {
                    if (dto.IsStopwatch && dto.StopwatchItem != null)
                    {
                        rows.Add(TaskListRowBuilder.FromStopwatch(dto.StopwatchItem));
                        continue;
                    }

                    if (dto.ManualTask != null)
                        rows.AddRange(TaskListRowBuilder.ExpandManualRows(
                            new TrackedTask(dto.ManualTask), displayMode));
                }

                return new TableData<TaskListRow>
                {
                    Items = rows,
                    TotalItems = response.TotalCount
                };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new TableData<TaskListRow> { Items = Array.Empty<TaskListRow>(), TotalItems = 0 };
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't load tasks.");
                return new TableData<TaskListRow> { Items = Array.Empty<TaskListRow>(), TotalItems = 0 };
            }
        }

        private Task ReloadAsync() => table.ReloadServerData();

        private string? GetRowColor(TaskListRow row) =>
            ProjectColorRules.Resolve(
                row.OrganizationColor,
                row.ProjectGroupColor,
                SettingsService.ProjectColorSource);

        private string GetRowLabel(TaskListRow row) =>
            ProjectColorRules.ResolveLabel(
                row.OrganizationName,
                row.ProjectGroupName,
                row.OrganizationColor,
                row.ProjectGroupColor,
                SettingsService.ProjectColorSource) ?? string.Empty;

        private async Task OnSearchChanged(string value)
        {
            searchString = value;
            await ReloadAsync();
        }

        private async Task OnRowClickAsync(TaskListRow row)
        {
            if (row.Kind == TaskListRowKind.Stopwatch && row.StopwatchItem != null)
                await OpenStopwatchSessionsAsync(row.StopwatchItem);
            else if (row.ManualTask != null)
                await OpenTaskDialog(row);
        }

        private static string FormatDuration(TaskListRow row)
        {
            if (row.Kind == TaskListRowKind.Stopwatch)
                return $"{(int)row.Duration.TotalHours:00}:{row.Duration.Minutes:00}:{row.Duration.Seconds:00}";

            return $"{(int)row.Duration.TotalHours:00}:{row.Duration.Minutes:00}";
        }

        private async Task OpenStopwatchSessionsAsync(StopwatchItemDto item)
        {
            var parameters = new DialogParameters<StopwatchSessionsDialog>
            {
                { x => x.ItemId, item.StopwatchItemId },
                { x => x.ItemName, item.Name },
                { x => x.ItemProjectName, ProjectDisplayHelper.FromDto(item.Project) },
                { x => x.HttpClient, client }
            };

            var dialog = await DialogService.ShowAsync<StopwatchSessionsDialog>(
                item.Name,
                parameters,
                new DialogOptions { MaxWidth = MaxWidth.Medium, FullWidth = true });
            var result = await dialog.Result;

            if (result is { Canceled: false })
                await ReloadAsync();
        }

        private async Task OpenTaskDialog(TaskListRow row)
        {
            var task = row.ManualTask!;
            var isOverlay = row.IsOverlayRow;

            string name;
            string? projectId;
            string? projectName;
            DateTime start;
            DateTime? end;
            TimeSpan duration;
            bool isAllDay;

            if (isOverlay)
            {
                name = row.OverlayName ?? row.Name;
                projectId = row.OverlayProjectId;
                projectName = row.ProjectDisplayName;
                start = row.OverlayStartDate ?? row.DisplayDate;
                end = row.OverlayEndDate;
                duration = row.OverlayDuration ?? row.Duration;
                isAllDay = false;
            }
            else
            {
                name = task.Name;
                projectId = task.ProjectId;
                projectName = task.Project?.DisplayName;
                start = task.StartDate;
                end = task.EndDate ?? (task.Duration > TimeSpan.Zero ? task.StartDate + task.Duration : null);
                duration = task.Duration;
                isAllDay = task.IsAllDay;
            }

            // Overlay rows are always read-only (employee can't edit manager values from here).
            // Locked originals are also read-only.
            var mode = task.IsLocked || isOverlay
                ? TrackedTaskDialogMode.ReadOnly
                : TrackedTaskDialogMode.Edit;

            var parameters = new DialogParameters<TrackedTaskDialog>
            {
                { x => x.Mode, mode },
                { x => x.TaskId, task.TaskId },
                { x => x.TaskName, name },
                { x => x.ProjectId, projectId },
                { x => x.ProjectName, projectName },
                { x => x.StartDate, start },
                { x => x.EndDate, end },
                { x => x.Duration, duration },
                { x => x.IsAllDay, isAllDay },
                { x => x.Use24HourTime, SettingsService.Use24HourTime },
                { x => x.HttpClient, client },
                { x => x.IsManagerAdjustmentView, isOverlay }
            };

            // When opening the adjusted overlay, pass the employee's original values for side-by-side compare.
            if (isOverlay)
            {
                parameters.Add(x => x.OriginalTaskName, task.Name);
                parameters.Add(x => x.OriginalProjectName, task.Project?.DisplayName);
                parameters.Add(x => x.OriginalStartDate, task.StartDate);
                parameters.Add(x => x.OriginalEndDate,
                    task.EndDate ?? (task.Duration > TimeSpan.Zero ? task.StartDate + task.Duration : null));
                parameters.Add(x => x.OriginalDuration, task.Duration);
            }

            var dialog = await DialogService.ShowAsync<TrackedTaskDialog>(name, parameters,
                new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true });
            var result = await dialog.Result;

            if (result is { Canceled: false })
                await ReloadAsync();
        }

        private async Task OpenCreateDialog()
        {
            var start = DateTime.Now.Date.AddHours(9);

            var parameters = new DialogParameters<TrackedTaskDialog>
            {
                { x => x.Mode, TrackedTaskDialogMode.Create },
                { x => x.StartDate, start },
                { x => x.Duration, TimeSpan.FromMinutes(30) },
                { x => x.Use24HourTime, SettingsService.Use24HourTime },
                { x => x.HttpClient, client }
            };

            var dialog = await DialogService.ShowAsync<TrackedTaskDialog>("New Task", parameters,
                new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true });
            var result = await dialog.Result;

            if (result is { Canceled: false })
                await ReloadAsync();
        }

        async Task DeleteRow(TrackedTask trackedTask)
        {
            var result = await DialogService.ShowMessageBoxAsync(
                "Confirm Delete",
                $"Are you sure you want to delete \"{trackedTask.Name}\"?",
                yesText: "Delete", cancelText: "Cancel");

            if (result != true) return;

            try
            {
                var response = await client.DeleteAsync($"{Constants.API.TrackedTask.Delete}/{trackedTask.TaskId}");

                if (response != null && response.IsSuccessStatusCode)
                {
                    await ReloadAsync();
                    Snackbar.Add("Task was removed", Severity.Success);
                }
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't delete task.");
            }
        }
    }
}
