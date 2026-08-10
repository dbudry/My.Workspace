using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using My.Client.Extensions;
using My.Client.Models;
using My.Client.Services;
using My.Shared.Constants;
using My.Shared.Dtos.TimeSubmission;
using My.Shared.Dtos.TrackedTask;
using My.Shared.Rules;

namespace My.Client.Components.TrackedTasks
{
    /// <summary>
    /// Project view: project + week + business/full on the first row; New Task on the
    /// second; then one row per task (task name left, day durations across).
    /// Duration only for now (no start time of day).
    /// </summary>
    public partial class WeekEntryPanel : IDisposable
    {
        public enum CellSaveStatus
        {
            Idle,
            Pending,
            Saving,
            Saved,
            Error
        }

        private sealed class DayCell
        {
            public DateTime Date { get; set; }
            public bool IsSubmitted { get; set; }
            public string? TaskId { get; set; }
            public string DurationText { get; set; } = "";
            public TimeSpan SavedDuration { get; set; }
            public DateTime BoundStartDate { get; set; }
            public CellSaveStatus Status { get; set; }
            public string? DurationError { get; set; }
            public string? ErrorMessage { get; set; }
            public CancellationTokenSource? DebounceCts { get; set; }
            public int SaveGeneration { get; set; }
            public bool IsReadOnly => IsSubmitted;
        }

        private sealed class TaskRow
        {
            /// <summary>Display name once any day has been saved (click opens dialog).</summary>
            public string? SelectedTaskName { get; set; }
            /// <summary>Free-text name while the row is still a draft (no saved days yet).</summary>
            public string DraftTaskName { get; set; } = "";
            public DayCell[] Cells { get; set; } = Array.Empty<DayCell>();
            public bool HasPersistedData => Cells.Any(c => !string.IsNullOrEmpty(c.TaskId));

            public string? ResolvedTaskName =>
                HasPersistedData
                    ? WeekEntryGridRules.NormalizeTaskNameKey(SelectedTaskName)
                    : WeekEntryGridRules.NormalizeTaskNameKey(DraftTaskName);
        }

        [Parameter] public EventCallback EntriesChanged { get; set; }

        /// <summary>
        /// Business week (Mon–Fri) vs full week — controlled on the Tasks page toolbar.
        /// </summary>
        [Parameter] public bool BusinessWeekOnly { get; set; } = true;

        /// <summary>
        /// Monday of the selected week — controlled on the Tasks page toolbar.
        /// </summary>
        [Parameter] public DateTime WeekStartMonday { get; set; }

        /// <summary>Reports loading state so the page can disable week navigation.</summary>
        [Parameter] public EventCallback<bool> LoadingChanged { get; set; }

        /// <summary>
        /// Fires when the selected project changes (including restore from preferences)
        /// so the Tasks toolbar can keep its Project picker and New Task in sync.
        /// </summary>
        [Parameter] public EventCallback ProjectSelectionChanged { get; set; }

        [Inject] private IHttpClientFactory ClientFactory { get; set; } = null!;
        [Inject] private TrackedTasksClient TrackedTasksClient { get; set; } = null!;
        [Inject] private ProjectsCache ProjectsCache { get; set; } = null!;
        [Inject] private UserSettingsService SettingsService { get; set; } = null!;
        [Inject] private AppSettingsCache AppSettingsCache { get; set; } = null!;
        [Inject] private ISnackbar Snackbar { get; set; } = null!;
        [Inject] private TasksPagePreferencesService PagePreferences { get; set; } = null!;
        [Inject] private IDialogService DialogService { get; set; } = null!;

        private HttpClient client = null!;
        private DateTime weekStartMonday;
        private bool _appliedBusinessWeekOnly = true;
        private DateTime _appliedWeekStart;
        private bool _weekInitialized;
        private Project? selectedProject;
        private bool use24HourTime;
        private TimeSpan defaultStartTime = DefaultStartTimeRules.DefaultTimeOfDay;
        private bool trackTimeOfDay = TymeTimeOfDayRules.DefaultTrackTimeOfDay;
        private bool isLoading;
        private string? loadError;
        private List<TrackedTask> weekTasks = new();
        private HashSet<(int Year, int Month)> submittedMonths = new();
        private List<string> knownTaskNames = new();
        private List<TaskRow> taskRows = new();
        private IReadOnlyList<DateTime> visibleDays = Array.Empty<DateTime>();
        private bool disposed;
        private CancellationTokenSource? loadCts;
        private CancellationTokenSource? tableNotifyCts;

