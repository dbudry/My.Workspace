using Microsoft.EntityFrameworkCore;
using My.DAL.Data;
using My.DAL.Models;

namespace My.Functions.Helpers
{
    /// <summary>
    /// Loads tracked tasks in a date window in one round-trip (split includes).
    /// Calendar/reports use this with a bounded window — not the unified Tasks pager
    /// (that uses <see cref="ITaskListManualStore"/> + <see cref="My.Shared.Rules.TaskListRules"/>).
    /// </summary>
    internal static class TrackedTaskRangeQuery
    {
        /// <summary>
        /// Safety only when the caller omits a date window. Date-bounded loads are uncapped
        /// so multi-year history inside a requested range is not silently truncated.
        /// </summary>
        internal const int UnboundedMaxRows = 50_000;

        private const string IncludeGraph = "Project.ProjectGroup,Project.Organization";

        internal static async Task<List<TrackedTask>> LoadAsync(
            ApplicationDbContext db,
            string userId,
            DateTime? from,
            DateTime? to,
            string? search,
            bool excludeStopwatchSessions,
            CancellationToken ct = default)
        {
            var filter = TrackedTaskListFilters.Build(userId, search, from, to, excludeStopwatchSessions);
            IQueryable<TrackedTask> query = db.TrackedTasks.AsNoTracking().Where(filter);

            foreach (var includeProperty in IncludeGraph.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                query = query.Include(includeProperty);

            query = query.AsSplitQuery().OrderByDescending(t => t.StartDate);

            // Bounded window: return everything in range (true paging is the tasklist path).
            if (from.HasValue && to.HasValue)
                return await query.ToListAsync(ct);

            return await query.Take(UnboundedMaxRows).ToListAsync(ct);
        }

        /// <summary>
        /// Same as <see cref="LoadAsync"/> but for a set of user ids (Reports team view).
        /// Empty <paramref name="userIds"/> returns an empty list without hitting SQL.
        /// </summary>
        internal static async Task<List<TrackedTask>> LoadForUsersAsync(
            ApplicationDbContext db,
            IReadOnlyCollection<string> userIds,
            DateTime? from,
            DateTime? to,
            CancellationToken ct = default)
        {
            if (userIds.Count == 0)
                return new List<TrackedTask>();

            var idList = userIds as List<string> ?? userIds.ToList();

            IQueryable<TrackedTask> query = db.TrackedTasks.AsNoTracking()
                .Where(t =>
                    idList.Contains(t.UserId)
                    && (from == null || t.StartDate >= from)
                    && (to == null || t.StartDate <= to));

            foreach (var includeProperty in IncludeGraph.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                query = query.Include(includeProperty);

            query = query.Include(t => t.User).AsSplitQuery().OrderByDescending(t => t.StartDate);

            if (from.HasValue && to.HasValue)
                return await query.ToListAsync(ct);

            return await query.Take(UnboundedMaxRows).ToListAsync(ct);
        }
    }
}
