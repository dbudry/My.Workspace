using My.Shared.Dtos.Paging;
using My.Shared.Dtos.Project;
using My.Shared.Dtos.StopwatchItem;
using My.Shared.Dtos.TaskList;
using My.Shared.Dtos.TrackedTask;

namespace My.Shared.Rules
{
    /// <summary>
    /// Merges stopwatch work items and manual tracked tasks into one sortable, filterable, paged
    /// list. Pure and deterministic so the server can page it in a single response and tests can
    /// pin the ordering without a database. Row semantics mirror the client's TaskListRowBuilder:
    /// stopwatch rows sort/display by LastWorkedAt and total (incl. live) duration; manual rows by
    /// StartDate and their own duration.
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
            if (pageSize < 1) pageSize = ListQueryParameters.DefaultPageSize;

            var rows = new List<Row>();

            foreach (var item in stopwatchItems)
            {
                var duration = item.TotalDuration;
                if (item.IsRunning && item.ActiveSessionStartDate.HasValue)
                    duration += StopwatchRules.ElapsedForActiveSession(item.ActiveSessionStartDate.Value, nowUtc);

                rows.Add(new Row
                {
                    Dto = new TaskListRowDto { IsStopwatch = true, StopwatchItem = item },
                    Name = item.Name,
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
                    Name = task.Name,
                    ProjectDisplay = task.Project?.DisplayName ?? string.Empty,
                    DurationTicks = task.Duration.Ticks,
                    SortDate = task.StartDate
                });
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                rows = rows.Where(r =>
                        r.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                        || r.ProjectDisplay.Contains(term, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            var ordered = (sortBy ?? SortDate) switch
            {
                SortName => Order(rows, r => r.Name, sortDescending),
                SortProject or "ProjectName" => Order(rows, r => r.ProjectDisplay, sortDescending),
                SortDuration => Order(rows, r => r.DurationTicks, sortDescending),
                _ => Order(rows, r => r.SortDate, sortDescending) // Date / default
            };

            // Deterministic tiebreak so paging never drops or duplicates a row when the primary
            // key ties (e.g. many rows share a date).
            var sorted = ordered
                .ThenByDescending(r => r.SortDate)
                .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var total = sorted.Count;
            var pageItems = sorted
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .Select(r => r.Dto)
                .ToList();

            var totalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize);
            return new PagedResponse<TaskListRowDto>
            {
                Items = pageItems,
                TotalCount = total,
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
