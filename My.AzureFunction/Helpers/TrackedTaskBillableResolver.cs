using Microsoft.EntityFrameworkCore;
using My.DAL.Data;
using My.Shared.Rules;

namespace My.Functions.Helpers
{
    internal static class TrackedTaskBillableResolver
    {
        internal static async Task<bool> ResolveAsync(ApplicationDbContext db, string? projectId)
        {
            if (string.IsNullOrEmpty(projectId))
                return false;

            var project = await db.Projects.AsNoTracking()
                .Where(p => p.ProjectId == projectId)
                .Select(p => new { p.IsBillable, p.IsSharedAvailability })
                .FirstOrDefaultAsync();

            if (project == null)
                return false;

            return TrackedTaskBillableRules.FromProject(project.IsBillable, project.IsSharedAvailability);
        }
    }
}