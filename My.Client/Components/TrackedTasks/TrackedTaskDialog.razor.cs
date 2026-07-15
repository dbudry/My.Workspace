using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;
using System.Globalization;
using System.Net.Http.Json;
using My.Client.Models;
using My.Client.Services;
using My.Shared.Constants;
using My.Shared.Dtos.Paging;
using My.Shared.Dtos.Project;
using My.Shared.Dtos.TrackedTask;
using My.Shared.Rules;

namespace My.Client.Components.TrackedTasks
{
    public enum TrackedTaskDialogMode
    {
        Create,
        Edit,
        ReadOnly
    }

    public partial class TrackedTaskDialog
    {
        [CascadingParameter]
        private IMudDialogInstance MudDialog { get; set; } = null!;

        [Parameter] public TrackedTaskDialogMode Mode { get; set; } = TrackedTaskDialogMode.Edit;
        [Parameter] public string? TaskId { get; set; }
        [Parameter] public string TaskName { get; set; } = "";
        [Parameter] public string? ProjectId { get; set; }
        [Parameter] public string? ProjectName { get; set; }
        [Parameter] public DateTime StartDate { get; set; } = DateTime.Now;
        [Parameter] public DateTime? EndDate { get; set; }
        [Parameter] public TimeSpan Duration { get; set; }
        [Parameter] public bool IsAllDay { get; set; }
        [Parameter] public bool Use24HourTime { get; set; }
        [Parameter] public HttpClient HttpClient { get; set; } = null!;

        [Inject] private IDialogService DialogService { get; set; } = null!;
        [Inject] private ISnackbar Snackbar { get; set; } = null!;
        [Inject] private ProjectsCache ProjectsCache { get; set; } = null!;
        [Inject] private AppSettingsCache AppSettingsCache { get; set; } = null!;

        private string DialogTitle => Mode switch
        {
            TrackedTaskDialogMode.Create => "New Task",
            TrackedTaskDialogMode.ReadOnly => TaskName,
            _ => $"Edit - {TaskName}"
        };

        private string editName = "";
        private DateTime? editStartDateOnly;
        private DateTime? editEndDateOnly;
        private TimeSpan? editStartTime;
        private int editDurationHours;
        private int editDurationMinutes;
        private bool editIsAllDay;
        private Project? selectedProject;

        private bool isBusy;
        private string? saveError;
        private string? timeParseError;
        private double workdayHours = AllDayEntryRules.DefaultWorkdayHours;

        private string WorkdayHoursLabel =>
            workdayHours == Math.Truncate(workdayHours) ? $"{workdayHours:0}h" : $"{workdayHours:0.##}h";

        private string StartTimeHelper => Use24HourTime
            ? "HH:mm — e.g. 14:30"
            : "h:mm AM/PM — e.g. 2:30 PM";

        private string StartTimePlaceholder => Use24HourTime ? "14:30" : "2:30 PM";