        private bool HasProject => selectedProject != null;

        /// <summary>Current project for the Tasks page toolbar binder.</summary>
        public Project? SelectedProject => selectedProject;

        /// <summary>True when New Task should be enabled on the page toolbar.</summary>
        public bool CanAddTaskRow =>
            selectedProject != null && !HasIncompleteDraftRow && !isLoading;

        protected override async Task OnInitializedAsync()
        {
            client = ClientFactory.CreateClient(Constants.API.ClientName);
            weekStartMonday = WeekStartMonday != default
                ? WeekStartMonday.Date
                : WeekEntryGridRules.GetWeekStartMonday(DateTime.Today);
            _appliedWeekStart = weekStartMonday;
            _appliedBusinessWeekOnly = BusinessWeekOnly;
            _weekInitialized = true;

            try
            {
                await SettingsService.GetSettingsAsync();
                use24HourTime = SettingsService.Use24HourTime;
            }
            catch
            {
                use24HourTime = false;
            }

            try
            {
                trackTimeOfDay = await AppSettingsCache.GetTymeTrackTimeOfDayAsync();
                defaultStartTime = trackTimeOfDay
                    ? SettingsService.DefaultStartTimeOfDay
                    : TymeTimeOfDayRules.DefaultStartTimeOfDayWhenNotTracked;
            }
            catch
            {
                trackTimeOfDay = TymeTimeOfDayRules.DefaultTrackTimeOfDay;
                defaultStartTime = DefaultStartTimeRules.DefaultTimeOfDay;
            }

            visibleDays = WeekEntryGridRules.GetVisibleWeekDays(weekStartMonday, BusinessWeekOnly);
            await RestoreSelectedProjectAsync();
            await NotifyProjectSelectionChangedAsync();
            await LoadWeekAsync();
        }

        protected override async Task OnParametersSetAsync()
        {
            if (!_weekInitialized)
                return;

            var weekChanged = WeekStartMonday != default
                && WeekStartMonday.Date != _appliedWeekStart.Date;
            var businessChanged = _appliedBusinessWeekOnly != BusinessWeekOnly;

            if (!weekChanged && !businessChanged)
                return;

            CancelAllDebounces();

            if (weekChanged)
            {
                weekStartMonday = WeekStartMonday.Date;
                _appliedWeekStart = weekStartMonday;
            }

            if (businessChanged)
                _appliedBusinessWeekOnly = BusinessWeekOnly;

            visibleDays = WeekEntryGridRules.GetVisibleWeekDays(weekStartMonday, BusinessWeekOnly);

            if (weekChanged)
                await LoadWeekAsync();
            else
            {
                RebuildTaskRows(preserveDraftRows: true);
                RecalcTotals();
            }
        }

        private async Task RestoreSelectedProjectAsync()
        {
            await PagePreferences.LoadAsync();
            var prefs = PagePreferences.Preferences;
            if (string.IsNullOrEmpty(prefs.SelectedProjectId))
                return;

            try
            {
                var project = await ProjectsCache.ResolveByIdAsync(
                    prefs.SelectedProjectId, prefs.SelectedProjectName);
                if (project != null && project.IsActive && !project.IsArchived)
                    selectedProject = project;
            }
            catch
            {
                // leave selection empty
            }
        }

        /// <summary>Called from the Tasks toolbar Project picker.</summary>
        public Task SetSelectedProjectAsync(Project? project) => OnProjectChanged(project);

        /// <summary>Called from the Tasks toolbar New Task button.</summary>
        public void RequestAddTaskRow()
        {
            AddTaskRow();
            // Parent toolbar click does not re-render this panel by itself.
            StateHasChanged();
        }

        /// <summary>Search for the Tasks toolbar ProjectAutocomplete.</summary>
        public Task<IEnumerable<Project>> SearchProjectsPublic(string? value, CancellationToken token)
            => SearchProjects(value, token);

