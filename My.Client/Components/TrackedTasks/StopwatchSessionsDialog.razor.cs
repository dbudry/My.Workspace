using Microsoft.AspNetCore.Components;
using MudBlazor;
using My.Client.Extensions;
using My.Client.Helpers;
using My.Client.Models;
using My.Client.Services;
using My.Shared.Constants;

namespace My.Client.Components.TrackedTasks
{
    public partial class StopwatchSessionsDialog
    {
        [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = null!;

        [Parameter] public string ItemId { get; set; } = null!;
        [Parameter] public string ItemName { get; set; } = "";
        [Parameter] public string? ItemProjectName { get; set; }
        /// <summary>When set, only sessions on this calendar day are shown (e.g. from a grouped calendar chip).</summary>
        [Parameter] public DateTime? DayFilter { get; set; }
        [Parameter] public HttpClient HttpClient { get; set; } = null!;

        [Inject] private ISnackbar Snackbar { get; set; } = null!;
        [Inject] private IDialogService DialogService { get; set; } = null!;
        [Inject] private UserSettingsService SettingsService { get; set; } = null!;
        [Inject] private StopwatchItemsClient StopwatchItemsClient { get; set; } = null!;

        private readonly List<TrackedTask> sessions = new();
        private readonly Dictionary<string, List<TrackedTask>> sessionsByDay = new();
        private bool isLoading = true;
        private bool changed;

        protected override async Task OnInitializedAsync()
        {
            await SettingsService.GetSettingsAsync();
            await LoadSessionsAsync();
        }

        private async Task LoadSessionsAsync()
        {
            isLoading = true;
            try
            {
                var dtos = await StopwatchItemsClient.LoadSessionsAsync(ItemId);
                sessions.Clear();
                sessions.AddRange(dtos.Select(d => new TrackedTask(d)));
                GroupSessionsByDay();
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

        private void GroupSessionsByDay()
        {
            sessionsByDay.Clear();
            var visible = sessions.AsEnumerable();
            if (DayFilter.HasValue)
                visible = visible.Where(s => s.StartDate.Date == DayFilter.Value.Date);

            var groups = visible
                .GroupBy(s => s.StartDate.Date.ToLongDateString())
                .OrderByDescending(g => g.First().StartDate.Date);

            foreach (var group in groups)
                sessionsByDay[group.Key] = group.OrderByDescending(s => s.StartDate).ToList();
        }

        private string? GetSessionProjectName(TrackedTask session)
            => ProjectDisplayHelper.FromModel(session.Project) ?? ItemProjectName;

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

        private async Task OpenSessionEditAsync(TrackedTask session)
        {
            var projectName = GetSessionProjectName(session);

            var parameters = new DialogParameters<TrackedTaskDialog>
            {
                { x => x.Mode, TrackedTaskDialogMode.Edit },
                { x => x.TaskId, session.TaskId },
                { x => x.TaskName, session.Name },
                { x => x.ProjectId, session.ProjectId },
                { x => x.ProjectName, projectName },
                { x => x.StartDate, session.StartDate },
                { x => x.EndDate, session.EndDate },
                { x => x.Duration, GetSessionDuration(session) },
                { x => x.IsAllDay, session.IsAllDay },
                { x => x.Use24HourTime, SettingsService.Use24HourTime },
                { x => x.HttpClient, HttpClient }
            };

            var dialog = await DialogService.ShowAsync<TrackedTaskDialog>(
                $"Edit — {session.Name}",
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

        private void Close() => MudDialog.Close(DialogResult.Ok(changed));
    }
}