using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;
using My.Client.Components.TrackedTasks;
using My.Client.Extensions;
using My.Client.Helpers;
using My.Client.Models;
using My.Client.Services;
using My.Shared.Constants;
using My.Shared.Dtos.Paging;
using My.Shared.Dtos.StopwatchItem;
using My.Shared.Dtos.TaskList;
using My.Shared.Dtos.TrackedTask;
using My.Shared.Rules;
using My.Shared.Validation;

namespace My.Client.Pages.Tyme
{
    public partial class Tasks
    {
        private MudTable<TaskListRow>? table;

        private TasksViewMode viewMode = TasksViewMode.Grid;

        /// <summary>Project view: business week (M–F) vs full week — right side of view-toggle row.</summary>
        private bool projectBusinessWeekOnly = true;
        private DateTime projectWeekStartMonday;
        private DateTime? projectWeekPickerDate;
        private bool isProjectWeekBusy;
        private WeekEntryPanel? weekEntryPanel;
        private WeekDayAcrossGrid? weekDayAcrossGrid;
        private Project? projectViewSelectedProject;

        // --- Weekly view ---
        private DateTime weekStartMonday;
        private DateTime? weekPickerDate;
        private List<TaskListRow> weeklyRows = new();
        private WeeklyTimeTotalsRules.Result weeklyTotals =
            new(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, ShowAdjustedSeparately: false);
        private bool isLoadingWeekly;
        private string? weeklyLoadError;
        private CancellationTokenSource? weeklyLoadCts;
        private WeeklyLayoutMode weeklyLayoutMode = WeeklyLayoutMode.List;
        private bool weeklyBusinessWeekOnly = true;
        private bool isWeeklyDayBusy;

        string searchString = "";
        private EmployeeTimeDisplayMode displayMode = EmployeeTimeDisplayMode.Both;

        /// <summary>Drives the color bar next to project names (Settings ProjectColorSource).</summary>
        private ProjectColorSource projectColorSource = ProjectColorSource.GroupThenOrganization;
        private bool isSavingColorSource;

        /// <summary>App Settings → Tyme → Track start time of day (default true).</summary>
        private bool trackTimeOfDay = TymeTimeOfDayRules.DefaultTrackTimeOfDay;

        /// <summary>Name drafts while a cell is open (keyed by task id).</summary>
        private readonly Dictionary<string, string> nameDrafts = new(StringComparer.Ordinal);

        /// <summary>H:MM drafts for in-row duration edits (keyed by task id).</summary>
        private readonly Dictionary<string, string> durationDrafts = new(StringComparer.Ordinal);

        /// <summary>Start-time text drafts for in-row edits (keyed by task id).</summary>
        private readonly Dictionary<string, string> startTimeDrafts = new(StringComparer.Ordinal);

        /// <summary>Task ids currently saving an inline edit.</summary>
        private readonly HashSet<string> savingTaskIds = new(StringComparer.Ordinal);

        /// <summary>
        /// Active click-to-edit cell: "taskId:name|project|date|time|duration".
        /// Null means all fields show as normal text.
        /// </summary>
        private string? activeInlineEditKey;

        /// <summary>Per-field FluentValidation errors (key = taskId:field).</summary>
        private readonly Dictionary<string, string> inlineFieldErrors = new(StringComparer.Ordinal);

        private static readonly TaskStartTimeTextValidator StartTimeValidator = new();
        private static readonly TaskNameTextValidator NameValidator = new();

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

        [Inject]
        private TasksPagePreferencesService PagePreferences { get; set; } = null!;

        [Inject]
        private ProjectsCache ProjectsCache { get; set; } = null!;

        [Inject]
        private StopwatchItemsClient StopwatchItemsClient { get; set; } = null!;

        #endregion

        private string FormatHours(TimeSpan t) => WeekEntryGridRules.FormatDuration(t);

        private static string FormatShortDate(DateTime d) => d.ToString("MM/dd/yy");

        private string FormatStartTime(DateTime d) => SettingsService.FormatTime(d);

        private static bool CanInlineEdit(TaskListRow row) =>
            row.Kind == TaskListRowKind.Manual
            && row.ManualTask != null
            && !row.IsLocked
            && !row.IsOverlayRow;

        private bool IsRowSaving(string taskId) => savingTaskIds.Contains(taskId);

        private static string InlineEditKey(string taskId, string field) => $"{taskId}:{field}";

        private bool IsEditing(string taskId, string field) =>
            string.Equals(activeInlineEditKey, InlineEditKey(taskId, field), StringComparison.Ordinal);

        private DateTime inlineEditOpenedUtc;

        private void BeginInlineEdit(string taskId, string field)
        {
            activeInlineEditKey = InlineEditKey(taskId, field);
            inlineEditOpenedUtc = DateTime.UtcNow;
            // Ensure re-render even when called from dblclick (MudTable can swallow updates).
            _ = InvokeAsync(StateHasChanged);
        }

        /// <summary>
        /// The click that opens an inline field can blur the new input in the same
        /// gesture and immediately commit/close it. Ignore that first blur.
        /// </summary>
        private bool ShouldIgnorePrematureInlineBlur() =>
            (DateTime.UtcNow - inlineEditOpenedUtc).TotalMilliseconds < 400;