        private async Task OnProjectChanged(Project? project)
        {
            CancelAllDebounces();
            selectedProject = project;
            RefreshKnownTaskNames();
            RebuildTaskRows(preserveDraftRows: false);
            RecalcTotals();
            await PagePreferences.UpdateAsync(p =>
            {
                p.SelectedProjectId = project?.ProjectId;
                p.SelectedProjectName = project?.DisplayName ?? project?.Name;
            });
            await NotifyProjectSelectionChangedAsync();
            await InvokeAsync(StateHasChanged);
        }

        private async Task NotifyProjectSelectionChangedAsync()
        {
            if (ProjectSelectionChanged.HasDelegate)
                await ProjectSelectionChanged.InvokeAsync();
        }

        /// <summary>
        /// Opens the task dialog for this day cell. Existing entry → edit/read-only;
        /// empty cell → create prefilled with the row task/project and that day.
        /// </summary>
        private async Task OpenTaskDialogForCellAsync(TaskRow row, DayCell cell)
        {
            if (string.IsNullOrEmpty(cell.TaskId))
            {
                if (cell.IsReadOnly || selectedProject == null)
                    return;

                var taskName = WeekEntryGridRules.SanitizeTaskName(row.ResolvedTaskName);
                if (string.IsNullOrEmpty(taskName))
                {
                    Snackbar.Add("Enter a task name on the row first.", Severity.Warning);
                    return;
                }

                var createStart = cell.Date.Date.Add(defaultStartTime);
                var createDuration = TimeSpan.FromMinutes(30);
                if (WeekEntryGridRules.TryParseDayDurationText(cell.DurationText, out var typed)
                    && typed > TimeSpan.Zero)
                    createDuration = typed;

                var createParams = new DialogParameters<TrackedTaskDialog>
                {
                    { x => x.Mode, TrackedTaskDialogMode.Create },
                    { x => x.TaskName, taskName },
                    { x => x.ProjectId, selectedProject.ProjectId },
                    { x => x.ProjectName, selectedProject.DisplayName },
                    { x => x.StartDate, createStart },
                    { x => x.Duration, createDuration },
                    { x => x.IsAllDay, false },
                    { x => x.Use24HourTime, use24HourTime },
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
                { x => x.TaskName, task.Name },
                { x => x.ProjectId, task.ProjectId },
                { x => x.ProjectName, task.Project?.DisplayName },
                { x => x.StartDate, task.StartDate },
                { x => x.EndDate, end },
                { x => x.Duration, task.Duration },
                { x => x.IsAllDay, task.IsAllDay },
                { x => x.Use24HourTime, use24HourTime },
                { x => x.HttpClient, client }
            };

            var dialog = await DialogService.ShowAsync<TrackedTaskDialog>(
                task.Name,
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

        private async Task<IEnumerable<Project>> SearchProjects(string? value, CancellationToken token)
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
                var from = weekStartMonday.Date;
                var to = WeekEntryGridRules.GetWeekEndSunday(weekStartMonday);

                var loadTasks = TrackedTasksClient.LoadRangeAsync(from, to, cancellationToken: token);
                var loadSubs = LoadSubmittedMonthsAsync(token);
                await Task.WhenAll(loadTasks, loadSubs);
                if (token.IsCancellationRequested) return;

                weekTasks = await loadTasks;
                submittedMonths = await loadSubs;
                visibleDays = WeekEntryGridRules.GetVisibleWeekDays(weekStartMonday, BusinessWeekOnly);
                RefreshKnownTaskNames();
                RebuildTaskRows(preserveDraftRows: false);
                RecalcTotals();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                loadError = "Couldn't load this week's time.";
                Snackbar.AddApiError(ex, loadError);
                weekTasks = new List<TrackedTask>();
                knownTaskNames = new List<string>();
                taskRows = new List<TaskRow>();
            }
            finally
            {
                // Always clear loading — a cancelled load that left isLoading=true
                // disabled New Task forever until the next successful load finished.
                isLoading = false;
                if (LoadingChanged.HasDelegate)
                    await LoadingChanged.InvokeAsync(false);
                if (!token.IsCancellationRequested)
                    await InvokeAsync(StateHasChanged);
            }
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

        private void RefreshKnownTaskNames()
        {
            var projectId = selectedProject?.ProjectId;
            if (string.IsNullOrEmpty(projectId))
            {
                knownTaskNames = new List<string>();
                return;
            }

            var from = weekStartMonday.Date;
            var to = WeekEntryGridRules.GetVisibleWeekEnd(weekStartMonday, BusinessWeekOnly);
            knownTaskNames = WeekEntryGridRules
                .DistinctManualTaskNames(weekTasks.Select(ToSlice), projectId, from, to)
                .ToList();
        }

        /// <summary>
        /// One row per distinct task name that has time this week for the project,
        /// plus any draft rows the user added via New Task.
        /// </summary>
        private void RebuildTaskRows(bool preserveDraftRows)
        {
            CancelAllDebounces();

            var projectId = selectedProject?.ProjectId;
            if (string.IsNullOrEmpty(projectId))
            {
                taskRows = new List<TaskRow>();
                return;
            }

            var from = weekStartMonday.Date;
            var to = WeekEntryGridRules.GetVisibleWeekEnd(weekStartMonday, BusinessWeekOnly);
            var names = WeekEntryGridRules
                .DistinctManualTaskNames(weekTasks.Select(ToSlice), projectId, from, to)
                .ToList();

            var drafts = preserveDraftRows
                ? taskRows.Where(r => !r.HasPersistedData).ToList()
                : new List<TaskRow>();

            var next = new List<TaskRow>();
            foreach (var name in names)
                next.Add(BuildRowForTaskName(name));

            foreach (var draft in drafts)
                next.Add(BuildDraftRow(draft.DraftTaskName));

            taskRows = next;
        }

        private TaskRow BuildRowForTaskName(string taskName)
        {
            var projectId = selectedProject!.ProjectId;
            var slices = weekTasks.Select(ToSlice).ToList();
            var submittedList = submittedMonths.Select(x => (x.Year, x.Month)).ToList();
            var cells = new DayCell[visibleDays.Count];

            for (var i = 0; i < visibleDays.Count; i++)
            {
                var day = visibleDays[i];
                var isSubmitted = WeekEntryGridRules.IsDaySubmitted(day, submittedList);
                var bind = WeekEntryGridRules.BindDayForTaskName(slices, projectId, taskName, day);

                if (bind.Kind == WeekEntryGridRules.DayBindKind.Single && bind.TaskId != null)
                {
                    var start = bind.StartDate ?? day.Date.Add(defaultStartTime);
                    cells[i] = new DayCell
                    {
                        Date = day,
                        IsSubmitted = isSubmitted,
                        TaskId = bind.TaskId,
                        DurationText = WeekEntryGridRules.FormatDayDurationInput(bind.EditableDuration),
                        SavedDuration = bind.EditableDuration,
                        BoundStartDate = start,
                        Status = CellSaveStatus.Idle
                    };
                }
                else if (bind.Kind == WeekEntryGridRules.DayBindKind.Multiple)
                {
                    cells[i] = new DayCell
                    {
                        Date = day,
                        IsSubmitted = true,
                        DurationText = WeekEntryGridRules.FormatDayDurationInput(bind.TotalManualDuration),
                        SavedDuration = bind.TotalManualDuration,
                        ErrorMessage = "Multiple — edit in All",
                        Status = CellSaveStatus.Idle
                    };
                }
                else
                {
                    cells[i] = EmptyCell(day, isSubmitted);
                }
            }

            return new TaskRow
            {
                SelectedTaskName = taskName,
                DraftTaskName = taskName,
                Cells = cells
            };
        }

        private TaskRow BuildDraftRow(string draftName = "")
        {
            var submittedList = submittedMonths.Select(x => (x.Year, x.Month)).ToList();
            var cells = visibleDays
                .Select(d => EmptyCell(d, WeekEntryGridRules.IsDaySubmitted(d, submittedList)))
                .ToArray();

            return new TaskRow
            {
                SelectedTaskName = null,
                DraftTaskName = draftName ?? "",
                Cells = cells
            };
        }

        private DayCell EmptyCell(DateTime day, bool isSubmitted) =>
            new()
            {
                Date = day,
                IsSubmitted = isSubmitted,
                DurationText = "",
                SavedDuration = TimeSpan.Zero,
                BoundStartDate = day.Date.Add(defaultStartTime),
                Status = CellSaveStatus.Idle
            };

        /// <summary>
        /// True when a row exists that has not saved any day yet (no TaskId on any cell).
        /// Blocks stacking multiple empty drafts via New Task.
        /// </summary>
        private bool HasIncompleteDraftRow =>
            taskRows.Any(r => !r.HasPersistedData);

        private void AddTaskRow()
        {
            if (selectedProject == null)
            {
                Snackbar.Add("Select a project first.", Severity.Info);
                return;
            }

            if (isLoading)
            {
                Snackbar.Add("Still loading this week…", Severity.Info);
                return;
            }

            // One unfinished row at a time: name and/or hours must be saved before another draft.
            if (HasIncompleteDraftRow)
            {
                Snackbar.Add("Finish the draft row (task name + hours) before adding another.", Severity.Info);
                return;
            }

            taskRows.Add(BuildDraftRow());
            _ = NotifyProjectSelectionChangedAsync();
        }

        private void OnDraftTaskNameChanged(TaskRow row, string? value)
        {
            if (row.HasPersistedData) return;
            // Keep mid-edit typing intact; ResolvedTaskName/Sanitize trim on save.
            row.DraftTaskName = value ?? "";
        }

        /// <summary>Day / week footer totals are derived live from the grid and weekTasks.</summary>
        private void RecalcTotals()
        {
            // no cached fields — footer methods re-sum on render
        }

        private static WeekEntryGridRules.WeekEntryTaskSlice ToSlice(TrackedTask t) =>
            new(t.TaskId, t.Name, t.ProjectId, t.StartDate, t.Duration, t.IsAllDay, t.StopwatchItemId);

        private string RowTotalLabel(TaskRow row)
        {
            var t = TimeSpan.Zero;
            foreach (var c in row.Cells)
                t += CellDuration(c);
            return WeekEntryGridRules.FormatDuration(t);
        }

        /// <summary>Sum of duration across all task rows for one day column (footer).</summary>
        private string DayColumnTotalLabel(int dayIndex) =>
            WeekEntryGridRules.FormatDuration(SumDayColumn(dayIndex));

        /// <summary>Sum of every cell in the grid — this project week total (right-hand column).</summary>
        private string GridWeekTotalLabel =>
            WeekEntryGridRules.FormatDuration(SumAllGridCells());

        /// <summary>Per-day total across every project (from loaded week data).</summary>
        private string AllProjectsDayTotalLabel(int dayIndex)
        {
            if (dayIndex < 0 || dayIndex >= visibleDays.Count)
                return WeekEntryGridRules.FormatDuration(TimeSpan.Zero);
            var day = visibleDays[dayIndex].Date;
            var t = TimeSpan.Zero;
            foreach (var task in weekTasks)
            {
                if (task.StartDate.Date == day)
                    t += task.Duration;
            }
            return WeekEntryGridRules.FormatDuration(WeekEntryGridRules.NormalizeDuration(t));
        }

        /// <summary>Week total across every project for visible days.</summary>
        private string AllProjectsWeekTotalLabel
        {
            get
            {
                var t = TimeSpan.Zero;
                for (var i = 0; i < visibleDays.Count; i++)
                {
                    var day = visibleDays[i].Date;
                    foreach (var task in weekTasks)
                    {
                        if (task.StartDate.Date == day)
                            t += task.Duration;
                    }
                }
                return WeekEntryGridRules.FormatDuration(WeekEntryGridRules.NormalizeDuration(t));
            }
        }

        private TimeSpan SumDayColumn(int dayIndex)
        {
            var t = TimeSpan.Zero;
            foreach (var row in taskRows)
            {
                if (dayIndex < 0 || dayIndex >= row.Cells.Length)
                    continue;
                t += CellDuration(row.Cells[dayIndex]);
            }
            return WeekEntryGridRules.NormalizeDuration(t);
        }

        private TimeSpan SumAllGridCells()
        {
            var t = TimeSpan.Zero;
            foreach (var row in taskRows)
            {
                foreach (var c in row.Cells)
                    t += CellDuration(c);
            }
            return WeekEntryGridRules.NormalizeDuration(t);
        }

        private static TimeSpan CellDuration(DayCell c)
        {
            if (WeekEntryGridRules.TryParseDayDurationText(c.DurationText, out var d) && d > TimeSpan.Zero)
                return d;
            if (c.SavedDuration > TimeSpan.Zero)
                return c.SavedDuration;
            return TimeSpan.Zero;
        }

        private void OnDurationChanged(TaskRow row, int dayIndex, string? value)
        {
            if (dayIndex < 0 || dayIndex >= row.Cells.Length) return;
            var cell = row.Cells[dayIndex];
            if (cell.IsReadOnly || selectedProject == null) return;

            // Soft filter only while typing — do not re-pad (that kills selection/caret).
            cell.DurationText = WeekEntryGridRules.FilterDurationInputChars(value);
            cell.DurationError = null;

            // Debounce when empty (clear) or when commit would accept (incl. bare "4").
            if (string.IsNullOrEmpty(cell.DurationText)
                || WeekEntryGridRules.TryCommitDayDurationText(cell.DurationText, out _))
            {
                ScheduleSave(row, dayIndex);
                return;
            }

            cell.DebounceCts?.Cancel();
            cell.Status = CellSaveStatus.Idle;
        }

        private void ScheduleSave(TaskRow row, int dayIndex)
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

        private async Task DebouncedSaveAsync(TaskRow row, int dayIndex, int generation, CancellationToken token)
        {
            try { await Task.Delay(500, token); }
            catch (OperationCanceledException) { return; }

            if (disposed || token.IsCancellationRequested) return;
            await SaveCellAsync(row, dayIndex, generation);
        }

        private async Task SaveCellAsync(TaskRow row, int dayIndex, int generation)
        {
            if (dayIndex < 0 || dayIndex >= row.Cells.Length) return;
            var cell = row.Cells[dayIndex];
            if (cell.SaveGeneration != generation) return;
            if (selectedProject == null || cell.IsReadOnly) return;

            // Always trim before validate/persist so stored names never carry edge spaces.
            var taskName = WeekEntryGridRules.SanitizeTaskName(row.ResolvedTaskName);
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

            if (newDuration > TimeSpan.Zero)
            {
                var nameError = WeekEntryGridRules.ValidateTaskName(taskName);
                if (nameError != null)
                {
                    cell.Status = CellSaveStatus.Error;
                    cell.ErrorMessage = nameError;
                    Snackbar.Add(nameError, Severity.Warning);
                    await InvokeAsync(StateHasChanged);
                    return;
                }

                // Reflect trimmed name in the draft field after a successful name validation.
                if (!row.HasPersistedData)
                    row.DraftTaskName = taskName;
            }

            var decision = WeekEntryGridRules.DecideMutation(
                cell.TaskId, cell.SavedDuration, newDuration);

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
                        await CreateCellAsync(row, cell, taskName!, newDuration);
                        break;
                    case WeekEntryGridRules.CellMutationKind.Update:
                        await UpdateCellAsync(row, cell, taskName!, newDuration);
                        break;
                    case WeekEntryGridRules.CellMutationKind.Delete:
                        await DeleteCellAsync(cell);
                        break;
                }

                if (cell.SaveGeneration != generation) return;

                // After first save, lock the display name (clickable dialog target).
                if (!string.IsNullOrEmpty(taskName))
                {
                    row.SelectedTaskName = taskName;
                    row.DraftTaskName = taskName;
                    RefreshKnownTaskNames();
                }

                cell.Status = CellSaveStatus.Saved;
                cell.ErrorMessage = null;
                RecalcTotals();
                await InvokeAsync(StateHasChanged);
                await NotifyEntriesChangedDebouncedAsync();
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

        private async Task CreateCellAsync(TaskRow row, DayCell cell, string taskName, TimeSpan duration)
        {
            // Keep existing start if re-creating; otherwise default 9:00.
            var startLocal = cell.BoundStartDate.Date == cell.Date.Date && cell.BoundStartDate.TimeOfDay > TimeSpan.Zero
                ? cell.Date.Date.Add(cell.BoundStartDate.TimeOfDay)
                : cell.Date.Date.Add(defaultStartTime);

            var dto = new CreateTrackedTaskDto
            {
                Name = taskName,
                StartDate = SettingsService.ConvertFromUserTime(startLocal),
                Duration = duration,
                IsAllDay = false,
                ProjectId = selectedProject!.ProjectId
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
            RefreshKnownTaskNames();
        }

        private async Task UpdateCellAsync(TaskRow row, DayCell cell, string taskName, TimeSpan duration)
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
                Name = taskName,
                StartDate = SettingsService.ConvertFromUserTime(start),
                EndDate = SettingsService.ConvertFromUserTime(end),
                Duration = duration,
                IsAllDay = false,
                ProjectId = selectedProject!.ProjectId
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
                existing.Name = taskName;
                existing.StartDate = start;
                existing.EndDate = end;
                existing.Duration = duration;
            }

            cell.BoundStartDate = start;
            cell.SavedDuration = duration;
            cell.DurationText = WeekEntryGridRules.FormatDayDurationInput(duration);
        }

        private async Task DeleteCellAsync(DayCell cell)
        {
            if (string.IsNullOrEmpty(cell.TaskId))
                return;

            var response = await client.DeleteAsync(
                $"{Constants.API.TrackedTask.Delete}/{cell.TaskId}");
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException(string.IsNullOrWhiteSpace(error) ? "Delete failed." : error);
            }

            weekTasks.RemoveAll(t => t.TaskId == cell.TaskId);
            cell.TaskId = null;
            cell.DurationText = "";
            cell.SavedDuration = TimeSpan.Zero;
            cell.BoundStartDate = cell.Date.Date.Add(defaultStartTime);
            RefreshKnownTaskNames();
        }

