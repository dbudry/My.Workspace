using Microsoft.EntityFrameworkCore;
using My.DAL.Data;
using My.Shared.Dtos.Project;
using My.Shared.Helpers;

namespace My.Functions.Helpers
{
    internal static class ProjectDeleteImpactQuery
    {
        internal static async Task<ProjectDeleteImpactDto> QueryAsync(ApplicationDbContext db, string projectId)
        {
            var stats = await db.TrackedTasks
                .AsNoTracking()
                .Where(t => t.ProjectId == projectId)
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    TaskCount = g.Count(),
                    GoogleCalendarTaskCount = g.Count(t => t.GoogleEventId != null),
                    TeamCalendarTaskCount = g.Count(t => t.TeamAvailabilityEventId != null)
                })
                .FirstOrDefaultAsync();

            var aliasCount = await db.TrackedTaskAliases
                .AsNoTracking()
                .CountAsync(a => a.ProjectId == projectId);

            return ProjectDeleteGuard.Evaluate(
                stats?.TaskCount ?? 0,
                stats?.GoogleCalendarTaskCount ?? 0,
                stats?.TeamCalendarTaskCount ?? 0,
                aliasCount);
        }
    }
}