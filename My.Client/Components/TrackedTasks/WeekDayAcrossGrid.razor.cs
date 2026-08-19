using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;
using My.Client.Extensions;
using My.Client.Models;
using My.Client.Services;
using My.Shared.Constants;
using My.Shared.Dtos.TimeSubmission;
using My.Shared.Dtos.TrackedTask;
using My.Shared.Rules;

namespace My.Client.Components.TrackedTasks;

/// <summary>
/// Week → Day layout: every project/task with time this week as rows; Mon–Fri (or full week)
/// as editable duration columns. Horizontal scroll when the window is narrow.
/// </summary>
public partial class WeekDayAcrossGrid : IDisposable
{
    private enum CellSaveStatus
    {
        Idle,
        Pending,
        Saving,
        Saved,
        Error
    }

    private sealed class DayCellVm
    {
        public DateTime Date { get; set; }
        public string? TaskId { get; set; }
        public string TaskName { get; set; } = "";
        public string? ProjectId { get; set; }
        public string ProjectName { get; set; } = "";
        public string DurationText { get; set; } = "";
        public TimeSpan SavedDuration { get; set; }
        public DateTime BoundStartDate { get; set; }
        public bool IsMultiple { get; set; }
        public bool IsSubmitted { get; set; }

        /// <summary>Cell holds time tracked via the Stopwatch. Read-only here — edited
        /// through the Sessions dialog, same rule as List/All (see RebuildRows).</summary>
        public bool IsStopwatch { get; set; }
        public string? StopwatchItemId { get; set; }
        public bool IsReadOnly => IsMultiple || IsSubmitted || IsStopwatch;
        public string? DurationError { get; set; }
        public string? ErrorMessage { get; set; }
        public string? HintTooltip { get; set; }
        public CellSaveStatus Status { get; set; }
        public int SaveGeneration { get; set; }
        public CancellationTokenSource? DebounceCts { get; set; }
    }

    private sealed class RowVm
    {
        public string ProjectId { get; set; } = "";
        public string ProjectName { get; set; } = "";
        public string TaskName { get; set; } = "";
        public DayCellVm[] Cells { get; set; } = Array.Empty<DayCellVm>();

        /// <summary>Row is a stopwatch work item's sessions for the week (grouped by
        /// StopwatchItemId), not a manual task — see RebuildRows.</summary>
        public bool IsStopwatch { get; set; }
        public string? StopwatchItemId { get; set; }

        /// <summary>Inline New Task row until at least one day is saved.</summary>
        public bool IsDraft { get; set; }
        public Project? DraftProject { get; set; }
        public string DraftTaskName { get; set; } = "";
        public bool HasPersistedData => Cells.Any(c => !string.IsNullOrEmpty(c.TaskId));
        public bool ShowDraftEditors => IsDraft && !HasPersistedData;
        public bool CanEnterDuration =>
            !ShowDraftEditors
            || WeekEntryGridRules.CanEnterDraftDayDuration(DraftProject != null);
    }

    [Parameter] public DateTime WeekStartMonday { get; set; }
    [Parameter] public bool BusinessWeekOnly { get; set; } = true;
    [Parameter] public EventCallback EntriesChanged { get; set; }
    [Parameter] public EventCallback<bool> LoadingChanged { get; set; }

    /// <summary>Filters visible rows by project or Details. Draft rows stay visible.</summary>
    [Parameter] public string? Search { get; set; }