        private async Task ClearSavedStatusAsync(DayCell cell, int generation)
        {
            try { await Task.Delay(1500); }
            catch { return; }

            if (disposed) return;
            if (cell.SaveGeneration != generation) return;
            if (cell.Status == CellSaveStatus.Saved)
            {
                cell.Status = CellSaveStatus.Idle;
                await InvokeAsync(StateHasChanged);
            }
        }

        private async Task NotifyEntriesChangedDebouncedAsync()
        {
            tableNotifyCts?.Cancel();
            tableNotifyCts?.Dispose();
            tableNotifyCts = new CancellationTokenSource();
            var token = tableNotifyCts.Token;
            try { await Task.Delay(400, token); }
            catch (OperationCanceledException) { return; }

            if (disposed || token.IsCancellationRequested) return;
            if (EntriesChanged.HasDelegate)
                await EntriesChanged.InvokeAsync();
        }

        private void CancelAllDebounces()
        {
            foreach (var row in taskRows)
            {
                foreach (var c in row.Cells)
                {
                    c.DebounceCts?.Cancel();
                    c.DebounceCts?.Dispose();
                    c.DebounceCts = null;
                    if (c.Status is CellSaveStatus.Pending)
                        c.Status = CellSaveStatus.Idle;
                }
            }
        }

        private static string DayHeader(DateTime day) => day.ToString("ddd d");

        private static string StatusLabel(DayCell cell) => cell.Status switch
        {
            CellSaveStatus.Pending => "…",
            CellSaveStatus.Saving => "Saving",
            CellSaveStatus.Saved => "Saved",
            CellSaveStatus.Error => cell.ErrorMessage ?? "Error",
            _ when !string.IsNullOrEmpty(cell.ErrorMessage) => cell.ErrorMessage!,
            _ when cell.IsSubmitted && cell.SavedDuration > TimeSpan.Zero => "Locked",
            _ => ""
        };

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            loadCts?.Cancel();
            loadCts?.Dispose();
            tableNotifyCts?.Cancel();
            tableNotifyCts?.Dispose();
            CancelAllDebounces();
        }
    }
}