        private void EndInlineEdit(string? taskId = null)
        {
            activeInlineEditKey = null;
            if (!string.IsNullOrEmpty(taskId))
            {
                nameDrafts.Remove(taskId);
                durationDrafts.Remove(taskId);
                startTimeDrafts.Remove(taskId);
                ClearInlineFieldErrors(taskId);
            }
        }

        private static string FieldErrorKey(string taskId, string field) => $"{taskId}:{field}";

        private string? GetInlineFieldError(string taskId, string field) =>
            inlineFieldErrors.TryGetValue(FieldErrorKey(taskId, field), out var e) ? e : null;

        private void SetInlineFieldError(string taskId, string field, string? error)
        {
            var key = FieldErrorKey(taskId, field);
            if (string.IsNullOrEmpty(error))
                inlineFieldErrors.Remove(key);
            else
                inlineFieldErrors[key] = error;
        }

        private void ClearInlineFieldErrors(string taskId)
        {
            foreach (var field in new[] { "name", "time", "duration", "date", "project" })
                inlineFieldErrors.Remove(FieldErrorKey(taskId, field));
        }

        private string GetNameDraft(TrackedTask task)
        {
            if (nameDrafts.TryGetValue(task.TaskId, out var draft))
                return draft;
            return task.Details ?? string.Empty;
        }

        private void SetNameDraft(TrackedTask task, string? value) =>
            nameDrafts[task.TaskId] = value ?? string.Empty;

        private string GetDurationDraft(TrackedTask task)
        {
            if (durationDrafts.TryGetValue(task.TaskId, out var draft))
                return draft;
            return WeekEntryGridRules.FormatDayDurationInput(task.Duration);
        }

        private void SetDurationDraft(TrackedTask task, string? value) =>
            // Soft filter only while typing — do not re-pad/re-order (that kills selection/caret).
            durationDrafts[task.TaskId] = WeekEntryGridRules.FilterDurationInputChars(value);

        private string GetStartTimeDraft(TrackedTask task)
        {
            if (startTimeDrafts.TryGetValue(task.TaskId, out var draft))
                return draft;
            return FormatStartTime(task.StartDate);
        }

        private void SetStartTimeDraft(TrackedTask task, string? value) =>
            startTimeDrafts[task.TaskId] = value ?? string.Empty;

        /// <summary>
        /// Prefer live ManualTask values over the immutable list-row snapshot.
        /// Same H:MM shape for manual and stopwatch so the Duration column lines up.
        /// </summary>
        private static string FormatRowDuration(TaskListRow row)
        {
            var duration = row.Kind == TaskListRowKind.Stopwatch
                ? row.Duration
                : (row.ManualTask?.Duration ?? row.Duration);
            // Zero still shows as empty for manual drafts; stopwatch shows 00:00 so the cell isn't blank.
            if (duration <= TimeSpan.Zero)
                return row.Kind == TaskListRowKind.Stopwatch ? "00:00" : WeekEntryGridRules.FormatDayDurationInput(duration);
            return WeekEntryGridRules.FormatDayDurationInput(duration);
        }

        private static string FormatRowDisplayName(TaskListRow row) =>
            row.ManualTask?.Details ?? row.Details;

        private static DateTime FormatRowDisplayDate(TaskListRow row) =>
            row.ManualTask?.StartDate ?? row.DisplayDate;

        private static string? FormatRowProjectName(TaskListRow row) =>
            row.ManualTask?.Project?.Name ?? row.ProjectName;

        private async Task<IEnumerable<Project>> SearchProjectsForInline(string? value, CancellationToken token)
        {
            try
            {
                return await ProjectsCache.LookupActiveAsync(search: value);
            }
            catch
            {
                return Enumerable.Empty<Project>();
            }
        }

        /// <summary>
        /// Commit name from the draft buffer on blur. Keep the editor open on validation
        /// failure; never unmount the field mid-blur (that crashed the page).
        /// </summary>
        private async Task CommitInlineNameAsync(TaskListRow row)
        {
            try
            {
                if (!CanInlineEdit(row)) { await ExitInlineEditSafeAsync(null); return; }
                var task = row.ManualTask!;
                if (!IsEditing(task.TaskId, "name")) return;
                if (ShouldIgnorePrematureInlineBlur()) return;

                var name = WeekEntryGridRules.SanitizeTaskDetails(GetNameDraft(task));
                SetNameDraft(task, name);

                var result = await NameValidator.ValidateAsync(new TaskNameText { Value = name });
                if (!result.IsValid)
                {
                    SetInlineFieldError(task.TaskId, "name", ValidationResultFormatter.ToMessage(result));
                    await InvokeAsync(StateHasChanged);
                    return;
                }

                SetInlineFieldError(task.TaskId, "name", null);
                task.Details = name;
                await SaveInlineAsync(row);
                await ExitInlineEditSafeAsync(task.TaskId);
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't save task name.");
            }
        }

