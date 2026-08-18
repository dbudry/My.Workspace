using Microsoft.EntityFrameworkCore;
using My.DAL.Data;
using My.Shared.Dtos.Project;

namespace My.Functions.Helpers
{
    internal static class ProjectBillableImpactQuery
    {
        internal static async Task<ProjectBillableImpactDto> QueryAsync(ApplicationDbContext db, string projectId)
        {
            var count = await db.TrackedTasks
                .AsNoTracking()
                .CountAsync(t => t.ProjectId == projectId && t.IsBillable);

            return new ProjectBillableImpactDto { BillableTaskCount = count };
        }
    }
}