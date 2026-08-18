using Microsoft.EntityFrameworkCore;
using My.DAL.Data;
using My.DAL.Models;
using My.Shared.Constants;
using My.Shared.Dtos.TrackedTask;
using My.Shared.Rules;

namespace My.Functions.Helpers
{
    /// <summary>
    /// EF/SQL implementation of <see cref="ITaskListManualStore"/>.
    /// Swappable later for Azure Table/Cosmos without changing <see cref="TaskListRules"/>.
    /// </summary>
    internal sealed class EfTaskListManualStore : ITaskListManualStore
    {
        private const string IncludeGraph = "Project.ProjectGroup,Project.Organization";
        private readonly ApplicationDbContext _db;
        private readonly AppMapper _mapper;

        public EfTaskListManualStore(ApplicationDbContext db, AppMapper mapper)
        {
            _db = db;
            _mapper = mapper;
        }

        public async Task<int> CountAsync(string userId, string? search, CancellationToken cancellationToken = default)
        {
            var filter = TrackedTaskListFilters.Build(
                userId, search, from: null, to: null, excludeStopwatchSessions: true);
            return await _db.TrackedTasks.AsNoTracking().CountAsync(filter, cancellationToken);
        }

        public async Task<IReadOnlyList<TrackedTaskDto>> GetOrderedPrefixAsync(
            string userId,
            string? search,
            string? sortBy,
            bool sortDescending,
            int take,
            CancellationToken cancellationToken = default)
        {
            if (take < 1)
                return Array.Empty<TrackedTaskDto>();

            var filter = TrackedTaskListFilters.Build(
                userId, search, from: null, to: null, excludeStopwatchSessions: true);
            // Same sort keys as TaskListRules so the prefix theorem holds.
            var orderBy = TaskListManualOrder.ForTaskList(sortBy, sortDescending);

            IQueryable<TrackedTask> query = _db.TrackedTasks.AsNoTracking().Where(filter);
            foreach (var includeProperty in IncludeGraph.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                query = query.Include(includeProperty);

            query = orderBy(query).AsSplitQuery();

            var entities = await query.Take(take).ToListAsync(cancellationToken);
            var hoursRow = await _db.AppSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == Constants.SettingKeys.WorkdayHours, cancellationToken);
            var hours = AllDayEntryRules.ParseWorkdayHours(hoursRow?.Value);
            return entities.Select(t =>
            {
                var dto = _mapper.TrackedTaskToDto(t);
                dto.Details ??= string.Empty; // TrackedTask.Details is nullable in the DB; DTO stays non-null.
                dto.Duration = AllDayEntryRules.EffectiveDuration(
                    t.IsAllDay, t.StartDate, t.EndDate, t.Duration, hours);
                return dto;
            }).ToList();
        }
    }

    /// <summary>
    /// Manual-row ordering aligned with <see cref="TaskListRules"/> sort keys + tiebreaks.
    /// Kept separate from generic tracked-task list OrderBy so task-list paging stays correct.
    /// </summary>
    internal static class TaskListManualOrder
    {
        public static Func<IQueryable<TrackedTask>, IOrderedQueryable<TrackedTask>> ForTaskList(
            string? sortBy,
            bool sortDescending)
        {
            // Tiebreaks match TaskListRules: ThenByDescending(SortDate), ThenBy(Name).
            return (sortBy ?? TaskListRules.SortDate) switch
            {
                TaskListRules.SortName => sortDescending
                    ? q => q.OrderByDescending(t => t.Details)
                        .ThenByDescending(t => t.StartDate)
                        .ThenBy(t => t.Details)
                    : q => q.OrderBy(t => t.Details)
                        .ThenByDescending(t => t.StartDate)
                        .ThenBy(t => t.Details),
                // Coalesce in a form EF can translate (null project sorts as empty).
                TaskListRules.SortProject or "ProjectName" => sortDescending
                    ? q => q.OrderByDescending(t => t.Project != null
                            ? (t.Project.DisplayName ?? t.Project.Name)
                            : "")
                        .ThenByDescending(t => t.StartDate)
                        .ThenBy(t => t.Details)
                    : q => q.OrderBy(t => t.Project != null
                            ? (t.Project.DisplayName ?? t.Project.Name)
                            : "")
                        .ThenByDescending(t => t.StartDate)
                        .ThenBy(t => t.Details),
                TaskListRules.SortDuration => sortDescending
                    ? q => q.OrderByDescending(TrackedTaskListFilters.DurationSeconds)
                        .ThenByDescending(t => t.StartDate)
                        .ThenBy(t => t.Details)
                    : q => q.OrderBy(TrackedTaskListFilters.DurationSeconds)
                        .ThenByDescending(t => t.StartDate)
                        .ThenBy(t => t.Details),
                _ => sortDescending
                    ? q => q.OrderByDescending(t => t.StartDate).ThenBy(t => t.Details)
                    : q => q.OrderBy(t => t.StartDate).ThenBy(t => t.Details)
            };
        }
    }
}