        private async Task OnInlineProjectChangedAsync(TaskListRow row, Project? project)
        {
            try
            {
                if (!CanInlineEdit(row)) return;
                var task = row.ManualTask!;

                // Ignore no-op re-renders; only save when project identity actually changes.
                var nextId = project?.ProjectId;
                if (string.Equals(task.ProjectId, nextId, StringComparison.Ordinal))
                {
                    task.Project = project ?? task.Project;
                    // Selection of the same project (or clear with no change) still leaves edit mode.
                    await ExitInlineEditSafeAsync(task.TaskId);
                    return;
                }

                task.ProjectId = nextId;
                task.Project = project;
                await SaveInlineAsync(row);
                await ExitInlineEditSafeAsync(task.TaskId);
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't save project.");
            }
        }

        private async Task OnInlineDateChangedAsync(TaskListRow row, DateTime? date)
        {
            try
            {
                if (!CanInlineEdit(row) || !date.HasValue) return;
                var task = row.ManualTask!;
                var day = date.Value.Date;
                if (task.StartDate.Date == day)
                    return;

                if (task.IsAllDay)
                {
                    var oldStart = task.StartDate.Date;
                    var oldEnd = (task.EndDate ?? task.StartDate).Date;
                    var spanDays = Math.Max(0, (oldEnd - oldStart).Days);
                    task.StartDate = DateTime.SpecifyKind(day, DateTimeKind.Utc);
                    task.EndDate = DateTime.SpecifyKind(day.AddDays(spanDays), DateTimeKind.Utc);
                }
                else
                {
                    var time = task.StartDate.TimeOfDay;
                    task.StartDate = day.Add(time);
                    if (task.Duration > TimeSpan.Zero)
                        task.EndDate = task.StartDate + task.Duration;
                }

                await SaveInlineAsync(row);
                await ExitInlineEditSafeAsync(task.TaskId);
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't save date.");
            }
        }

        private async Task CommitInlineStartTimeAsync(TaskListRow row)
        {
            try
            {
                if (!CanInlineEdit(row) || row.IsAllDay || !trackTimeOfDay)
                {
                    await ExitInlineEditSafeAsync(null);
                    return;
                }
                var task = row.ManualTask!;
                if (!IsEditing(task.TaskId, "time")) return;
                if (ShouldIgnorePrematureInlineBlur()) return;

                var text = GetStartTimeDraft(task);
                var result = await StartTimeValidator.ValidateAsync(new TaskStartTimeText
                {
                    Value = text,
                    Use24HourTime = SettingsService.Use24HourTime
                });

                if (!result.IsValid)
                {
                    // Keep the input open and show field-level FluentValidation errors.
                    SetInlineFieldError(task.TaskId, "time", ValidationResultFormatter.ToMessage(result));
                    await InvokeAsync(StateHasChanged);
                    return;
                }

                if (!TimeOfDayTextRules.TryParse(text, out var timeOfDay))
                {
                    SetInlineFieldError(task.TaskId, "time",
                        TimeOfDayTextRules.InvalidMessage(SettingsService.Use24HourTime));
                    await InvokeAsync(StateHasChanged);
                    return;
                }

                SetInlineFieldError(task.TaskId, "time", null);
                task.StartDate = task.StartDate.Date.Add(timeOfDay);
                if (task.Duration > TimeSpan.Zero)
                    task.EndDate = task.StartDate + task.Duration;

                await SaveInlineAsync(row);
                await ExitInlineEditSafeAsync(task.TaskId);
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't save start time.");
            }
        }

        private Task OnInlineDurationKeyDown(TaskListRow row, KeyboardEventArgs e) =>
            e.Key == "Enter" ? CommitInlineDurationAsync(row) : Task.CompletedTask;

        private Task OnInlineStartTimeKeyDown(TaskListRow row, KeyboardEventArgs e) =>
            e.Key == "Enter" ? CommitInlineStartTimeAsync(row) : Task.CompletedTask;

        private async Task CommitInlineDurationAsync(TaskListRow row)
        {
            try
            {
                if (!CanInlineEdit(row) || row.IsAllDay) { await ExitInlineEditSafeAsync(null); return; }
                var task = row.ManualTask!;
                if (!IsEditing(task.TaskId, "duration")) return;
                if (ShouldIgnorePrematureInlineBlur()) return;

                // Blur: soft-filter → commit ("4"→4h). Normalize only if needed to clamp mins/24h.
                var text = WeekEntryGridRules.FilterDurationInputChars(GetDurationDraft(task));
                if (!WeekEntryGridRules.TryCommitDayDurationText(text, out var duration))
                {
                    text = WeekEntryGridRules.NormalizeDayDurationText(text);
                    if (!WeekEntryGridRules.TryCommitDayDurationText(text, out duration))
                    {
                        SetInlineFieldError(task.TaskId, "duration", null);
                        await ExitInlineEditSafeAsync(task.TaskId);
                        return;
                    }
                }

                if (duration <= TimeSpan.Zero)
                {
                    SetInlineFieldError(task.TaskId, "duration", null);
                    await ExitInlineEditSafeAsync(task.TaskId);
                    return;
                }

                SetInlineFieldError(task.TaskId, "duration", null);
                task.Duration = WeekEntryGridRules.NormalizeDuration(duration);
                task.EndDate = task.StartDate + task.Duration;
                await SaveInlineAsync(row);
                await ExitInlineEditSafeAsync(task.TaskId);
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't save duration.");
            }
        }

