using My.Shared.Dtos.Paging;
using My.Shared.Dtos.Project;
using My.Shared.Dtos.StopwatchItem;
using My.Shared.Dtos.TaskList;
using My.Shared.Dtos.TrackedTask;

namespace My.Shared.Rules
{
    /// <summary>
    /// Merges stopwatch work items and manual tracked tasks into one sortable, filterable, paged
    /// list. Pure and deterministic so any store (SQL, Azure Table, …) can page without loading
    /// full history. Row semantics mirror the client's TaskListRowBuilder: stopwatch rows
    /// sort/display by LastWorkedAt and total (incl. live) duration; manual rows by StartDate
    /// and their own duration.
    /// </summary>
    public static class TaskListRules
    {
        public const string SortName = "Name";
        public const string SortProject = "Project";
        public const string SortDuration = "Duration";
        public const string SortDate = "Date";

        private sealed class Row
        {
            public required TaskListRowDto Dto { get; init; }
            public required string Name { get; init; }
            public required string ProjectDisplay { get; init; }
            public required long DurationTicks { get; init; }
            public required DateTime SortDate { get; init; }
        }

        /// <summary>
        /// How many ordered manuals the store must return so page
        /// <paramref name="pageNumber"/> of size <paramref name="pageSize"/> is exact after merge
        /// with stopwatch rows. Equals offset + pageSize (not a history cap).
        /// </summary>
        public static int RequiredManualPrefixLength(int pageNumber, int pageSize)
        {
            if (pageNumber < 1) pageNumber = 1;
            pageSize = EffectivePageSize(pageSize);
            return (pageNumber - 1) * pageSize + pageSize;
        }

        /// <summary>
        /// Full in-memory path (tests / small fixtures). Prefer
        /// <see cref="BuildPageFromManualPrefix"/> in production so the store never loads all manuals.
        /// </summary>
        public static PagedResponse<TaskListRowDto> BuildPage(
            IEnumerable<StopwatchItemDto> stopwatchItems,
            IEnumerable<TrackedTaskDto> manualTasks,
            string? search,
            string? sortBy,
            bool sortDescending,
            int pageNumber,
            int pageSize,
            DateTime nowUtc)
        {
            if (pageNumber < 1) pageNumber = 1;
            pageSize = EffectivePageSize(pageSize);

            var rows = BuildRows(stopwatchItems, manualTasks, nowUtc);
            rows = ApplySearch(rows, search);
            var sorted = SortRows(rows, sortBy, sortDescending);

            return ToPage(sorted, totalCount: sorted.Count, pageNumber, pageSize);
        }

        /// <summary>
        /// Production paging without loading full history.
        /// <list type="bullet">
        /// <item><paramref name="stopwatchItems"/> — all stopwatch work items for the user (small set).</item>
        /// <item><paramref name="manualPrefix"/> — first
        /// <see cref="RequiredManualPrefixLength"/> manuals in task-list sort order, search applied.</item>
        /// <item><paramref name="totalMatchingManuals"/> — full manual match count from the store.</item>
        /// </list>
        /// Search is applied to stopwatch rows here; manuals must already be filtered by the store.
        /// </summary>
        public static PagedResponse<TaskListRowDto> BuildPageFromManualPrefix(
            IEnumerable<StopwatchItemDto> stopwatchItems,
            IEnumerable<TrackedTaskDto> manualPrefix,
            int totalMatchingManuals,
            string? search,
            string? sortBy,
            bool sortDescending,
            int pageNumber,
            int pageSize,
            DateTime nowUtc)
        {
            if (pageNumber < 1) pageNumber = 1;
            pageSize = EffectivePageSize(pageSize);
            if (totalMatchingManuals < 0) totalMatchingManuals = 0;

            var swRows = BuildRows(stopwatchItems, Array.Empty<TrackedTaskDto>(), nowUtc);
            swRows = ApplySearch(swRows, search);

            // Manuals already match search at the store; do not re-filter (prefix theorem).
            var manualRows = BuildRows(Array.Empty<StopwatchItemDto>(), manualPrefix, nowUtc);
            var combined = new List<Row>(swRows.Count + manualRows.Count);
            combined.AddRange(swRows);
            combined.AddRange(manualRows);

            var sorted = SortRows(combined, sortBy, sortDescending);
            var total = swRows.Count + totalMatchingManuals;
            return ToPage(sorted, totalCount: total, pageNumber, pageSize);
        }

