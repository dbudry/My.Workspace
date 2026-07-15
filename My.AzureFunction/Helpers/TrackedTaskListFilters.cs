using System.Linq.Expressions;
using My.DAL.Models;

namespace My.Functions.Helpers
{
    public static class TrackedTaskListFilters
    {
        public static Expression<Func<TrackedTask, bool>> Build(
            string userId,
            string? search,
            DateTime? from,
            DateTime? to,
            bool excludeStopwatchSessions = false)
        {
            var term = search?.Trim();

            return t =>
                t.UserId == userId
                && (!excludeStopwatchSessions || t.StopwatchItemId == null)
                && (from == null || t.StartDate >= from)
                && (to == null || t.StartDate <= to)
                && (term == null || term == ""
                    || t.Name.Contains(term)
                    || (t.Project != null && (
                        t.Project.Name.Contains(term)
                        || (t.Project.DisplayName != null && t.Project.DisplayName.Contains(term))
                        || (t.Project.Slug != null && t.Project.Slug.Contains(term))
                        || (t.Project.Organization != null && t.Project.Organization.Name.Contains(term)))));
        }

        public static Func<IQueryable<TrackedTask>, IOrderedQueryable<TrackedTask>> OrderBy(
            string? sortBy,
            bool sortDescending)
        {
            return (sortBy ?? "StartDate").ToLowerInvariant() switch
            {
                "name" => sortDescending
                    ? q => q.OrderByDescending(t => t.Name)
                    : q => q.OrderBy(t => t.Name),
                "duration" => sortDescending
                    ? q => q.OrderByDescending(t => t.Duration)
                    : q => q.OrderBy(t => t.Duration),
                "enddate" => sortDescending
                    ? q => q.OrderByDescending(t => t.EndDate)
                    : q => q.OrderBy(t => t.EndDate),
                "project" or "projectname" => sortDescending
                    ? q => q.OrderByDescending(t => t.Project!.Name)
                    : q => q.OrderBy(t => t.Project!.Name),
                _ => sortDescending
                    ? q => q.OrderByDescending(t => t.StartDate)
                    : q => q.OrderBy(t => t.StartDate)
            };
        }
    }
}