        /// <summary>
        /// Leave edit mode after the blur pipeline finishes so we never dispose a MudTextField
        /// mid-OnBlur (that threw and tripped the page error boundary).
        /// </summary>
        private async Task ExitInlineEditSafeAsync(string? taskId)
        {
            await Task.Yield();
            EndInlineEdit(taskId);
            await InvokeAsync(StateHasChanged);
        }

        private async Task SaveInlineAsync(TaskListRow row)
        {
            var task = row.ManualTask;
            if (task == null || !CanInlineEdit(row)) return;
            if (!savingTaskIds.Add(task.TaskId)) return;

            try
            {
                DateTime startDate;
                DateTime? endDate;
                if (task.IsAllDay)
                {
                    startDate = DateTime.SpecifyKind(task.StartDate.Date, DateTimeKind.Utc);
                    endDate = DateTime.SpecifyKind((task.EndDate ?? task.StartDate).Date, DateTimeKind.Utc);
                }
                else
                {
                    // UI holds wall clock in UserSettings.TimeZone; API stores UTC.
                    // When time-of-day is off, pin start to midnight of the entry's date.
                    DateTime wallStart = task.StartDate;
                    if (!trackTimeOfDay)
                    {
                        wallStart = task.StartDate.Date.Add(
                            TymeTimeOfDayRules.DefaultStartTimeOfDayWhenNotTracked);
                        task.StartDate = wallStart;
                        if (task.Duration > TimeSpan.Zero)
                            task.EndDate = wallStart + task.Duration;
                    }

                    var localEnd = task.EndDate ?? (task.Duration > TimeSpan.Zero ? wallStart + task.Duration : null);
                    startDate = SettingsService.ConvertFromUserTime(wallStart);
                    endDate = localEnd.HasValue ? SettingsService.ConvertFromUserTime(localEnd.Value) : null;
                }

                var dto = new UpdateTrackedTaskDto
                {
                    TaskId = task.TaskId,
                    Details = WeekEntryGridRules.SanitizeTaskDetails(task.Details),
                    StartDate = startDate,
                    EndDate = endDate,
                    Duration = task.Duration,
                    IsAllDay = task.IsAllDay,
                    ProjectId = task.ProjectId
                };
                task.Details = dto.Details;

                var response = await client.PutAsJsonAsync(Constants.API.TrackedTask.Update, dto);
                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    Snackbar.Add(string.IsNullOrWhiteSpace(error) ? "Couldn't save row." : error, Severity.Error);
                    await RefreshCurrentViewAsync();
                    return;
                }

                nameDrafts.Remove(task.TaskId);
                durationDrafts.Remove(task.TaskId);
                startTimeDrafts.Remove(task.TaskId);
                // Keep ManualTask mutations visible — do not full-reload grid (would wipe focus
                // and re-snapshot). Week totals need a refresh of the footer only.
                if (viewMode == TasksViewMode.Weekly)
                    RefreshWeeklyTotalsOnly();
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't save row.");
                await RefreshCurrentViewAsync();
            }
            finally
            {
                savingTaskIds.Remove(task.TaskId);
                await InvokeAsync(StateHasChanged);
            }
        }

        /// <summary>
        /// Recompute week footer from the in-memory rows (ManualTask already mutated).
        /// Avoids a full reload that would replace TaskListRow snapshots and hide edits.
        /// </summary>
        private void RefreshWeeklyTotalsOnly()
        {
            var from = weekStartMonday.Date;
            var to = WeekEntryGridRules.GetWeekEndSunday(weekStartMonday);
            var slices = weeklyRows
                .Where(r => r.ManualTask != null && !r.IsOverlayRow)
                .Select(r =>
                {
                    var t = r.ManualTask!;
                    TimeSpan? adjusted = null;
                    if (t.ManagerAdjustment != null && t.AdjustmentKind is "Alias" or "Direct")
                        adjusted = t.ManagerAdjustment.Duration;
                    return new WeeklyTimeTotalsRules.TaskDurationSlice(t.StartDate, t.Duration, adjusted);
                });
            weeklyTotals = WeeklyTimeTotalsRules.Compute(slices, from, to, displayMode);
        }

