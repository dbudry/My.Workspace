using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;
using System.Net.Http.Json;
using My.Client.Components.TrackedTasks;
using My.Client.Extensions;
using My.Client.Models;
using My.Client.Services;
using My.Shared.Constants;
using My.Shared.Dtos.Paging;
using My.Shared.Dtos.TrackedTask;
using My.Shared.Rules;
using Heron.MudCalendar;

namespace My.Client.Pages.Tyme
{
    public partial class TaskCalendar
    {
        private readonly List<TrackedTask> trackedTasksList = new();
        private List<TaskCalendarItem> calendarItems = new();

        private bool isLoading = true;

        private HttpClient client = null!;

        private bool contextMenuVisible;
        private double contextMenuX;
        private double contextMenuY;
        private TaskCalendarItem? contextMenuTarget;

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
        private TrackedTasksClient TrackedTasksClient { get; set; } = null!;

        [Inject]
        private UserSettingsService SettingsService { get; set; } = null!;

        #endregion

        // Heron's MudCalendar measures its parent at construction time. If we render it on
        // the first paint, the page chrome hasn't settled yet and the day/week grid lays out
        // with zero or near-zero height — the "everything crunched together" look. Holding
        // the calendar back until OnAfterRenderAsync's firstRender callback fires lets the
        // browser settle layout first, then we flip the flag and MudCalendar measures
        // correctly on its first real render.
        private bool _calendarReady;

        protected override async Task OnInitializedAsync()
        {
            var authState = await AuthenticationStateTask;
            var user = authState.User;

            if (user.Identity != null && !user.Identity.IsAuthenticated)
                Navigation.NavigateTo($"{Navigation.BaseUri}auth/login", true);

            client = ClientFactory.CreateClient(Constants.API.ClientName);

            SetPageTitle?.Invoke("Task Calendar");

            await SettingsService.GetSettingsAsync();
            await LoadData();
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender && !_calendarReady)
            {
                _calendarReady = true;
                await InvokeAsync(StateHasChanged);
            }
        }