        /// <summary>Same name/project match used when search is applied in memory (stopwatch side).</summary>
        public static bool MatchesSearch(string? name, string? projectDisplay, string? search)
        {
            if (string.IsNullOrWhiteSpace(search)) return true;
            var term = search.Trim();
            return (!string.IsNullOrEmpty(name) && name.Contains(term, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrEmpty(projectDisplay) && projectDisplay.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        private static int EffectivePageSize(int pageSize)
        {
            if (pageSize < 1) return ListQueryParameters.DefaultPageSize;
            return Math.Min(pageSize, ListQueryParameters.MaxPageSize);
        }

        private static List<Row> BuildRows(
            IEnumerable<StopwatchItemDto> stopwatchItems,
            IEnumerable<TrackedTaskDto> manualTasks,
            DateTime nowUtc)
        {
            var rows = new List<Row>();

            foreach (var item in stopwatchItems)
            {
                var duration = item.TotalDuration;
                if (item.IsRunning && item.ActiveSessionStartDate.HasValue)
                    duration += StopwatchRules.ElapsedForActiveSession(item.ActiveSessionStartDate.Value, nowUtc);

                rows.Add(new Row
                {
                    Dto = new TaskListRowDto { IsStopwatch = true, StopwatchItem = item },
                    Name = item.Name ?? string.Empty,
                    ProjectDisplay = StopwatchProjectDisplay(item.Project),
                    DurationTicks = duration.Ticks,
                    SortDate = item.LastWorkedAt
                });
            }

            foreach (var task in manualTasks)
            {
                rows.Add(new Row
                {
                    Dto = new TaskListRowDto { IsStopwatch = false, ManualTask = task },
                    Name = task.Name ?? string.Empty,
                    ProjectDisplay = task.Project?.DisplayName ?? task.Project?.Name ?? string.Empty,
                    DurationTicks = task.Duration.Ticks,
                    SortDate = task.StartDate
                });
            }

            return rows;
        }

        private static List<Row> ApplySearch(List<Row> rows, string? search)
        {
            if (string.IsNullOrWhiteSpace(search))
                return rows;

            var term = search.Trim();
            return rows.Where(r =>
                    r.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || r.ProjectDisplay.Contains(term, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        private static List<Row> SortRows(List<Row> rows, string? sortBy, bool sortDescending)
        {
            var ordered = (sortBy ?? SortDate) switch
            {
                SortName => Order(rows, r => r.Name, sortDescending),
                SortProject or "ProjectName" => Order(rows, r => r.ProjectDisplay, sortDescending),
                SortDuration => Order(rows, r => r.DurationTicks, sortDescending),
                _ => Order(rows, r => r.SortDate, sortDescending) // Date / default
            };

            // Deterministic tiebreak so paging never drops or duplicates a row when the primary
            // key ties (e.g. many rows share a date).
            return ordered
                .ThenByDescending(r => r.SortDate)
                .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static PagedResponse<TaskListRowDto> ToPage(
            List<Row> sortedPrefixOrFull,
            int totalCount,
            int pageNumber,
            int pageSize)
        {
            var pageItems = sortedPrefixOrFull
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .Select(r => r.Dto)
                .ToList();

            var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);
            return new PagedResponse<TaskListRowDto>
            {
                Items = pageItems,
                TotalCount = totalCount,
                PageSize = pageSize,
                CurrentPage = pageNumber,
                TotalPages = totalPages,
                HasNext = pageNumber < totalPages,
                HasPrevious = pageNumber > 1
            };
        }

        private static IOrderedEnumerable<Row> Order<TKey>(IEnumerable<Row> rows, Func<Row, TKey> key, bool descending) =>
            descending ? rows.OrderByDescending(key) : rows.OrderBy(key);

        // Matches the client's ProjectDisplayHelper.FromDto so sorting/filtering by project agree
        // with what the stopwatch row shows.
        private static string StopwatchProjectDisplay(ProjectDto? project)
        {
            if (project == null) return string.Empty;
            return string.IsNullOrEmpty(project.ProjectGroupName)
                ? project.Name
                : $"{project.ProjectGroupName} - {project.Name}";
        }
    }
}