        protected override async Task OnInitializedAsync()
        {
            var authState = await AuthenticationStateTask;
            var user = authState.User;

            if (user.Identity != null && !user.Identity.IsAuthenticated)
                Navigation.NavigateTo($"{Navigation.BaseUri}auth/login", true);

            client = ClientFactory.CreateClient(Constants.API.ClientName);

            SetPageTitle?.Invoke("Tasks");

            var todayMonday = WeekEntryGridRules.GetWeekStartMonday(DateTime.Today);
            weekStartMonday = todayMonday;
            weekPickerDate = todayMonday;
            projectWeekStartMonday = todayMonday;
            projectWeekPickerDate = todayMonday;

            // Restore saved view/week/search before the other, slower awaits below so the
            // "All" tab + current week default fields set just above are on screen for as
            // short a window as possible (see preferencesRestored for why the window matters
            // at all, not just how long it's visible for).
            await RestorePreferencesAsync();

            var settings = await SettingsService.GetSettingsAsync();
            projectColorSource = NormalizeLabelColorToggle(settings.ProjectColorSource);
            try
            {
                trackTimeOfDay = await AppSettingsCache.GetTymeTrackTimeOfDayAsync();
            }
            catch
            {
                trackTimeOfDay = TymeTimeOfDayRules.DefaultTrackTimeOfDay;
            }

            await LoadDisplayModeFromAppSettingsAsync();

            if (viewMode == TasksViewMode.Weekly && weeklyLayoutMode == WeeklyLayoutMode.List)
                await LoadWeeklyAsync();
        }

        /// <summary>
        /// Toggle only has Org vs Project Group. Map other saved values onto one of those.
        /// </summary>
        private static ProjectColorSource NormalizeLabelColorToggle(ProjectColorSource source) =>
            source == ProjectColorSource.Organization
                ? ProjectColorSource.Organization
                : ProjectColorSource.ProjectGroup;

        /// <summary>
        /// Flips the color bar source (org vs project group). Local state drives the UI
        /// immediately; settings are saved in the background for other pages.
        /// </summary>
        private async Task OnProjectColorSourceChangedAsync(ProjectColorSource source)
        {
            source = NormalizeLabelColorToggle(source);
            if (source == projectColorSource) return;

            var previous = projectColorSource;
            projectColorSource = source;
            // Re-render rows now — colors live on existing row models; no need to reload.
            await InvokeAsync(StateHasChanged);

            isSavingColorSource = true;
            try
            {
                await SettingsService.UpdateProjectColorSourceAsync(source);
            }
            catch (Exception ex)
            {
                projectColorSource = previous;
                Snackbar.AddApiError(ex, "Couldn't save label color preference.");
                await InvokeAsync(StateHasChanged);
            }
            finally
            {
                isSavingColorSource = false;
            }
        }

        /// <summary>
        /// Guards <see cref="PersistPreferencesAsync"/> until the saved preferences have
        /// actually been read back. Every field this page starts with (viewMode, ...) is a
        /// hardcoded default ("All") set synchronously before this class-level flag matters and
        /// before <see cref="RestorePreferencesAsync"/> gets a chance to run its awaits. If the
        /// user clicks a tab during that window — plausible on a slow first load — the resulting
        /// PersistPreferencesAsync call would overwrite the real saved preferences in local
        /// storage with those defaults.
        /// </summary>
        private bool preferencesRestored;

        private async Task RestorePreferencesAsync()
        {
            await PagePreferences.LoadAsync();
            // Cold WASM start: JS interop can fail once; retry after a tick without wiping prefs.
            if (!PagePreferences.IsLoadSuccessful)
            {
                await Task.Delay(50);
                await PagePreferences.LoadAsync();
            }

            var p = PagePreferences.Preferences;

            viewMode = TasksPagePreferences.ParseViewMode(p.ViewMode);
            weeklyLayoutMode = TasksPagePreferences.ParseWeeklyLayout(p.WeeklyLayout);
            weeklyBusinessWeekOnly = p.WeeklyBusinessWeekOnly;
            projectBusinessWeekOnly = p.ProjectBusinessWeekOnly;
            searchString = p.SearchString ?? "";

            // Deliberately NOT from localStorage — see SessionWeeklyWeekStartMonday. Only a
            // value set earlier THIS app session (i.e. we navigated away and back without a
            // real reload) overrides the current-week default already sitting in these fields;
            // a fresh reload has nothing here and stays on the current week.
            if (PagePreferences.SessionWeeklyWeekStartMonday.HasValue)
            {
                weekStartMonday = PagePreferences.SessionWeeklyWeekStartMonday.Value;
                weekPickerDate = weekStartMonday;
            }

            if (PagePreferences.SessionProjectWeekStartMonday.HasValue)
            {
                projectWeekStartMonday = PagePreferences.SessionProjectWeekStartMonday.Value;
                projectWeekPickerDate = projectWeekStartMonday;
            }

            preferencesRestored = true;
        }

