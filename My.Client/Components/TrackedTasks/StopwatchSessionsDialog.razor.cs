using Microsoft.AspNetCore.Components;
using MudBlazor;
using My.Client.Extensions;
using My.Client.Helpers;
using My.Client.Models;
using My.Client.Services;
using My.Shared.Constants;
using My.Shared.Dtos.StopwatchItem;

namespace My.Client.Components.TrackedTasks
{
    public partial class StopwatchSessionsDialog
    {
        /// <summary>
        /// Only the most recent day (today / last entered) starts expanded; older days stay condensed.
        /// </summary>
        private const int ExpandRecentDayCount = 1;

        [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = null!;

        [Parameter] public string ItemId { get; set; } = null!;
        [Parameter] public string ItemName { get; set; } = "";
        [Parameter] public string? ItemProjectId { get; set; }
        [Parameter] public string? ItemProjectName { get; set; }
        /// <summary>When set, only sessions on this calendar day are shown (e.g. from a grouped calendar chip).</summary>
        [Parameter] public DateTime? DayFilter { get; set; }
        [Parameter] public HttpClient HttpClient { get; set; } = null!;

        [Inject] private ISnackbar Snackbar { get; set; } = null!;
        [Inject] private IDialogService DialogService { get; set; } = null!;
        [Inject] private UserSettingsService SettingsService { get; set; } = null!;
        [Inject] private StopwatchItemsClient StopwatchItemsClient { get; set; } = null!;
        [Inject] private ProjectsCache ProjectsCache { get; set; } = null!;

        private readonly List<TrackedTask> sessions = new();
        private readonly List<DayGroup> dayGroups = new();
        private readonly List<MonthFilter> monthFilters = new();
        private readonly HashSet<string> expandedDayKeys = new(StringComparer.Ordinal);
        private string? selectedMonthKey;
        private bool isLoading = true;
        private bool isBusy;
        private bool changed;

        private string displayItemName = "";
        private string? displayProjectName;
        private string? currentProjectId;
        private bool hasLockedSessions;
        private bool canEditWorkItem => !hasLockedSessions;
        private bool hasRunningSession => sessions.Any(s => s.IsRunning);

        private int visibleDayCount => dayGroups.Count;
        private int visibleSessionCount => dayGroups.Sum(d => d.Sessions.Count);
        private TimeSpan visibleTotalDuration => dayGroups.Aggregate(TimeSpan.Zero, (sum, d) => sum + d.Total);

        protected override async Task OnInitializedAsync()
        {
            displayItemName = ItemName;
            displayProjectName = ItemProjectName;
            currentProjectId = ItemProjectId;
            await SettingsService.GetSettingsAsync();
            await LoadSessionsAsync();
        }

        private async Task LoadSessionsAsync()
        {
            isLoading = true;
            try
            {
                var dtos = await StopwatchItemsClient.LoadSessionsAsync(ItemId);
                var tz = SettingsService.GetTimeZoneInfo();
                sessions.Clear();
                sessions.AddRange(dtos.Select(d => new TrackedTask(d, tz)));
                hasLockedSessions = sessions.Any(s => s.IsLocked);
                RebuildGroups(preserveExpansion: false);
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't load sessions.");
            }
            finally
            {
                isLoading = false;
            }
        }

        private void RebuildGroups(bool preserveExpansion)
        {
            var previousExpanded = preserveExpansion
                ? expandedDayKeys.ToHashSet(StringComparer.Ordinal)
                : null;

            var baseVisible = sessions.AsEnumerable();
            if (DayFilter.HasValue)
                baseVisible = baseVisible.Where(s => s.StartDate.Date == DayFilter.Value.Date);

            var baseList = baseVisible.ToList();

            // Month chips always reflect the full (day-filtered) history, not the selected month slice.
            monthFilters.Clear();
            monthFilters.AddRange(
                baseList
                    .GroupBy(s => new DateTime(s.StartDate.Year, s.StartDate.Month, 1))
                    .OrderByDescending(g => g.Key)
                    .Select(g => new MonthFilter(
                        Key: g.Key.ToString("yyyy-MM"),
                        Label: g.Key.ToString("MMM yyyy"),
                        MonthStart: g.Key,
                        Total: g.Aggregate(TimeSpan.Zero, (sum, s) => sum + GetSessionDuration(s)))));

            if (selectedMonthKey is not null && monthFilters.All(m => m.Key != selectedMonthKey))
                selectedMonthKey = null;

            var filtered = baseList.AsEnumerable();
            if (selectedMonthKey is not null)
            {
                filtered = filtered.Where(s =>
                    s.StartDate.Year == int.Parse(selectedMonthKey.AsSpan(0, 4))
                    && s.StartDate.Month == int.Parse(selectedMonthKey.AsSpan(5, 2)));
            }

            dayGroups.Clear();
            dayGroups.AddRange(
                filtered
                    .GroupBy(s => s.StartDate.Date)
                    .OrderByDescending(g => g.Key)
                    .Select(g => new DayGroup(
                        Key: g.Key.ToString("yyyy-MM-dd"),
                        Label: g.Key.ToLongDateString(),
                        Date: g.Key,
                        Sessions: g.OrderByDescending(s => s.StartDate).ToList(),
                        Total: g.Aggregate(TimeSpan.Zero, (sum, s) => sum + GetSessionDuration(s)))));

            expandedDayKeys.Clear();
            if (previousExpanded is not null)
            {
                foreach (var key in dayGroups.Select(d => d.Key).Where(previousExpanded.Contains))
                    expandedDayKeys.Add(key);
            }
            else
            {
                ApplyDefaultExpansion();
            }
        }

        private void ApplyDefaultExpansion()
        {
            expandedDayKeys.Clear();
            // Only the latest day is open; every earlier day stays collapsed for scanning.
            foreach (var day in dayGroups.Take(ExpandRecentDayCount))
                expandedDayKeys.Add(day.Key);
        }

        private void OnDayExpandedChanged(string dayKey, bool expanded)
        {
            if (expanded)
                expandedDayKeys.Add(dayKey);
            else
                expandedDayKeys.Remove(dayKey);
        }

        private void ExpandAllDays()
        {
            expandedDayKeys.Clear();
            foreach (var day in dayGroups)
                expandedDayKeys.Add(day.Key);
        }

        private void CollapseAllDays()
        {
            expandedDayKeys.Clear();
        }

        private void SelectMonthFilter(string? monthKey)
        {
            selectedMonthKey = monthKey;
            RebuildGroups(preserveExpansion: false);
        }

        private string? GetSessionProjectName(TrackedTask session)
            => ProjectDisplayHelper.FromModel(session.Project) ?? ItemProjectName;

        /// <summary>Log a finished session (start + duration) against this work item.</summary>
        private async Task AddSessionAsync()
        {
            await SettingsService.GetSettingsAsync();
            var start = DateTime.Today.Add(SettingsService.DefaultStartTimeOfDay);

            var parameters = new DialogParameters<TrackedTaskDialog>
            {
                { x => x.Mode, TrackedTaskDialogMode.Create },
                { x => x.TaskName, ItemName },
                { x => x.ProjectId, ItemProjectId },
                { x => x.ProjectName, ItemProjectName },
                { x => x.StartDate, start },
                { x => x.Duration, TimeSpan.Zero },
                { x => x.Use24HourTime, SettingsService.Use24HourTime },
                { x => x.HttpClient, HttpClient },
                { x => x.StopwatchItemId, ItemId }
            };

            var dialog = await DialogService.ShowAsync<TrackedTaskDialog>(
                "Create Duration",
                parameters,
                new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true, BackdropClick = false });
            var result = await dialog.Result;
            if (result is { Canceled: false })
            {
                changed = true;
                await LoadSessionsAsync();
            }
        }