        /// <summary>
        /// Free-text binding for the Start Time field. Renders the current <c>editStartTime</c>
        /// in 24-hour or 12-hour format depending on the user's preference, and on set parses
        /// the typed string with <see cref="ParseTimeText"/>. Invalid text sets
        /// <c>timeParseError</c> so the field shows red and the user knows to retry; <c>editStartTime</c>
        /// stays at its last good value so a save doesn't silently nuke their time.
        /// </summary>
        private string EditStartTimeText
        {
            get
            {
                if (!editStartTime.HasValue) return string.Empty;
                var dt = DateTime.Today.Add(editStartTime.Value);
                return Use24HourTime ? dt.ToString("HH:mm") : dt.ToString("h:mm tt", CultureInfo.InvariantCulture);
            }
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    editStartTime = null;
                    timeParseError = null;
                    return;
                }
                if (ParseTimeText(value, out var parsed))
                {
                    editStartTime = parsed;
                    timeParseError = null;
                }
                else
                {
                    timeParseError = "Couldn't read that as a time. Try 9:30 AM or 14:30.";
                }
            }
        }

        /// <summary>
        /// Liberal time parser that accepts the formats people actually type:
        /// <list type="bullet">
        ///   <item>"9", "9p", "9pm", "9 pm" — bare hour + optional AM/PM</item>
        ///   <item>"9:30", "9:30 PM", "9:30pm" — 12-hour with or without space</item>
        ///   <item>"14:30", "21:00" — 24-hour</item>
        /// </list>
        /// Returns true and the parsed TimeSpan if it can make sense of the input.
        /// </summary>
        private static bool ParseTimeText(string raw, out TimeSpan result)
        {
            result = TimeSpan.Zero;
            var s = raw.Trim().ToUpperInvariant().Replace(" ", "");

            string[] formats = {
                "h:mmtt", "h:mtt", "htt",
                "hh:mmtt", "hh:mtt", "hhtt",
                "H:mm", "H:m", "H",
                "HH:mm", "HH:m", "HH",
                "h:mm", "h:m", "h",
                "hh:mm", "hh:m", "hh"
            };

            if (DateTime.TryParseExact(s, formats, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var dt))
            {
                result = dt.TimeOfDay;
                return true;
            }

            if (DateTime.TryParse(raw, CultureInfo.CurrentCulture,
                    DateTimeStyles.None, out var dt2))
            {
                result = dt2.TimeOfDay;
                return true;
            }

            return false;
        }

        private string SpanDaysLabel
        {
            get
            {
                var start = editStartDateOnly ?? StartDate.Date;
                var end = editEndDateOnly ?? start;
                var workdays = AllDayEntryRules.WorkdaysInSpan(start, end);
                if (workdays == 0) return "0 workdays (weekend only)";
                return workdays == 1 ? "1 workday" : $"{workdays} workdays";
            }
        }

        protected override async Task OnInitializedAsync()
        {
            editName = TaskName;
            editStartDateOnly = StartDate.Date;
            editStartTime = StartDate.TimeOfDay;
            editDurationHours = (int)Duration.TotalHours;
            editDurationMinutes = Duration.Minutes;
            editIsAllDay = IsAllDay;
            editEndDateOnly = IsAllDay
                ? (EndDate?.Date ?? StartDate.Date)
                : null;

            if (Mode != TrackedTaskDialogMode.ReadOnly)
            {
                await LoadProjects();

                try
                {
                    var settings = await AppSettingsCache.GetAsync();
                    var raw = settings.FirstOrDefault(s => s.Key == Constants.SettingKeys.WorkdayHours)?.Value;
                    workdayHours = AllDayEntryRules.ParseWorkdayHours(raw);
                }
                catch { /* fall back to default; this is just for the helper label */ }
            }
        }

        private void OnAllDayChanged(bool value)
        {
            editIsAllDay = value;
            if (value)
            {
                // Default end = same day for a single-day vacation.
                editEndDateOnly ??= editStartDateOnly ?? StartDate.Date;
            }
        }

        private async Task LoadProjects()
        {
            if (string.IsNullOrEmpty(ProjectId))
                return;

            try
            {
                var match = await ProjectsCache.LookupAsync(search: ProjectName ?? ProjectId);
                selectedProject = match.FirstOrDefault(p => p.ProjectId == ProjectId);

                // Lookup is capped at 25 rows — fall back to a name search when id-only misses.
                if (selectedProject == null && !string.IsNullOrEmpty(ProjectName))
                {
                    match = await ProjectsCache.LookupAsync(search: ProjectName);
                    selectedProject = match.FirstOrDefault(p => p.ProjectId == ProjectId)
                        ?? match.FirstOrDefault(p => p.Name.Equals(ProjectName, StringComparison.OrdinalIgnoreCase));
                }
            }
            catch { }
        }

        private async Task<IEnumerable<Project>> SearchProjects(string? value, CancellationToken token)
        {
            try
            {
                var results = await ProjectsCache.LookupAsync(search: value);
                return results.Where(p => p.IsActive && !p.IsArchived);
            }
            catch
            {
                return Enumerable.Empty<Project>();
            }
        }

        private void OnProjectChanged(Project? project) => selectedProject = project;

        private async Task SaveAsync()
        {
            if (string.IsNullOrWhiteSpace(editName))
            {
                saveError = "Task name is required.";
                return;
            }

            if (!editIsAllDay && !string.IsNullOrEmpty(timeParseError))
            {
                saveError = timeParseError;
                return;
            }

            isBusy = true;
            saveError = null;

            try
            {
                DateTime startDate;
                TimeSpan duration;
                DateTime? endDate;

                if (editIsAllDay)
                {
                    var startDay = (editStartDateOnly ?? StartDate.Date).Date;
                    var endDay = (editEndDateOnly ?? startDay).Date;
                    if (endDay < startDay)
                    {
                        saveError = "End date can't be before start date.";
                        isBusy = false;
                        return;
                    }
                    // Stamp Kind=Utc so System.Text.Json emits the "Z" suffix. The server
                    // sees the literal date the user picked (no offset, no shift). Without
                    // this, MudDatePicker hands us a Kind=Unspecified DateTime that gets
                    // serialized without a timezone — the server then interprets it via
                    // its own locale and a May 20 entry rounds to May 19 UTC for anyone
                    // west of UTC.
                    startDate = DateTime.SpecifyKind(startDay, DateTimeKind.Utc);
                    endDate = DateTime.SpecifyKind(endDay, DateTimeKind.Utc);
                    duration = AllDayEntryRules.DurationFor(startDay, endDay, workdayHours);
                }
                else
                {
                    startDate = (editStartDateOnly ?? StartDate.Date).Add(editStartTime ?? TimeSpan.Zero);
                    duration = new TimeSpan(editDurationHours, editDurationMinutes, 0);
                    endDate = startDate.Add(duration);
                }

                HttpResponseMessage response;

                if (Mode == TrackedTaskDialogMode.Create)
                {
                    var dto = new CreateTrackedTaskDto
                    {
                        Name = editName,
                        StartDate = startDate,
                        Duration = duration,
                        IsAllDay = editIsAllDay,
                        EndDate = editIsAllDay ? endDate : null,
                        ProjectId = selectedProject?.ProjectId
                    };
                    response = await HttpClient.PostAsJsonAsync(Constants.API.TrackedTask.Create, dto);
                }
                else
                {
                    var dto = new UpdateTrackedTaskDto
                    {
                        TaskId = TaskId!,
                        Name = editName,
                        StartDate = startDate,
                        EndDate = endDate,
                        Duration = duration,
                        IsAllDay = editIsAllDay,
                        ProjectId = selectedProject?.ProjectId
                    };
                    response = await HttpClient.PutAsJsonAsync(Constants.API.TrackedTask.Update, dto);
                }

                if (response.IsSuccessStatusCode)
                {
                    MudDialog.Close(DialogResult.Ok(true));
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    saveError = string.IsNullOrEmpty(error) ? "Failed to save changes." : error;
                }
            }
            catch (Exception ex)
            {
                saveError = ex.Message;
            }

            isBusy = false;
        }

        private async Task DeleteAsync()
        {
            if (Mode != TrackedTaskDialogMode.Edit || string.IsNullOrEmpty(TaskId)) return;

            var confirmed = await DialogService.ShowMessageBoxAsync(
                "Confirm Delete", $"Are you sure you want to delete \"{editName}\"?",
                yesText: "Delete", cancelText: "Cancel");

            if (confirmed != true) return;

            isBusy = true;
            try
            {
                var response = await HttpClient.DeleteAsync($"{Constants.API.TrackedTask.Delete}/{TaskId}");
                if (response.IsSuccessStatusCode)
                {
                    Snackbar.Add("Task deleted.", Severity.Success);
                    MudDialog.Close(DialogResult.Ok(true));
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    saveError = string.IsNullOrEmpty(error) ? "Failed to delete task." : error;
                }
            }
            catch (Exception ex)
            {
                saveError = ex.Message;
            }
            isBusy = false;
        }

        private async Task DuplicateAsync()
        {
            if (Mode != TrackedTaskDialogMode.Edit || string.IsNullOrEmpty(TaskId)) return;

            isBusy = true;
            try
            {
                var response = await HttpClient.PostAsJsonAsync(
                    $"{Constants.API.TrackedTask.Duplicate}/{TaskId}/duplicate",
                    new DuplicateTrackedTaskDto());

                if (response.IsSuccessStatusCode)
                {
                    Snackbar.Add("Task duplicated.", Severity.Success);
                    MudDialog.Close(DialogResult.Ok(true));
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    saveError = string.IsNullOrEmpty(error) ? "Failed to duplicate task." : error;
                }
            }
            catch (Exception ex)
            {
                saveError = ex.Message;
            }
            isBusy = false;
        }

        private async Task HandleKeyDown(KeyboardEventArgs e)
        {
            if (e.Key == "Escape")
            {
                Close();
                return;
            }

            var mod = e.CtrlKey || e.MetaKey;
            if (!mod) return;

            if (e.Key == "Enter" && Mode != TrackedTaskDialogMode.ReadOnly && !isBusy)
                await SaveAsync();
            else if ((e.Key == "d" || e.Key == "D") && Mode == TrackedTaskDialogMode.Edit && !isBusy)
                await DuplicateAsync();
            else if ((e.Key == "Delete" || e.Key == "Backspace") && Mode == TrackedTaskDialogMode.Edit && !isBusy)
                await DeleteAsync();
        }

        private string FormatDateTime(DateTime dt)
        {
            return Use24HourTime
                ? dt.ToString("MM/dd/yyyy HH:mm")
                : dt.ToString("MM/dd/yyyy h:mm tt");
        }

        private void Close() => MudDialog.Close(DialogResult.Cancel());
    }
}
