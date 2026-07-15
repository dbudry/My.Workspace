using Microsoft.EntityFrameworkCore;
using My.DAL.Data;

namespace My.Functions.Helpers
{
    internal static class ProjectBillableSync
    {
        internal static async Task SetTaskBillableFlagsAsync(ApplicationDbContext db, string projectId, bool isBillable)
        {
            await db.TrackedTasks
                .Where(t => t.ProjectId == projectId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.IsBillable, isBillable));
        }
    }
}