        /// <summary>Start a live timer session on this work item (requires a project).</summary>
        private async Task StartTimerAsync()
        {
            if (string.IsNullOrEmpty(ItemProjectId))
            {
                Snackbar.Add("Assign a project on the work item before starting the timer.", Severity.Warning);
                return;
            }

            isBusy = true;
            try
            {
                await StopwatchItemsClient.StartAsync(ItemId);
                changed = true;
                Snackbar.Add("Timer started.", Severity.Success);
                await LoadSessionsAsync();
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't start the timer.");
            }
            finally
            {
                isBusy = false;
            }
        }

        /// <summary>
        /// Stopwatch sessions bill by <see cref="TrackedTask.Duration"/> (rounded up to whole
        /// minutes on stop). Raw start/stop timestamps can differ by only a few seconds while
        /// the stored duration is a full minute — using the clock delta would under-report.
        /// </summary>
        private static TimeSpan GetSessionDuration(TrackedTask session) => session.Duration;

        /// <summary>
        /// When duration was rounded up, the actual stop instant can share the same minute as
        /// start — show start + billed duration so the row matches the Duration column.
        /// </summary>
        private static DateTime GetSessionDisplayEnd(TrackedTask session)
        {
            if (!session.EndDate.HasValue)
                return session.StartDate;

            return session.Duration > TimeSpan.Zero
                ? session.StartDate.Add(session.Duration)
                : session.EndDate.Value;
        }

        private static string FormatDuration(TimeSpan duration)
            => $"{(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}";

        private async Task EditWorkItemAsync()
        {
            if (!canEditWorkItem)
            {
                Snackbar.Add(
                    "Name and project cannot change while any session is in a submitted month.",
                    Severity.Warning);
                return;
            }

            var parameters = new DialogParameters<StopwatchItemDialog>
            {
                { x => x.ItemId, ItemId },
                { x => x.ItemName, displayItemName },
                { x => x.ProjectId, currentProjectId },
                { x => x.ProjectName, displayProjectName },
                { x => x.SearchProjects, (Func<string?, CancellationToken, Task<IEnumerable<Project>>>)SearchProjectsAsync }
            };

            var dialog = await DialogService.ShowAsync<StopwatchItemDialog>(
                "Edit work item",
                parameters,
                new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true });