    private IEnumerable<RowVm> VisibleRows
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Search))
                return rows;

            return rows.Where(r =>
                r.ShowDraftEditors
                || WeekEntryGridRules.MatchesEntrySearch(
                    Search, r.ProjectName, r.TaskName, r.DraftTaskName));
        }
    }

    [Inject] private IHttpClientFactory ClientFactory { get; set; } = null!;
    [Inject] private TrackedTasksClient TrackedTasksClient { get; set; } = null!;
    [Inject] private UserSettingsService SettingsService { get; set; } = null!;
    [Inject] private AppSettingsCache AppSettingsCache { get; set; } = null!;
    [Inject] private ProjectsCache ProjectsCache { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;

    private HttpClient client = null!;
    private DateTime _appliedWeekStart;
    private bool _appliedBusinessWeekOnly = true;
    private bool _initialized;
    private bool isLoading;
    private string? loadError;
    private List<TrackedTask> weekTasks = new();
    private List<RowVm> rows = new();
    private IReadOnlyList<DateTime> visibleDays = Array.Empty<DateTime>();
    private HashSet<(int Year, int Month)> submittedMonths = new();
    private TimeSpan defaultStartTime = DefaultStartTimeRules.DefaultTimeOfDay;
    private CancellationTokenSource? loadCts;
    private bool disposed;

    protected override async Task OnInitializedAsync()
    {
        client = ClientFactory.CreateClient(Constants.API.ClientName);
        _appliedWeekStart = WeekStartMonday != default
            ? WeekStartMonday.Date
            : WeekEntryGridRules.GetWeekStartMonday(DateTime.Today);
        _appliedBusinessWeekOnly = BusinessWeekOnly;
        _initialized = true;

        try
        {
            await SettingsService.GetSettingsAsync();
            var trackTime = await AppSettingsCache.GetTymeTrackTimeOfDayAsync();
            defaultStartTime = trackTime
                ? SettingsService.DefaultStartTimeOfDay
                : TymeTimeOfDayRules.DefaultStartTimeOfDayWhenNotTracked;
        }
        catch { /* defaults */ }

        visibleDays = WeekEntryGridRules.GetVisibleWeekDays(_appliedWeekStart, BusinessWeekOnly);
        await LoadWeekAsync();
    }

    protected override async Task OnParametersSetAsync()
    {
        if (!_initialized) return;

        var weekChanged = WeekStartMonday != default
            && WeekStartMonday.Date != _appliedWeekStart.Date;
        var businessChanged = _appliedBusinessWeekOnly != BusinessWeekOnly;
        if (!weekChanged && !businessChanged) return;

        CancelAllDebounces();

        if (weekChanged)
            _appliedWeekStart = WeekStartMonday.Date;

        if (businessChanged)
            _appliedBusinessWeekOnly = BusinessWeekOnly;

        visibleDays = WeekEntryGridRules.GetVisibleWeekDays(_appliedWeekStart, BusinessWeekOnly);

        if (weekChanged)
            await LoadWeekAsync();
        else
            RebuildRows();
    }

    private async Task LoadWeekAsync()
    {
        loadCts?.Cancel();
        loadCts?.Dispose();
        loadCts = new CancellationTokenSource();
        var token = loadCts.Token;

        isLoading = true;
        loadError = null;
        if (LoadingChanged.HasDelegate)
            await LoadingChanged.InvokeAsync(true);
        await InvokeAsync(StateHasChanged);

        try
        {
            var from = _appliedWeekStart.Date;
            var to = WeekEntryGridRules.GetWeekEndSunday(_appliedWeekStart);

            var loadTasks = TrackedTasksClient.LoadRangeAsync(from, to, cancellationToken: token);
            var loadSubs = LoadSubmittedMonthsAsync(token);
            await Task.WhenAll(loadTasks, loadSubs);
            if (token.IsCancellationRequested) return;

            weekTasks = await loadTasks;
            submittedMonths = await loadSubs;
            visibleDays = WeekEntryGridRules.GetVisibleWeekDays(_appliedWeekStart, BusinessWeekOnly);
            RebuildRows();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            loadError = "Couldn't load this week's time.";
            Snackbar.AddApiError(ex, loadError);
            weekTasks = new List<TrackedTask>();
            rows = new List<RowVm>();
        }
        finally
        {
            isLoading = false;
            if (LoadingChanged.HasDelegate)
                await LoadingChanged.InvokeAsync(false);
            if (!token.IsCancellationRequested)
                await InvokeAsync(StateHasChanged);
        }
    }

    /// <summary>Called from Tasks toolbar New Task in Week → Day mode. Multiple drafts allowed.</summary>
    public void RequestAddDraftRow()
    {
        if (isLoading)
        {
            Snackbar.Add("Still loading this week…", Severity.Info);
            return;
        }

        rows.Add(BuildDraftRow());
        StateHasChanged();
    }

    /// <summary>
    /// Removes an unused draft row (no saved days / no duration persisted). Does not hit the API.
    /// </summary>
    private void RemoveDraftRow(RowVm row)
    {
        if (!row.ShowDraftEditors)
            return;

        foreach (var cell in row.Cells)
        {
            cell.DebounceCts?.Cancel();
            cell.DebounceCts?.Dispose();
            cell.DebounceCts = null;
        }

        rows.Remove(row);
        StateHasChanged();
    }

    private RowVm BuildDraftRow()
    {
        var submittedList = submittedMonths.Select(x => (x.Year, x.Month)).ToList();
        var cells = visibleDays.Select(day => new DayCellVm
        {
            Date = day,
            TaskName = "",
            ProjectId = null,
            ProjectName = "",
            DurationText = "",
            SavedDuration = TimeSpan.Zero,
            BoundStartDate = day.Date.Add(defaultStartTime),
            IsMultiple = false,
            IsSubmitted = WeekEntryGridRules.IsDaySubmitted(day, submittedList),
            Status = CellSaveStatus.Idle
        }).ToArray();

        return new RowVm
        {
            IsDraft = true,
            DraftTaskName = "",
            DraftProject = null,
            ProjectId = "",
            ProjectName = "",
            TaskName = "",
            Cells = cells
        };
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

    private void OnDraftProjectChanged(RowVm row, Project? project)
    {
        if (!row.ShowDraftEditors) return;
        row.DraftProject = project;
        var pid = project?.ProjectId;
        var pname = project?.DisplayName ?? project?.Name ?? "";
        foreach (var c in row.Cells)
        {
            c.ProjectId = pid;
            c.ProjectName = pname;
        }
        StateHasChanged();
    }

    private void OnDraftTaskNameChanged(RowVm row, string? value)
    {
        if (!row.ShowDraftEditors) return;
        row.DraftTaskName = value ?? "";
        StateHasChanged();
    }

    private async Task<HashSet<(int Year, int Month)>> LoadSubmittedMonthsAsync(CancellationToken token)
    {
        try
        {
            var list = await client.GetFromJsonAsync<List<TimeSubmissionDto>>(
                Constants.API.TimeSubmission.Get, token);
            if (list == null) return new HashSet<(int, int)>();
            return list.Select(s => (s.Year, s.Month)).ToHashSet();
        }
        catch
        {
            return new HashSet<(int, int)>();
        }
    }

    private void RebuildRows()
    {
        CancelAllDebounces();

        var from = _appliedWeekStart.Date;
        var to = WeekEntryGridRules.GetVisibleWeekEnd(_appliedWeekStart, BusinessWeekOnly);
        var slices = weekTasks.Select(ToSlice).ToList();
        var submittedList = submittedMonths.Select(x => (x.Year, x.Month)).ToList();

        // Keep unfinished New Task drafts across reload/rebuild of saved rows.
        var drafts = rows.Where(r => r.ShowDraftEditors).ToList();

        var keys = new SortedSet<(string ProjectId, string ProjectName, string TaskName)>(
            Comparer<(string ProjectId, string ProjectName, string TaskName)>.Create((a, b) =>
            {
                var c = string.Compare(a.ProjectName, b.ProjectName, StringComparison.OrdinalIgnoreCase);
                if (c != 0) return c;
                c = string.Compare(a.TaskName, b.TaskName, StringComparison.OrdinalIgnoreCase);
                if (c != 0) return c;
                return string.Compare(a.ProjectId, b.ProjectId, StringComparison.Ordinal);
            }));

        foreach (var t in weekTasks)
        {
            if (!string.IsNullOrEmpty(t.StopwatchItemId))
                continue;
            var slice = ToSlice(t);
            if (!WeekEntryGridRules.OverlapsDayRange(slice, from, to))
                continue;
            // Details is optional (see WeekEntryGridRules.ValidateTaskDetails) — an empty
            // name is still a real row, not a filler. Excluding it here made tasks
            // saved with no Details vanish from this grid on the next reload while
            // remaining visible (and correctly saved) in All / Week List.
            var name = WeekEntryGridRules.NormalizeTaskDetailsKey(t.Details);
            var pid = t.ProjectId ?? "";
            var pname = t.Project?.DisplayName ?? t.Project?.Name ?? "No project";
            keys.Add((pid, pname, name));
        }

        var next = new List<RowVm>();
        foreach (var key in keys)
        {
            var cells = new DayCellVm[visibleDays.Count];
            for (var i = 0; i < visibleDays.Count; i++)
            {
                var day = visibleDays[i];
                var projectId = string.IsNullOrEmpty(key.ProjectId) ? null : key.ProjectId;
                var isSubmitted = WeekEntryGridRules.IsDaySubmitted(day, submittedList);
                var bind = BindDay(slices, weekTasks, projectId, key.TaskName, day);

                if (bind.Kind == WeekEntryGridRules.DayBindKind.Single && bind.TaskId != null)
                {
                    var start = bind.StartDate ?? day.Date.Add(defaultStartTime);
                    cells[i] = new DayCellVm
                    {
                        Date = day,
                        TaskId = bind.TaskId,
                        TaskName = key.TaskName,
                        ProjectId = projectId,
                        ProjectName = key.ProjectName,
                        DurationText = WeekEntryGridRules.FormatDayDurationInput(bind.EditableDuration),
                        SavedDuration = WeekEntryGridRules.NormalizeDuration(bind.EditableDuration),
                        BoundStartDate = start,
                        IsMultiple = false,
                        IsSubmitted = isSubmitted,
                        Status = CellSaveStatus.Idle
                    };
                }
                else if (bind.Kind == WeekEntryGridRules.DayBindKind.Multiple
                    || bind.Kind == WeekEntryGridRules.DayBindKind.AllDay)
                {
                    cells[i] = new DayCellVm
                    {
                        Date = day,
                        TaskId = bind.TaskId,
                        TaskName = key.TaskName,
                        ProjectId = projectId,
                        ProjectName = key.ProjectName,
                        DurationText = WeekEntryGridRules.FormatDayDurationInput(bind.TotalManualDuration),
                        SavedDuration = bind.TotalManualDuration,
                        BoundStartDate = day.Date.Add(defaultStartTime),
                        IsMultiple = true,
                        IsSubmitted = true, // treat as read-only
                        Status = CellSaveStatus.Idle,
                        ErrorMessage = bind.Kind == WeekEntryGridRules.DayBindKind.AllDay
                            ? "All day"
                            : "Multiple",
                        HintTooltip = bind.Kind == WeekEntryGridRules.DayBindKind.AllDay
                            ? "All-day entry. It counts as a full workday."
                            : "More than one time entry on this day. Open List view to edit them one at a time."
                    };
                }
                else
                {
                    // Empty day on an existing task row — allow typing hours to create.
                    cells[i] = new DayCellVm
                    {
                        Date = day,
                        TaskName = key.TaskName,
                        ProjectId = projectId,
                        ProjectName = key.ProjectName,
                        DurationText = "",
                        SavedDuration = TimeSpan.Zero,
                        BoundStartDate = day.Date.Add(defaultStartTime),
                        IsMultiple = false,
                        IsSubmitted = isSubmitted,
                        Status = CellSaveStatus.Idle
                    };
                }
            }

            next.Add(new RowVm
            {
                ProjectId = key.ProjectId,
                ProjectName = key.ProjectName,
                TaskName = key.TaskName,
                Cells = cells
            });
        }

        // Stopwatch work items this week: one read-only row per item (grouped by
        // StopwatchItemId, not name — an item's name isn't guaranteed unique), summed per
        // day. Previously these sessions were skipped entirely, so a week that was mostly
        // stopwatch-tracked showed "No time this week" here while List/All showed the full
        // total. Edited via the Sessions dialog only, same rule as the other read-only
        // (Multiple / All day) cells above — not typed inline.
        var stopwatchGroups = weekTasks
            .Where(t => !string.IsNullOrEmpty(t.StopwatchItemId))
            .Select(t => (Task: t, Slice: ToSlice(t)))
            .Where(x => WeekEntryGridRules.OverlapsDayRange(x.Slice, from, to))
            .GroupBy(x => x.Task.StopwatchItemId!)
            .OrderBy(g => g.First().Task.Project?.DisplayName ?? g.First().Task.Project?.Name ?? "No project",
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.First().Task.Details, StringComparer.OrdinalIgnoreCase);

        foreach (var group in stopwatchGroups)
        {
            var sessions = group.Select(x => x.Task).ToList();
            var first = sessions[0];
            var projectId = string.IsNullOrEmpty(first.ProjectId) ? null : first.ProjectId;
            var projectName = first.Project?.DisplayName ?? first.Project?.Name ?? "No project";
            var taskName = first.Details ?? "";

            var cells = new DayCellVm[visibleDays.Count];
            for (var i = 0; i < visibleDays.Count; i++)
            {
                var day = visibleDays[i];
                var dayTotal = TimeSpan.Zero;
                foreach (var s in sessions)
                {
                    if (s.StartDate.Date == day.Date)
                        dayTotal += s.Duration;
                }

                cells[i] = new DayCellVm
                {
                    Date = day,
                    TaskName = taskName,
                    ProjectId = projectId,
                    ProjectName = projectName,
                    DurationText = WeekEntryGridRules.FormatDayDurationInput(dayTotal),
                    SavedDuration = dayTotal,
                    BoundStartDate = day.Date.Add(defaultStartTime),
                    IsStopwatch = true,
                    StopwatchItemId = group.Key,
                    IsSubmitted = WeekEntryGridRules.IsDaySubmitted(day, submittedList),
                    Status = CellSaveStatus.Idle,
                    ErrorMessage = dayTotal > TimeSpan.Zero ? "Stopwatch" : null,
                    HintTooltip = dayTotal > TimeSpan.Zero
                        ? "Tracked with the Stopwatch. Open Sessions to edit."
                        : null
                };
            }

            next.Add(new RowVm
            {
                ProjectId = projectId ?? "",
                ProjectName = projectName,
                TaskName = taskName,
                IsStopwatch = true,
                StopwatchItemId = group.Key,
                Cells = cells
            });
        }

        foreach (var draft in drafts)
            next.Add(draft);

        rows = next;
    }

    private static WeekEntryGridRules.DayBinding BindDay(
        List<WeekEntryGridRules.WeekEntryTaskSlice> slices,
        List<TrackedTask> tasks,
        string? projectId,
        string taskName,
        DateTime day)
    {
        if (string.IsNullOrEmpty(projectId))
        {
            var manuals = tasks
                .Where(t =>
                    !t.IsAllDay
                    && string.IsNullOrEmpty(t.StopwatchItemId)
                    && string.IsNullOrEmpty(t.ProjectId)
                    && t.StartDate.Date == day.Date
                    && WeekEntryGridRules.TaskNamesEqual(t.Details, taskName))
                .OrderBy(t => t.StartDate)
                .ToList();
            return manuals.Count switch
            {
                0 => new WeekEntryGridRules.DayBinding(
                    WeekEntryGridRules.DayBindKind.Empty, null, null, null, TimeSpan.Zero, TimeSpan.Zero),
                1 => new WeekEntryGridRules.DayBinding(
                    WeekEntryGridRules.DayBindKind.Single, manuals[0].TaskId, manuals[0].Details,
                    manuals[0].StartDate, manuals[0].Duration, manuals[0].Duration),
                _ => new WeekEntryGridRules.DayBinding(
                    WeekEntryGridRules.DayBindKind.Multiple, null, taskName, null, TimeSpan.Zero,
                    manuals.Aggregate(TimeSpan.Zero, (s, t) => s + t.Duration))
            };
        }

        return WeekEntryGridRules.BindDayForTaskDetails(slices, projectId, taskName, day);
    }

    private static WeekEntryGridRules.WeekEntryTaskSlice ToSlice(TrackedTask t) =>
        new(t.TaskId, t.Details, t.ProjectId, t.StartDate, t.Duration, t.IsAllDay, t.StopwatchItemId, t.EndDate);

    private static string DayHeader(DateTime day) =>
        $"{day:ddd} {day.Day}";

    private static string RowTotalLabel(RowVm row)
    {
        var t = TimeSpan.Zero;
        foreach (var c in row.Cells)
            t += CellDuration(c);
        return WeekEntryGridRules.FormatDuration(t);
    }

    private string DayColumnTotalLabel(int dayIndex)
    {
        var t = TimeSpan.Zero;
        foreach (var row in VisibleRows)
        {
            if (dayIndex >= 0 && dayIndex < row.Cells.Length)
                t += CellDuration(row.Cells[dayIndex]);
        }
        return WeekEntryGridRules.FormatDuration(t);
    }

    private string GrandTotalLabel
    {
        get
        {
            var t = TimeSpan.Zero;
            foreach (var row in VisibleRows)
            foreach (var c in row.Cells)
                t += CellDuration(c);
            return WeekEntryGridRules.FormatDuration(t);
        }
    }

    private static TimeSpan CellDuration(DayCellVm c)
    {
        if (WeekEntryGridRules.TryParseDayDurationText(c.DurationText, out var d) && d > TimeSpan.Zero)
            return d;
        if (c.SavedDuration > TimeSpan.Zero)
            return c.SavedDuration;
        return TimeSpan.Zero;
    }

    private static string StatusLabel(DayCellVm cell) => cell.Status switch
    {
        CellSaveStatus.Pending => "…",
        CellSaveStatus.Saving => "Saving…",
        CellSaveStatus.Saved => "Saved",
        CellSaveStatus.Error => cell.ErrorMessage ?? "Error",
        _ => string.IsNullOrEmpty(cell.ErrorMessage) ? "" : cell.ErrorMessage
    };

    private void OnDurationChanged(RowVm row, int dayIndex, string? value)
    {
        if (dayIndex < 0 || dayIndex >= row.Cells.Length) return;
        var cell = row.Cells[dayIndex];
        if (cell.IsReadOnly) return;

        // Soft filter only while typing — do not re-pad (that kills selection/caret).
        cell.DurationText = WeekEntryGridRules.FilterDurationInputChars(value);
        cell.DurationError = null;

        if (WeekEntryGridRules.ShouldAutosaveDayDurationText(cell.DurationText))
        {
            ScheduleSave(row, dayIndex);
            return;
        }

        cell.DebounceCts?.Cancel();
        cell.Status = CellSaveStatus.Idle;
    }

    private void OnDurationBlur(RowVm row, int dayIndex)
    {
        if (dayIndex < 0 || dayIndex >= row.Cells.Length) return;
        var cell = row.Cells[dayIndex];
        if (cell.IsReadOnly) return;
        ScheduleSave(row, dayIndex);
    }

    private void OnDurationKeyDown(RowVm row, int dayIndex, KeyboardEventArgs e)
    {
        if (e.Key != "Enter") return;
        if (dayIndex < 0 || dayIndex >= row.Cells.Length) return;
        var cell = row.Cells[dayIndex];
        if (cell.IsReadOnly) return;
        cell.DebounceCts?.Cancel();
        var generation = ++cell.SaveGeneration;
        _ = SaveCellAsync(row, dayIndex, generation);
    }

    private void ScheduleSave(RowVm row, int dayIndex)
    {
        var cell = row.Cells[dayIndex];
        cell.DebounceCts?.Cancel();
        cell.DebounceCts?.Dispose();
        cell.DebounceCts = new CancellationTokenSource();
        var token = cell.DebounceCts.Token;
        cell.Status = CellSaveStatus.Pending;
        cell.ErrorMessage = null;
        var generation = ++cell.SaveGeneration;
        _ = DebouncedSaveAsync(row, dayIndex, generation, token);
    }

    private async Task DebouncedSaveAsync(RowVm row, int dayIndex, int generation, CancellationToken token)
    {
        try { await Task.Delay(500, token); }
        catch (OperationCanceledException) { return; }

        if (disposed || token.IsCancellationRequested) return;
        await SaveCellAsync(row, dayIndex, generation);
    }

    private async Task SaveCellAsync(RowVm row, int dayIndex, int generation)
    {
        if (dayIndex < 0 || dayIndex >= row.Cells.Length) return;
        var cell = row.Cells[dayIndex];
        if (cell.SaveGeneration != generation) return;
        if (cell.IsReadOnly) return;

        var durationText = WeekEntryGridRules.FilterDurationInputChars(cell.DurationText);
        if (!WeekEntryGridRules.TryCommitDayDurationText(durationText, out var newDuration))
        {
            durationText = WeekEntryGridRules.NormalizeDayDurationText(durationText);
            if (!WeekEntryGridRules.TryCommitDayDurationText(durationText, out newDuration))
            {
                cell.Status = CellSaveStatus.Idle;
                cell.DurationError = null;
                await InvokeAsync(StateHasChanged);
                return;
            }
        }

        cell.DurationText = WeekEntryGridRules.FormatDayDurationInput(newDuration);

        if (newDuration <= TimeSpan.Zero && string.IsNullOrEmpty(cell.TaskId))
        {
            cell.Status = CellSaveStatus.Idle;
            await InvokeAsync(StateHasChanged);
            return;
        }

        // New day entry from a draft row: require project + task name first.
        if (newDuration > TimeSpan.Zero && string.IsNullOrEmpty(cell.TaskId))
        {
            if (row.ShowDraftEditors)
            {
                if (row.DraftProject == null || string.IsNullOrEmpty(row.DraftProject.ProjectId))
                {
                    cell.Status = CellSaveStatus.Error;
                    cell.ErrorMessage = "Select a project.";
                    Snackbar.Add("Select a project for this task row.", Severity.Warning);
                    await InvokeAsync(StateHasChanged);
                    return;
                }

                var draftName = WeekEntryGridRules.SanitizeTaskDetails(row.DraftTaskName);
                var nameError = WeekEntryGridRules.ValidateTaskDetails(draftName);
                if (nameError != null)
                {
                    cell.Status = CellSaveStatus.Error;
                    cell.ErrorMessage = nameError;
                    Snackbar.Add(nameError, Severity.Warning);
                    await InvokeAsync(StateHasChanged);
                    return;
                }

                ApplyDraftIdentity(row, draftName);
            }
            else if (string.IsNullOrEmpty(cell.ProjectId) && string.IsNullOrEmpty(row.ProjectId))
            {
                cell.Status = CellSaveStatus.Error;
                cell.ErrorMessage = "Assign a project first.";
                Snackbar.Add("This task has no project — pick a project on the draft row.", Severity.Warning);
                await InvokeAsync(StateHasChanged);
                return;
            }
        }

        var decision = WeekEntryGridRules.DecideMutation(cell.TaskId, cell.SavedDuration, newDuration);
        if (decision.Kind == WeekEntryGridRules.CellMutationKind.None)
        {
            cell.Status = CellSaveStatus.Idle;
            await InvokeAsync(StateHasChanged);
            return;
        }

        cell.Status = CellSaveStatus.Saving;
        await InvokeAsync(StateHasChanged);

        try
        {
            switch (decision.Kind)
            {
                case WeekEntryGridRules.CellMutationKind.Create:
                    await CreateCellAsync(row, cell, newDuration);
                    break;
                case WeekEntryGridRules.CellMutationKind.Update:
                    await UpdateCellAsync(cell, row.TaskName, newDuration);
                    break;
                case WeekEntryGridRules.CellMutationKind.Delete:
                    await DeleteCellAsync(cell);
                    break;
            }

            if (cell.SaveGeneration != generation) return;

            cell.Status = CellSaveStatus.Saved;
            cell.ErrorMessage = null;
            cell.DurationError = null;
            await InvokeAsync(StateHasChanged);
            if (EntriesChanged.HasDelegate)
                await EntriesChanged.InvokeAsync();
            _ = ClearSavedStatusAsync(cell, generation);
        }
        catch (Exception ex)
        {
            if (cell.SaveGeneration != generation) return;
            cell.Status = CellSaveStatus.Error;
            cell.ErrorMessage = "Couldn't save.";
            Snackbar.AddApiError(ex, "Couldn't save day entry.");
            await InvokeAsync(StateHasChanged);
        }
    }

    private static void ApplyDraftIdentity(RowVm row, string taskName)
    {
        row.TaskName = taskName;
        row.ProjectId = row.DraftProject?.ProjectId ?? "";
        row.ProjectName = row.DraftProject?.DisplayName ?? row.DraftProject?.Name ?? "";
        foreach (var c in row.Cells)
        {
            c.TaskName = taskName;
            c.ProjectId = string.IsNullOrEmpty(row.ProjectId) ? null : row.ProjectId;
            c.ProjectName = row.ProjectName;
        }
    }

    private async Task CreateCellAsync(RowVm row, DayCellVm cell, TimeSpan duration)
    {
        var startLocal = cell.Date.Date.Add(defaultStartTime);
        var projectId = string.IsNullOrEmpty(cell.ProjectId)
            ? (string.IsNullOrEmpty(row.ProjectId) ? null : row.ProjectId)
            : cell.ProjectId;
        var dto = new CreateTrackedTaskDto
        {
            Details = WeekEntryGridRules.SanitizeTaskDetails(
                string.IsNullOrWhiteSpace(cell.TaskName) ? row.TaskName : cell.TaskName),
            StartDate = SettingsService.ConvertFromUserTime(startLocal),
            Duration = duration,
            IsAllDay = false,
            ProjectId = projectId
        };

        var response = await client.PostAsJsonAsync(Constants.API.TrackedTask.Create, dto);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException(string.IsNullOrWhiteSpace(error) ? "Create failed." : error);
        }

        var created = await response.Content.ReadFromJsonAsync<TrackedTaskDto>();
        if (created == null)
            throw new InvalidOperationException("Create returned no body.");

        var model = new TrackedTask(created, SettingsService.GetTimeZoneInfo());
        weekTasks.Add(model);

        cell.TaskId = model.TaskId;
        cell.BoundStartDate = model.StartDate;
        cell.SavedDuration = WeekEntryGridRules.NormalizeDuration(model.Duration);
        cell.DurationText = WeekEntryGridRules.FormatDayDurationInput(cell.SavedDuration);
    }

    private async Task UpdateCellAsync(DayCellVm cell, string taskName, TimeSpan duration)
    {
        if (string.IsNullOrEmpty(cell.TaskId))
            throw new InvalidOperationException("No task bound for update.");

        var start = cell.BoundStartDate;
        if (start.Date != cell.Date.Date)
            start = cell.Date.Date.Add(defaultStartTime);

        var end = start.Add(duration);
        var dto = new UpdateTrackedTaskDto
        {
            TaskId = cell.TaskId,
            Details = WeekEntryGridRules.SanitizeTaskDetails(taskName),
            StartDate = SettingsService.ConvertFromUserTime(start),
            EndDate = SettingsService.ConvertFromUserTime(end),
            Duration = duration,
            IsAllDay = false,
            ProjectId = string.IsNullOrEmpty(cell.ProjectId) ? null : cell.ProjectId
        };

        var response = await client.PutAsJsonAsync(Constants.API.TrackedTask.Update, dto);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException(string.IsNullOrWhiteSpace(error) ? "Update failed." : error);
        }

        var existing = weekTasks.FirstOrDefault(t => t.TaskId == cell.TaskId);
        if (existing != null)
        {
            existing.Details = dto.Details;
            existing.StartDate = start;
            existing.EndDate = end;
            existing.Duration = duration;
        }

        cell.BoundStartDate = start;
        cell.SavedDuration = WeekEntryGridRules.NormalizeDuration(duration);
        cell.DurationText = WeekEntryGridRules.FormatDayDurationInput(cell.SavedDuration);
    }

    private async Task DeleteCellAsync(DayCellVm cell)
    {
        if (string.IsNullOrEmpty(cell.TaskId))
            return;

        var response = await client.DeleteAsync($"{Constants.API.TrackedTask.Delete}/{cell.TaskId}");
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException(string.IsNullOrWhiteSpace(error) ? "Delete failed." : error);
        }

        weekTasks.RemoveAll(t => t.TaskId == cell.TaskId);
        cell.TaskId = null;
        cell.SavedDuration = TimeSpan.Zero;
        cell.DurationText = "";
        cell.BoundStartDate = cell.Date.Date.Add(defaultStartTime);
    }

    private async Task ClearSavedStatusAsync(DayCellVm cell, int generation)
    {
        try { await Task.Delay(1200); }
        catch { return; }
        if (disposed || cell.SaveGeneration != generation) return;
        if (cell.Status == CellSaveStatus.Saved)
        {
            cell.Status = CellSaveStatus.Idle;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task OpenCellAsync(DayCellVm cell)
    {
        // Stopwatch-tracked time: no TaskId to bind an inline editor to, and typing over it
        // would fight the stopwatch's own session records — open Sessions instead, same as
        // clicking a stopwatch row in List/All.
        if (cell.IsStopwatch)
        {
            if (string.IsNullOrEmpty(cell.StopwatchItemId))
                return;

            var swParams = new DialogParameters<StopwatchSessionsDialog>
            {
                { x => x.ItemId, cell.StopwatchItemId },
                { x => x.ItemName, cell.TaskName },
                { x => x.ItemProjectId, cell.ProjectId },
                { x => x.ItemProjectName, cell.ProjectName },
                { x => x.HttpClient, client }
            };

            var swDialog = await DialogService.ShowAsync<StopwatchSessionsDialog>(
                cell.TaskName,
                swParams,
                new DialogOptions { MaxWidth = MaxWidth.Medium, FullWidth = true });
            var swResult = await swDialog.Result;

            if (swResult is { Canceled: false })
            {
                await LoadWeekAsync();
                if (EntriesChanged.HasDelegate)
                    await EntriesChanged.InvokeAsync();
            }

            return;
        }

        // No entry yet: open Create prefilled for this row's task/project/day.
        if (string.IsNullOrEmpty(cell.TaskId))
        {
            if (cell.IsReadOnly)
                return;

            var createStart = cell.Date.Date.Add(defaultStartTime);
            var createDuration = TimeSpan.Zero;
            if (WeekEntryGridRules.TryParseDayDurationText(cell.DurationText, out var typed)
                && typed > TimeSpan.Zero)
                createDuration = typed;

            var createParams = new DialogParameters<TrackedTaskDialog>
            {
                { x => x.Mode, TrackedTaskDialogMode.Create },
                { x => x.TaskName, cell.TaskName },
                { x => x.ProjectId, cell.ProjectId },
                { x => x.ProjectName, cell.ProjectName },
                { x => x.StartDate, createStart },
                { x => x.Duration, createDuration },
                { x => x.IsAllDay, false },
                { x => x.Use24HourTime, SettingsService.Use24HourTime },
                { x => x.HttpClient, client }
            };

            var createDialog = await DialogService.ShowAsync<TrackedTaskDialog>(
                "New Task",
                createParams,
                new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true });
            var createResult = await createDialog.Result;
            if (createResult is { Canceled: false })
            {
                await LoadWeekAsync();
                if (EntriesChanged.HasDelegate)
                    await EntriesChanged.InvokeAsync();
            }

            return;
        }

        var task = weekTasks.FirstOrDefault(t => t.TaskId == cell.TaskId);
        if (task == null)
            return;

        var end = task.EndDate
            ?? (task.Duration > TimeSpan.Zero ? task.StartDate + task.Duration : null);
        var mode = task.IsLocked || cell.IsSubmitted
            ? TrackedTaskDialogMode.ReadOnly
            : TrackedTaskDialogMode.Edit;

        var parameters = new DialogParameters<TrackedTaskDialog>
        {
            { x => x.Mode, mode },
            { x => x.TaskId, task.TaskId },
            { x => x.TaskName, task.Details },
            { x => x.ProjectId, task.ProjectId },
            { x => x.ProjectName, task.Project?.DisplayName },
            { x => x.StartDate, task.StartDate },
            { x => x.EndDate, end },
            { x => x.Duration, task.Duration },
            { x => x.IsAllDay, task.IsAllDay },
            { x => x.Use24HourTime, SettingsService.Use24HourTime },
            { x => x.HttpClient, client }
        };

        var dialog = await DialogService.ShowAsync<TrackedTaskDialog>(
            task.Details,
            parameters,
            new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true });
        var result = await dialog.Result;

        if (result is { Canceled: false })
        {
            await LoadWeekAsync();
            if (EntriesChanged.HasDelegate)
                await EntriesChanged.InvokeAsync();
        }
    }

    private void CancelAllDebounces()
    {
        foreach (var row in rows)
        foreach (var cell in row.Cells)
        {
            cell.DebounceCts?.Cancel();
            cell.DebounceCts?.Dispose();
            cell.DebounceCts = null;
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        CancelAllDebounces();
        loadCts?.Cancel();
        loadCts?.Dispose();
    }
}