        private async Task LoadData()
        {
            isLoading = true;
            try
            {
                // Default to the last 12 months. The Heron calendar handles navigation
                // within that window; if older data is needed we can wire a date-range
                // event later.
                var fromCutoff = DateTime.UtcNow.AddMonths(-12);
                var loaded = await TrackedTasksClient.LoadRangeAsync(fromCutoff, null);

                trackedTasksList.Clear();
                calendarItems.Clear();

                foreach (var task in loaded)
                {
                    trackedTasksList.Add(task);

                    if (task.IsAllDay)
                    {
                        var firstDay = task.StartDate.Date;
                        var lastDay = (task.EndDate ?? task.StartDate).Date;
                        if (lastDay < firstDay) lastDay = firstDay;

                        for (var d = firstDay; d <= lastDay; d = d.AddDays(1))
                            calendarItems.Add(BuildAllDayChip(task, d));
                        continue;
                    }

                    if (!string.IsNullOrEmpty(task.StopwatchItemId))
                        continue;
                }

                var tasksById = loaded.ToDictionary(t => t.TaskId);

                var stopwatchSessions = loaded
                    .Where(t => !t.IsAllDay && !string.IsNullOrEmpty(t.StopwatchItemId))
                    .Select(t => new StopwatchCalendarRules.SessionSlice
                    {
                        TaskId = t.TaskId,
                        StopwatchItemId = t.StopwatchItemId!,
                        Name = t.Name,
                        StartDate = t.StartDate,
                        EndDate = t.EndDate,
                        Duration = t.Duration,
                        IsLocked = t.IsLocked
                    });

                foreach (var group in StopwatchCalendarRules.GroupByWorkItemAndDay(stopwatchSessions))
                {
                    if (!tasksById.TryGetValue(group.RepresentativeTaskId, out var sample))
                        continue;
                    var label = group.SessionCount > 1
                        ? $"{group.Name} ({group.SessionCount} sessions)"
                        : group.Name;

                    calendarItems.Add(new TaskCalendarItem
                    {
                        TaskId = group.RepresentativeTaskId,
                        Text = label,
                        Start = group.Start,
                        End = group.End,
                        AllDay = false,
                        ProjectName = sample.Project?.DisplayName,
                        OrganizationName = sample.Project?.OrganizationName,
                        OrganizationColor = sample.Project?.OrganizationColor,
                        ProjectGroupName = sample.Project?.ProjectGroupName,
                        ProjectGroupColor = sample.Project?.ProjectGroupColor,
                        ProjectId = sample.ProjectId,
                        Duration = group.TotalDuration,
                        IsLocked = group.IsLocked,
                        IsStopwatchGroup = true,
                        StopwatchItemId = group.StopwatchItemId,
                        StopwatchDay = group.Day,
                        SessionCount = group.SessionCount
                    });
                }

                foreach (var task in loaded.Where(t => !t.IsAllDay && string.IsNullOrEmpty(t.StopwatchItemId)))
                {
                    var start = task.StartDate;
                    var end = task.EndDate ?? task.StartDate.Add(task.Duration);
                    if (end <= start)
                        end = start.AddMinutes(15);

                    calendarItems.Add(new TaskCalendarItem
                    {
                        TaskId = task.TaskId,
                        Text = task.Name,
                        Start = start,
                        End = end,
                        AllDay = false,
                        ProjectName = task.Project?.DisplayName,
                        OrganizationName = task.Project?.OrganizationName,
                        OrganizationColor = task.Project?.OrganizationColor,
                        ProjectGroupName = task.Project?.ProjectGroupName,
                        ProjectGroupColor = task.Project?.ProjectGroupColor,
                        ProjectId = task.ProjectId,
                        Duration = task.Duration,
                        IsLocked = task.IsLocked
                    });

                    if (task.ManagerAdjustment != null && task.AdjustmentKind is "Alias" or "Direct")
                    {
                        var adj = task.ManagerAdjustment;
                        var adjStart = adj.StartDate.ToLocalTime();
                        var adjEnd = adjStart.Add(adj.Duration);
                        if (adjEnd <= adjStart)
                            adjEnd = adjStart.AddMinutes(15);

                        var isAlias = task.AdjustmentKind == "Alias";
                        calendarItems.Add(new TaskCalendarItem
                        {
                            TaskId = task.TaskId,
                            Text = isAlias ? $"{adj.Name} (adjustment)" : $"{adj.Name} (adjusted)",
                            Start = adjStart,
                            End = adjEnd,
                            AllDay = false,
                            ProjectName = adj.ProjectName,
                            ProjectId = adj.ProjectId,
                            Duration = adj.Duration,
                            IsLocked = task.IsLocked,
                            IsManagerAdjustmentOverlay = isAlias,
                            IsManagerAdjusted = !isAlias
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't load calendar tasks.");
            }

            isLoading = false;
        }

        /// <summary>
        /// Builds a single-day all-day chip for one calendar day in an all-day span.
        /// Called once per day a multi-day vacation/OOO covers, so Heron paints a chip
        /// on each day. All chips share the same TaskId so clicking any of them opens
        /// the same TrackedTask.
        /// </summary>
        private static TaskCalendarItem BuildAllDayChip(TrackedTask task, DateTime day)
            => new()
            {
                TaskId = task.TaskId,
                Text = task.Name,
                Start = day,
                End = day.AddDays(1).AddTicks(-1),
                AllDay = true,
                ProjectName = task.Project?.DisplayName,
                OrganizationName = task.Project?.OrganizationName,
                OrganizationColor = task.Project?.OrganizationColor,
                ProjectGroupName = task.Project?.ProjectGroupName,
                ProjectGroupColor = task.Project?.ProjectGroupColor,
                ProjectId = task.ProjectId,
                Duration = task.Duration,
                IsLocked = task.IsLocked
            };

        private async Task OnItemClicked(TaskCalendarItem item)
        {
            CloseContextMenu();

            if (item.IsStopwatchGroup && !string.IsNullOrEmpty(item.StopwatchItemId))
            {
                var sample = trackedTasksList.FirstOrDefault(t => t.TaskId == item.TaskId);
                var sessionsDialogParameters = new DialogParameters<StopwatchSessionsDialog>
                {
                    { x => x.ItemId, item.StopwatchItemId },
                    { x => x.ItemName, sample?.Name ?? item.Text },
                    { x => x.ItemProjectName, item.ProjectName },
                    { x => x.DayFilter, item.StopwatchDay },
                    { x => x.HttpClient, client }
                };

                var dialog = await DialogService.ShowAsync<StopwatchSessionsDialog>(
                    item.Text,
                    sessionsDialogParameters,
                    new DialogOptions { MaxWidth = MaxWidth.Medium, FullWidth = true });
                var result = await dialog.Result;

                if (result is { Canceled: false })
                    await LoadData();
                return;
            }

            var task = trackedTasksList.FirstOrDefault(t => t.TaskId == item.TaskId);

            var parameters = new DialogParameters<TrackedTaskDialog>
            {
                { x => x.Mode, item.IsLocked ? TrackedTaskDialogMode.ReadOnly : TrackedTaskDialogMode.Edit },
                { x => x.TaskId, item.TaskId },
                { x => x.TaskName, task?.Name ?? item.Text },
                { x => x.ProjectId, item.ProjectId },
                { x => x.ProjectName, item.ProjectName },
                { x => x.StartDate, item.Start },
                { x => x.EndDate, item.End },
                { x => x.Duration, item.Duration },
                { x => x.IsAllDay, task?.IsAllDay ?? false },
                { x => x.Use24HourTime, SettingsService.Use24HourTime },
                { x => x.HttpClient, client }
            };

            await ShowDialogAsync(task?.Name ?? item.Text, parameters);
        }

        private async Task OnCellClicked(DateTime clickedDate)
        {
            CloseContextMenu();

            // If the user clicked a bare date (month view), default to 9 AM local time
            var start = clickedDate.TimeOfDay == TimeSpan.Zero
                ? clickedDate.Date.AddHours(9)
                : clickedDate;

            var parameters = new DialogParameters<TrackedTaskDialog>
            {
                { x => x.Mode, TrackedTaskDialogMode.Create },
                { x => x.StartDate, start },
                { x => x.Duration, TimeSpan.FromMinutes(30) },
                { x => x.Use24HourTime, SettingsService.Use24HourTime },
                { x => x.HttpClient, client }
            };

            await ShowDialogAsync("New Task", parameters);
        }

        private async Task ShowDialogAsync(string title, DialogParameters<TrackedTaskDialog> parameters)
        {
            var dialog = await DialogService.ShowAsync<TrackedTaskDialog>(title, parameters,
                new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true });

            var result = await dialog.Result;
            if (result != null && !result.Canceled)
            {
                await LoadData();
                StateHasChanged();
            }
        }

        private void OnItemContextMenu(CalendarItemClickEventArgs<TaskCalendarItem> args)
        {
            contextMenuTarget = args.Item;
            contextMenuX = args.MouseEventArgs.ClientX;
            contextMenuY = args.MouseEventArgs.ClientY;
            contextMenuVisible = true;
            StateHasChanged();
        }

        private void CloseContextMenu()
        {
            contextMenuVisible = false;
            contextMenuTarget = null;
        }

        private async Task ContextMenuEdit()
        {
            var item = contextMenuTarget;
            CloseContextMenu();
            if (item != null) await OnItemClicked(item);
        }

        private async Task ContextMenuDuplicate()
        {
            var item = contextMenuTarget;
            CloseContextMenu();
            if (item == null || item.IsLocked) return;

            try
            {
                var response = await client.PostAsJsonAsync(
                    $"{Constants.API.TrackedTask.Duplicate}/{item.TaskId}/duplicate",
                    new DuplicateTrackedTaskDto());

                if (response.IsSuccessStatusCode)
                {
                    Snackbar.Add("Task duplicated.", Severity.Success);
                    await LoadData();
                    StateHasChanged();
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    Snackbar.Add(string.IsNullOrEmpty(error) ? "Failed to duplicate task." : error, Severity.Error);
                }
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't duplicate task.");
            }
        }

        private async Task ContextMenuDelete()
        {
            var item = contextMenuTarget;
            CloseContextMenu();
            if (item == null || item.IsLocked) return;

            var confirmed = await DialogService.ShowMessageBoxAsync(
                "Confirm Delete", $"Delete \"{item.Text}\"?",
                yesText: "Delete", cancelText: "Cancel");

            if (confirmed != true) return;

            try
            {
                var response = await client.DeleteAsync($"{Constants.API.TrackedTask.Delete}/{item.TaskId}");
                if (response.IsSuccessStatusCode)
                {
                    Snackbar.Add("Task deleted.", Severity.Success);
                    await LoadData();
                    StateHasChanged();
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    Snackbar.Add(string.IsNullOrEmpty(error) ? "Failed to delete task." : error, Severity.Error);
                }
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't delete task.");
            }
        }

        /// <summary>Resolves a calendar chip's background per the user's color-source
        /// preference. Falls back to a neutral gray when the resolver returns null
        /// (None source, or no colors set) so the chip stays readable.</summary>
        private string GetChipColor(TaskCalendarItem item)
            => My.Shared.Rules.ProjectColorRules.ResolveOrFallback(
                item.OrganizationColor,
                item.ProjectGroupColor,
                SettingsService.ProjectColorSource,
                "#616161");

        /// <summary>Tooltip text for a calendar chip — names the Org or Group whose
        /// color is being shown. Empty string (MudTooltip stays silent) when the
        /// source has no entity to label, e.g. the user picked "None" or the task's
        /// project has no Org/Group set.</summary>
        private string GetChipLabel(TaskCalendarItem item)
            => My.Shared.Rules.ProjectColorRules.ResolveLabel(
                item.OrganizationName,
                item.ProjectGroupName,
                item.OrganizationColor,
                item.ProjectGroupColor,
                SettingsService.ProjectColorSource) ?? string.Empty;
    }
}