            var result = await dialog.Result;
            if (result is not { Canceled: false, Data: (string savedName, string savedProjectId) })
                return;

            isBusy = true;
            try
            {
                var updated = await StopwatchItemsClient.UpdateAsync(new UpdateStopwatchItemDto
                {
                    StopwatchItemId = ItemId,
                    Details = savedName,
                    ProjectId = savedProjectId
                });
                displayItemName = updated.Details;
                currentProjectId = updated.ProjectId;
                displayProjectName = ProjectDisplayHelper.FromDto(updated.Project) ?? displayProjectName;
                changed = true;
                await LoadSessionsAsync();
                Snackbar.Add("Work item saved.", Severity.Success);
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't save the work item.");
            }
            finally
            {
                isBusy = false;
            }
        }

        private async Task<IEnumerable<Project>> SearchProjectsAsync(string? value, CancellationToken token)
        {
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

        private async Task OpenSessionEditAsync(TrackedTask session)
        {
            var projectName = GetSessionProjectName(session);

            var parameters = new DialogParameters<TrackedTaskDialog>
            {
                { x => x.Mode, TrackedTaskDialogMode.Edit },
                { x => x.TaskId, session.TaskId },
                // Prefer work-item identity so name/project stay locked to the parent stopwatch item.
                { x => x.TaskName, string.IsNullOrWhiteSpace(displayItemName) ? session.Details : displayItemName },
                { x => x.ProjectId, string.IsNullOrEmpty(currentProjectId) ? session.ProjectId : currentProjectId },
                { x => x.ProjectName, string.IsNullOrEmpty(displayProjectName) ? projectName : displayProjectName },
                { x => x.StartDate, session.StartDate },
                { x => x.EndDate, session.EndDate },
                { x => x.Duration, GetSessionDuration(session) },
                { x => x.IsAllDay, false },
                { x => x.Use24HourTime, SettingsService.Use24HourTime },
                { x => x.HttpClient, HttpClient },
                { x => x.StopwatchItemId, ItemId }
            };

            var dialog = await DialogService.ShowAsync<TrackedTaskDialog>(
                "Edit Duration",
                parameters,
                new DialogOptions
                {
                    MaxWidth = MaxWidth.Small,
                    FullWidth = true,
                    CloseOnEscapeKey = true,
                    BackdropClick = false
                });

            var result = await dialog.Result;

            if (result is { Canceled: false })
            {
                changed = true;
                await LoadSessionsAsync();
            }
        }

        private async Task DeleteSessionAsync(TrackedTask session)
        {
            var confirmed = await DialogService.ShowMessageBoxAsync(
                "Delete session",
                $"Delete this session ({FormatDuration(GetSessionDuration(session))} on {session.StartDate:g})?",
                yesText: "Delete",
                cancelText: "Cancel");

            if (confirmed != true)
                return;

            try
            {
                var response = await HttpClient.DeleteAsync($"{Constants.API.TrackedTask.Delete}/{session.TaskId}");
                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    Snackbar.Add(string.IsNullOrWhiteSpace(error) ? "Couldn't delete the session." : error, Severity.Error);
                    return;
                }

                changed = true;
                Snackbar.Add("Session deleted.", Severity.Success);
                await LoadSessionsAsync();
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't delete the session.");
            }
        }

        /// <summary>
        /// Deletes the whole work item and every session under it. Lives here (not on the
        /// main list) so it takes a deliberate trip into the item's history.
        /// </summary>
        private async Task DeleteWorkItemAsync()
        {
            if (hasRunningSession)
            {
                Snackbar.Add("Stop the timer before deleting this work item.", Severity.Warning);
                return;
            }

            var confirmed = await DialogService.ShowMessageBoxAsync(
                "Delete work item",
                $"Delete \"{displayItemName}\" and all {sessions.Count} of its logged session{(sessions.Count == 1 ? "" : "s")}? This can't be undone.",
                yesText: "Delete",
                cancelText: "Cancel");

            if (confirmed != true)
                return;

            isBusy = true;
            try
            {
                await StopwatchItemsClient.DeleteAsync(ItemId);
                changed = true;
                Snackbar.Add("Work item deleted.", Severity.Success);
                MudDialog.Close(DialogResult.Ok(changed));
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't delete the work item.");
            }
            finally
            {
                isBusy = false;
            }
        }

        private void Close() => MudDialog.Close(DialogResult.Ok(changed));

        private sealed record DayGroup(
            string Key,
            string Label,
            DateTime Date,
            List<TrackedTask> Sessions,
            TimeSpan Total);

        private sealed record MonthFilter(
            string Key,
            string Label,
            DateTime MonthStart,
            TimeSpan Total);
    }
}
