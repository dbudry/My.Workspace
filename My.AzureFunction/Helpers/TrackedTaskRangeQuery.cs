using Microsoft.EntityFrameworkCore;
using My.DAL.Data;
using My.DAL.Models;

namespace My.Functions.Helpers
{
    /// <summary>
    /// Loads all tracked tasks in a date window in one database round-trip (split includes),
    /// avoiding per-page COUNT + SKIP queries that calendar/reports used to trigger.
    /// </summary>
    internal static class TrackedTaskRangeQuery
    {
        internal const int MaxRows = 10_000;
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

            return await query.Take(MaxRows).ToListAsync(ct);
        }
    }
}