        private Task PersistPreferencesAsync()
        {
            // Nothing has been restored yet — saving now would stamp the still-default
            // ("All" view) field values over whatever the user actually had saved. Skip; the
            // fields get corrected in place once RestorePreferencesAsync finishes, so there's
            // nothing lost by not persisting this particular call.
            if (!preferencesRestored)
                return Task.CompletedTask;

            // In-memory only, not part of the localStorage-backed object below.
            PagePreferences.SessionWeeklyWeekStartMonday = weekStartMonday;
            PagePreferences.SessionProjectWeekStartMonday = projectWeekStartMonday;

            return PagePreferences.UpdateAsync(p =>
            {
                p.ViewMode = viewMode.ToString();
                p.ProjectBusinessWeekOnly = projectBusinessWeekOnly;
                p.WeeklyLayout = weeklyLayoutMode.ToString();
                p.WeeklyBusinessWeekOnly = weeklyBusinessWeekOnly;
                p.SearchString = searchString;
                // Project selection is also written from WeekEntryPanel; keep whatever is already set.
            });
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

        private async Task OnViewModeChangedAsync(TasksViewMode mode)
        {
            // Leave any open inline editor (project dropdown, duration field, etc.)
            // so AutoFocus does not reopen a picker after the view swaps.
            EndInlineEdit();
            viewMode = mode;
            await PersistPreferencesAsync();
            if (mode == TasksViewMode.Weekly && weeklyLayoutMode == WeeklyLayoutMode.List)
                await LoadWeeklyAsync();
        }

        private async Task OnWeeklyLayoutChangedAsync(WeeklyLayoutMode mode)
        {
            EndInlineEdit();
            weeklyLayoutMode = mode;
            await PersistPreferencesAsync();
            if (mode == WeeklyLayoutMode.List)
                await LoadWeeklyAsync();
        }

        private async Task OnWeeklyBusinessWeekChanged(bool value)
        {
            weeklyBusinessWeekOnly = value;
            await PersistPreferencesAsync();
        }

        private async Task OnWeeklyDayLoadingChanged(bool loading)
        {
            isWeeklyDayBusy = loading;
            await InvokeAsync(StateHasChanged);
        }

        private async Task OnProjectBusinessWeekChanged(bool value)
        {
            projectBusinessWeekOnly = value;
            await PersistPreferencesAsync();
        }

        private async Task OnProjectPanelSelectionChanged()
        {
            projectViewSelectedProject = weekEntryPanel?.SelectedProject;
            await InvokeAsync(StateHasChanged);
        }

        private async Task OnProjectViewProjectChangedAsync(Project? project)
        {
            projectViewSelectedProject = project;
            if (weekEntryPanel != null)
                await weekEntryPanel.SetSelectedProjectAsync(project);
            await InvokeAsync(StateHasChanged);
        }

        private Task<IEnumerable<Project>> SearchProjectsForProjectView(string? value, CancellationToken token)
        {
            if (weekEntryPanel != null)
                return weekEntryPanel.SearchProjectsPublic(value, token);
            return SearchProjectsForInline(value, token);
        }

        private void OnProjectViewNewTask()
        {
            if (projectViewSelectedProject == null)
            {
                Snackbar.Add("Select a project first.", Severity.Info);
                return;
            }

            if (weekEntryPanel == null)
            {
                Snackbar.Add("Project week is still loading…", Severity.Info);
                return;
            }

            // Panel owns draft rows; it snackbars if a draft is already open.
            weekEntryPanel.RequestAddTaskRow();
            StateHasChanged();
        }

        private async Task OnProjectWeekLoadingChanged(bool loading)
        {
            isProjectWeekBusy = loading;
            await InvokeAsync(StateHasChanged);
        }

        private async Task PrevProjectWeekAsync()
        {
            projectWeekStartMonday = projectWeekStartMonday.AddDays(-7);
            projectWeekPickerDate = projectWeekStartMonday;
            await PersistPreferencesAsync();
        }

        private async Task NextProjectWeekAsync()
        {
            projectWeekStartMonday = projectWeekStartMonday.AddDays(7);
            projectWeekPickerDate = projectWeekStartMonday;
            await PersistPreferencesAsync();
        }

        private async Task OnProjectWeekDatePickedAsync(DateTime? date)
        {
            if (!date.HasValue) return;
            projectWeekPickerDate = date;
            var monday = WeekEntryGridRules.GetWeekStartMonday(date.Value);
            if (monday == projectWeekStartMonday) return;
            projectWeekStartMonday = monday;
            projectWeekPickerDate = monday;
            await PersistPreferencesAsync();
        }

        private async Task PrevWeekAsync()
        {
            EndInlineEdit();
            weekStartMonday = weekStartMonday.AddDays(-7);
            weekPickerDate = weekStartMonday;
            await PersistPreferencesAsync();
            await LoadWeeklyAsync();
        }

        private async Task NextWeekAsync()
        {
            EndInlineEdit();
            weekStartMonday = weekStartMonday.AddDays(7);
            weekPickerDate = weekStartMonday;
            await PersistPreferencesAsync();
            await LoadWeeklyAsync();
        }

        /// <summary>User picks any date; snap to that date's Monday-start week.</summary>
        private async Task OnWeekDatePickedAsync(DateTime? date)
        {
            if (!date.HasValue) return;
            weekPickerDate = date;
            var monday = WeekEntryGridRules.GetWeekStartMonday(date.Value);
            if (monday == weekStartMonday) return;
            EndInlineEdit();
            weekStartMonday = monday;
            weekPickerDate = monday;
            await PersistPreferencesAsync();
            await LoadWeeklyAsync();
        }

        /// <summary>"Today" button — jumps the Week tab back to the current week on demand.
        /// Distinct from the (now-fixed) startup default: this is an explicit, user-driven
        /// jump, so persisting it here is always correct.</summary>
        private async Task GoToTodayWeekAsync()
        {
            var monday = WeekEntryGridRules.GetWeekStartMonday(DateTime.Today);
            if (monday == weekStartMonday) return;
            EndInlineEdit();
            weekStartMonday = monday;
            weekPickerDate = monday;
            await PersistPreferencesAsync();
            await LoadWeeklyAsync();
        }

        /// <summary>"Today" button for the Project tab — same idea as <see cref="GoToTodayWeekAsync"/>.</summary>
        private async Task GoToTodayProjectWeekAsync()
        {
            var monday = WeekEntryGridRules.GetWeekStartMonday(DateTime.Today);
            if (monday == projectWeekStartMonday) return;
            projectWeekStartMonday = monday;
            projectWeekPickerDate = monday;
            await PersistPreferencesAsync();
        }

        private async Task LoadWeeklyAsync()
        {
            weeklyLoadCts?.Cancel();
            weeklyLoadCts?.Dispose();
            weeklyLoadCts = new CancellationTokenSource();
            var token = weeklyLoadCts.Token;

            isLoadingWeekly = true;
            weeklyLoadError = null;
            await InvokeAsync(StateHasChanged);

            try
            {
                var from = weekStartMonday.Date;
                var to = WeekEntryGridRules.GetWeekEndSunday(weekStartMonday);

                var tasks = await TrackedTasksClient.LoadRangeAsync(from, to, cancellationToken: token);
                if (token.IsCancellationRequested) return;

                weeklyRows = TaskListRowBuilder.FromWeekTasks(
                    tasks, displayMode, SettingsService.GetTimeZoneInfo());

                var durationSlices = tasks.Select(t =>
                {
                    TimeSpan? adjusted = null;
                    if (t.ManagerAdjustment != null
                        && t.AdjustmentKind is "Alias" or "Direct")
                    {
                        adjusted = t.ManagerAdjustment.Duration;
                    }

                    return new WeeklyTimeTotalsRules.TaskDurationSlice(
                        t.StartDate, t.Duration, adjusted);
                });
                weeklyTotals = WeeklyTimeTotalsRules.Compute(
                    durationSlices, from, to, displayMode);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                weeklyLoadError = "Couldn't load this week's tasks.";
                Snackbar.AddApiError(ex, weeklyLoadError);
                weeklyRows = new List<TaskListRow>();
                weeklyTotals = new(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, false);
            }
            finally
            {
                if (!token.IsCancellationRequested)
                {
                    isLoadingWeekly = false;
                    await InvokeAsync(StateHasChanged);
                }
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
                await SettingsService.GetSettingsAsync();
                var tz = SettingsService.GetTimeZoneInfo();
                var rows = new List<TaskListRow>();
                foreach (var dto in response.Items)
                {
                    if (dto.IsStopwatch && dto.StopwatchItem != null)
                    {
                        rows.Add(TaskListRowBuilder.FromStopwatch(dto.StopwatchItem, tz));
                        continue;
                    }

                    if (dto.ManualTask != null)
                    {
                        rows.AddRange(TaskListRowBuilder.ExpandManualRows(
                            new TrackedTask(dto.ManualTask, tz), displayMode, tz));
                    }
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

        private Task ReloadAsync()
        {
            if (table is null)
                return Task.CompletedTask;
            return table.ReloadServerData();
        }

        private async Task RefreshCurrentViewAsync()
        {
            if (viewMode == TasksViewMode.Grid)
                await ReloadAsync();
            else if (viewMode == TasksViewMode.Weekly)
                await LoadWeeklyAsync();
        }

        /// <summary>Project-view week entry mutated rows — refresh grid/weekly when visible.</summary>
        private async Task OnWeekEntriesChanged()
        {
            await RefreshCurrentViewAsync();
        }

        private IReadOnlyList<TaskListRow> DisplayedWeeklyRows
        {
            get
            {
                if (string.IsNullOrWhiteSpace(searchString))
                    return weeklyRows;

                return weeklyRows
                    .Where(r => WeekEntryGridRules.MatchesEntrySearch(
                        searchString,
                        r.Details,
                        r.ProjectName,
                        r.ProjectDisplayName,
                        r.OrganizationName,
                        r.ProjectGroupName,
                        r.OverlayDetails,
                        r.ManualTask?.Project?.Slug))
                    .ToList();
            }
        }

        private async Task OnSearchChanged(string value)
        {
            searchString = value;
            await PersistPreferencesAsync();
            if (viewMode == TasksViewMode.Grid)
                await ReloadAsync();
            else
                await InvokeAsync(StateHasChanged);
        }

        /// <summary>
        /// Stopwatch rows are work items (one-to-many sessions). Open the sessions dialog so
        /// each start/stop/duration can be edited. Manual rows use click-to-edit fields instead.
        /// </summary>
        private async Task OnRowClickAsync(TaskListRow row)
        {
            if (row.Kind == TaskListRowKind.Stopwatch && row.StopwatchItem != null)
                await OpenStopwatchSessionsAsync(row.StopwatchItem);
            else if (row.IsLocked && row.ManualTask != null)
                await OpenTaskDialog(row);
        }

        private static string FormatDuration(TaskListRow row) => FormatRowDuration(row);

        private async Task OpenStopwatchSessionsAsync(StopwatchItemDto item)
        {
            var parameters = new DialogParameters<StopwatchSessionsDialog>
            {
                { x => x.ItemId, item.StopwatchItemId },
                { x => x.ItemName, item.Details },
                { x => x.ItemProjectId, item.ProjectId },
                { x => x.ItemProjectName, ProjectDisplayHelper.FromDto(item.Project) },
                { x => x.HttpClient, client }
            };

            var dialog = await DialogService.ShowAsync<StopwatchSessionsDialog>(
                item.Details,
                parameters,
                new DialogOptions { MaxWidth = MaxWidth.Medium, FullWidth = true });
            var result = await dialog.Result;

            if (result is { Canceled: false })
                await RefreshCurrentViewAsync();
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
                name = row.OverlayDetails ?? row.Details;
                projectId = row.OverlayProjectId;
                projectName = row.ProjectDisplayName;
                start = row.OverlayStartDate ?? row.DisplayDate;
                end = row.OverlayEndDate;
                duration = row.OverlayDuration ?? row.Duration;
                isAllDay = false;
            }
            else
            {
                name = task.Details;
                projectId = task.ProjectId;
                projectName = task.Project?.DisplayName;
                start = task.StartDate;
                end = task.EndDate ?? (task.Duration > TimeSpan.Zero ? task.StartDate + task.Duration : null);
                duration = task.Duration;
                isAllDay = task.IsAllDay;
            }

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

            if (isOverlay)
            {
                parameters.Add(x => x.OriginalTaskName, task.Details);
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
                await RefreshCurrentViewAsync();
        }

        /// <summary>
        /// Week toolbar New Task: Day layout adds an inline draft row; List keeps the create dialog.
        /// </summary>
        private void OnWeeklyNewTask()
        {
            if (weeklyLayoutMode == WeeklyLayoutMode.Day)
            {
                if (weekDayAcrossGrid == null)
                {
                    Snackbar.Add("Week day grid is still loading…", Severity.Info);
                    return;
                }

                weekDayAcrossGrid.RequestAddDraftRow();
                return;
            }

            _ = OpenCreateDialog();
        }

        private async Task OpenCreateDialog()
        {
            var defaultTime = trackTimeOfDay
                ? SettingsService.DefaultStartTimeOfDay
                : TymeTimeOfDayRules.DefaultStartTimeOfDayWhenNotTracked;

            var start = viewMode == TasksViewMode.Weekly
                ? weekStartMonday.Date.Add(defaultTime)
                : DateTime.Now.Date.Add(defaultTime);

            // Prefer today when it falls inside the selected week.
            if (viewMode == TasksViewMode.Weekly)
            {
                var today = DateTime.Today;
                var weekEnd = WeekEntryGridRules.GetWeekEndSunday(weekStartMonday);
                if (today >= weekStartMonday && today <= weekEnd)
                    start = today.Add(defaultTime);
            }

            var parameters = new DialogParameters<TrackedTaskDialog>
            {
                { x => x.Mode, TrackedTaskDialogMode.Create },
                { x => x.StartDate, start },
                { x => x.Duration, TimeSpan.Zero },
                { x => x.Use24HourTime, SettingsService.Use24HourTime },
                { x => x.HttpClient, client }
            };

            var dialog = await DialogService.ShowAsync<TrackedTaskDialog>("New Task", parameters,
                new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true });
            var result = await dialog.Result;

            if (result is { Canceled: false })
                await RefreshCurrentViewAsync();
        }

        async Task DeleteRow(TrackedTask trackedTask)
        {
            var result = await DialogService.ShowMessageBoxAsync(
                "Confirm Delete",
                $"Are you sure you want to delete \"{trackedTask.Details}\"?",
                yesText: "Delete", cancelText: "Cancel");

            if (result != true) return;

            try
            {
                var response = await client.DeleteAsync($"{Constants.API.TrackedTask.Delete}/{trackedTask.TaskId}");

                if (response != null && response.IsSuccessStatusCode)
                {
                    await RefreshCurrentViewAsync();
                    Snackbar.Add("Task was removed", Severity.Success);
                }
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't delete task.");
            }
        }

        /// <summary>
        /// Deletes a stopwatch work item and all of its sessions (same as Stopwatch page).
        /// </summary>
        private async Task DeleteStopwatchItemAsync(StopwatchItemDto item)
        {
            if (item.HasLockedSessions)
            {
                Snackbar.Add(
                    "Cannot delete: one or more sessions are in a submitted month.",
                    Severity.Warning);
                return;
            }

            var result = await DialogService.ShowMessageBoxAsync(
                "Delete work item",
                $"Delete \"{item.Details}\" and all of its logged sessions? This can't be undone.",
                yesText: "Delete", cancelText: "Cancel");

            if (result != true) return;

            try
            {
                await StopwatchItemsClient.DeleteAsync(item.StopwatchItemId);
                await RefreshCurrentViewAsync();
                Snackbar.Add("Work item was removed", Severity.Success);
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't delete work item.");
            }
        }
    }
}
