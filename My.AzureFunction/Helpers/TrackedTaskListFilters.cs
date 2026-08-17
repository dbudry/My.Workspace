using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using My.DAL.Models;

namespace My.Functions.Helpers
{
    public static class TrackedTaskListFilters
    {
        /// <summary>
        /// SQL-translatable duration key in seconds. All-day rows stored as 00:00:00
        /// (SQL <c>time</c> cannot hold 24h+) use DATEDIFF(second, Start, End). Timed
        /// rows use the time column parts. Not an exact match for
        /// <c>AllDayEntryRules.EffectiveDuration</c> (which excludes weekends), but
        /// monotonic with the real duration. DateTime subtraction inside OrderBy
        /// does not translate against SQL Server.
        /// </summary>
        public static readonly Expression<Func<TrackedTask, int>> DurationSeconds = t =>
            t.IsAllDay && t.Duration == TimeSpan.Zero && t.EndDate != null
                ? EF.Functions.DateDiffSecond(t.StartDate, t.EndDate.Value)
                : t.Duration.Hours * 3600 + t.Duration.Minutes * 60 + t.Duration.Seconds;

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
                    || t.Details.Contains(term)
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
                    ? q => q.OrderByDescending(t => t.Details)
                    : q => q.OrderBy(t => t.Details),
                "duration" => sortDescending
                    ? q => q.OrderByDescending(DurationSeconds)
                    : q => q.OrderBy(DurationSeconds),
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